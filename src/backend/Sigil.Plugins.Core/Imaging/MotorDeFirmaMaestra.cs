// Motor de la Firma Maestra (ADR-009, RF-01/02): validación de los TRES parámetros por
// cómputo local sobre píxeles (canal alfa por inspección directa, contraste RMS por
// histograma, nitidez por varianza de Laplaciano) + normalización a las dimensiones
// estándar con salida PNG RGBA 8-bit NO entrelazado (contrato del doc 04 §4 — el mismo
// formato que el spike validó contra el motor PDF).
//
// Semántica de las métricas (definición canónica — el frontend espeja los mensajes):
//   - alphaRatio: fracción de píxeles TOTALMENTE transparentes (alfa == 0). Una foto de
//     firma sobre papel es 100% opaca → 0.0 → rechazo (RF-02 exige fondo transparente).
//   - rmsContrast: RMS del APARTAMIENTO de la tinta respecto del fondo blanco — sobre los
//     píxeles NO transparentes: sqrt(mean(((255 − L)/255)²)) con L = luminancia BT.601
//     compuesta. CALIBRACIÓN (2026-07-16, mandato del doc 04 §4 "calibrar en la
//     implementación"): el RMS global de toda la imagen castiga a las firmas legítimas de
//     trazo fino (5% de cobertura de tinta negra da ~0.21 global < 0.25) — la intención del
//     umbral es "trazo suficientemente oscuro", no "mucha tinta". Tinta negra → ~1.0;
//     trazos desvaídos → ~0.03, ambos SIN depender de la cobertura.
//   - laplacianVariance: varianza del Laplaciano de 4 vecinos sobre la luminancia 0–255
//     compuesta. Bordes nítidos → alta; imagen borrosa/rampas suaves → baja.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Sigil.Plugins.Core.Imaging;

public sealed class ResultadoDeFirma
{
    public ResultadoDeFirma(bool esValida, IReadOnlyList<string> motivos, string metricsJson, byte[]? pngNormalizado)
    {
        EsValida = esValida;
        Motivos = motivos;
        MetricsJson = metricsJson;
        PngNormalizado = pngNormalizado;
    }

    public bool EsValida { get; }
    public IReadOnlyList<string> Motivos { get; }

    /// <summary>JSON con las métricas medidas — va a validationdetails (auditoría, doc 03 §4.5).</summary>
    public string MetricsJson { get; }

    /// <summary>Solo cuando EsValida: el PNG normalizado que se persiste e incrusta.</summary>
    public byte[]? PngNormalizado { get; }
}

public static class MotorDeFirmaMaestra
{
    // Techo anti "bomba de descompresión" (antagonista C1, 2026-07-16): el límite de carga
    // acota los bytes COMPRIMIDOS, no los píxeles — un PNG chico puede declarar dimensiones
    // gigantes y pedir gigas al decodificar (w*h*4 + w*h*8 del análisis), matando el worker
    // del sandbox. 4096×4096 sobra para cualquier firma real (el target es 600×200).
    public const int MaxDimensionPx = 4096;

    public static ResultadoDeFirma Procesar(byte[] bytes, SignatureSpec spec)
    {
        // 1. SOLO el header (Identify no asigna píxeles): formato y dimensiones ANTES del Load.
        IImageInfo? info;
        IImageFormat? formato;
        try
        {
            info = Image.Identify(bytes, out formato);
        }
        catch (Exception)
        {
            return Rechazo("La imagen no se pudo decodificar — subí un PNG válido.");
        }
        if (info is null || formato is null)
            return Rechazo("La imagen no se pudo decodificar — subí un PNG válido.");
        if (!string.Equals(formato.Name, "PNG", StringComparison.OrdinalIgnoreCase))
            return Rechazo($"El archivo debe ser PNG (recibido: {formato.Name}) — doc 04 §3.4.");
        if (info.Width > MaxDimensionPx || info.Height > MaxDimensionPx)
            return Rechazo($"La imagen es demasiado grande ({info.Width}×{info.Height} px; máximo {MaxDimensionPx}×{MaxDimensionPx}). Exportá la firma en menor resolución.");

        Image<Rgba32> img;
        try
        {
            img = Image.Load<Rgba32>(bytes);
        }
        catch (Exception)
        {
            return Rechazo("La imagen no se pudo decodificar — subí un PNG válido.");
        }

        using (img)
        {
            var m = Medir(img);
            var metricsJson = JsonSerializer.Serialize(new
            {
                alphaRatio = Math.Round(m.AlphaRatio, 4),
                rmsContrast = Math.Round(m.RmsContrast, 4),
                laplacianVariance = Math.Round(m.LaplacianVariance, 2),
                width = img.Width,
                height = img.Height,
                bytes = bytes.Length,
            });

            var motivos = new List<string>();
            if (m.AlphaRatio < spec.MinAlphaRatio)
                motivos.Add($"La imagen no tiene fondo transparente suficiente (transparencia {m.AlphaRatio:P0}, mínimo {spec.MinAlphaRatio:P0}). Exportá la firma como PNG con fondo transparente — una foto sobre papel no sirve.");
            if (m.RmsContrast < spec.MinRmsContrast)
                motivos.Add($"El contraste de la firma es demasiado bajo ({m.RmsContrast:F2}, mínimo {spec.MinRmsContrast:F2}). Usá un trazo más oscuro.");
            if (m.LaplacianVariance < spec.MinLaplacianVar)
                motivos.Add($"La imagen está borrosa (nitidez {m.LaplacianVariance:F0}, mínimo {spec.MinLaplacianVar:F0}). Subí una versión más nítida.");

            if (motivos.Count > 0)
                return new ResultadoDeFirma(false, motivos, metricsJson, null);

            var normalizado = Normalizar(img, spec);
            if (normalizado.Length > spec.MaxKB * 1024)
                return new ResultadoDeFirma(false,
                    new[] { $"La firma normalizada pesa {normalizado.Length / 1024} KB (máximo {spec.MaxKB} KB). Simplificá la imagen." },
                    metricsJson, null);

            return new ResultadoDeFirma(true, Array.Empty<string>(), metricsJson, normalizado);
        }
    }

    // ── métricas (ADR-009) ───────────────────────────────────────────────────

    private readonly struct Metricas(double alphaRatio, double rmsContrast, double laplacianVariance)
    {
        public double AlphaRatio { get; } = alphaRatio;
        public double RmsContrast { get; } = rmsContrast;
        public double LaplacianVariance { get; } = laplacianVariance;
    }

    private static Metricas Medir(Image<Rgba32> img)
    {
        int w = img.Width, h = img.Height;
        long transparentes = 0, tinta = 0;

        // Luminancia BT.601 compuesta sobre BLANCO (el fondo transparente cuenta como papel).
        var lum = new double[w * h];
        double sumaDesvio2 = 0; // apartamiento² de la TINTA respecto del blanco
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var p = img[x, y];
            var a = p.A / 255.0;
            var r = p.R * a + 255 * (1 - a);
            var g = p.G * a + 255 * (1 - a);
            var b = p.B * a + 255 * (1 - a);
            var l = 0.299 * r + 0.587 * g + 0.114 * b;
            lum[y * w + x] = l;

            if (p.A == 0)
            {
                transparentes++;
            }
            else
            {
                tinta++;
                var desvio = (255 - l) / 255.0;
                sumaDesvio2 += desvio * desvio;
            }
        }

        // RMS del apartamiento de la tinta vs el fondo — independiente de la cobertura.
        var rms = tinta > 0 ? Math.Sqrt(sumaDesvio2 / tinta) : 0;

        // Varianza del Laplaciano de 4 vecinos (interior de la imagen).
        double sumaL = 0, sumaL2 = 0;
        long n = 0;
        for (var y = 1; y < h - 1; y++)
        for (var x = 1; x < w - 1; x++)
        {
            var c = lum[y * w + x];
            var lap = 4 * c - lum[(y - 1) * w + x] - lum[(y + 1) * w + x] - lum[y * w + x - 1] - lum[y * w + x + 1];
            sumaL += lap;
            sumaL2 += lap * lap;
            n++;
        }
        var lapVar = n > 0 ? (sumaL2 / n) - (sumaL / n) * (sumaL / n) : 0;

        return new Metricas((double)transparentes / (w * h), rms, lapVar);
    }

    // ── normalización (Q-05 / ADR-009) ───────────────────────────────────────

    private static byte[] Normalizar(Image<Rgba32> original, SignatureSpec spec)
    {
        // Encajar SIN deformar dentro del lienzo estándar; padding transparente, centrada.
        var escala = Math.Min((double)spec.TargetWidthPx / original.Width,
                              (double)spec.TargetHeightPx / original.Height);
        var w = Math.Max(1, (int)Math.Round(original.Width * escala));
        var h = Math.Max(1, (int)Math.Round(original.Height * escala));

        using var lienzo = new Image<Rgba32>(spec.TargetWidthPx, spec.TargetHeightPx); // transparente
        using var reducida = original.Clone(ctx => ctx.Resize(w, h));
        var offsetX = (spec.TargetWidthPx - w) / 2;
        var offsetY = (spec.TargetHeightPx - h) / 2;
        lienzo.Mutate(ctx => ctx.DrawImage(reducida, new Point(offsetX, offsetY), 1f));

        using var ms = new MemoryStream();
        lienzo.Save(ms, new PngEncoder
        {
            // Contrato del doc 04 §4: RGBA 8-bit NO entrelazado (lo que el motor PDF espera).
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
            InterlaceMethod = PngInterlaceMode.None,
        });
        return ms.ToArray();
    }

    private static ResultadoDeFirma Rechazo(string motivo)
        => new(false, new[] { motivo }, "{}", null);
}

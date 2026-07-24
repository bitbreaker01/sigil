// M8 — Normalización de firma: umbrales de alfa / contraste RMS /
// varianza de Laplaciano con imágenes SINTÉTICAS límite; salida PNG RGBA 8-bit no
// entrelazado. Las imágenes se fabrican en memoria (sin binarios en el repo), con el
// mismo patrón de trazos del spike que validó ImageSharp en el sandbox.

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Sigil.Plugins.Core.Imaging;

namespace Sigil.Plugins.Core.Tests.Imaging;

public class MotorDeFirmaMaestraTests
{
    private static readonly SignatureSpec Spec = SignatureSpec.Parse(
        """{ "targetWidthPx": 600, "targetHeightPx": 200, "maxKB": 150, "minAlphaRatio": 0.15, "minRmsContrast": 0.25, "minLaplacianVar": 80 }""");

    // ── el spec se parsea del env JSON ───────────────────────────

    [Fact]
    public void Spec_SeParsea_DelJsonCanonicoDelDoc04()
    {
        Assert.Equal(600, Spec.TargetWidthPx);
        Assert.Equal(200, Spec.TargetHeightPx);
        Assert.Equal(150, Spec.MaxKB);
        Assert.Equal(0.15, Spec.MinAlphaRatio);
        Assert.Equal(0.25, Spec.MinRmsContrast);
        Assert.Equal(80, Spec.MinLaplacianVar);
    }

    [Fact]
    public void Spec_JsonInvalido_Lanza()
    {
        Assert.ThrowsAny<Exception>(() => SignatureSpec.Parse("no es json"));
    }

    // ── M8: los tres umbrales con imágenes límite ────────────────

    [Fact]
    public void M8_FirmaValida_PasaLosTresUmbrales()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaValida(), Spec);

        Assert.True(r.EsValida, string.Join("; ", r.Motivos));
        Assert.NotNull(r.PngNormalizado);
        Assert.NotNull(r.MetricsJson);
    }

    [Fact] // sin fondo transparente (foto de firma sobre papel) → rechazo con motivo
    public void M8_SinTransparencia_EsRechazada_PorAlfa()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaOpacaSobreBlanco(), Spec);

        Assert.False(r.EsValida);
        Assert.Contains(r.Motivos, m => m.Contains("transparen", StringComparison.OrdinalIgnoreCase));
        Assert.Null(r.PngNormalizado); // jamás se normaliza una rechazada
    }

    [Fact] // trazos casi blancos → contraste RMS bajo el umbral
    public void M8_BajoContraste_EsRechazada_PorContraste()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaDeBajoContraste(), Spec);

        Assert.False(r.EsValida);
        Assert.Contains(r.Motivos, m => m.Contains("contraste", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // rampas suaves sin bordes → varianza de Laplaciano bajo el umbral (borrosa)
    public void M8_Borrosa_EsRechazada_PorNitidez()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaBorrosa(), Spec);

        Assert.False(r.EsValida);
        Assert.Contains(r.Motivos, m => m.Contains("nitidez", StringComparison.OrdinalIgnoreCase) ||
                                        m.Contains("borrosa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // no-PNG (JPEG) → rechazo por formato (PNG decodificable)
    public void M8_FormatoNoPng_EsRechazado()
    {
        using var img = new Image<Rgba32>(100, 50);
        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);

        var r = MotorDeFirmaMaestra.Procesar(ms.ToArray(), Spec);

        Assert.False(r.EsValida);
        Assert.Contains(r.Motivos, m => m.Contains("PNG", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M8_BytesQueNoSonImagen_EsRechazado_SinExcepcion()
    {
        var r = MotorDeFirmaMaestra.Procesar([1, 2, 3, 4, 5], Spec);
        Assert.False(r.EsValida);
        Assert.NotEmpty(r.Motivos);
    }

    [Fact] // C1 antagonista — bomba de descompresión: dimensiones sobre el techo → veredicto, no OOM
    public void M8_DimensionesSobreElTecho_SonRechazadas_SinDecodificarPixeles()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaValida(ancho: MotorDeFirmaMaestra.MaxDimensionPx + 1, alto: 10), Spec);
        Assert.False(r.EsValida);
        Assert.Contains(r.Motivos, m => m.Contains("grande", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // PNG entrelazado (Adam7) como INPUT: se acepta — la salida normalizada igual es no-entrelazada
    public void M8_InputEntrelazado_SeAcepta_YLaSalidaEsNoEntrelazada()
    {
        byte[] entrelazado;
        using (var img = SixLabors.ImageSharp.Image.Load<Rgba32>(FirmaValida()))
        using (var ms = new MemoryStream())
        {
            img.Save(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, InterlaceMethod = PngInterlaceMode.Adam7 });
            entrelazado = ms.ToArray();
        }

        var r = MotorDeFirmaMaestra.Procesar(entrelazado, Spec);

        Assert.True(r.EsValida, string.Join("; ", r.Motivos));
        using var n = SixLabors.ImageSharp.Image.Load<Rgba32>(r.PngNormalizado!);
        Assert.Equal(PngInterlaceMode.None, n.Metadata.GetPngMetadata().InterlaceMethod);
    }

    // ── casos CERCA del umbral (el comparador es <, no <=; un off-by-epsilon se vería acá) ──

    [Theory] // alphaRatio exactamente calculable: N píxeles transparentes sobre 10.000
    [InlineData(1400, false)] // 0.14 < 0.15 → rechazo
    [InlineData(1600, true)]  // 0.16 ≥ 0.15 → pasa (las otras métricas también pasan: tinta negra nítida)
    public void M8_AlphaRatio_CercaDelUmbral_DecideExacto(int pixelesTransparentes, bool esperado)
    {
        var png = Png(100, 100, img =>
        {
            Rellenar(img, new Rgba32(0, 0, 0, 255)); // todo tinta negra (contraste y nitidez altos)
            var n = 0;
            for (var y = 0; y < 100 && n < pixelesTransparentes; y++)
            for (var x = 0; x < 100 && n < pixelesTransparentes; x++)
            {
                // tablero disperso: bordes duros por todos lados → Laplaciano alto
                if ((x + y) % 2 == 0) { img[x, y] = new Rgba32(0, 0, 0, 0); n++; }
            }
        });

        var r = MotorDeFirmaMaestra.Procesar(png, Spec);

        if (esperado)
            Assert.True(r.EsValida, string.Join("; ", r.Motivos));
        else
            Assert.Contains(r.Motivos, m => m.Contains("transparen", StringComparison.OrdinalIgnoreCase));
    }

    // ── normalización (Q-05): contrato de salida exacto ────────────

    [Fact]
    public void M8_Normalizacion_EmiteExactamenteLasDimensionesEstandar_RgbaNoEntrelazado()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaValida(ancho: 1200, alto: 400), Spec);
        Assert.True(r.EsValida, string.Join("; ", r.Motivos));

        using var normalizada = Image.Load<Rgba32>(r.PngNormalizado!, out var formato);
        Assert.Equal("PNG", formato.Name.ToUpperInvariant());
        Assert.Equal(600, normalizada.Width);
        Assert.Equal(200, normalizada.Height);

        var meta = normalizada.Metadata.GetPngMetadata();
        Assert.Equal(PngColorType.RgbWithAlpha, meta.ColorType);
        Assert.Equal(PngBitDepth.Bit8, meta.BitDepth);
        Assert.Equal(PngInterlaceMode.None, meta.InterlaceMethod);

        Assert.True(r.PngNormalizado!.Length <= Spec.MaxKB * 1024,
            $"la normalizada pesa {r.PngNormalizado.Length} bytes > {Spec.MaxKB} KB");
    }

    [Fact] // aspecto distinto al 3:1 del target: encaja SIN deformar, centrada, padding transparente
    public void M8_Normalizacion_PreservaElAspecto_ConPaddingTransparente()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaValida(ancho: 400, alto: 400), Spec); // 1:1
        Assert.True(r.EsValida, string.Join("; ", r.Motivos));

        using var n = Image.Load<Rgba32>(r.PngNormalizado!);
        Assert.Equal(600, n.Width);
        Assert.Equal(200, n.Height);
        // la imagen 1:1 escalada a 200x200 queda centrada → las esquinas laterales son transparentes
        Assert.Equal(0, n[5, 100].A);
        Assert.Equal(0, n[594, 100].A);
    }

    [Fact] // el MetricsJson es parseable y trae las tres métricas (auditoría)
    public void M8_MetricsJson_TraeLasTresMetricas()
    {
        var r = MotorDeFirmaMaestra.Procesar(FirmaValida(), Spec);

        using var doc = System.Text.Json.JsonDocument.Parse(r.MetricsJson);
        Assert.True(doc.RootElement.TryGetProperty("alphaRatio", out _));
        Assert.True(doc.RootElement.TryGetProperty("rmsContrast", out _));
        Assert.True(doc.RootElement.TryGetProperty("laplacianVariance", out _));
    }

    // ── fixtures sintéticas (mismo patrón de trazos del spike de sandbox) ───

    private static byte[] FirmaValida(int ancho = 600, int alto = 200)
        => Png(ancho, alto, (img) => TrazosDiagonales(img, new Rgba32(0, 0, 0, 255)));

    private static byte[] FirmaOpacaSobreBlanco()
        => Png(600, 200, (img) =>
        {
            Rellenar(img, new Rgba32(255, 255, 255, 255)); // papel: TODO opaco
            TrazosDiagonales(img, new Rgba32(0, 0, 0, 255));
        });

    private static byte[] FirmaDeBajoContraste()
        => Png(600, 200, (img) => TrazosDiagonales(img, new Rgba32(246, 246, 246, 255))); // casi blanco

    private static byte[] FirmaBorrosa()
        => Png(600, 200, (img) =>
        {
            // rampas suaves en AMBOS ejes (sin ningún borde duro — también el alfa se
            // desvanece gradualmente): el Laplaciano queda ~0 en toda la imagen
            for (var x = 0; x < img.Width; x++)
            {
                var t = (double)x / img.Width;
                var gris = (byte)(255 - 200 * Math.Sin(t * Math.PI)); // 255→55→255, suave
                for (var y = 30; y < img.Height - 30; y++)
                {
                    // fade vertical del alfa en 50 px hacia cada banda transparente
                    var haciaBorde = Math.Min(y - 30, img.Height - 30 - y);
                    var alfa = (byte)Math.Min(255, haciaBorde * 255 / 50);
                    img[x, y] = new Rgba32(gris, gris, gris, alfa);
                }
            }
        });

    private static byte[] Png(int ancho, int alto, Action<Image<Rgba32>> dibujar)
    {
        using var img = new Image<Rgba32>(ancho, alto); // (0,0,0,0) = transparente
        dibujar(img);
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
            InterlaceMethod = PngInterlaceMode.None,
        });
        return ms.ToArray();
    }

    private static void Rellenar(Image<Rgba32> img, Rgba32 color)
    {
        for (var y = 0; y < img.Height; y++)
        for (var x = 0; x < img.Width; x++)
            img[x, y] = color;
    }

    private static void TrazosDiagonales(Image<Rgba32> img, Rgba32 color)
    {
        // trazos gruesos en diagonal (bordes duros → alta varianza de Laplaciano)
        for (var i = 0; i < 5; i++)
        {
            double x0 = 30 + i * (img.Width - 90) / 5.0, y0 = img.Height - 30;
            double x1 = x0 + 70, y1 = 30;
            var pasos = (int)(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2) + 1;
            for (var s = 0; s <= pasos; s++)
            {
                var t = (double)s / pasos;
                var cx = x0 + (x1 - x0) * t;
                var cy = y0 + (y1 - y0) * t;
                for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                {
                    int px = (int)(cx + dx), py = (int)(cy + dy);
                    if (px >= 0 && px < img.Width && py >= 0 && py < img.Height && dx * dx + dy * dy <= 16)
                        img[px, py] = color;
                }
            }
        }
    }
}

// Composición del documento final (ADR-011, doc 04 §6/§7 pasos 3-5): incrustar las firmas
// en sus zonas, agregar la hoja de cierre (con overflow), escribir los metadatos del
// documento y serializar UNA SOLA VEZ (los bytes van a hash_final, TSA y upload).
//
// CORRECCIÓN DE DISEÑO (2026-07-16): el doc 04 §6.2/§7 paso 4 pedía imprimir el "número
// de ledger" en la hoja/metadatos, pero el ledger (autonumber) nace en el paso 8 — DESPUÉS
// del upload (orden mandatorio por idempotencia, §7). El número no puede conocerse al
// componer. La hoja imprime hash_contenido + URL de verificación + txId (que el QR ya
// codifica); el número de ledger lo muestra la verificación (VerifyDocument). Enmienda en doc 04.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sigil.Plugins.Core.Pdf;

public sealed class FirmaAIncrustar
{
    public FirmaAIncrustar(byte[] png, IReadOnlyList<(int Page, double X, double Y, double W, double H)> zonas)
    {
        Png = png;
        Zonas = zonas;
    }

    public byte[] Png { get; }
    public IReadOnlyList<(int Page, double X, double Y, double W, double H)> Zonas { get; }
}

public sealed class FirmanteEnHoja
{
    public FirmanteEnHoja(string nombre, string email, DateTime firmadoEnUtc, byte[] snapshotPng)
    {
        Nombre = nombre;
        Email = email;
        FirmadoEnUtc = firmadoEnUtc;
        SnapshotPng = snapshotPng;
    }

    public string Nombre { get; }
    public string Email { get; }
    public DateTime FirmadoEnUtc { get; }
    public byte[] SnapshotPng { get; }
}

public static class ComposicionDeDocumento
{
    /// <summary>Firmantes por página de hoja de cierre — con más, overflow a páginas adicionales (ADR-011).</summary>
    public const int FirmantesPorHoja = 6;

    private static readonly object CandadoDeFuente = new();
    private static bool _fuenteConfigurada;

    /// <summary>
    /// Pipeline puro de composición: bytes del contenido aprobado → bytes del documento final.
    /// UNA serialización (doc 04 §7 paso 5): el llamante calcula hash_final sobre el retorno.
    /// </summary>
    public static byte[] ComponerDocumentoFinal(
        byte[] contenidoPdf,
        IReadOnlyList<FirmaAIncrustar> firmas,
        IReadOnlyList<FirmanteEnHoja> firmantes,
        string hashContenido,
        string urlDeVerificacion,
        Guid transactionId)
    {
        ConfigurarFuenteEmbebida();

        using var ms = new MemoryStream(contenidoPdf, 0, contenidoPdf.Length, writable: false, publiclyVisible: true);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

        // Paso 3 — incrustar cada firma en SUS zonas (contrato de coordenadas §6.1).
        // El contenido ORIGINAL de cada página tocada se envuelve en q/Q UNA vez: un PDF
        // real puede terminar con la CTM alterada o estado gráfico sin balancear, y la
        // firma aterrizaría en cualquier lado (antagonista A8 — patrón estándar de stamping).
        var paginasEnvueltas = new HashSet<int>();
        var n = 0;
        foreach (var firma in firmas)
        {
            var (w, h, rgb, alpha) = DecodificarPng(firma.Png);
            foreach (var z in firma.Zonas)
            {
                var page = doc.Pages[z.Page - 1];
                if (paginasEnvueltas.Add(z.Page))
                {
                    page.Contents.PrependContent().CreateStream(System.Text.Encoding.ASCII.GetBytes("q\n"));
                    page.Contents.AppendContent().CreateStream(System.Text.Encoding.ASCII.GetBytes("\nQ\n"));
                }
                var m = TransformacionDeCoordenadas.ParaZona(page, z.X, z.Y, z.W, z.H);
                XObjectManual.DibujarImagenRgba(doc, page, $"SigFm{++n}", w, h, rgb, alpha, m);
            }
        }

        // Paso 4a — hoja de cierre consolidada, con overflow (§6.2).
        AgregarHojasDeCierre(doc, firmantes, hashContenido, urlDeVerificacion, transactionId);

        // Paso 4b — metadatos del documento (visibles en las propiedades — enmienda: sin nº de ledger).
        doc.Info.Title = "Documento firmado con Sigil";
        doc.Info.Subject = $"Verificación: {urlDeVerificacion}";
        doc.Info.Keywords = $"Sigil; SHA-256:{hashContenido}; txId:{transactionId}";
        doc.Info.Creator = "Sigil";

        using var salida = new MemoryStream();
        doc.Save(salida, closeStream: false); // ÚNICA serialización
        return salida.ToArray();
    }

    private static void AgregarHojasDeCierre(
        PdfDocument doc, IReadOnlyList<FirmanteEnHoja> firmantes,
        string hashContenido, string url, Guid txId)
    {
        var paginas = Math.Max(1, (int)Math.Ceiling(firmantes.Count / (double)FirmantesPorHoja));
        for (var p = 0; p < paginas; p++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);

            var lote = firmantes.Skip(p * FirmantesPorHoja).Take(FirmantesPorHoja).ToList();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                var titulo = new XFont("Segoe WP", 16, XFontStyleEx.Bold);
                var normal = new XFont("Segoe WP", 9);
                var chica = new XFont("Segoe WP", 7);

                gfx.DrawString("Firmado con Sigil", titulo, XBrushes.Black, new XPoint(50, 50));
                gfx.DrawString($"Hoja de firmas {p + 1} de {paginas} — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
                    chica, XBrushes.Gray, new XPoint(50, 66));

                double y = 100;
                foreach (var f in lote)
                {
                    gfx.DrawString(f.Nombre, normal, XBrushes.Black, new XPoint(230, y + 22));
                    gfx.DrawString(f.Email, chica, XBrushes.Gray, new XPoint(230, y + 36));
                    gfx.DrawString($"Firmó: {f.FirmadoEnUtc:yyyy-MM-dd HH:mm:ss} UTC", chica, XBrushes.Gray,
                        new XPoint(230, y + 50));
                    gfx.DrawLine(XPens.LightGray, 50, y + 78, 562, y + 78);
                    y += 90;
                }

                // Pie probatorio: hash + verificación (SOLO en la última hoja).
                if (p == paginas - 1)
                {
                    gfx.DrawString("SHA-256 del documento aprobado (hash_contenido):", chica, XBrushes.Black,
                        new XPoint(50, 700));
                    gfx.DrawString(hashContenido, chica, XBrushes.Black, new XPoint(50, 712));
                    gfx.DrawString($"Verificación: {url}?screen=verify&txId={txId}", chica, XBrushes.Black,
                        new XPoint(50, 726));
                    gfx.DrawString($"Identificador: {txId}", chica, XBrushes.Gray, new XPoint(50, 738));
                }
            }

            // Estampas (imagen del SNAPSHOT congelado) — XObject manual, después del XGraphics
            // para no pelear por el content stream. Posición en puntos (página carta, sin rotación).
            double ey = 100;
            var idx = 0;
            foreach (var f in lote)
            {
                var (w, h, rgb, alpha) = DecodificarPng(f.SnapshotPng);
                // caja 160×54 pt manteniendo el aspecto del snapshot
                var escala = Math.Min(160.0 / w, 54.0 / h);
                double dw = w * escala, dh = h * escala;
                var m = new MatrizCm(dw, 0, 0, dh, 50, 792 - ey - 60);
                XObjectManual.DibujarImagenRgba(doc, page, $"SigSt{p}_{idx++}", w, h, rgb, alpha, m);
                ey += 90;
            }

            // QR de verificación (§6.2) — solo en la última hoja, junto al pie.
            if (p == paginas - 1)
            {
                var qr = QrPng($"{url}?screen=verify&txId={txId}");
                var (qw, qh, qrgb, qalpha) = DecodificarPng(qr);
                XObjectManual.DibujarImagenRgba(doc, page, $"SigQr{p}", qw, qh, qrgb, qalpha,
                    new MatrizCm(100, 0, 0, 100, 462, 40));
            }
        }
    }

    private static byte[] QrPng(string contenido)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data); // jamás el renderer QRCode (System.Drawing — prohibido)
        return png.GetGraphic(10);
    }

    private static (int W, int H, byte[] Rgb, byte[] Alpha) DecodificarPng(byte[] png)
    {
        using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(png);
        int w = img.Width, h = img.Height;
        var rgb = new byte[w * h * 3];
        var alpha = new byte[w * h];
        var i = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var px = img[x, y];
            rgb[i * 3] = px.R;
            rgb[i * 3 + 1] = px.G;
            rgb[i * 3 + 2] = px.B;
            alpha[i] = px.A;
            i++;
        }
        return (w, h, rgb, alpha);
    }

    /// <summary>
    /// Fuente EMBEBIDA (PdfSharp.WPFonts — Segoe WP): el sandbox no tiene filesystem ni
    /// fuentes del sistema; el resolver sirve los bytes desde el assembly. Idempotente.
    /// </summary>
    public static void ConfigurarFuenteEmbebida()
    {
        if (_fuenteConfigurada) return;
        lock (CandadoDeFuente)
        {
            if (_fuenteConfigurada) return;
            GlobalFontSettings.FontResolver ??= new FuenteEmbebidaResolver();
            _fuenteConfigurada = true;
        }
    }

    private sealed class FuenteEmbebidaResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
            => new(bold ? "SegoeWP#b" : "SegoeWP#r");

        public byte[]? GetFont(string faceName) => faceName switch
        {
            "SegoeWP#b" => PdfSharp.WPFonts.FontDataHelper.SegoeWPBold,
            _ => PdfSharp.WPFonts.FontDataHelper.SegoeWP,
        };
    }
}

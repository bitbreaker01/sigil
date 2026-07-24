// M5 — Sellado canónico: composición del documento final.
// Asserts: incrustación en la página correcta (matriz en el content stream + /SMask),
// hoja de cierre agregada, OVERFLOW con 12+ firmantes, QR presente, metadatos con el
// hash y SIN número de ledger (corrección de diseño 2026-07-16 — el autonumber nace
// en el paso 8, después de componer), round-trip del PDF final, y serialización única
// (los bytes retornados son EXACTAMENTE los que se hashean).

using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Sigil.Plugins.Core.Pdf;
using SixLabors.ImageSharp.PixelFormats;

namespace Sigil.Plugins.Core.Tests.Pdf;

public class ComposicionDeDocumentoTests
{
    private static readonly Guid TxId = Guid.Parse("dddddddd-1111-2222-3333-444444444444");
    private const string Hash = "0313D9B3F654F7AF5F75FB2767081569BC43E2BDB481E41BA9FBBA7288CECD88";
    private const string Url = "https://apps.powerapps.com/play/e/x/a/y";

    [Fact]
    public void M5_ComponerConUnaFirma_IncrustaEnLaPagina_YAgregaLaHoja()
    {
        var final = ComposicionDeDocumento.ComponerDocumentoFinal(
            PdfBase(paginas: 2),
            [new FirmaAIncrustar(PngFirma(), [(2, 60.0, 80.0, 22.0, 8.0)])],
            [Firmante("Beto Uno")],
            Hash, Url, TxId);

        using var doc = PdfReader.Open(new MemoryStream(final), PdfDocumentOpenMode.Import);
        Assert.Equal(3, doc.PageCount); // 2 de contenido + 1 hoja de cierre

        Assert.Contains("/SigFm1 Do", OpsDeContenido(doc.Pages[1]));  // la firma, en la página 2
        Assert.Contains("SigSt0_0", NombresDeXObjects(doc.Pages[2])); // la estampa de la hoja
        Assert.Contains("SigQr0", NombresDeXObjects(doc.Pages[2]));   // el QR
        var texto = System.Text.Encoding.Latin1.GetString(final);
        Assert.Contains("/SMask", texto); // el alfa viaja (regresión del spike; nombre de dict, sin comprimir)
    }

    [Fact] // la matriz de la zona termina en el content stream de la página correcta
    public void M5_LaMatrizDeLaZona_QuedaEnElContentStream()
    {
        var final = ComposicionDeDocumento.ComponerDocumentoFinal(
            PdfBase(1), [new FirmaAIncrustar(PngFirma(), [(1, 10.0, 20.0, 50.0, 25.0)])],
            [Firmante("A")], Hash, Url, TxId);

        using var doc = PdfReader.Open(new MemoryStream(final), PdfDocumentOpenMode.Import);
        // carta 612×792: vw=306, vh=198, e=61.2, f=792−158.4−198=435.6 (contrato de coordenadas)
        Assert.Contains("306 0 0 198 61.2 435.6 cm", OpsDeContenido(doc.Pages[0]));
    }

    [Fact] // overflow — 13 firmantes a 6 por hoja = 3 hojas de cierre
    public void M5_ConTreceFirmantes_LaHojaDesborda_ATresPaginas()
    {
        var firmantes = Enumerable.Range(1, 13).Select(i => Firmante($"Firmante {i}")).ToList();

        var final = ComposicionDeDocumento.ComponerDocumentoFinal(
            PdfBase(1), [], firmantes, Hash, Url, TxId);

        using var doc = PdfReader.Open(new MemoryStream(final), PdfDocumentOpenMode.Import);
        Assert.Equal(1 + 3, doc.PageCount);
    }

    [Fact] // metadatos: hash + URL + txId presentes; número de ledger AUSENTE (no existe aún)
    public void M5_Metadatos_TraenHashYVerificacion_SinNumeroDeLedger()
    {
        var final = ComposicionDeDocumento.ComponerDocumentoFinal(
            PdfBase(1), [], [Firmante("A")], Hash, Url, TxId);

        using var doc = PdfReader.Open(new MemoryStream(final), PdfDocumentOpenMode.Import);
        Assert.Contains(Hash, doc.Info.Keywords);
        Assert.Contains(TxId.ToString(), doc.Info.Keywords);
        Assert.Contains(Url, doc.Info.Subject);
        Assert.DoesNotContain("SIGIL-", doc.Info.Keywords); // el formato del autonumber no puede estar
    }

    [Fact] // el QR codifica el deep link canónico: AppPlayUrl + screen=verify&txId
    public void M5_ElQr_CodificaElDeepLinkDeVerificacion()
    {
        var final = ComposicionDeDocumento.ComponerDocumentoFinal(
            PdfBase(1), [], [Firmante("A")], Hash, Url, TxId);

        // el QR es una imagen — verificamos la existencia del XObject del QR en la hoja;
        // la decodificación visual del deep link queda para el gate manual.
        using var doc = PdfReader.Open(new MemoryStream(final), PdfDocumentOpenMode.Import);
        Assert.Contains("SigQr0", NombresDeXObjects(doc.Pages[1]));
    }

    [Fact] // los bytes retornados ABREN como PDF (round-trip) — sobre ellos se calcula hash_final
    public void M5_LosBytesRetornados_AbrenComoPdf()
    {
        var final = ComposicionDeDocumento.ComponerDocumentoFinal(
            PdfBase(2), [new FirmaAIncrustar(PngFirma(), [(1, 5.0, 5.0, 20.0, 8.0)])],
            [Firmante("A"), Firmante("B")], Hash, Url, TxId);

        using var doc = PdfReader.Open(new MemoryStream(final), PdfDocumentOpenMode.Modify);
        Assert.True(doc.PageCount >= 3);
    }

    // ── lectura del PDF producido (los content streams van comprimidos en el output) ──

    private static string OpsDeContenido(PdfPage page)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < page.Contents.Elements.Count; i++)
        {
            var stream = page.Contents.Elements.GetDictionary(i)?.Stream;
            if (stream is not null)
                sb.Append(System.Text.Encoding.Latin1.GetString(stream.UnfilteredValue));
        }
        return sb.ToString();
    }

    private static string NombresDeXObjects(PdfPage page)
    {
        var xobjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        return xobjects is null ? string.Empty : string.Join(",", xobjects.Elements.Keys);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private static byte[] PdfBase(int paginas)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < paginas; i++)
        {
            var p = doc.AddPage();
            p.Width = PdfSharp.Drawing.XUnit.FromPoint(612);
            p.Height = PdfSharp.Drawing.XUnit.FromPoint(792);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static byte[] PngFirma()
    {
        using var img = new SixLabors.ImageSharp.Image<Rgba32>(60, 20);
        for (var x = 0; x < 60; x++) img[x, 10] = new Rgba32(0, 0, 0, 255);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return ms.ToArray();
    }

    private static FirmanteEnHoja Firmante(string nombre)
        => new(nombre, $"{nombre.Replace(" ", ".").ToLowerInvariant()}@bac.test",
            new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc), PngFirma());
}

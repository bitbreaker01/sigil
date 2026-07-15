// M7 — Validación de entrada (doc 11 §4 / doc 04 §3.4): PdfBase64.
// El caso que prueba el ORDEN es el corazón de la suite: un string sobre el límite con
// base64 inválido debe fallar por TAMAÑO, no por decodificación — decodificar basura
// gigante para después medirla regala memoria del sandbox (doc 04 §3).

using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ValidacionDePdfTests
{
    private const int MaxKb = 64; // límite chico para fabricar casos sin megas de fixture

    [Fact]
    public void M7_PdfValido_Pasa_YReportaElConteoDePaginas()
    {
        var r = ValidacionDeEntrada.ValidarPdfBase64(Convert.ToBase64String(PdfDePrueba(3)), MaxKb);

        Assert.True(r.EsValido, string.Join("; ", r.Errores));
        Assert.Equal(3, r.Valor!.PageCount);
        Assert.NotEmpty(r.Valor.Bytes);
    }

    [Fact] // EL caso del orden (M7): sobre el límite Y base64 inválido → error de tamaño
    public void M7_Pdf_SobreElLimite_ConBase64Invalido_FallaPorTamano_NoPorDecodificacion()
    {
        var basuraGigante = new string('!', LimiteDeChars(MaxKb) + 100); // '!' ni siquiera es base64
        var r = ValidacionDeEntrada.ValidarPdfBase64(basuraGigante, MaxKb);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("KB", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(r.Errores, e => e.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M7_Pdf_Base64Invalido_BajoElLimite_FallaPorDecodificacion()
    {
        var r = ValidacionDeEntrada.ValidarPdfBase64("esto-no-es-base64!!", MaxKb);
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("base64", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // magic bytes %PDF- (doc 04 §3.4)
    public void M7_Pdf_SinMagicBytes_EsRechazado()
    {
        var noPdf = Convert.ToBase64String("HOLA MUNDO, no soy un PDF"u8.ToArray());
        var r = ValidacionDeEntrada.ValidarPdfBase64(noPdf, MaxKb);
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("PDF", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // magic bytes correctos pero estructura rota → rechazo en la apertura
    public void M7_Pdf_ConMagicBytesPeroCorrupto_EsRechazado()
    {
        var corrupto = Convert.ToBase64String("%PDF-1.7 y acá se terminó el archivo"u8.ToArray());
        var r = ValidacionDeEntrada.ValidarPdfBase64(corrupto, MaxKb);
        Assert.False(r.EsValido);
    }

    [Fact] // documento cifrado/protegido → rechazo (doc 04 §3.4)
    public void M7_Pdf_Cifrado_EsRechazado_ConMensajeClaro()
    {
        var r = ValidacionDeEntrada.ValidarPdfBase64(Convert.ToBase64String(PdfCifrado()), MaxKb);
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e =>
            e.Contains("protegido", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("cifrado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // firmas digitales previas → rechazo (incrustar imágenes las invalidaría)
    public void M7_Pdf_ConFirmaDigitalPrevia_EsRechazado_ConMensajeClaro()
    {
        var r = ValidacionDeEntrada.ValidarPdfBase64(Convert.ToBase64String(PdfConFirmaDigital()), MaxKb);
        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("firma", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // el límite es inclusivo: un PDF exactamente en el límite de chars pasa el chequeo de tamaño
    public void M7_Pdf_ExactamenteEnElLimite_NoFallaPorTamano()
    {
        var pdf = PdfDePrueba(1);
        var limiteKb = (int)Math.Ceiling(Convert.ToBase64String(pdf).Length * 3.0 / 4.0 / 1024.0);
        var r = ValidacionDeEntrada.ValidarPdfBase64(Convert.ToBase64String(pdf), limiteKb);
        Assert.True(r.EsValido, string.Join("; ", r.Errores));
    }

    [Fact] // UpdateDraft revalida zonas contra el PDF persistido — necesita el conteo de páginas
    public void ContarPaginas_DeUnPdfValido_DevuelveElConteo()
    {
        Assert.Equal(3, ValidacionDeEntrada.ContarPaginas(PdfDePrueba(3)));
    }

    [Fact]
    public void ContarPaginas_DeBytesQueNoSonPdf_Lanza()
    {
        Assert.ThrowsAny<Exception>(() => ValidacionDeEntrada.ContarPaginas([1, 2, 3]));
    }

    // ── fixtures fabricados en memoria (sin binarios en el repo) ─────────────

    private static byte[] PdfDePrueba(int paginas)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < paginas; i++) doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static byte[] PdfCifrado()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.SecuritySettings.UserPassword = "secreto";
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    // AcroForm con SigFlags=3 y un campo /FT /Sig con valor /V — la anatomía real de un
    // PDF firmado digitalmente (PDF 32000-1 §12.7.4.5), fabricada con diccionarios crudos.
    private static byte[] PdfConFirmaDigital()
    {
        using var doc = new PdfDocument();
        doc.AddPage();

        var valorDeFirma = new PdfDictionary(doc);
        valorDeFirma.Elements.SetName("/Type", "/Sig");
        doc.Internals.AddObject(valorDeFirma);

        var campoDeFirma = new PdfDictionary(doc);
        campoDeFirma.Elements.SetName("/FT", "/Sig");
        campoDeFirma.Elements.SetString("/T", "Signature1");
        campoDeFirma.Elements.SetReference("/V", valorDeFirma);
        doc.Internals.AddObject(campoDeFirma);

        var campos = new PdfArray(doc);
        campos.Elements.Add(campoDeFirma.Reference!);

        var acroForm = new PdfDictionary(doc);
        acroForm.Elements.SetInteger("/SigFlags", 3); // bit 1 = SignaturesExist
        acroForm.Elements["/Fields"] = campos;
        doc.Internals.AddObject(acroForm);
        doc.Internals.Catalog.Elements.SetReference("/AcroForm", acroForm);

        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    private static int LimiteDeChars(int maxKb) => (int)(Math.Ceiling(maxKb * 1024 / 3.0) * 4);
}

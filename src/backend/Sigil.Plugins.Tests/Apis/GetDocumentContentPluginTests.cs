// sanic_sigil_capi_GetDocumentContent — orquestación (doc 04 §3.1/§3.3).
// Las filas de M1 de esta API: ajeno rechazado; participante NO lee borradores no
// enviados; final solo en Completado. El happy path devuelve los bytes exactos en base64.

using System;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class GetDocumentContentPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;
    private readonly Guid _firmante;

    public GetDocumentContentPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        _firmante = _arnes.SembrarUsuario("Beto Firmante", "beto@bac.test");
    }

    private void Ejecutar(Guid txId, Guid llamante, string documentType)
    {
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        _arnes.Contexto.InputParameters["DocumentType"] = documentType;
        _arnes.Ejecutar(new GetDocumentContentPlugin(), SchemaNames.Apis.GetDocumentContent, llamante);
    }

    [Fact] // M1 — ni creador ni participante: afuera
    public void M1_UnAjeno_EsRechazado()
    {
        var ajeno = _arnes.SembrarUsuario("Ajeno", "ajeno@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Completado);

        Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, ajeno, "content"));
    }

    [Fact] // M1 — la fila explícita: "participante lee borrador no enviado"
    public void M1_ParticipanteEnBorrador_EsRechazado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.SembrarParticipante(txId, _firmante);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _firmante, "content"));
        Assert.Contains("enviado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // M1 — final solo en Completado
    public void M1_Final_FueraDeCompletado_EsRechazado_InclusoParaElCreador()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Sellando);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador, "final"));
        Assert.Contains("Completad", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DocumentTypeInvalido_EsRechazado_ConElContratoEnElMensaje()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador, "borrador"));
        Assert.Contains("content", ex.Message);
        Assert.Contains("final", ex.Message);
    }

    [Fact]
    public void Feliz_ElCreadorLeeSuBorrador_YRecibeLosBytesExactos()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        var pdf = ArnesDeApi.PdfDePrueba(1);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, pdf);

        Ejecutar(txId, _creador, "content");

        Assert.Equal(Convert.ToBase64String(pdf), _arnes.Contexto.OutputParameters["PdfBase64"]);
    }

    [Fact]
    public void Feliz_UnParticipante_LeeElFinal_EnCompletado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Completado);
        _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.Firmado);
        var pdfFinal = ArnesDeApi.PdfDePrueba(2);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.FinalFile, pdfFinal);

        Ejecutar(txId, _firmante, "final");

        Assert.Equal(Convert.ToBase64String(pdfFinal), _arnes.Contexto.OutputParameters["PdfBase64"]);
    }

    [Fact] // el contenido lo puede leer el participante desde Pendiente de Firma (doc 04 §3.3)
    public void Feliz_UnParticipante_LeeElContenido_TrasElEnvio()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma);
        _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.TurnoActivo);
        var pdf = ArnesDeApi.PdfDePrueba(1);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, pdf);

        Ejecutar(txId, _firmante, "content");

        Assert.Equal(Convert.ToBase64String(pdf), _arnes.Contexto.OutputParameters["PdfBase64"]);
    }
}

// M1 — Autorización negativa (doc 11 §4): un test negativo por cada fila de la tabla
// de autorización del doc 04 §3.3, más los positivos que delimitan la regla.
// Regla general del doc 04: "cada validación de autorización que falte es una escalada
// de privilegios de facto" — estas reglas son puras (Domain/) y la cáscara solo las invoca.

using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ReglasDeAutorizacionTests
{
    private static readonly Guid Creador = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Participante = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Ajeno = Guid.Parse("99999999-9999-9999-9999-999999999999");

    // ── UpdateDraft / DeleteDraft: creador + estado Borrador (doc 04 §3.3) ──

    [Fact]
    public void M1_EdicionDeBorrador_ElCreadorEnBorrador_EstaAutorizado()
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarEdicionDeBorrador(
            llamante: Creador, creador: Creador, estado: TransactionStatus.Borrador);
        Assert.Null(motivo);
    }

    [Fact]
    public void M1_EdicionDeBorrador_UnNoCreador_EsRechazado()
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarEdicionDeBorrador(
            llamante: Ajeno, creador: Creador, estado: TransactionStatus.Borrador);
        Assert.NotNull(motivo);
        Assert.Contains("creador", motivo, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // toda transacción que ya no es borrador es intocable por estas APIs
    [InlineData(TransactionStatus.PendienteDeFirma)]
    [InlineData(TransactionStatus.FirmadoParcialmente)]
    [InlineData(TransactionStatus.Sellando)]
    [InlineData(TransactionStatus.Completado)]
    [InlineData(TransactionStatus.Rechazado)]
    [InlineData(TransactionStatus.Expirado)]
    [InlineData(TransactionStatus.ErrorDeSellado)]
    [InlineData(TransactionStatus.Cancelado)]
    public void M1_EdicionDeBorrador_EnEstadoNoBorrador_EsRechazada_AunSiendoElCreador(TransactionStatus estado)
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarEdicionDeBorrador(
            llamante: Creador, creador: Creador, estado: estado);
        Assert.NotNull(motivo);
        Assert.Contains("borrador", motivo, StringComparison.OrdinalIgnoreCase);
    }

    // ── GetDocumentContent (doc 04 §3.3): creador O participante; final solo en
    //    Completado; content para participantes solo desde Pendiente de Firma ──

    [Fact]
    public void M1_LecturaDeDocumento_UnAjeno_EsRechazado_EnTodoCaso()
    {
        foreach (var tipo in new[] { DocumentType.Content, DocumentType.Final })
        {
            var motivo = ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
                tipo, llamante: Ajeno, creador: Creador, esParticipante: false,
                estado: TransactionStatus.Completado);
            Assert.NotNull(motivo);
        }
    }

    [Fact] // la fila explícita de M1: "participante lee borrador no enviado"
    public void M1_LecturaDeContenido_ParticipanteEnBorrador_EsRechazado()
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            DocumentType.Content, llamante: Participante, creador: Creador,
            esParticipante: true, estado: TransactionStatus.Borrador);
        Assert.NotNull(motivo);
    }

    [Fact] // el creador SIEMPRE puede leer su propio contenido (incluso en borrador)
    public void M1_LecturaDeContenido_CreadorEnBorrador_EstaAutorizado()
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            DocumentType.Content, llamante: Creador, creador: Creador,
            esParticipante: false, estado: TransactionStatus.Borrador);
        Assert.Null(motivo);
    }

    [Theory] // "content para participantes solo desde Pendiente de Firma en adelante"
    [InlineData(TransactionStatus.PendienteDeFirma)]
    [InlineData(TransactionStatus.FirmadoParcialmente)]
    [InlineData(TransactionStatus.Sellando)]
    [InlineData(TransactionStatus.Completado)]
    [InlineData(TransactionStatus.Rechazado)]
    [InlineData(TransactionStatus.Expirado)]
    [InlineData(TransactionStatus.ErrorDeSellado)]
    [InlineData(TransactionStatus.Cancelado)]
    public void M1_LecturaDeContenido_ParticipanteConDocumentoYaPresentado_EstaAutorizado(TransactionStatus estado)
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            DocumentType.Content, llamante: Participante, creador: Creador,
            esParticipante: true, estado: estado);
        Assert.Null(motivo);
    }

    [Theory] // final SOLO en Completado — antes no existe un final legítimo que servir
    [InlineData(TransactionStatus.Borrador)]
    [InlineData(TransactionStatus.PendienteDeFirma)]
    [InlineData(TransactionStatus.FirmadoParcialmente)]
    [InlineData(TransactionStatus.Sellando)]
    [InlineData(TransactionStatus.Rechazado)]
    [InlineData(TransactionStatus.Expirado)]
    [InlineData(TransactionStatus.ErrorDeSellado)]
    [InlineData(TransactionStatus.Cancelado)]
    public void M1_LecturaDeFinal_FueraDeCompletado_EsRechazada_InclusoParaElCreador(TransactionStatus estado)
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            DocumentType.Final, llamante: Creador, creador: Creador,
            esParticipante: false, estado: estado);
        Assert.NotNull(motivo);
        Assert.Contains("completad", motivo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void M1_LecturaDeFinal_EnCompletado_CreadorYParticipante_Autorizados()
    {
        Assert.Null(ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            DocumentType.Final, llamante: Creador, creador: Creador,
            esParticipante: false, estado: TransactionStatus.Completado));
        Assert.Null(ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            DocumentType.Final, llamante: Participante, creador: Creador,
            esParticipante: true, estado: TransactionStatus.Completado));
    }
}

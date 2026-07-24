// Reglas de autorización de negocio — funciones PURAS: la cáscara net462
// resuelve identidades y estado contra Dataverse y delega acá la decisión.
// Contrato: null = autorizado; string = motivo accionable del rechazo (va al
// InvalidPluginExecutionException del plugin, jamás silencioso).

using System;

namespace Sigil.Plugins.Core.Domain;

/// <summary>Tipo de documento que sirve sanic_sigil_capi_GetDocumentContent (contrato: "content" | "final").</summary>
public enum DocumentType
{
    Content,
    Final,
}

public static class ReglasDeAutorizacion
{
    /// <summary>
    /// UpdateDraft / DeleteDraft (y a futuro SendTransaction): el llamante es el creador
    /// (owner) Y el estado es Borrador.
    /// </summary>
    public static string? MotivoParaRechazarEdicionDeBorrador(Guid llamante, Guid creador, TransactionStatus estado)
    {
        if (llamante != creador)
            return "Solo el creador de la transacción puede modificar o borrar el borrador.";
        if (estado != TransactionStatus.Borrador)
            return "La transacción ya no es un borrador.";
        return null;
    }

    /// <summary>
    /// CancelTransaction (T13): el llamante es el creador y el estado es
    /// Pendiente de Firma, Firmado Parcialmente o Error de Sellado — jamás Sellando
    /// (el pipeline está trabajando) ni terminales.
    /// </summary>
    public static string? MotivoParaRechazarCancelacion(Guid llamante, Guid creador, TransactionStatus estado)
    {
        if (llamante != creador)
            return "Solo el creador de la transacción puede cancelarla.";
        return estado is TransactionStatus.PendienteDeFirma
            or TransactionStatus.FirmadoParcialmente
            or TransactionStatus.ErrorDeSellado
            ? null
            : "La transacción no se puede cancelar en su estado actual.";
    }

    /// <summary>
    /// SubmitSignature / RejectTransaction: el llamante es participante de
    /// ESA transacción con estado Turno Activo, y la transacción está en un estado firmable.
    /// Un Pendiente en secuencial no puede accionar: aún no le llegó el documento.
    /// </summary>
    public static string? MotivoParaRechazarAccionDeFirmante(
        bool esParticipante, ParticipantStatus? estadoDelParticipante, TransactionStatus estadoDeTx)
    {
        if (!esParticipante)
            return "No sos participante de esta transacción.";
        if (estadoDeTx is not (TransactionStatus.PendienteDeFirma or TransactionStatus.FirmadoParcialmente))
            return "La transacción ya no admite firmas ni rechazos en su estado actual.";
        if (estadoDelParticipante != ParticipantStatus.TurnoActivo)
            return "No es tu turno de firma (o ya accionaste sobre esta transacción).";
        return null;
    }

    /// <summary>
    /// GetDocumentContent: creador O participante; "final" solo en Completado;
    /// "content" para participantes solo desde Pendiente de Firma en adelante — la existencia
    /// del registro de participante NO implica que el documento ya le fue presentado.
    /// </summary>
    public static string? MotivoParaRechazarLecturaDeDocumento(
        DocumentType tipo, Guid llamante, Guid creador, bool esParticipante, TransactionStatus estado)
    {
        var esCreador = llamante == creador;
        if (!esCreador && !esParticipante)
            return "Solo el creador o un participante de la transacción pueden leer sus documentos.";

        if (tipo == DocumentType.Final)
            return estado == TransactionStatus.Completado
                ? null
                : "El documento final solo existe cuando la transacción está Completada.";

        // DocumentType.Content: el creador siempre; el participante, solo si ya se envió.
        if (!esCreador && estado == TransactionStatus.Borrador)
            return "El documento aún no fue enviado a firma.";
        return null;
    }
}

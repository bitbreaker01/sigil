// Reglas de autorización de negocio (doc 04 §3.3) — funciones PURAS: la cáscara net462
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
    /// (owner) Y el estado es Borrador. Doc 04 §3.3 fila 1.
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
    /// GetDocumentContent (doc 04 §3.3): creador O participante; "final" solo en Completado;
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

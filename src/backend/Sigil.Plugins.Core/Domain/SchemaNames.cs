// Nombres de schema de Dataverse (doc 12 §5: las constantes viven en Domain/ y usan los
// nombres completos sanic_sigil_*). La EXISTENCIA de cada artefacto la garantiza la suite
// de conformidad (CF-A04/A16/A17) — acá solo se centraliza el string para que un typo sea
// un error de compilación y no un bug silencioso.

namespace Sigil.Plugins.Core.Domain;

public static class SchemaNames
{
    /// <summary>sanic_sigil_tbl_transaction (doc 03 §4.1).</summary>
    public static class Tx
    {
        public const string Entidad = "sanic_sigil_tbl_transaction";
        public const string Name = "sanic_sigil_name";
        public const string Status = "sanic_sigil_status";
        public const string RoutingType = "sanic_sigil_routingtype";
        public const string Message = "sanic_sigil_message";
        public const string ExpirationDays = "sanic_sigil_expirationdays";
        public const string LockToken = "sanic_sigil_locktoken";
        public const string ContentFile = "sanic_sigil_contentfile";
        public const string ContentHash = "sanic_sigil_contenthash";
        public const string FinalFile = "sanic_sigil_finalfile";
        public const string OwnerId = "ownerid";
    }

    /// <summary>sanic_sigil_tbl_participant (doc 03 §4.2).</summary>
    public static class Participante
    {
        public const string Entidad = "sanic_sigil_tbl_participant";
        public const string Name = "sanic_sigil_name";
        public const string TransactionId = "sanic_sigil_transactionid";
        public const string UserId = "sanic_sigil_userid";
        public const string Order = "sanic_sigil_order";
        public const string Status = "sanic_sigil_status";
        public const string OwnerId = "ownerid";
    }

    /// <summary>sanic_sigil_tbl_signaturezone (doc 03 §4.3).</summary>
    public static class Zona
    {
        public const string Entidad = "sanic_sigil_tbl_signaturezone";
        public const string Name = "sanic_sigil_name";
        public const string ParticipantId = "sanic_sigil_participantid";
        public const string Page = "sanic_sigil_page";
        public const string PosX = "sanic_sigil_posx";
        public const string PosY = "sanic_sigil_posy";
        public const string Width = "sanic_sigil_width";
        public const string Height = "sanic_sigil_height";
        public const string OwnerId = "ownerid";
    }

    /// <summary>sanic_sigil_tbl_event (doc 03 §4.6).</summary>
    public static class Evento
    {
        public const string Entidad = "sanic_sigil_tbl_event";
        public const string Name = "sanic_sigil_name";
        public const string TransactionId = "sanic_sigil_transactionid";
        public const string Type = "sanic_sigil_type";
        public const string ActorName = "sanic_sigil_actorname";
        public const string ActorEmail = "sanic_sigil_actoremail";
        public const string ParticipantId = "sanic_sigil_participantid";
        public const string DocumentHash = "sanic_sigil_documenthash";
        public const string OccurredOn = "sanic_sigil_occurredon";
        public const string Details = "sanic_sigil_details";
        public const string OwnerId = "ownerid";
    }

    /// <summary>systemuser — columnas que Sigil consulta para snapshots (doc 03 §4.2).</summary>
    public static class Usuario
    {
        public const string Entidad = "systemuser";
        public const string Id = "systemuserid";
        public const string FullName = "fullname";
        public const string Email = "internalemailaddress";
        public const string IsDisabled = "isdisabled";
    }

    /// <summary>Custom APIs (doc 04 §3.1 / doc 12 §3).</summary>
    public static class Apis
    {
        public const string CreateTransaction = "sanic_sigil_capi_CreateTransaction";
        public const string UpdateDraft = "sanic_sigil_capi_UpdateDraft";
        public const string DeleteDraft = "sanic_sigil_capi_DeleteDraft";
        public const string GetDocumentContent = "sanic_sigil_capi_GetDocumentContent";
    }

    /// <summary>Variables de entorno (doc 03 §8).</summary>
    public static class EnvVars
    {
        public const string MaxPdfSizeKB = "sanic_sigil_env_MaxPdfSizeKB";
        public const string MaxParticipants = "sanic_sigil_env_MaxParticipants";
    }
}

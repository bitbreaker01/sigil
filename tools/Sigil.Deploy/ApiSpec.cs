// Especificación DECLARATIVA de las Custom APIs desplegadas (F2) — la fuente única de
// verdad del despliegue y el espejo EXACTO de doc 04 §3.1/§3.2 y de las pruebas CF-D
// (RunbookD_BackendTests). Si esto y CF-D divergen, CF-D queda rojo — por diseño.

namespace Sigil.Deploy;

// Tipos de parámetro/propiedad de Custom API (customapirequestparameter.type /
// customapiresponseproperty.type). Solo los que usamos; valores del option set de la plataforma.
internal static class ParamType
{
    public const int Boolean = 0;
    public const int DateTime = 1;
    public const int Integer = 7;
    public const int String = 10;
    public const int Guid = 12;
}

internal static class Binding
{
    public const int Global = 0;
    public const int Entity = 1;
}

internal sealed record RequestParam(string Name, int Type, bool Optional);

internal sealed record ResponseProp(string Name, int Type);

internal sealed record CustomApiSpec(
    string UniqueName,
    string DisplayName,
    string Description,
    int BindingType,
    string? BoundEntityLogicalName,
    string PluginTypeName,
    RequestParam[] RequestParams,
    ResponseProp[] ResponseProps,
    string? ExecutePrivilege = null) // null = privilegio de usuario (Catalogo.UserPrivilege)
{
    public string PrivilegioEfectivo => ExecutePrivilege ?? Catalogo.UserPrivilege;
}

internal static class Catalogo
{
    public const string PackageName = "sanic_Sigil";
    public const string SolutionName = "sigil_core_sigil";
    public const string TxTable = "sanic_sigil_tbl_transaction";

    // Privilegio de nivel usuario (doc 04 §3.2): lo tiene el rol Sigil | SR | User.
    public const string UserPrivilege = "prvReadsanic_sigil_tbl_transaction";

    // Privilegio de SERVICIO (doc 04 §3.2): solo el rol Sigil | SR | Service lo posee —
    // un usuario común NO puede invocar los jobs aunque conozca su firma.
    public const string ServicePrivilege = "prvWritesanic_sigil_tbl_ledgerentry";

    // El worker de sellado (step asíncrono — no es Custom API).
    public const string WorkerPluginType = "Sigil.Plugins.Apis.SealingWorkerPlugin";
    public const string WorkerStepName = "Sigil | Step | SealingWorker on Update of transaction";

    // Valores de env vars que el CÓDIGO DESPLEGADO HOY lee (doc 04 §3.4). Derivados de los docs:
    //   MaxPdfSizeKB 20480 = 20 MB (doc 04 §7 dimensiona PDFs de ~20 MB);
    //   MaxParticipants 20 (doc 04 §3.4, default);
    //   ExpirationDefaultDays 7 = valor de DEV (doc 09 §6 manda plazos CORTOS en Dev para
    //   probar expiración rápido; el valor de negocio de Test/Prod se fija por ambiente).
    //   El resto de la config por-ambiente se setea cuando su consumidor se despliega.
    public static readonly (string Schema, string Valor)[] EnvValues =
    {
        ("sanic_sigil_env_MaxPdfSizeKB", "20480"),
        ("sanic_sigil_env_MaxParticipants", "20"),
        ("sanic_sigil_env_ExpirationDefaultDays", "7"),
        // JSON canónico del doc 04 §4 (umbrales iniciales de ADR-009 — calibrables por ambiente)
        ("sanic_sigil_env_SignatureImageSpec",
            """{ "targetWidthPx": 600, "targetHeightPx": 200, "maxKB": 150, "minAlphaRatio": 0.15, "minRmsContrast": 0.25, "minLaplacianVar": 80 }"""),
        // TSA en Dev: HABILITADA con Sectigo primero (el spike probó que DigiCert está
        // bloqueada desde la red del sandbox — orden por ambiente, ADR-005). En Prod el
        // orden de negocio se fija por ambiente.
        ("sanic_sigil_env_TsaEnabled", "yes"),
        ("sanic_sigil_env_TsaEndpoints",
            """{ "endpoints": [ { "url": "https://timestamp.sectigo.com", "timeoutSeconds": 10, "minIntervalSeconds": 15 }, { "url": "https://timestamp.digicert.com", "timeoutSeconds": 10, "minIntervalSeconds": 0 } ] }"""),
        // Placeholder de Dev: la URL real nace con el primer `pac code push` (doc 09 §6) —
        // ACTUALIZAR entonces. Los QR de documentos sellados en Dev apuntan acá hasta eso.
        ("sanic_sigil_env_AppPlayUrl", "https://apps.powerapps.com/play/e/dev-pendiente/a/dev-pendiente"),
        // Dev: cadencia CORTA para probar recordatorios rápido (doc 09 §6); Test/Prod = negocio.
        ("sanic_sigil_env_ReminderCadenceDays", "2"),
        ("sanic_sigil_env_DefaultLanguage", "es"),
    };

    public static readonly CustomApiSpec[] Apis =
    {
        new(
            UniqueName: "sanic_sigil_capi_CreateTransaction",
            DisplayName: "Sigil | CAPI | CreateTransaction",
            Description: "Crea el borrador de una transacción de firma (RF-25/26).",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.CreateTransactionPlugin",
            RequestParams: new[]
            {
                new RequestParam("Name", ParamType.String, Optional: false),
                new RequestParam("Message", ParamType.String, Optional: true),
                new RequestParam("RoutingType", ParamType.String, Optional: false),
                new RequestParam("ExpirationDays", ParamType.Integer, Optional: true),
                new RequestParam("PdfBase64", ParamType.String, Optional: false),
                new RequestParam("ParticipantsJson", ParamType.String, Optional: false),
                new RequestParam("ZonesJson", ParamType.String, Optional: true),
            },
            ResponseProps: new[] { new ResponseProp("TransactionId", ParamType.Guid) }),

        new(
            UniqueName: "sanic_sigil_capi_UpdateDraft",
            DisplayName: "Sigil | CAPI | UpdateDraft",
            Description: "Edita un borrador; todos los campos opcionales (null = sin cambio).",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.UpdateDraftPlugin",
            RequestParams: new[]
            {
                new RequestParam("Name", ParamType.String, Optional: true),
                new RequestParam("Message", ParamType.String, Optional: true),
                new RequestParam("RoutingType", ParamType.String, Optional: true),
                new RequestParam("ExpirationDays", ParamType.Integer, Optional: true),
                new RequestParam("PdfBase64", ParamType.String, Optional: true),
                new RequestParam("ParticipantsJson", ParamType.String, Optional: true),
                new RequestParam("ZonesJson", ParamType.String, Optional: true),
            },
            ResponseProps: Array.Empty<ResponseProp>()),

        new(
            UniqueName: "sanic_sigil_capi_DeleteDraft",
            DisplayName: "Sigil | CAPI | DeleteDraft",
            Description: "Borra un borrador (eventos primero — T3).",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.DeleteDraftPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: Array.Empty<ResponseProp>()),

        new(
            UniqueName: "sanic_sigil_capi_GetDocumentContent",
            DisplayName: "Sigil | CAPI | GetDocumentContent",
            Description: "Devuelve el PDF de contenido o final en base64 (RF-03/05/24).",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.GetDocumentContentPlugin",
            RequestParams: new[] { new RequestParam("DocumentType", ParamType.String, Optional: false) },
            ResponseProps: new[] { new ResponseProp("PdfBase64", ParamType.String) }),

        new(
            UniqueName: "sanic_sigil_capi_SendTransaction",
            DisplayName: "Sigil | CAPI | SendTransaction",
            Description: "Borrador → Pendiente de Firma (T4): valida zonas RF-28, ancla contenthash, comparte y activa turnos.",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.SendTransactionPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: Array.Empty<ResponseProp>()),

        new(
            UniqueName: "sanic_sigil_capi_SubmitSignature",
            DisplayName: "Sigil | CAPI | SubmitSignature",
            Description: "Registra la intención de firma (RF-04) con snapshot de la Firma Maestra vigente.",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.SubmitSignaturePlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: new[] { new ResponseProp("IsLastSigner", ParamType.Boolean) }),

        new(
            UniqueName: "sanic_sigil_capi_RejectTransaction",
            DisplayName: "Sigil | CAPI | RejectTransaction",
            Description: "Rechazo por un participante en Turno Activo (RF-13) — motivo obligatorio.",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.RejectTransactionPlugin",
            RequestParams: new[] { new RequestParam("Reason", ParamType.String, Optional: false) },
            ResponseProps: Array.Empty<ResponseProp>()),

        new(
            UniqueName: "sanic_sigil_capi_CancelTransaction",
            DisplayName: "Sigil | CAPI | CancelTransaction",
            Description: "Cancelación por el creador (RF-30) — Pendiente de Firma, Firmado Parcialmente o Error de Sellado.",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.CancelTransactionPlugin",
            RequestParams: new[] { new RequestParam("Reason", ParamType.String, Optional: true) },
            ResponseProps: Array.Empty<ResponseProp>()),

        new(
            UniqueName: "sanic_sigil_capi_ValidateMasterSignature",
            DisplayName: "Sigil | CAPI | ValidateMasterSignature",
            Description: "Valida (ADR-009: alfa/contraste/nitidez, cómputo local) y normaliza la Firma Maestra. Con Persist=true crea la nueva versión vigente; sin él solo valida (preview antes de confirmar).",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.ValidateMasterSignaturePlugin",
            RequestParams: new[]
            {
                new RequestParam("ImageBase64", ParamType.String, Optional: false),
                new RequestParam("Persist", ParamType.Boolean, Optional: true),
            },
            ResponseProps: new[]
            {
                new ResponseProp("IsValid", ParamType.Boolean),
                new ResponseProp("FailureReasons", ParamType.String),
                new ResponseProp("MetricsJson", ParamType.String),
                new ResponseProp("NormalizedImageBase64", ParamType.String),
            }),

        new(
            UniqueName: "sanic_sigil_capi_RetrySealing",
            DisplayName: "Sigil | CAPI | RetrySealing",
            Description: "Error de Sellado → Sellando (T10): re-dispara el worker idempotente.",
            BindingType: Binding.Entity,
            BoundEntityLogicalName: TxTable,
            PluginTypeName: "Sigil.Plugins.Apis.RetrySealingPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: Array.Empty<ResponseProp>()),

        new(
            UniqueName: "sanic_sigil_capi_GetMasterSignature",
            DisplayName: "Sigil | CAPI | GetMasterSignature",
            Description: "Devuelve el PNG normalizado de la Firma Maestra vigente del llamante (RF-01).",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.GetMasterSignaturePlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: new[]
            {
                new ResponseProp("ImageBase64", ParamType.String),
                new ResponseProp("ValidatedOn", ParamType.DateTime),
            }),

        new(
            UniqueName: "sanic_sigil_capi_GetMasterSignatureHistory",
            DisplayName: "Sigil | CAPI | GetMasterSignatureHistory",
            Description: "Historial de versiones de la Firma Maestra del llamante (versionado inmutable, doc 03 §4.5). Out: HistoryJson.",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.GetMasterSignatureHistoryPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: new[]
            {
                new ResponseProp("HistoryJson", ParamType.String),
            }),

        new(
            UniqueName: "sanic_sigil_capi_SearchDocuments",
            DisplayName: "Sigil | CAPI | SearchDocuments",
            Description: "Búsqueda paginada de documentos del llamante (creados ∪ participados) con filtros (texto, creador, estado, participante, versión de firma), orden y paginación server-side. Out: ResultsJson, Total, NextPagingCookie.",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.SearchDocumentsPlugin",
            RequestParams: new[]
            {
                new RequestParam("Text", ParamType.String, Optional: true),
                new RequestParam("CreatorId", ParamType.Guid, Optional: true),
                new RequestParam("Status", ParamType.Integer, Optional: true),
                new RequestParam("ParticipantIds", ParamType.String, Optional: true), // CSV de GUIDs (AND)
                new RequestParam("SignatureVersion", ParamType.Integer, Optional: true),
                new RequestParam("Sort", ParamType.String, Optional: true),
                new RequestParam("PageSize", ParamType.Integer, Optional: true),
                new RequestParam("PagingCookie", ParamType.String, Optional: true),
            },
            ResponseProps: new[]
            {
                new ResponseProp("ResultsJson", ParamType.String),
                new ResponseProp("Total", ParamType.Integer),
                new ResponseProp("NextPagingCookie", ParamType.String),
            }),

        new(
            UniqueName: "sanic_sigil_capi_VerifyDocument",
            DisplayName: "Sigil | CAPI | VerifyDocument",
            Description: "Verificación (RF-20/21, ADR-007): constancia + veredicto contra finalhash + verificación cruzada del historial.",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.VerifyDocumentPlugin",
            RequestParams: new[]
            {
                // Ambos opcionales, pero al menos uno es obligatorio (lo valida el plugin): TransactionId
                // (QR / Detail) o Sha256Hash solo (búsqueda por hash en el ledger — RF-20/21).
                new RequestParam("TransactionId", ParamType.Guid, Optional: true),
                new RequestParam("Sha256Hash", ParamType.String, Optional: true),
            },
            ResponseProps: new[]
            {
                new ResponseProp("Found", ParamType.Boolean),
                new ResponseProp("IsIntact", ParamType.Boolean),
                new ResponseProp("MetadataJson", ParamType.String),
                new ResponseProp("TsaTokenBase64", ParamType.String),
            }),

        new(
            UniqueName: "sanic_sigil_capi_ExpireTransactions",
            DisplayName: "Sigil | CAPI | ExpireTransactions",
            Description: "Job diario (RF-27): expira vencidas (T12) + saneamiento de Sellando zombi (T14).",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.ExpireTransactionsPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: new[]
            {
                new ResponseProp("ExpiredCount", ParamType.Integer),
                new ResponseProp("SanitizedCount", ParamType.Integer),
            },
            ExecutePrivilege: ServicePrivilege),

        new(
            UniqueName: "sanic_sigil_capi_ProcessReminders",
            DisplayName: "Sigil | CAPI | ProcessReminders",
            Description: "Job diario (RF-12): recordatorios por cadencia — RemindersJson autosuficiente para el flow.",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.ProcessRemindersPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: new[] { new ResponseProp("RemindersJson", ParamType.String) },
            ExecutePrivilege: ServicePrivilege),

        new(
            UniqueName: "sanic_sigil_capi_ResealPending",
            DisplayName: "Sigil | CAPI | ResealPending",
            Description: "Job diario (ADR-005): reintenta TSA sobre ledgers pendientes; con TSA off los mueve a Sin sello.",
            BindingType: Binding.Global,
            BoundEntityLogicalName: null,
            PluginTypeName: "Sigil.Plugins.Apis.ResealPendingPlugin",
            RequestParams: Array.Empty<RequestParam>(),
            ResponseProps: new[]
            {
                new ResponseProp("ResealedCount", ParamType.Integer),
                new ResponseProp("MovedToNoTsaCount", ParamType.Integer),
                new ResponseProp("StillPendingCount", ParamType.Integer),
                new ResponseProp("AnchorMismatchCount", ParamType.Integer),
            },
            ExecutePrivilege: ServicePrivilege),
    };
}

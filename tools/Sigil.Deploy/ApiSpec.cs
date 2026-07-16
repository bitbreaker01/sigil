// Especificación DECLARATIVA de las 4 Custom APIs de la Fase 2 — es la fuente única de
// verdad del despliegue y el espejo EXACTO de doc 04 §3.1/§3.2 y de las pruebas CF-D
// (RunbookD_BackendTests). Si esto y CF-D divergen, CF-D queda rojo — por diseño.

namespace Sigil.Deploy;

// Tipos de parámetro/propiedad de Custom API (customapirequestparameter.type /
// customapiresponseproperty.type). Solo los que usamos; valores del option set de la plataforma.
internal static class ParamType
{
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
    ResponseProp[] ResponseProps);

internal static class Catalogo
{
    public const string PackageName = "sanic_Sigil";
    public const string SolutionName = "sigil_core_sigil";
    public const string TxTable = "sanic_sigil_tbl_transaction";

    // Privilegio de nivel usuario (doc 04 §3.2): lo tiene el rol Sigil | SR | User.
    public const string UserPrivilege = "prvReadsanic_sigil_tbl_transaction";

    // Valores de env vars que el CÓDIGO DESPLEGADO HOY lee (doc 04 §3.4). Derivados de los docs:
    //   MaxPdfSizeKB 20480 = 20 MB (doc 04 §7 dimensiona PDFs de ~20 MB);
    //   MaxParticipants 20 (doc 04 §3.4, default). El resto de la config por-ambiente se setea
    //   cuando su consumidor se despliega (doc 09 §6). En Test/Prod: por pipeline, no por acá.
    public static readonly (string Schema, string Valor)[] EnvValues =
    {
        ("sanic_sigil_env_MaxPdfSizeKB", "20480"),
        ("sanic_sigil_env_MaxParticipants", "20"),
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
    };
}

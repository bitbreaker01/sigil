// Runner del spike sandbox — registro puro por SDK (sin pac CLI, sin herramientas Windows):
//   1. Conecta con AuthType=ClientSecret (mismo patrón que DataverseFixture.cs de conformidad).
//   2. Upsert del pluginpackage 'sanic_SigilSpike' (content = base64 del nupkg).
//   3. Espera el pluginassembly + plugintype auto-creados.
//   4. Crea (si falta) la Custom API sanic_sigil_capi_RunSpike + response property Result (String=10).
//   5. Ejecuta la API y muestra el JSON de resultado.
//   6. Trae el plugintracelog más reciente del tipo (timings del sandbox).

using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

const string PackageName = "sanic_SigilSpike";
const string PluginTypeName = "SigilSpike.Plugin.RunSpikePlugin";
const string ApiUniqueName = "sanic_sigil_capi_RunSpike";

string? pathArg = args.FirstOrDefault(a => !a.StartsWith("--"));
string nupkgPath = pathArg ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../SigilSpike.Plugin/bin/Release/sanic_SigilSpike.1.0.0.nupkg"));

if (!args.Contains("--grant") && !File.Exists(nupkgPath))
{
    Console.Error.WriteLine($"[FATAL] nupkg no encontrado: {nupkgPath}");
    return 1;
}

var url = Environment.GetEnvironmentVariable("SIGIL_DATAVERSE_URL")
    ?? throw new InvalidOperationException("SIGIL_DATAVERSE_URL no configurada");
var clientId = Environment.GetEnvironmentVariable("SIGIL_CLIENT_ID")
    ?? throw new InvalidOperationException("SIGIL_CLIENT_ID no configurada");
var clientSecret = Environment.GetEnvironmentVariable("SIGIL_CLIENT_SECRET")
    ?? throw new InvalidOperationException("SIGIL_CLIENT_SECRET no configurada");

// Secret entre comillas simples: un ';' o '=' en el valor no corrompe el parse.
var connectionString =
    $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true";

Console.WriteLine($"[1] Conectando a {url} ...");
using var client = new ServiceClient(connectionString);
if (!client.IsReady)
{
    Console.Error.WriteLine($"[FATAL] Conexión no establecida: {client.LastError}");
    return 1;
}
Console.WriteLine($"[1] Conectado. Org: {client.ConnectedOrgFriendlyName} ({client.ConnectedOrgId})");

// Habilitar plugin trace log (2 = All) para leer el stack completo de fallos del sandbox.
var orgRow = client.RetrieveMultiple(new QueryExpression("organization") { ColumnSet = new ColumnSet("plugintracelogsetting") }).Entities[0];
if (orgRow.GetAttributeValue<OptionSetValue>("plugintracelogsetting")?.Value != 2)
{
    client.Update(new Entity("organization", orgRow.Id) { ["plugintracelogsetting"] = new OptionSetValue(2) });
    Console.WriteLine("[1b] Plugin trace log habilitado (All).");
}

if (args.Contains("--grant"))
{
    return await GrantPrivilegesAsync(client);
}

// ---------------------------------------------------------------------------
// 2. Upsert pluginpackage
// ---------------------------------------------------------------------------
byte[] nupkgBytes = File.ReadAllBytes(nupkgPath);
string contentB64 = Convert.ToBase64String(nupkgBytes);
Console.WriteLine($"[2] nupkg: {nupkgPath} ({nupkgBytes.Length:N0} bytes)");

var pkgQuery = new QueryExpression("pluginpackage")
{
    ColumnSet = new ColumnSet("pluginpackageid", "name", "version"),
    Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, PackageName) } },
};
var existingPkgs = client.RetrieveMultiple(pkgQuery);

Guid packageId;
if (existingPkgs.Entities.Count > 0)
{
    packageId = existingPkgs.Entities[0].Id;
    Console.WriteLine($"[2] pluginpackage existente ({packageId}) — actualizando content (re-deploy).");
    var upd = new Entity("pluginpackage", packageId)
    {
        ["content"] = contentB64,
        ["version"] = "1.0.0",
    };
    client.Update(upd);
}
else
{
    var pkg = new Entity("pluginpackage")
    {
        ["name"] = PackageName,
        ["version"] = "1.0.0",
        ["content"] = contentB64,
    };
    packageId = client.Create(pkg);
    Console.WriteLine($"[2] pluginpackage creado: {packageId}");
}

// ---------------------------------------------------------------------------
// 3. Esperar pluginassembly + plugintype auto-creados (propagación hasta ~90 s)
// ---------------------------------------------------------------------------
Console.WriteLine($"[3] Esperando plugintype '{PluginTypeName}' ...");
Entity? pluginType = null;
var deadline = DateTime.UtcNow.AddSeconds(90);
while (DateTime.UtcNow < deadline)
{
    var typeQuery = new QueryExpression("plugintype")
    {
        ColumnSet = new ColumnSet("plugintypeid", "typename", "pluginassemblyid"),
        Criteria = { Conditions = { new ConditionExpression("typename", ConditionOperator.Equal, PluginTypeName) } },
    };
    var types = client.RetrieveMultiple(typeQuery);
    if (types.Entities.Count > 0)
    {
        pluginType = types.Entities[0];
        break;
    }
    Console.WriteLine("    ... aún no aparece, reintento en 5 s");
    await Task.Delay(TimeSpan.FromSeconds(5));
}

if (pluginType is null)
{
    Console.Error.WriteLine("[FATAL] plugintype no apareció tras 90 s. ¿Falló el procesamiento del package?");
    return 1;
}
var assemblyRef = (EntityReference)pluginType["pluginassemblyid"];
Console.WriteLine($"[3] plugintype: {pluginType.Id} | pluginassembly: {assemblyRef.Id} ({assemblyRef.Name})");

// ---------------------------------------------------------------------------
// 4. Custom API + response property (crear si faltan)
// ---------------------------------------------------------------------------
var apiQuery = new QueryExpression("customapi")
{
    ColumnSet = new ColumnSet("customapiid", "uniquename"),
    Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, ApiUniqueName) } },
};
var apis = client.RetrieveMultiple(apiQuery);

Guid apiId;
if (apis.Entities.Count > 0)
{
    apiId = apis.Entities[0].Id;
    Console.WriteLine($"[4] customapi existente: {apiId}");
}
else
{
    var api = new Entity("customapi")
    {
        ["uniquename"] = ApiUniqueName,
        ["name"] = "Sigil | CAPI | RunSpike",
        ["displayname"] = "Sigil | CAPI | RunSpike",
        ["description"] = "Spike: stack criptográfico completo + alcanzabilidad TSA dentro del sandbox.",
        ["bindingtype"] = new OptionSetValue(0),          // Global
        ["isfunction"] = false,
        ["isprivate"] = true,
        ["allowedcustomprocessingsteptype"] = new OptionSetValue(0), // None
        ["plugintypeid"] = new EntityReference("plugintype", pluginType.Id),
    };
    apiId = client.Create(api);
    Console.WriteLine($"[4] customapi creada: {apiId}");

    var respProp = new Entity("customapiresponseproperty")
    {
        ["customapiid"] = new EntityReference("customapi", apiId),
        ["uniquename"] = "Result",
        ["name"] = "Result",
        ["displayname"] = "Result",
        ["description"] = "JSON con los resultados por paso del spike.",
        ["type"] = new OptionSetValue(10),                // String
    };
    var respPropId = client.Create(respProp);
    Console.WriteLine($"[4] customapiresponseproperty 'Result' creada: {respPropId}");
}

// ---------------------------------------------------------------------------
// 5. Ejecutar la Custom API
// ---------------------------------------------------------------------------
Console.WriteLine($"[5] Ejecutando {ApiUniqueName} (esto corre el stack DENTRO del sandbox; TSA puede tardar) ...");
var sw = System.Diagnostics.Stopwatch.StartNew();
var request = new OrganizationRequest(ApiUniqueName);
OrganizationResponse response;
try
{
    response = client.Execute(request);
}
catch (Exception ex)
{
    sw.Stop();
    Console.Error.WriteLine($"[5] EJECUCIÓN FALLÓ tras {sw.ElapsedMilliseconds} ms: {ex.Message}");
    await DumpTraceLogAsync(client, PluginTypeName);
    return 2;
}
sw.Stop();
Console.WriteLine($"[5] Ejecución OK en {sw.ElapsedMilliseconds} ms. Outputs: {response.Results.Count}");

if (response.Results.TryGetValue("Result", out var resultObj) && resultObj is string resultJson)
{
    Console.WriteLine("[5] Result JSON (prettified):");
    try
    {
        using var doc = JsonDocument.Parse(resultJson);
        Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch
    {
        Console.WriteLine(resultJson);
    }
    Console.WriteLine();
    Console.WriteLine("[5] Result JSON (raw, para el reporte):");
    Console.WriteLine(resultJson);
}
else
{
    Console.WriteLine("[5] SIN output 'Result' — outputs recibidos: " +
        string.Join(", ", response.Results.Keys));
}

// ---------------------------------------------------------------------------
// 6. Plugin trace log más reciente del tipo
// ---------------------------------------------------------------------------
await DumpTraceLogAsync(client, PluginTypeName);

Console.WriteLine();
Console.WriteLine("=== IDs registrados (cleanup posterior) ===");
Console.WriteLine($"pluginpackage: {packageId}");
Console.WriteLine($"pluginassembly: {assemblyRef.Id}");
Console.WriteLine($"plugintype:    {pluginType.Id}");
Console.WriteLine($"customapi:     {apiId}");
return 0;

// Auto-elevación del spike: agrega al rol custom del SP los privilegios que faltan para
// registrar plugin packages y Custom APIs. Requiere que el SP pueda editar roles
// (System Customizer lo permite). Si Dataverse bloquea la escalada, se reporta el detalle.
static async Task<int> GrantPrivilegesAsync(ServiceClient client)
{
    var who = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)client.Execute(new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());
    Console.WriteLine($"[grant] UserId = {who.UserId}");

    // Roles del SP.
    var rolesQuery = new QueryExpression("role")
    {
        ColumnSet = new ColumnSet("roleid", "name", "ismanaged"),
        LinkEntities =
        {
            new LinkEntity("role", "systemuserroles", "roleid", "roleid", JoinOperator.Inner)
            {
                LinkCriteria = { Conditions = { new ConditionExpression("systemuserid", ConditionOperator.Equal, who.UserId) } },
            },
        },
    };
    var roles = client.RetrieveMultiple(rolesQuery);
    Console.WriteLine("[grant] Roles del SP:");
    foreach (var r in roles.Entities)
    {
        Console.WriteLine($"    - {r.GetAttributeValue<string>("name")} ({r.Id}, managed={r.GetAttributeValue<bool>("ismanaged")})");
    }

    var target = roles.Entities.FirstOrDefault(r => !r.GetAttributeValue<bool>("ismanaged")
            && !string.Equals(r.GetAttributeValue<string>("name"), "System Customizer", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(r.GetAttributeValue<string>("name"), "System Administrator", StringComparison.OrdinalIgnoreCase))
        ?? roles.Entities.FirstOrDefault(r => !r.GetAttributeValue<bool>("ismanaged"));
    if (target is null)
    {
        Console.Error.WriteLine("[grant] FATAL: el SP no tiene ningún rol editable (no managed).");
        return 3;
    }
    Console.WriteLine($"[grant] Rol objetivo: {target.GetAttributeValue<string>("name")} ({target.Id})");

    string[] wanted =
    {
        "prvCreatePluginAssembly", "prvReadPluginAssembly", "prvWritePluginAssembly", "prvDeletePluginAssembly",
        "prvCreatePluginType", "prvReadPluginType", "prvWritePluginType", "prvDeletePluginType",
        "prvCreatePluginPackage", "prvReadPluginPackage", "prvWritePluginPackage", "prvDeletePluginPackage",
        "prvCreateCustomAPI", "prvReadCustomAPI", "prvWriteCustomAPI", "prvDeleteCustomAPI",
        "prvCreateCustomAPIRequestParameter", "prvReadCustomAPIRequestParameter", "prvWriteCustomAPIRequestParameter", "prvDeleteCustomAPIRequestParameter",
        "prvCreateCustomAPIResponseProperty", "prvReadCustomAPIResponseProperty", "prvWriteCustomAPIResponseProperty", "prvDeleteCustomAPIResponseProperty",
        "prvCreateSdkMessageProcessingStep", "prvReadSdkMessageProcessingStep", "prvWriteSdkMessageProcessingStep", "prvDeleteSdkMessageProcessingStep",
        "prvReadPluginTraceLog",
    };

    // IDs de los privilegios que existen con esos nombres.
    var privQuery = new QueryExpression("privilege")
    {
        ColumnSet = new ColumnSet("privilegeid", "name"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, wanted.Cast<object>().ToArray()) } },
    };
    var privs = client.RetrieveMultiple(privQuery);
    Console.WriteLine($"[grant] Privilegios encontrados: {privs.Entities.Count}/{wanted.Length}");
    var foundNames = privs.Entities.Select(p => p.GetAttributeValue<string>("name")).ToHashSet();
    foreach (var missing in wanted.Where(w => !foundNames.Contains(w)))
    {
        Console.WriteLine($"    (no existe en este org: {missing})");
    }

    // Privilegios que el rol ya tiene.
    var current = (Microsoft.Crm.Sdk.Messages.RetrieveRolePrivilegesRoleResponse)client.Execute(
        new Microsoft.Crm.Sdk.Messages.RetrieveRolePrivilegesRoleRequest { RoleId = target.Id });
    var currentIds = current.RolePrivileges.Select(rp => rp.PrivilegeId).ToHashSet();

    var toAdd = privs.Entities
        .Where(p => !currentIds.Contains(p.Id))
        .Select(p => new Microsoft.Crm.Sdk.Messages.RolePrivilege
        {
            PrivilegeId = p.Id,
            Depth = Microsoft.Crm.Sdk.Messages.PrivilegeDepth.Global,
        })
        .ToArray();

    if (toAdd.Length == 0)
    {
        Console.WriteLine("[grant] El rol ya tiene todos los privilegios pedidos. Nada que hacer.");
        return 0;
    }

    Console.WriteLine($"[grant] Agregando {toAdd.Length} privilegios al rol ...");
    try
    {
        client.Execute(new Microsoft.Crm.Sdk.Messages.AddPrivilegesRoleRequest
        {
            RoleId = target.Id,
            Privileges = toAdd,
        });
        Console.WriteLine("[grant] OK — privilegios agregados.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[grant] FALLÓ la escalada (reportar al admin): {ex.Message}");
        return 3;
    }
    await Task.Delay(TimeSpan.FromSeconds(5)); // dejar propagar la cache de privilegios
    return 0;
}

static async Task DumpTraceLogAsync(ServiceClient client, string typeName)
{
    // Pequeña espera: el trace log se escribe asíncrono respecto de la respuesta.
    await Task.Delay(TimeSpan.FromSeconds(5));
    try
    {
        var traceQuery = new QueryExpression("plugintracelog")
        {
            TopCount = 1,
            ColumnSet = new ColumnSet("messageblock", "performanceexecutionduration",
                "performanceexecutionstarttime", "exceptiondetails", "createdon"),
            Criteria = { Conditions = { new ConditionExpression("typename", ConditionOperator.Equal, typeName) } },
            Orders = { new OrderExpression("createdon", OrderType.Descending) },
        };
        var traces = client.RetrieveMultiple(traceQuery);
        if (traces.Entities.Count == 0)
        {
            Console.WriteLine("[6] Sin plugintracelog (¿tracing deshabilitado en el org?).");
            return;
        }
        var t = traces.Entities[0];
        Console.WriteLine($"[6] plugintracelog {t.Id} — createdon={t.GetAttributeValue<DateTime>("createdon"):O}, " +
            $"duration={t.GetAttributeValue<int>("performanceexecutionduration")} ms");
        Console.WriteLine("--- messageblock ---");
        Console.WriteLine(t.GetAttributeValue<string>("messageblock"));
        var exd = t.GetAttributeValue<string>("exceptiondetails");
        if (!string.IsNullOrWhiteSpace(exd))
        {
            Console.WriteLine("--- exceptiondetails ---");
            Console.WriteLine(exd);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[6] No se pudo leer plugintracelog: {ex.Message}");
    }
}

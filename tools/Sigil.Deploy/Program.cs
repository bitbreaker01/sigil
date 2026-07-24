// Sigil.Deploy — despliegue del backend en Dev por SDK puro (sin pac CLI).
//   Registra el plugin package sanic_Sigil y las 17 Custom APIs de negocio (spec: ApiSpec.cs),
//   idempotente (re-corre sin duplicar), y las coloca en la solución sigil_core_sigil.
//
// Modos:
//   (default)  despliega: package + espera plugintypes + upsert de las 17 APIs.
//   --grant    agrega al rol del SP los privilegios prv* para registrar plugins/APIs y sale.
//   --package-only   solo (re)sube el package (sin tocar APIs).
//   --interactive (o --user)   fuerza login interactivo (browser) en vez del SP.
//
// Autenticación (auto): si SIGIL_CLIENT_ID/SECRET están en el entorno → Service Principal (sin
//   browser, ideal para CI); si no (o con --interactive) → login interactivo del usuario en el
//   navegador (requiere System Administrator). SIGIL_DATAVERSE_URL es siempre obligatorio.

using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Deploy;

string? nupkgArg = args.FirstOrDefault(a => !a.StartsWith("--"));
bool grantMode = args.Contains("--grant");
bool packageOnly = args.Contains("--package-only");
bool interactive = args.Contains("--interactive") || args.Contains("--user");

var url = Env("SIGIL_DATAVERSE_URL");
var clientId = Environment.GetEnvironmentVariable("SIGIL_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("SIGIL_CLIENT_SECRET");

// Dos modos de autenticación, elegidos automáticamente. La identidad de DESPLIEGUE es
// independiente de la de runtime: elegir una u otra acá no cambia qué identidad opera los
// flows ni el sellado.
//  - Service Principal (por defecto si hay SIGIL_CLIENT_ID/SECRET): sin browser, para CI.
//  - Login interactivo (--interactive, o si faltan esas credenciales): abre el navegador y te
//    logueás como TU usuario (necesitás System Administrator). No requiere el SP.
bool useSp = !interactive && !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
var connectionString = useSp
    ? $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true"
    // AppId = app público de muestra de Microsoft para conexiones interactivas de Dataverse
    // (documentado en ServiceClient). RedirectUri DEBE ser loopback (http://localhost): MSAL.NetCore
    // en el flujo de system-browser rechaza el redirect legacy app://... con loopback_redirect_uri
    // (MSAL 4.84). El AppId de muestra tiene http://localhost registrado. LoginPrompt=Auto abre el browser.
    : $"AuthType=OAuth;Url={url};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost;LoginPrompt=Auto;RequireNewInstance=true";

Console.WriteLine($"[1] Conectando a {url} ({(useSp ? "service principal" : "login interactivo")}) ...");
using var client = new ServiceClient(connectionString);
if (!client.IsReady)
{
    Console.Error.WriteLine($"[FATAL] Conexión no establecida: {client.LastError}");
    return 1;
}
Console.WriteLine($"[1] Conectado. Org: {client.ConnectedOrgFriendlyName} ({client.ConnectedOrgId})");

if (grantMode)
    return await GrantPrivilegesAsync(client);

// ── 2. Upsert del plugin package ───────────────────────────────────────────
string nupkgPath = ResolverNupkg(nupkgArg);
if (!File.Exists(nupkgPath))
{
    Console.Error.WriteLine($"[FATAL] nupkg no encontrado: {nupkgPath}");
    // OJO: usar `dotnet build` (NO `dotnet pack`). El SDK Microsoft.PowerApps.MSBuild.Plugin
    // arma el plugin package DURANTE el build; `dotnet pack` produjo un nupkg VACÍO (sin el DLL)
    // en clean, y uno con el DLL STALE (versión vieja → Dataverse NO recarga) en incremental —
    // el sellado siguió corriendo código viejo (2026-07-19). Verificable: el DLL adentro del
    // nupkg debe tener el AssemblyVersion bumpeado, y pluginassembly.version debe subir tras deploy.
    Console.Error.WriteLine("        Generalo con: dotnet build src/backend/Sigil.Plugins -c Release");
    return 1;
}
byte[] nupkgBytes = File.ReadAllBytes(nupkgPath);
string contentB64 = Convert.ToBase64String(nupkgBytes);
string pkgVersion = LeerVersionDelNupkg(nupkgBytes); // Dataverse cachea por versión → debe subir
Console.WriteLine($"[2] nupkg: {nupkgPath} ({nupkgBytes.Length:N0} bytes, versión {pkgVersion})");

Guid packageId = UpsertPackage(client, contentB64, pkgVersion);

// ── 3. Esperar los plugintypes auto-descubiertos ───────────────────────────
var typesEsperados = Catalogo.Apis.Select(a => a.PluginTypeName)
    .Append(Catalogo.WorkerPluginType)
    .Distinct()
    .ToArray();
var typePorNombre = await EsperarPluginTypesAsync(client, typesEsperados);
if (typePorNombre is null)
    return 1;

if (packageOnly)
{
    Console.WriteLine("[OK] Package desplegado (--package-only).");
    return 0;
}

// ── 4. Upsert de las 17 Custom APIs ────────────────────────────────────────
foreach (var api in Catalogo.Apis)
{
    Console.WriteLine($"[4] {api.UniqueName} ...");
    Guid apiId = UpsertCustomApi(client, api, typePorNombre[api.PluginTypeName]);
    ReemplazarRequestParams(client, apiId, api);
    ReemplazarResponseProps(client, apiId, api);
    Console.WriteLine($"[4]   OK ({api.RequestParams.Length} params, {api.ResponseProps.Length} response props).");
}

// ── 4b. Step ASÍNCRONO del worker de sellado ────────────────────
RegistrarStepDelWorker(client, typePorNombre[Catalogo.WorkerPluginType]);

// ── 5. Valores de env vars que el backend LEE (sin ellos las APIs no funcionan) ──
// Solo los que el código desplegado consume hoy. El resto de la config
// por-ambiente (TsaEndpoints, AppPlayUrl…) se setea cuando su consumidor se despliega.
// En Test/Prod estos valores viajan/ajustan por el pipeline, no por acá.
foreach (var (schema, valor) in Catalogo.EnvValues)
    UpsertEnvValue(client, schema, valor);

// ── 6. Publicar: refresca el modelo OData del Web API. Sin esto, un parámetro de Custom API
// recién agregado se acepta por el path SDK pero el Web API lo rechaza ("... is not a valid
// parameter for the operation ...") hasta que la metadata se re-cachea (2026-07-19).
Console.WriteLine("[6] Publicando customizations (refresca la metadata OData del Web API) ...");
client.Execute(new Microsoft.Crm.Sdk.Messages.PublishAllXmlRequest());
Console.WriteLine("[6]   PublishAllXml OK.");

Console.WriteLine();
Console.WriteLine($"[OK] Despliegue completo. package={packageId}. Ejecutá las pruebas CF-D para verificar.");
return 0;

// ───────────────────────────────────────────────────────────────────────────
// Helpers
// ───────────────────────────────────────────────────────────────────────────

static string Env(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} no configurada (source .env).");

static string ResolverNupkg(string? arg)
{
    if (arg is not null) return Path.GetFullPath(arg);
    var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sigil.slnx")))
        dir = dir.Parent;
    var raiz = dir?.FullName ?? Directory.GetCurrentDirectory();
    var binDir = Path.Combine(raiz, "src", "backend", "Sigil.Plugins", "bin", "Release");
    // El nupkg más nuevo que exista (evita hardcodear la versión — bump-friendly).
    return Directory.Exists(binDir)
        ? Directory.GetFiles(binDir, "sanic_Sigil.*.nupkg")
              .OrderByDescending(f => File.GetLastWriteTimeUtc(f)).FirstOrDefault()
          ?? Path.Combine(binDir, "sanic_Sigil.nupkg")
        : Path.Combine(binDir, "sanic_Sigil.nupkg");
}

// Lee <version> del .nuspec dentro del nupkg — la fuente de verdad del bump.
static string LeerVersionDelNupkg(byte[] nupkg)
{
    using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(nupkg), System.IO.Compression.ZipArchiveMode.Read);
    var nuspec = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    if (nuspec is null) return "1.0.0";
    using var r = new StreamReader(nuspec.Open());
    var xml = r.ReadToEnd();
    var m = System.Text.RegularExpressions.Regex.Match(xml, "<version>([^<]+)</version>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    return m.Success ? m.Groups[1].Value.Trim() : "1.0.0";
}

// Crea o actualiza un registro pasándolo por la solución sigil_core_sigil (SolutionUniqueName
// hace que el componente quede DENTRO de la solución — clave para el pipeline ALM).
static Guid CrearEnSolucion(ServiceClient client, Entity e)
{
    var req = new CreateRequest { Target = e };
    req.Parameters["SolutionUniqueName"] = Catalogo.SolutionName;
    return ((CreateResponse)client.Execute(req)).id;
}

static Guid UpsertPackage(ServiceClient client, string contentB64, string version)
{
    var q = new QueryExpression("pluginpackage")
    {
        ColumnSet = new ColumnSet("pluginpackageid", "name"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, Catalogo.PackageName) } },
    };
    var existentes = client.RetrieveMultiple(q).Entities;
    if (existentes.Count > 0)
    {
        var id = existentes[0].Id;
        // SOLO content: Dataverse deriva la versión del nuspec del content y rechaza el
        // update directo del campo `version` (0x80040265). Bump la versión en el .csproj y
        // Dataverse la lee del nupkg nuevo — así recarga el assembly.
        client.Update(new Entity("pluginpackage", id) { ["content"] = contentB64 });
        Console.WriteLine($"[2] pluginpackage existente ({id}) — content actualizado a versión {version} (re-deploy).");
        return id;
    }
    var creado = CrearEnSolucion(client, new Entity("pluginpackage")
    {
        ["name"] = Catalogo.PackageName,
        ["version"] = version,
        ["content"] = contentB64,
    });
    Console.WriteLine($"[2] pluginpackage creado: {creado}");
    return creado;
}

static async Task<Dictionary<string, Guid>?> EsperarPluginTypesAsync(ServiceClient client, string[] typeNames)
{
    Console.WriteLine($"[3] Esperando {typeNames.Length} plugintypes (propagación hasta ~120 s) ...");
    var deadline = DateTime.UtcNow.AddSeconds(120);
    while (DateTime.UtcNow < deadline)
    {
        var q = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("plugintypeid", "typename"),
            Criteria = { Conditions = { new ConditionExpression("typename", ConditionOperator.In, typeNames.Cast<object>().ToArray()) } },
        };
        var filas = client.RetrieveMultiple(q).Entities;
        if (filas.Count >= typeNames.Length)
        {
            var mapa = filas.ToDictionary(f => f.GetAttributeValue<string>("typename"), f => f.Id);
            Console.WriteLine($"[3] {mapa.Count} plugintypes descubiertos.");
            return mapa;
        }
        Console.WriteLine($"    ... {filas.Count}/{typeNames.Length}, reintento en 6 s");
        await Task.Delay(TimeSpan.FromSeconds(6));
    }
    Console.Error.WriteLine("[FATAL] no aparecieron todos los plugintypes tras 120 s. ¿Falló el procesamiento del package?");
    return null;
}

static Guid UpsertCustomApi(ServiceClient client, CustomApiSpec api, Guid pluginTypeId)
{
    var q = new QueryExpression("customapi")
    {
        ColumnSet = new ColumnSet("customapiid"),
        Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, api.UniqueName) } },
    };
    var existentes = client.RetrieveMultiple(q).Entities;
    if (existentes.Count > 0)
    {
        var id = existentes[0].Id;
        // Solo campos mutables sin recrear (binding/isfunction NO se tocan post-creación).
        client.Update(new Entity("customapi", id)
        {
            ["description"] = api.Description,
            // false: los clientes tipados del frontend (power-apps add-dataverse-api) y el
            // picker de "unbound action" de los cloud flows LEEN del $metadata, que IsPrivate=true
            // oculta (donde era true como "higiene de metadata"): IsPrivate NO
            // es control de acceso — la seguridad real son Execute Privileges + autorización en el plugin.
            ["isprivate"] = false,
            ["executeprivilegename"] = api.PrivilegioEfectivo,
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(0), // None
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
        });
        Console.WriteLine($"[4]   customapi existente ({id}) — actualizada.");
        return id;
    }

    var e = new Entity("customapi")
    {
        ["uniquename"] = api.UniqueName,
        ["name"] = api.DisplayName,
        ["displayname"] = api.DisplayName,
        ["description"] = api.Description,
        ["bindingtype"] = new OptionSetValue(api.BindingType),
        ["isfunction"] = false,
        ["isprivate"] = false, // ver nota en el update de arriba (el frontend/flows leen del $metadata)
        ["executeprivilegename"] = api.PrivilegioEfectivo,
        ["allowedcustomprocessingsteptype"] = new OptionSetValue(0), // None
        ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
    };
    if (api.BoundEntityLogicalName is not null)
        e["boundentitylogicalname"] = api.BoundEntityLogicalName;

    var creado = CrearEnSolucion(client, e);
    Console.WriteLine($"[4]   customapi creada: {creado}");
    return creado;
}

// Reemplazo DECLARATIVO: el uniquename de un request parameter ES la clave de
// InputParameters que lee el plugin (context.InputParameters["RoutingType"]) → DEBE ser el
// nombre desnudo. Es único por-API, no global, así que se borran los existentes de esta API
// y se recrean desde la spec (fuente única de verdad; limpia cualquier registro previo malo).
static void ReemplazarRequestParams(ServiceClient client, Guid apiId, CustomApiSpec api)
{
    var existentes = client.RetrieveMultiple(new QueryExpression("customapirequestparameter")
    {
        ColumnSet = new ColumnSet("customapirequestparameterid"),
        Criteria = { Conditions = { new ConditionExpression("customapiid", ConditionOperator.Equal, apiId) } },
    }).Entities;
    foreach (var e in existentes)
        client.Delete("customapirequestparameter", e.Id);

    foreach (var p in api.RequestParams)
        CrearEnSolucion(client, new Entity("customapirequestparameter")
        {
            ["customapiid"] = new EntityReference("customapi", apiId),
            ["uniquename"] = p.Name,
            ["name"] = p.Name,
            ["displayname"] = p.Name,
            ["type"] = new OptionSetValue(p.Type),
            ["isoptional"] = p.Optional,
        });
}

// Step ASÍNCRONO del worker: Update de sanic_sigil_tbl_transaction, filtering
// attribute sanic_sigil_status, post-operation (40), modo async (1), con POST-IMAGE del
// status (el guard del worker la exige). Idempotente por nombre.
static void RegistrarStepDelWorker(ServiceClient client, Guid workerTypeId)
{
    var stepQ = new QueryExpression("sdkmessageprocessingstep")
    {
        ColumnSet = new ColumnSet("sdkmessageprocessingstepid"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, Catalogo.WorkerStepName) } },
    };
    var existentes = client.RetrieveMultiple(stepQ).Entities;
    if (existentes.Count > 0)
    {
        // El plugintypeid puede cambiar entre redeploys del package: re-apuntarlo.
        client.Update(new Entity("sdkmessageprocessingstep", existentes[0].Id)
        {
            ["plugintypeid"] = new EntityReference("plugintype", workerTypeId),
        });
        Console.WriteLine($"[4b] step del worker existente ({existentes[0].Id}) — plugintype re-apuntado.");
        return;
    }

    // sdkmessage Update + su filter para la tabla de transacciones.
    var msgQ = new QueryExpression("sdkmessage")
    {
        ColumnSet = new ColumnSet("sdkmessageid"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, "Update") } },
    };
    var updateMsg = client.RetrieveMultiple(msgQ).Entities.Single();

    var filterQ = new QueryExpression("sdkmessagefilter")
    {
        ColumnSet = new ColumnSet("sdkmessagefilterid"),
        Criteria =
        {
            Conditions =
            {
                new ConditionExpression("sdkmessageid", ConditionOperator.Equal, updateMsg.Id),
                new ConditionExpression("primaryobjecttypecode", ConditionOperator.Equal, Catalogo.TxTable),
            },
        },
    };
    var filter = client.RetrieveMultiple(filterQ).Entities.Single();

    var step = new Entity("sdkmessageprocessingstep")
    {
        ["name"] = Catalogo.WorkerStepName,
        ["plugintypeid"] = new EntityReference("plugintype", workerTypeId),
        ["sdkmessageid"] = new EntityReference("sdkmessage", updateMsg.Id),
        ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filter.Id),
        ["stage"] = new OptionSetValue(40),          // post-operation
        ["mode"] = new OptionSetValue(1),            // ASÍNCRONO
        ["rank"] = 1,
        ["filteringattributes"] = "sanic_sigil_status", // jamás locktoken
        ["asyncautodelete"] = true,                  // higiene de system jobs exitosos
        ["description"] = "Pipeline de sellado: compone, sella con TSA y crea el ledger.",
    };
    var stepId = CrearEnSolucion(client, step);
    Console.WriteLine($"[4b] step del worker creado: {stepId}");

    var postImage = new Entity("sdkmessageprocessingstepimage")
    {
        ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
        ["imagetype"] = new OptionSetValue(1),       // post-image
        ["name"] = "PostImage",
        ["entityalias"] = "PostImage",               // el nombre que el worker busca
        ["messagepropertyname"] = "Target",
        ["attributes"] = "sanic_sigil_status",
    };
    client.Create(postImage);
    Console.WriteLine("[4b] post-image del step creada (sanic_sigil_status).");
}

// Upsert del VALOR de una env var (environmentvariablevalue), ligado a su definición.
// La definición ya existe (paso 8 / CF-A09); acá se le da o corrige el valor.
static void UpsertEnvValue(ServiceClient client, string schemaName, string valor)
{
    var defQ = new QueryExpression("environmentvariabledefinition")
    {
        ColumnSet = new ColumnSet("environmentvariabledefinitionid", "schemaname"),
        Criteria = { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName) } },
    };
    var defs = client.RetrieveMultiple(defQ).Entities;
    if (defs.Count == 0)
    {
        Console.Error.WriteLine($"[5]   AVISO: no existe la definición {schemaName} (¿paso 8 pendiente?). Se omite el valor.");
        return;
    }
    var defId = defs[0].Id;

    var valQ = new QueryExpression("environmentvariablevalue")
    {
        ColumnSet = new ColumnSet("environmentvariablevalueid", "value"),
        Criteria = { Conditions = { new ConditionExpression("environmentvariabledefinitionid", ConditionOperator.Equal, defId) } },
    };
    var vals = client.RetrieveMultiple(valQ).Entities;
    if (vals.Count > 0)
    {
        if (vals[0].GetAttributeValue<string>("value") == valor)
        {
            Console.WriteLine($"[5]   {schemaName} = {valor} (sin cambio).");
            return;
        }
        client.Update(new Entity("environmentvariablevalue", vals[0].Id) { ["value"] = valor });
        Console.WriteLine($"[5]   {schemaName} = {valor} (actualizado).");
        return;
    }
    CrearEnSolucion(client, new Entity("environmentvariablevalue")
    {
        ["environmentvariabledefinitionid"] = new EntityReference("environmentvariabledefinition", defId),
        ["value"] = valor,
    });
    Console.WriteLine($"[5]   {schemaName} = {valor} (creado).");
}

static void ReemplazarResponseProps(ServiceClient client, Guid apiId, CustomApiSpec api)
{
    var existentes = client.RetrieveMultiple(new QueryExpression("customapiresponseproperty")
    {
        ColumnSet = new ColumnSet("customapiresponsepropertyid"),
        Criteria = { Conditions = { new ConditionExpression("customapiid", ConditionOperator.Equal, apiId) } },
    }).Entities;
    foreach (var e in existentes)
        client.Delete("customapiresponseproperty", e.Id);

    foreach (var r in api.ResponseProps)
        CrearEnSolucion(client, new Entity("customapiresponseproperty")
        {
            ["customapiid"] = new EntityReference("customapi", apiId),
            ["uniquename"] = r.Name,
            ["name"] = r.Name,
            ["displayname"] = r.Name,
            ["type"] = new OptionSetValue(r.Type),
        });
}

// Agrega al rol editable del SP los privilegios prv* para registrar plugins/APIs. Idempotente.
static async Task<int> GrantPrivilegesAsync(ServiceClient client)
{
    var who = (WhoAmIResponse)client.Execute(new WhoAmIRequest());
    Console.WriteLine($"[grant] UserId = {who.UserId}");

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
    var roles = client.RetrieveMultiple(rolesQuery).Entities;
    var target = roles.FirstOrDefault(r => !r.GetAttributeValue<bool>("ismanaged")
            && !string.Equals(r.GetAttributeValue<string>("name"), "System Customizer", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(r.GetAttributeValue<string>("name"), "System Administrator", StringComparison.OrdinalIgnoreCase))
        ?? roles.FirstOrDefault(r => !r.GetAttributeValue<bool>("ismanaged"));
    if (target is null)
    {
        Console.Error.WriteLine("[grant] FATAL: el SP no tiene ningún rol editable.");
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
    };
    var privQuery = new QueryExpression("privilege")
    {
        ColumnSet = new ColumnSet("privilegeid", "name"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.In, wanted.Cast<object>().ToArray()) } },
    };
    var privs = client.RetrieveMultiple(privQuery).Entities;
    Console.WriteLine($"[grant] Privilegios encontrados: {privs.Count}/{wanted.Length}");

    var current = (RetrieveRolePrivilegesRoleResponse)client.Execute(
        new RetrieveRolePrivilegesRoleRequest { RoleId = target.Id });
    var currentIds = current.RolePrivileges.Select(rp => rp.PrivilegeId).ToHashSet();

    var toAdd = privs.Where(p => !currentIds.Contains(p.Id))
        .Select(p => new RolePrivilege { PrivilegeId = p.Id, Depth = PrivilegeDepth.Global })
        .ToArray();
    if (toAdd.Length == 0)
    {
        Console.WriteLine("[grant] El rol ya tiene todos los privilegios. Nada que hacer.");
        return 0;
    }
    Console.WriteLine($"[grant] Agregando {toAdd.Length} privilegios ...");
    client.Execute(new AddPrivilegesRoleRequest { RoleId = target.Id, Privileges = toAdd });
    Console.WriteLine("[grant] OK — privilegios agregados.");
    await Task.Delay(TimeSpan.FromSeconds(5));
    return 0;
}

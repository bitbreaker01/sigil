# Deploy y ALM

**Alcance.** Cómo el backend y la solución llegan a un ambiente. Dos mecanismos complementarios: la
**herramienta de despliegue** (`tools/Sigil.Deploy`) que registra el plugin package y las 17 Custom APIs por
SDK — para Dev y diagnóstico; y el **pipeline ALM de la solución** (dos workflows de GitHub) que exporta los
`.zip` managed/unmanaged a **GitHub Releases** y versiona la metadata no-código en `solutions/unpacked/`. El
árbol del repo y los comandos de build están en [Estructura y build](02-estructura-y-build.md); el catálogo
de las 17 Custom APIs, en [Backend](03-backend.md); las recetas de "cómo agregar" están en
[Cómo extender](09-como-extender.md).

**La distinción clave:** en **Dev** el backend se despliega **directo por SDK** con la herramienta (rápido,
idempotente, con diagnóstico). En **Test/Prod** el backend viaja **en la solución** por el pipeline (export
desde Dev → import). El despliegue directo por SDK **no** es el camino de promoción entre ambientes.

---

## 1. La herramienta de despliegue (`tools/Sigil.Deploy`)

Un ejecutable de consola `net8.0` (`Program.cs`, `ApiSpec.cs`) que conecta a Dataverse por `ServiceClient` y
deja el backend registrado. **Idempotente de punta a punta**: cada componente se busca antes de crearse y se
actualiza en su lugar; correrla dos veces converge al mismo estado. Se corre desde
`src/frontend`-independiente, con el package ya compilado (`dotnet build src/backend/Sigil.Plugins -c Release`
genera el `sanic_Sigil.<version>.nupkg`).

### 1.1 Autenticación — dos modos

La herramienta detecta el modo **automáticamente** (`Program.cs:27`):

| Modo | Cuándo | Connection string |
|------|--------|-------------------|
| **Service Principal** | Están las env vars y no se pasó `--interactive` | `AuthType=ClientSecret;Url=…;ClientId=…;ClientSecret=…;RequireNewInstance=true` (`Program.cs:39`) |
| **Interactivo** | `--interactive`/`--user`, o faltan las credenciales | `AuthType=OAuth;AppId=51f81489-…;RedirectUri=http://localhost;LoginPrompt=Auto;RequireNewInstance=true` (`Program.cs:44`) |

El Service Principal es el camino de CI (sin browser); requiere `SIGIL_DATAVERSE_URL`, `SIGIL_CLIENT_ID` y
`SIGIL_CLIENT_SECRET`. El interactivo requiere **System Administrator** y abre el navegador; el
`RedirectUri=http://localhost` (loopback) es obligatorio por la app pública de Microsoft.

```bash
# Service Principal (CI)
export SIGIL_DATAVERSE_URL="https://<org>.crm.dynamics.com"
export SIGIL_CLIENT_ID="..."; export SIGIL_CLIENT_SECRET="..."
dotnet run --project tools/Sigil.Deploy -c Release

# Interactivo (System Administrator)
dotnet run --project tools/Sigil.Deploy -c Release -- --interactive
```

### 1.2 Los flags

| Flag | Efecto |
|------|--------|
| *(ninguno)* | Despliegue completo: package → espera de plugin types → upsert de las 17 APIs → step del worker → env vars → publish |
| `--grant` | Solo agrega los privilegios `prv*` (registrar plugins/APIs) al rol editable del Service Principal, y sale (`Program.cs:55`) |
| `--package-only` | Solo re-sube/actualiza el plugin package, sin tocar las APIs (`Program.cs:87`) |
| `--interactive` / `--user` | Fuerza login interactivo en vez del Service Principal |

### 1.3 El orden de operaciones

El despliegue completo ejecuta, en orden (`Program.cs:46`):

1. **Conectar** el `ServiceClient` (modo auto-detectado).
2. **Upsert del plugin package** (`UpsertPackage`, `Program.cs:169`). Resuelve el `.nupkg` más reciente de
   `src/backend/Sigil.Plugins/bin/Release`, lee la `<version>` del `.nuspec` embebido, busca el package por
   nombre `sanic_Sigil` y actualiza solo el `content` si existe (o lo crea en la solución `sigil_core_sigil`).
   Dataverse recalcula la versión desde el nuspec.
3. **Esperar el descubrimiento de plugin types** (`EsperarPluginTypesAsync`, `Program.cs:197`): polling cada
   6 s con deadline de 120 s hasta que la plataforma reporta los 18 tipos (17 APIs + `SealingWorkerPlugin`).
4. **Upsert de las 17 Custom APIs** (`UpsertCustomApi`, `Program.cs:222`): por cada una, busca por
   `uniquename`, actualiza los campos mutables o crea en la solución, y **reemplaza** sus request parameters y
   response properties (borra los existentes, crea los del spec).
5. **Registrar el step asíncrono del worker** (`RegistrarStepDelWorker`, `Program.cs:300`): step
   **post-operation asíncrono** sobre `Update` de `sanic_sigil_tbl_transaction`, con *filtering attribute*
   `sanic_sigil_status` y una **post-image** del status. Idempotente por nombre.
6. **Upsert de env values** (`UpsertEnvValue`, `Program.cs:372`): despliega los **valores** de las env vars
   que el backend lee hoy (de `Catalogo.EnvValues`), buscando cada definición por schema name.
7. **Publicar customizations** (`PublishAllXmlRequest`, `Program.cs:116`): refresca la metadata OData del Web
   API. Sin este paso, los parámetros nuevos son rechazados por el Web API.

> **Regla dura de versión.** Dataverse **cachea el assembly del plugin package por versión**. Ante cualquier
> cambio de código hay que subir `<Version>` en `Sigil.Plugins.csproj` (hoy `1.0.18`) **antes** de empaquetar;
> si no, la plataforma sigue corriendo el código viejo aunque se actualice el content (lección confirmada: un
> redeploy con la misma versión no recargó el fix). La herramienta lee esa versión del nupkg.

### 1.4 El catálogo declarativo (`ApiSpec.cs`)

`ApiSpec.cs` es la **fuente única de verdad** del despliegue: la clase `Catalogo` declara las 17 APIs, los
privilegios, el package y las env vars. Cada API es un `CustomApiSpec` (`ApiSpec.cs:28`) con: `UniqueName`,
`DisplayName`, `Description`, `BindingType` (`Binding.Global = 0` / `Binding.Entity = 1`),
`BoundEntityLogicalName`, `PluginTypeName`, `RequestParams[]`, `ResponseProps[]` y `ExecutePrivilege`
(null → `UserPrivilege`).

Los tipos de parámetro se codifican como enteros de la plataforma (`ApiSpec.cs:9`): `Boolean = 0`,
`DateTime = 1`, `Integer = 7`, `String = 10`, `Guid = 12`.

**Los dos niveles de Execute Privilege** (`ApiSpec.cs:49`):

- `UserPrivilege = "prvReadsanic_sigil_tbl_transaction"` — el default, que tiene el rol de usuario. La
  mayoría de las APIs.
- `ServicePrivilege = "prvWritesanic_sigil_tbl_ledgerentry"` — solo lo posee el rol de servicio. Lo llevan
  los **tres jobs** (`ExpireTransactions`, `ProcessReminders`, `ResealPending`), de modo que un usuario común
  no puede invocarlos aunque conozca su firma.

**Las env values desplegadas** (`Catalogo.EnvValues`, `ApiSpec.cs:65`) son los valores por defecto de **Dev**:

| Env var (`sanic_sigil_env_*`) | Valor Dev | Para qué |
|-------------------------------|-----------|----------|
| `MaxPdfSizeKB` | `20480` (20 MB) | Techo del PDF de contenido |
| `MaxParticipants` | `20` | Cap de firmantes por transacción |
| `ExpirationDefaultDays` | `7` | Vencimiento por defecto |
| `SignatureImageSpec` | JSON de umbrales | Calibración del motor de firma maestra |
| `TsaEnabled` | `yes` | Marca de tiempo activada |
| `TsaEndpoints` | JSON (Sectigo, DigiCert) | Endpoints RFC 3161 en orden |
| `AppPlayUrl` | `SIGIL_APP_PLAY_URL` o placeholder | URL de la app (para el QR de la hoja de cierre) |
| `ReminderCadenceDays` | `2` | Cadencia de recordatorios |
| `DefaultLanguage` | `es` | Idioma de las notificaciones |

> **Cómo lee el backend una env var.** El plugin resuelve el valor con `EnvVars` (`Data/EnvVars.cs`):
> `Execute("RetrieveEnvironmentVariableValue")` con caché **por ejecución** (la plataforma no la cachea). Una
> variable faltante o mal formada **falla ruidoso** (`InvalidPluginExecutionException`) — una validación de
> tamaño con un default inventado sería una validación de mentira. Por eso la definición viaja en la solución
> y el valor lo pone el deploy tool (Dev) o se configura por ambiente (Test/Prod).

---

## 2. El modelo ALM de la solución

Sigil vive en una única solución de Dataverse (`sigil_core_sigil`, publisher `sanic`). El ALM tiene **dos
planos**:

- **Artefactos desplegables (`.zip`)** — managed + unmanaged, exportados desde Dev. **No** viven en git
  (inflarían el repo): se publican como **assets de GitHub Releases**.
- **Metadata no-código versionada** — se unpackea y se commitea a `solutions/unpacked/`, donde el **diff de
  git es el changelog real** de la configuración.

La razón del split: el código (Code App, plugin package, Custom APIs, steps) tiene su **fuente en el repo**
(`src/frontend`, `src/backend`, `tools/Sigil.Deploy`), así que unpackearlo de la solución sería redundante y
ruidoso. Solo se versiona lo que **no** es código: tablas, choices, roles, flujos, definiciones de env var.

### 2.1 Qué se versiona y qué se bota

El unpack (`pac solution unpack`) produce todo; el pipeline **poda** lo que ya es código:

| Se mantiene en `solutions/unpacked/` | Se bota (ya es código) |
|--------------------------------------|------------------------|
| `Entities/` (las 6 tablas + schema) | `CanvasApps` → fuente en `src/frontend` |
| `OptionSets/` (los 5 choices) | `pluginpackages` → fuente en `src/backend/Sigil.Plugins` |
| `Roles/` (roles de seguridad) | `customapis` → los registra `tools/Sigil.Deploy` |
| `Workflows/` (los cloud flows) | `SdkMessageProcessingSteps` → los registra `tools/Sigil.Deploy` |
| `environmentvariabledefinitions/` (definiciones, no valores) | |
| `Other/` (Solution.xml, Customizations.xml, Relationships, FieldSecurityProfiles) | |

Los **cloud flows** aparecen en `Workflows/` como pares `*.json` + `*.json.data.xml`: tres flujos —
notificaciones por estado de transacción, notificaciones por turno de participante, y los jobs diarios.

> **El Code App no se reconstruye desde `unpacked/`.** Solution Packager no soporta round-trip de Code Apps:
> su fuente es `src/frontend`. El unpack de `CanvasApps` se bota; el unpack existe para historia/diff de la
> metadata, no para re-empaquetar el Code App.

### 2.2 Nivel 1 — `solution-sync.yml` (semi-automático)

Se dispara al **publicar un Release** (o por `workflow_dispatch` con un tag, o invocado por el Nivel 2). Corre
**offline, sin secrets** — solo procesa un zip ya publicado. Pasos:

1. Checkout de `main` (fetch completo).
2. Setup .NET 8 + instalar `pac` como *dotnet tool*.
3. **Descargar el zip unmanaged** del Release (`gh release download`), prefiriendo el que matchea
   `*unmanaged*`.
4. **Unpack** offline: `pac solution unpack --packagetype Unmanaged`.
5. **Podar**: borra `CanvasApps`, `pluginpackages`, `customapis`, `SdkMessageProcessingSteps`.
6. **Sincronizar** a `solutions/unpacked/` (reemplaza el contenido).
7. **Commit + push** a `main` si hay cambios (`chore(solution): sync de metadata …`). Con `contents: write`.

### 2.3 Nivel 2 — `solution-release.yml` (un botón)

`workflow_dispatch` con la `version` como input (p.ej. `1.1.0.4`). Corre en el environment `dev` (con los
secrets del Service Principal: `SIGIL_DATAVERSE_URL`, `SIGIL_CLIENT_ID`, `SIGIL_CLIENT_SECRET`,
`SIGIL_TENANT_ID`). Pasos:

1. Checkout + setup .NET 8 + `pac`.
2. **Autenticar** el Service Principal: `pac auth create --applicationId … --clientSecret … --tenant …`.
3. **Exportar** managed **y** unmanaged de Dev: `pac solution export --managed false/true`.
4. **Crear el Release** con los dos zips como assets (tag `sol-v{version}`); si ya existe, sube con
   `--clobber`.
5. **Reusar el Nivel 1** (`uses: ./.github/workflows/solution-sync.yml`) con el tag, para el unpack + commit
   de metadata.

El resultado: un Release con los dos zips desplegables **y** un commit en `main` con la metadata diffeada. El
import a Test/Prod se hace con esos zips (managed en producción).

---

## Referencias externas

- **Dataverse `ServiceClient` / connection strings (`AuthType=ClientSecret`, `OAuth`)** — Microsoft Learn,
  *"Use the Dataverse ServiceClient"*.
- **Plugin packages de Dataverse (`pluginpackage`, versión por assembly)** — Microsoft Learn, *"Dependent
  assembly plug-ins"*.
- **Custom APIs de Dataverse (binding, request parameters, response properties, Execute Privilege)** —
  Microsoft Learn, *"Create and use Custom APIs"*.
- **Environment variables de Dataverse (`RetrieveEnvironmentVariableValue`)** — Microsoft Learn,
  *"Environment variables overview"*.
- **Power Platform CLI (`pac solution export`/`unpack`, `pac auth create`)** — Microsoft Learn, *"Microsoft
  Power Platform CLI Command Groups"*.
- **Solution Packager y ALM de soluciones** — Microsoft Learn, *"Use source control with solution files"*.

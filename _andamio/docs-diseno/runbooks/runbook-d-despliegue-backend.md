# Runbook D — Despliegue del Backend (plugin package + Custom APIs)

**Cuándo:** cada vez que se despliega o actualiza el backend de Sigil en un ambiente (Dev primero; Test/Prod por el pipeline). **Fase:** F2. **Verificación:** suite de conformidad `CF-D01..D13`.

**Superficie actual (2026-07-16):** el package `sanic_Sigil` contiene **15 Custom APIs** (4 CRUD de borrador, 4 del ciclo de vida, 2 de Firma Maestra, RetrySealing, VerifyDocument, 3 jobs) **+ el step ASÍNCRONO del worker de sellado** (`Sigil | Step | SealingWorker on Update of transaction` con post-image — verificado por CF-D09). Env vars que setea la herramienta: MaxPdfSizeKB, MaxParticipants, ExpirationDefaultDays (7 en Dev), SignatureImageSpec, **TsaEnabled=yes, TsaEndpoints (Sectigo primero en Dev — DigiCert está bloqueada desde el sandbox), AppPlayUrl (placeholder Dev — ACTUALIZAR tras el primer `pac code push`)**.

> **Operación — Sellando eterno:** si el worker agota los 4 reintentos de plataforma, la transacción queda en Sellando. La salida automática es el **saneamiento T14** de `sanic_sigil_capi_ExpireTransactions` (desplegada desde F2.4 — mueve a Error de Sellado toda Sellando > 24 h sin actividad). **Hasta que el flow diario que la invoca exista (F4), correr el job manualmente** (con las credenciales del SP): `OrganizationRequest("sanic_sigil_capi_ExpireTransactions")` — devuelve `ExpiredCount`/`SanitizedCount`. Los otros dos jobs (`ProcessReminders`, `ResealPending`) se invocan igual.
>
> **Decisión de negocio RESUELTA (2026-07-16):** el negocio agregó "TSA abandonada" (**159460012**, copiado del portal al Apéndice A) a `sanic_sigil_choice_eventtype`; `ResealPending` emite ese evento al mover ledgers a "Sin sello TSA" con la TSA deshabilitada (desde package v1.0.10). **Al promover a Test/Prod:** el valor nuevo del choice debe viajar en la solución `sigil_core_sigil` — Dataverse NO valida valores de choice al escribir, así que si el destino no lo tiene, el evento se crea con etiqueta en blanco (bug silencioso). Verificá con `CF-A16` en el destino tras el import.

Este runbook cubre el despliegue por **SDK puro** (herramienta `tools/Sigil.Deploy`), que no requiere `pac` CLI ni herramientas de Windows. La sección §7 documenta el camino alternativo con `pac` (doc 09 §4) para quien lo prefiera.

> **Qué hace la herramienta automáticamente** (para que sepas qué NO tenés que hacer a mano): registra el plugin package `sanic_Sigil`, espera que la plataforma descubra los plugin types, crea/actualiza las Custom APIs con sus parámetros, propiedades de respuesta, binding, privilegio de ejecución e `IsPrivate`, setea los valores de env vars que el backend lee, e **intenta** colocar todo en la solución `sigil_core_sigil` (colocación en el CREATE de cada componente vía `SolutionUniqueName` — **verificala a mano según §6**, no la des por garantizada). Es **idempotente**: podés re-correrla sin duplicar nada.
>
> **Modos:** sin flags = despliegue completo (package + APIs + env values). `--grant` = solo otorgar privilegios al SP (§2). `--package-only` = solo (re)subir el package sin tocar las APIs (útil para redeploy rápido del assembly tras un bump).

---

## 0. Prerequisitos (confirmá ANTES de empezar)

| # | Prerequisito | Cómo confirmarlo | Si falta |
|---|--------------|------------------|----------|
| P1 | La solución `sigil_core_sigil` existe en el ambiente | `CF-A03` verde | Runbook A §7.3 |
| P2 | El modelo de datos existe (6 tablas + choices + keys) | `CF-A04/A05/A06/A16/A17/A18` verdes | Runbook A §7.x |
| P3 | Las **definiciones** de env vars existen (9) | `CF-A09` verde | Runbook A paso 8 |
| P4 | El **Service Principal** (application user) puede registrar plugins/APIs | §2 de este runbook | §2 (modo `--grant`) o pedir a un admin |
| P5 | Las credenciales del SP están en `.env` (URL, CLIENT_ID, CLIENT_SECRET) | `source .env` sin error | Runbook A §A4 |

> **Nota sobre P4 (importante):** registrar plugin packages y Custom APIs exige privilegios que el rol **System Customizer** NO tiene por completo (hallazgo del spike: faltaba `prvCreatePluginAssembly`). En Dev le diste **System Administrator** al app user y con eso alcanza. En **Test/Prod**, si no querés dar System Administrator, usá el modo `--grant` (§2) que agrega SOLO los privilegios `prv*` necesarios a un rol editable del SP.

---

## 1. Construir el plugin package

**Cómo** (desde la raíz del repo):
```bash
dotnet build src/backend/Sigil.Plugins -c Release
dotnet pack  src/backend/Sigil.Plugins -c Release --no-build
```
**Éxito:** se genera `src/backend/Sigil.Plugins/bin/Release/sanic_Sigil.<version>.nupkg`. La `<version>` sale de `<Version>` en `Sigil.Plugins.csproj`.

**Si falla:**
- Error de compilación → el backend no compila; arreglá el código antes de desplegar.
- El nupkg queda **byte-idéntico** al anterior tras un cambio de código → el `pack --no-build` reusó uno viejo: borralo (`rm src/backend/Sigil.Plugins/bin/Release/*.nupkg`) y repetí `build` + `pack`.

> **REGLA CRÍTICA — bump de versión:** Dataverse **cachea el assembly del plugin package por VERSIÓN**. Si cambiás código, **subí `<Version>` en `Sigil.Plugins.csproj`** (ej. 1.0.1 → 1.0.2) ANTES de empaquetar. Si no, la plataforma sigue corriendo el código viejo aunque actualices el content (lección confirmada en F2: un redeploy con la misma versión NO recargó el fix). La herramienta lee la versión del nupkg automáticamente.

---

## 2. (Solo si P4 no está) Otorgar privilegios al Service Principal

**Cómo:**
```bash
source .env
dotnet run --project tools/Sigil.Deploy -c Release -- --grant
```
Agrega al primer rol **editable** (no-managed, distinto de System Customizer/Administrator) del SP los privilegios `prvCreate/Read/Write/Delete` de PluginAssembly, PluginType, PluginPackage, CustomAPI, CustomAPIRequestParameter y CustomAPIResponseProperty. Idempotente.

**Éxito:** imprime `OK — privilegios agregados` o `ya tiene todos los privilegios`.

**Si falla** (`el SP no tiene ningún rol editable` o la escalada es bloqueada): el SP no puede auto-elevarse. Un administrador debe, en el portal (admin.powerplatform.microsoft.com → Environments → Dev → Settings → Users + permissions → Application users), asignar al app user un rol con esos privilegios, o **System Administrator** (lo más simple en Dev).

> **Propagación:** los privilegios de rol pueden tardar más de unos segundos en hacerse efectivos. Si el deploy (§3) falla por permisos **justo después** de un `--grant`, esperá 1–2 minutos y reintentá.

---

## 3. Desplegar

**Cómo:**
```bash
source .env
dotnet run --project tools/Sigil.Deploy -c Release
```
El programa imprime cada paso. Espera hasta ~120 s a que la plataforma descubra los plugin types (propagación asíncrona — normal).

**Éxito:** termina con `[OK] Despliegue completo. package=<guid>`. Muestra las Custom APIs creadas/actualizadas, el step del worker y los valores de env vars.

**Si falla:**
- `no aparecieron todos los plugintypes tras 120 s` → el package no se procesó. Verificá que el nupkg contenga `Sigil.Plugins.dll` (con los 4 `IPlugin`) y que NO contenga `Microsoft.Xrm.Sdk.dll` (`unzip -l <nupkg> | grep Xrm.Sdk` debe dar vacío). Re-corré.
- `PluginPackage version cannot be updated` **(código `0x80040265`)** → intento de cambiar el campo `version` de un package existente por SDK: no se puede (Dataverse lo deriva del nuspec del content). La herramienta, en el **update**, toca **solo `content`** (en el CREATE inicial sí escribe `version`); si ves este error, actualizá la herramienta.
- Fallo de conexión → revisá `.env` (URL/CLIENT_ID/CLIENT_SECRET) y que el SP esté habilitado.

---

## 4. Verificar (la prueba de que quedó bien)

**Cómo:**
```bash
source .env
dotnet test tests/conformance/Sigil.Conformance.Tests -c Release --filter 'FullyQualifiedName~RunbookD'
```
**Éxito:** `CF-D01..D13` verdes (registro de las 15 APIs + step + 6 smokes E2E: D05 lectura, D06 ciclo, D08 firma, D10 sellado con TSA real, D11 verificación, D12/D13 jobs):
- **CF-D01** — el plugin package `sanic_Sigil` existe.
- **CF-D02** — cada Custom API con binding/isfunction/isprivate/executeprivilege correcto (los 3 jobs con el privilegio de SERVICIO).
- **CF-D03/D04** — los parámetros de request y propiedades de respuesta con su tipo.
- **CF-D05** — **smoke E2E real**: crea un borrador con un PDF de verdad, lo recupera con `GetDocumentContent` (round-trip byte a byte) y lo borra. Es el criterio de salida de F1 (doc 10).

**Si falla:**
- `CF-D03 ... parámetro X no existe` → el uniquename de un request parameter debe ser el **nombre desnudo** (ej. `RoutingType`), porque **ES la clave de `InputParameters` que lee el plugin**. La herramienta ya reemplaza los params de forma declarativa; re-corré §3.
- `CF-D05 ... variable de entorno ... no está configurada` → faltan valores de env vars: la herramienta setea `MaxPdfSizeKB` y `MaxParticipants`; si agregaste APIs que leen otras env vars, sumá sus valores (§5).
- `CF-D05 ... plazo de expiración debe ser positivo` sin pasar `ExpirationDays` → estás corriendo **código viejo** (cache de versión). Volvé a §1 y **subí la versión**.
- **Fallo único y transitorio justo tras el redeploy** (`error inesperado — revisar el trace`) → la plataforma está recargando el package; esperá ~30–60 s y re-corré la suite. Si el MISMO test falla dos corridas seguidas, es un bug real: revisá el plugin trace log en Dev.

---

## 5. Valores de variables de entorno (responsabilidad por ambiente)

La herramienta setea **los 9 valores de env var** que el backend lee (doc 03 §8): `MaxPdfSizeKB=20480`, `MaxParticipants=20`, `ExpirationDefaultDays=7`, `SignatureImageSpec`, `TsaEnabled=yes`, `TsaEndpoints` (Sectigo primero en Dev), `ReminderCadenceDays=2`, `DefaultLanguage=es`, `AppPlayUrl` (placeholder Dev). Valores de Dev; en Test/Prod los fija el pipeline por ambiente (doc 09 §6):

| Env var | Cuándo se setea | Quién |
|---------|-----------------|-------|
| `MaxPdfSizeKB`, `MaxParticipants` | Con las APIs CRUD (la herramienta) | Automático |
| `ExpirationDefaultDays` | Con `SendTransaction` (la herramienta). **Valor de Dev: 7** (doc 09 §6 manda plazos cortos en Dev; Test/Prod fijan el valor de negocio por ambiente) | Automático |
| `SignatureImageSpec` | Con `ValidateMasterSignature` (la herramienta) — el JSON canónico del doc 04 §4 | Automático |
| `TsaEnabled`, `TsaEndpoints`, `ReminderCadenceDays`, `DefaultLanguage` | Cuando se desplieguen las APIs de sellado/jobs (F2 tardío) | Runbook (a extender) |
| `AppPlayUrl` | Tras el **primer `pac code push`** de la Code App (no existe antes) | Runbook A / doc 09 §6 |

> En **Test/Prod** estos valores NO se copian de Dev: son configuración del ambiente destino (Gate 4 del Runbook B). El pipeline exporta las *definiciones*; los *valores* se fijan por ambiente.

---

## 6. Verificación manual en el portal (lo que la suite no cubre)

**Cómo:** en make.powerapps.com → Solutions → **Sigil | Core | Sigil** → objetos:

1. **Confirmá que aparecen dentro de la solución** (no solo en el ambiente): el plugin package `sanic_Sigil`, las 15 Custom APIs y el step del worker. La herramienta pasa `SolutionUniqueName` **solo al CREAR cada componente** (no en re-deploys de componentes existentes), y es comportamiento de plataforma no garantizado — por eso verificá visualmente que quedaron **como componentes de la solución** (crítico para el pipeline ALM — doc 09 §4).

**Éxito:** los objetos (package + 15 APIs + step) figuran en la solución.
**Si falla** (aparecen en el ambiente pero NO en la solución): agregalas a mano — Solution → Add existing → (Plugin package / Custom API) → seleccioná los `sanic_*`. Registrá el hallazgo (la colocación por `SolutionUniqueName` no funcionó como se espera).

---

## 7. Rollback

**Cómo (revertir el despliegue en Dev):**
1. En make.powerapps.com → Solutions → Sigil | Core | Sigil → borrá las 15 Custom APIs + el step del worker (esto borra params/response props/imagen en cascada).
2. Borrá el plugin package `sanic_Sigil` (comportamiento de plataforma: borra en cascada el pluginassembly y los 4 plugintypes).
3. Los valores de env vars (`environmentvariablevalue`) podés dejarlos o borrarlos; no rompen nada.

**Verificación del rollback:** `CF-D01..D05` vuelven a **rojo** (por diseño). El ambiente queda como antes del despliegue.

> **Advertencia:** no borres el package mientras una Custom API todavía referencia sus plugin types — la plataforma bloquea el borrado por dependencia. Borrá primero las APIs, después el package (el orden de arriba).

---

## 8. Camino alternativo con `pac` CLI (dev-loop estándar — doc 09 §4)

Si preferís el dev-loop documentado en vez de la herramienta SDK:
```bash
pac auth create --applicationId <CLIENT_ID> --clientSecret <SECRET> --tenant <TENANT> --environment <URL>
# NO VERIFICADO — confirmá el subcomando y flag exactos con `pac plugin --help` / `pac plugin push --help`
# antes de usar (la sintaxis del push de plugin package puede diferir entre versiones de pac).
pac plugin push ...
```
**Limitación (verificada, doc 09 §4):** `pac plugin push` NO acepta solución destino → tras el primer push, asociá el package a `sigil_core_sigil` (Add existing en el portal, o `pac solution add-solution-component`). Y `pac` **no crea las Custom APIs**: esas se authoran en el portal o se importan por solución. Por eso la herramienta SDK (§3) es más completa para el primer despliegue: hace package + APIs + solución en un paso. **El comando exacto de `pac plugin push` queda como NO VERIFICADO** (no se probó en F2 — se usó la ruta SDK); confirmalo con `--help` en una máquina con `pac` instalado.

---

## 9. Notas para Test/Prod

- **Credenciales:** el despliegue automático corre con el SP del ambiente destino. En Test/Prod usá las credenciales de ESE ambiente (nunca las de Dev).
- **Preferencia ALM:** en Test/Prod el backend viaja **en la solución** por el pipeline (export desde Dev → import), NO por push directo. Este runbook (SDK directo) es para **Dev** y para reproducir/diagnosticar. La ruta de solución es el Runbook B (gates post-import).
- **Bump de versión entre ambientes:** el `<Version>` del package debe subir con cada cambio que viaje; Dataverse rechaza reprocesar la misma versión con código distinto.

---

*Relacionado: Runbook A (aprovisionamiento) · Runbook B (gates post-import) · doc 04 §3 (Custom APIs) · doc 09 §4 (flujo hacia Dev).*

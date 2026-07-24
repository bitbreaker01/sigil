# Testing y CI

**Alcance.** La estrategia de pruebas de Sigil y cómo la orquesta CI. La pirámide: unit del **núcleo puro**
(base ancha, corre en cualquier plataforma), unit de la **cáscara** con el stub propio de
`IOrganizationService`, unit del **frontend** (Vitest), y arriba las suites contra ambiente real —
**conformidad**, **carrera de locks** y **E2E** (Playwright). El contrato del **Apéndice A** que atan los
tests, y los jobs de `ci.yml`. La frontera núcleo/cáscara y por qué habilita esta pirámide están en
[Backend](03-backend.md); el árbol del repo, en [Estructura y build](02-estructura-y-build.md).

**Regla del proyecto: Strict TDD** — ninguna línea de producción sin un test rojo que la exija (red → green →
refactor), backend y frontend por igual.

---

## 1. La pirámide

| Capa | Proyecto/carpeta | Runtime | Necesita ambiente | Corre en CI |
|------|------------------|---------|-------------------|-------------|
| **Unit — núcleo puro** | `Sigil.Plugins.Core.Tests` | `net8.0` | No | Sí, cada PR (barato) |
| **Unit — cáscara** | `Sigil.Plugins.Tests` | `net462` | No (stub) | Sí, cada PR, **en Windows** |
| **Unit — frontend** | `sigil-app/src/**/*.test.ts(x)` | Vitest/jsdom | No (mock) | Sí, cada PR |
| **Conformidad** | `Sigil.Conformance.Tests` | `net8.0` | Sí (Dev) | Compila cada PR; corre a demanda |
| **Carrera de locks** | `Sigil.LockRace` | `net8.0` | Sí (Dev) | Compila cada PR; corre a demanda |
| **E2E** | `sigil-app/e2e/` | Playwright | Sí (Dev, app real) | A demanda |

La base es ancha a propósito: como el 90% del motor es **puro** (cripto, PDF, reglas, validación,
imaging), se testea sin Dataverse, en segundos, en cualquier plataforma. Las capas de arriba, caras y lentas
(necesitan ambiente), se reservan para lo que **solo** el ambiente real puede responder.

---

## 2. Unit del núcleo puro (`Sigil.Plugins.Core.Tests`)

Tests xUnit sobre las clases puras del núcleo, sin dependencia de Dataverse. Cubren:

- **Crypto** — `HashUtilTests` (SHA-256 hex; vector NIST `"abc"` → `BA7816BF…`), `ClienteTsaTests` (RFC 3161
  con respuestas fabricadas por BouncyCastle).
- **Pdf** — `ComposicionDeDocumentoTests` (incrustación, hoja de cierre, QR, overflow con 12+ firmantes,
  metadatos), `TransformacionDeCoordenadasTests` (contrato %↔px con fixtures rotados y CropBox≠MediaBox).
- **Imaging** — `MotorDeFirmaMaestraTests` (umbrales de alpha/contraste/nitidez, normalización).
- **Domain** — `ReglasDeCicloDeVidaTests` (máquina de estados, decisión de último firmante),
  `ReglasDeAutorizacionTests`, `ReglasDeJobsTests`, y las validaciones (`ValidacionDePdf`,
  `ValidacionDeJson`, `ValidacionDeEncabezado`).

### 2.1 `ChoicesTests` — el enum contra el Apéndice A

`Domain/ChoicesTests.cs` es un test de **contrato**: garantiza que los enums de `Choices.cs` sean el espejo
exacto del **Apéndice A** del [contrato de convenciones de nomenclatura](../referencia/12-convenciones-nomenclatura.md).
Lee ese markdown, lo parsea con un regex que extrae `(choice, etiqueta, valor)` de la tabla, convierte la
etiqueta al **nombre lógico** del enum (quita diacríticos con `NormalizationForm.FormD`, capitaliza: "Pendiente
de Firma" → `PendienteDeFirma`), y verifica, para los 5 choices globales, que **cada miembro exista con su
nombre** y que **el valor int coincida exactamente** con el documento. También verifica que todos los valores
caigan en el rango `159460000…159469999` (el prefijo `15946` del publisher).

El punto: si alguien cambia el documento o el enum sin sincronizar el otro, este test se pone **rojo antes**
de que un flujo de notificación (que compara por número) rompa en silencio en producción.

---

## 3. Unit de la cáscara (`Sigil.Plugins.Tests`)

Tests xUnit de la orquestación de los handlers, contra el mismo runtime `net462` del assembly registrado. La
cáscara es tan delgada que se cubre con un **stub liviano**, no con un framework pesado.

### 3.1 El stub propio de `IOrganizationService`

**Decisión:** no se usa FakeXrmEasy (su licencia comercial no aplica a uso interno cerrado). En su lugar,
`Stub/StubOrganizationService.cs` — un stub propio que cubre las operaciones que la cáscara usa: `Create`,
`Retrieve`, `RetrieveMultiple`, `Update`, `Delete`, `Execute` (con `GrantAccess` nativo). Características:

- **Honra `ColumnSet`** al proyectar (corrección deliberada: ignorarlo antes tapó una regresión real de
  producción).
- **Honra filtros** `Equal`, `In`, `LessThan` con AND. Un `OR` lanza `NotSupportedException` ruidoso — no
  finge verde.
- **Clona** en cada operación para evitar aliasing (mutar la variable local del test ≠ "modificar la BD").
- Expone estado verificable: `Tablas` (el contenido), `Operaciones` (la secuencia **en orden** — para asertar
  "el lock va primero", "el evento antes que la transacción"), `Compartidos` (los `GrantAccess`), y hooks de
  inyección de fallos (`InterceptarCreate`).

**Límites declarados:** no simula locks de SQL (eso lo prueba la carrera de locks, §6) ni los mensajes de
file blocks (por eso el seam `IFileTransfer`, ver [Backend](03-backend.md)).

Un arnés (`Stub/ArnesDeApi.cs`) compone stub + `StubFileTransfer` (en memoria) + `StubTracingService` +
`StubPluginExecutionContext`, con *seeders* del modelo (`SembrarTransaccion`, `SembrarParticipante`,
`SembrarFirmaMaestra`, …) y las env vars por defecto de test (p.ej. `TsaEnabled: "no"`). El arnés se
autoimplementa como `IServiceProvider` y ejecuta el plugin fijando `InitiatingUserId = llamante`.

Los tests de `Apis/` recorren cada handler por su camino feliz y sus `InvalidPluginExecutionException`: p.ej.
`SubmitSignaturePluginTests` cubre el participante ajeno rechazado, el pendiente en secuencial, la ausencia de
firma maestra, la primera firma de dos (`IsLastSigner=false`), etc.

---

## 4. El contrato del Apéndice A

El [documento de convenciones de nomenclatura](../referencia/12-convenciones-nomenclatura.md) no es solo
prosa: su **Apéndice A** es una tabla markdown con los 5 choices globales, sus etiquetas y **sus valores
numéricos reales** — copiados del portal, nunca calculados (los flujos comparan por número). Ese apéndice es
un **contrato vivo** que **dos** suites parsean:

- `ChoicesTests` (§2.1) — lo contrasta contra los enums de `Choices.cs` (sin ambiente).
- `RunbookA_ModeloDatosTests` (§5) — lo contrasta contra los option sets de **Dataverse real**.

Los 5 choices y su cardinalidad: `transactionstatus` (9), `participantstatus` (4), `routingtype` (2),
`tsastatus` (3), `eventtype` (13) — 31 opciones en total. Si el apéndice y el código (o el ambiente) divergen,
alguna de las dos suites se pone roja.

---

## 5. Conformidad (`tests/conformance/Sigil.Conformance.Tests`)

**TDD de infraestructura:** tests xUnit que se conectan al **ambiente real** por `ServiceClient` y verifican
que cada componente exista y esté bien creado — publisher, solución, tablas, columnas (tipo **y** binding),
alternate keys, choices, roles, perfil de seguridad de columna, env vars, Custom APIs, plugin steps.

- **Conexión y auto-omisión** (`DataverseFixture.cs`): conecta por `AuthType=ClientSecret` con las env vars
  `SIGIL_DATAVERSE_URL`/`SIGIL_CLIENT_ID`/`SIGIL_CLIENT_SECRET`. **Sin `SIGIL_DATAVERSE_URL`**, los tests se
  **auto-omiten** con motivo (`Skippable`/`Skip.If`) — jamás fingen verde. Con URL pero error de conexión,
  fallan **ruidoso** (no se enmascara un ambiente roto).
- **Nombres `CF-*`**, agrupados por área en clases como `RunbookA_ModeloDatosTests` (modelo de datos), `RunbookA_FundacionesTests`
  (tablas, relaciones, forms), `RunbookA_SeguridadOperacionTests` (privilegios, FLS), `RunbookD_BackendTests`.
- **Ejemplos verificados:** `CF-A16` lee el Apéndice A **antes** de requerir el cliente (una regresión del
  documento se ve sin conectarse) y luego contrasta cada choice contra `RetrieveOptionSetRequest`; `CF-A17`
  verifica que cada columna tenga el **tipo Y el binding** correctos (un Picklist atado al choice equivocado o
  un Lookup a la tabla equivocada pasan el chequeo de tipo pero rompen flows en silencio); `CF-A18` verifica el
  formato exacto del autonumber del ledger (`SIGIL-{DATETIMEUTC:yyyy}-{SEQNUM:6}`).

Son parte de los gates post-despliegue: rojos hasta que el componente existe, verdes cuando está bien creado.

---

## 6. Carrera de locks (`tests/integration/Sigil.LockRace`)

Un script de consola que prueba la serialización SQL bajo **concurrencia real** contra Dev — lo que el stub no
puede simular. Dos escenarios:

- **A — N firmantes en paralelo:** crea una transacción paralela con N usuarios semilla (`SIGIL_SEED_UPNS`),
  impersonando a cada uno (`ServiceClient.CallerId`), y dispara `SubmitSignature` **sincronizados con una
  barrera** para maximizar el solapamiento. **La prueba real del lock**: exactamente **uno** debe resultar
  `IsLastSigner == true` — cero (zombi) o más de uno (doble worker) significa lock roto. Además verifica: estado
  final *Completado*, **un solo** ledger (alternate key), y los N participantes en *Firmado*.
- **B — doble clic del mismo firmante:** dos requests paralelos del mismo usuario. Verifica idempotencia (ambos
  `IsLastSigner=false`, participante *Firmado* una vez, un solo evento) y que la transacción quede en
  *Firmado Parcialmente* — un doble clic no debe transicionar.

Exit codes: `0` pasa, `1` fatal (setup), `2` la carrera falló. Env vars: `SIGIL_DATAVERSE_URL`,
`SIGIL_CLIENT_ID`, `SIGIL_CLIENT_SECRET`, `SIGIL_SEED_UPNS` (≥2 usuarios con rol de usuario). Limpia sus datos
en `finally`.

---

## 7. Frontend — Vitest y Playwright

### 7.1 Unit (Vitest)

Config en `vite.config.ts` (sección `test`): `environment: 'jsdom'`, `globals: true`,
`setupFiles: ['./src/test/setup.ts']`, `include: ['src/**/*.test.{ts,tsx}']`. El setup (`src/test/setup.ts`)
importa `@testing-library/jest-dom/vitest` y polyfillea lo que jsdom no trae pero el browser real sí:
`crypto.subtle` (Web Crypto, para el hash local) y `Blob.prototype.arrayBuffer()`.

Los tests corren **contra el mock** (`api/mock.ts`), sin red. Dos patrones:

- **Modelos puros** (`*Model.test.ts`) — lógica sin React: gating del wizard, geometría de zonas
  (`pdf/zoneGeometry.test.ts` — el ratio 3:1, move/resize, clamp), mapeos, orden. Asertan **claves de i18n**,
  no texto.
- **Hooks** (`use*.test.tsx`) — con testing-library contra el mock: `useDashboard`, `useDetail`, `useSign`,
  `useVerify`, `useOnboarding`, `useCreateWizard`.

Además hay tests de los módulos puros del seam: `api/api.test.ts` (validaciones, roundtrip de coordenadas,
binarios), `api/faults.test.ts` (extracción del mensaje de fault), `i18n/i18n.test.ts`.

> **Por qué `faults.ts` se testea y `powerApps.ts` no.** `powerApps.ts` importa `@microsoft/power-apps`, cuyo
> entrypoint ESM no resuelve bajo Node — Vitest no puede cargarlo. `faults.ts` se mantiene como módulo puro
> (sin ese import) justo para ser testeable. Ver [Frontend](06-frontend.md).

### 7.2 E2E (Playwright, `e2e/`)

E2E contra la app **real hosteada en Dev**, no el mock. La app corre dentro de un iframe llamado
`fullscreen-app-host`; el helper `app(page)` (`e2e/helpers.ts`) la engancha con
`page.frameLocator('iframe[name="fullscreen-app-host"]')`, y toda interacción de la UI de Sigil pasa por ahí.

- **`auth.setup.ts`** — login contra Entra (email → password → KMSI), guarda el `storageState` en
  `e2e/.auth/user.json` para reusarlo. **Detecta MFA** por signos conocidos y **falla con diagnóstico** si
  está activo (no lo saltea).
- **`create-sign.spec.ts`** — la cadena autónoma **crear→firmar** (self-seeding): la cuenta de prueba se
  agrega como firmante, **dibuja la zona en el canvas** con `page.mouse` (move + down/up), envía, recarga el
  dashboard y firma. Repetible sin datos sembrados a mano.
- **`verify.spec.ts`** — sube un PDF fixture (`e2e/fixtures/sample.pdf`) y espera el veredicto de "sin registro
  de sellado" (`found=false`).

Config (`playwright.config.ts`): `workers: 1` (la misma cuenta obliga a serializar), `retries: 1` en CI, un
proyecto `setup` que corre antes de `chromium`.

---

## 8. CI (`.github/workflows/`)

### 8.1 `ci.yml` — el gate de merge

Corre en cada `pull_request`, en push a `main` y por `workflow_dispatch`. `DOTNET_VERSION: "10.0.x"` (alineado
con `global.json`), Node 20, `contents: read`, con concurrencia que cancela runs anteriores de la misma rama.
Jobs:

| Job | Runner | Qué hace |
|-----|--------|----------|
| `frontend` | ubuntu | `npm ci` → `typecheck` → `lint` → `test` → `build` (el gate completo del Code App) |
| `core` | ubuntu | Compila y **corre** los tests del núcleo puro (net8) |
| `plugins` | **windows** | Corre los tests de la cáscara (net462, solo ejecutan en Windows) |
| `conformance-harness` | ubuntu | Compila la conformidad y verifica que **se auto-omita** sin secrets — delata en el PR si alguien la rompe |
| `lock-race-harness` | ubuntu | **Compila** el script de carrera de locks (no lo ejecuta) |
| `conformance` | ubuntu (`environment: dev`) | Solo `workflow_dispatch`: corre la conformidad **contra Dev** con los secrets del Service Principal |

Los dos "harness" (`conformance-harness`, `lock-race-harness`) son el truco para que las suites que **necesitan
ambiente** no se pudran sin correr en cada PR: se **compilan** siempre (y la conformidad demuestra que se
auto-omite limpio), aunque su ejecución real quede a demanda.

### 8.2 `playwright-e2e.yml`

`workflow_dispatch` únicamente (no en cada PR), en el environment `dev`. Instala Node 20 y Chromium
(`npx playwright install --with-deps chromium`), corre `npx playwright test` con los secrets del ambiente
(`SIGIL_APP_URL`, `SIGIL_E2E_USER`, `SIGIL_E2E_PASS`) y sube el reporte HTML como artifact.

---

## Referencias externas

- **xUnit** — documentación de xUnit.net.
- **`Skippable`/`SkippableFact` (auto-omisión de tests xUnit)** — paquete Xunit.SkippableFact.
- **Dataverse `RetrieveOptionSetRequest` / metadata de columnas** — Microsoft Learn, *"Customize entity and
  attribute metadata"*.
- **Vitest (config `test`, jsdom, setup files)** — documentación de Vitest.
- **Testing Library (`@testing-library/react`, `jest-dom`)** — documentación de Testing Library.
- **Playwright (`frameLocator`, `storageState`, proyectos y dependencias)** — documentación de Playwright.

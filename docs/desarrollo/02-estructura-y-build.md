# Estructura del repositorio y build

**Alcance.** Layout del repositorio con el propósito de cada carpeta, la tabla de proyectos .NET con
su *target framework* y rol, y cómo **buildear, correr y testear** cada pieza. Para el panorama de
alto nivel, ver la [Guía del Desarrollador](../guias/04-guia-desarrollador.md).

---

## 1. Árbol del repositorio

```
sigil/
├── Sigil.slnx                     # Solución .NET (formato XML slnx): los 7 proyectos C#
├── global.json                    # Pinea el SDK de .NET (roll-forward latestFeature)
├── src/
│   ├── backend/
│   │   ├── Sigil.Plugins.Core/        # NÚCLEO PURO (netstandard2.0) — sin Dataverse
│   │   │   ├── Crypto/                #   HashUtil (SHA-256), ClienteTsa (RFC 3161), TsaConfig
│   │   │   ├── Domain/                #   SchemaNames, Choices, reglas puras, validación, contratos JSON
│   │   │   ├── Imaging/               #   MotorDeFirmaMaestra, SignatureSpec
│   │   │   └── Pdf/                   #   ComposicionDeDocumento, TransformacionDeCoordenadas, XObjectManual
│   │   ├── Sigil.Plugins/             # CÁSCARA (net462) — plugins registrados en Dataverse
│   │   │   ├── Apis/                  #   17 Custom APIs + SigilApiPlugin (base) + LockDeFila + SealingWorker
│   │   │   └── Data/                  #   Consultas, EnvVars, seams (IFileTransfer, ISelladorTsa)
│   │   ├── Sigil.Plugins.Core.Tests/  # Tests del núcleo puro (net8, corren en cualquier plataforma)
│   │   └── Sigil.Plugins.Tests/       # Tests de la cáscara (net462, con el stub de IOrganizationService)
│   └── frontend/
│       └── sigil-app/                 # Code App (React + TS + Vite)
│           ├── src/
│           │   ├── api/               #   El seam de datos: SigilApi, powerApps.ts (real), mock.ts, index.ts
│           │   ├── app/               #   PowerProvider, queryClient, toast
│           │   ├── screens/           #   Las pantallas (dashboard, create, sign, detail, verify, ...)
│           │   ├── generated/         #   Clientes tipados de las Custom APIs (autogenerados — NO editar)
│           │   ├── pdf/               #   Visor pdf.js + editor de zonas
│           │   ├── i18n/              #   es.ts / en.ts
│           │   ├── domain/, lib/, components/
│           │   ├── App.tsx, main.tsx  #   Shell + bootstrap
│           │   └── vite-env.d.ts
│           ├── e2e/                   #   Playwright (contra Dev)
│           ├── power.config.json      #   Config del Code App (connection reference, data sources)
│           ├── package.json, vite.config.ts, tsconfig.json, playwright.config.ts
│           └── .power/                #   Esquemas del SDK (dataSourcesInfo, etc.)
├── tools/
│   └── Sigil.Deploy/                  # Despliegue del backend por SDK (net8): Program.cs, ApiSpec.cs
├── tests/
│   ├── conformance/Sigil.Conformance.Tests/   # Suite de conformidad (CF-*) — verifica el ambiente real
│   └── integration/Sigil.LockRace/            # Script de carrera de locks (contra Dev)
├── solutions/
│   ├── README.md                      # El modelo ALM de la solución (Releases + metadata)
│   └── unpacked/                      # Metadata NO-código, versionada y diffeable (Entities, Workflows, Roles, ...)
├── docs/
│   ├── guias/                         # Documentación viva (usuario, operador, cumplimiento, desarrollador)
│   ├── referencia/                    # Contratos vivos que los tests corroboran (ej. Apéndice A de choices)
│   └── desarrollo/                    # Esta carpeta: referencia técnica profunda del código
└── .github/workflows/                 # CI (ci.yml) + ALM de la solución (solution-sync, solution-release) + e2e
```

---

## 2. Los proyectos .NET

Los siete proyectos de `Sigil.slnx`, con su *target framework* y rol:

| Proyecto | TFM | Rol |
|----------|-----|-----|
| `Sigil.Plugins.Core` | `netstandard2.0` | **Núcleo puro.** Toda la lógica sin dependencias de Dataverse: hashing, cliente TSA, composición de PDF, coordenadas, motor de imagen, reglas de estado, autorización, validación. `netstandard2.0` para ser consumible desde el plugin `net462` **y** testeable desde `net8` |
| `Sigil.Plugins` | `net462` | **Cáscara.** Los plugins registrados: las 17 Custom APIs, `SigilApiPlugin` (base), `LockDeFila`, `SealingWorkerPlugin`, acceso a datos. `net462` es requisito de los plugin packages de Dataverse. Se empaqueta como plugin package `sanic_Sigil` |
| `Sigil.Plugins.Core.Tests` | `net8.0` | Tests del núcleo puro (xUnit). Corren en cualquier plataforma, en segundos |
| `Sigil.Plugins.Tests` | `net462` | Tests de la cáscara (xUnit) con un **stub propio** de `IOrganizationService`. Mismo runtime que el assembly registrado; **ejecutan solo en Windows** (en Linux/CI se compilan con reference assemblies) |
| `Sigil.Deploy` | `net8.0` | Herramienta de despliegue del backend por SDK (`ServiceClient`). Ejecutable de consola, no se empaqueta |
| `Sigil.Conformance.Tests` | `net8.0` | Suite de conformidad (xUnit + Skippable). Se conecta al ambiente real y verifica que cada componente exista; se auto-omite sin conexión |
| `Sigil.LockRace` | `net8.0` | Script de integración que dispara `SubmitSignature` concurrentes contra Dev y verifica que exactamente uno resulte "último" |

**La frontera núcleo/cáscara** es la decisión arquitectónica central del backend: ninguna dependencia
de `Microsoft.Xrm.Sdk` entra a `Sigil.Plugins.Core`. El plugin package excluye el *runtime asset* de
`Microsoft.CrmSdk.CoreAssemblies` (`ExcludeAssets="runtime"`): la plataforma provee ese assembly, no
debe viajar en el package.

> **Nota de versión del plugin package.** Dataverse cachea el assembly del plugin package **por
> versión**. Ante cualquier cambio de código del backend hay que subir `<Version>` en
> `Sigil.Plugins.csproj` antes de empaquetar; si no, la plataforma sigue corriendo el código viejo
> aunque se actualice el contenido.

---

## 3. Prerequisitos

| Herramienta | Versión | Para qué |
|-------------|---------|----------|
| **.NET SDK** | El de `global.json` (`10.0.202`, roll-forward `latestFeature`) | Compilar/testear todos los proyectos C#. CI usa la línea `10.0.x` (`DOTNET_VERSION` en `ci.yml`) |
| **Node.js** | 20 (el de CI) | Frontend: build, tests, lint |
| **pac CLI** (Power Platform CLI) | — | ALM de la solución (`pac solution export/unpack`), push del Code App (`pac code push`) y generación de clientes tipados (`pac code add-data-source`). En los workflows de ALM se instala como *dotnet tool* |
| **`power-apps` CLI** (`@microsoft/power-apps`) | La del proyecto | Correr el Code App localmente contra Dev (`power-apps run` / `power-apps push`) |

Para desplegar el backend a un ambiente se necesita, además, o un **Service Principal** (variables de
entorno `SIGIL_DATAVERSE_URL`, `SIGIL_CLIENT_ID`, `SIGIL_CLIENT_SECRET`) o un usuario con **System
Administrator** para login interactivo.

---

## 4. Backend — buildear, empaquetar, testear, desplegar

Compilar el núcleo y la cáscara, y **empaquetar** el plugin package:

```bash
# Compila y arma el plugin package (el SDK Microsoft.PowerApps.MSBuild.Plugin lo genera DURANTE el build).
dotnet build src/backend/Sigil.Plugins -c Release
#   → produce src/backend/Sigil.Plugins/bin/Release/sanic_Sigil.<version>.nupkg
```

> **Usar `dotnet build`, no `dotnet pack`, para el plugin package.** El package se arma durante el
> build; `dotnet pack` produce un nupkg sin el DLL correcto. La herramienta de despliegue toma
> automáticamente el `sanic_Sigil.*.nupkg` más reciente de `bin/Release`.

Tests:

```bash
# Núcleo puro — corre en cualquier plataforma (Linux/macOS/Windows), en segundos.
dotnet test src/backend/Sigil.Plugins.Core.Tests -c Release

# Cáscara — mismo runtime que el plugin (net462); EJECUTA solo en Windows.
dotnet test src/backend/Sigil.Plugins.Tests -c Release
```

Desplegar el backend a un ambiente (registra el plugin package y las Custom APIs, idempotente):

```bash
# Con Service Principal (sin browser, ideal para CI): exporta las variables y corré la herramienta.
export SIGIL_DATAVERSE_URL="https://<org>.crm.dynamics.com"
export SIGIL_CLIENT_ID="..."
export SIGIL_CLIENT_SECRET="..."
dotnet run --project tools/Sigil.Deploy -c Release

# Con login interactivo del usuario (requiere System Administrator; abre el navegador):
dotnet run --project tools/Sigil.Deploy -c Release -- --interactive
```

La herramienta detecta el modo de autenticación automáticamente: si están `SIGIL_CLIENT_ID/SECRET`
usa el Service Principal; si no (o con `--interactive`), abre login interactivo (redirect
`http://localhost`). Otros modos: `--grant` (otorga al rol del SP los privilegios para registrar
plugins/APIs), `--package-only` (solo re-sube el package). El despliegue directo por SDK es para
**Dev** y diagnóstico; en **Test/Prod** el backend viaja **en la solución** por el pipeline ALM.

---

## 5. Frontend — correr, testear, buildear, publicar

Todo desde `src/frontend/sigil-app/`:

```bash
npm ci                 # instalar dependencias (lockfile)

npm run dev            # servidor de desarrollo (Vite). Usa el MOCK del backend (sin ambiente)
npm run dev:lan        # igual, escuchando en 0.0.0.0 (probar desde el móvil en la LAN)

npm run typecheck      # tsc --noEmit
npm run lint           # eslint (--max-warnings 0)
npm run test           # Vitest (unit — hooks/modelos contra el mock, sin red)
npm run test:watch     # Vitest en watch
npm run e2e            # Playwright (E2E contra la app real hosteada en Dev)

npm run build          # tsc -b && vite build → dist/  (build de producción = backend REAL)
```

**Mock vs real: lo decide el modo de build, no una bandera a mano.** En `src/api/index.ts`, la
selección es `const USE_REAL_BACKEND = import.meta.env.PROD`: los builds de producción (`vite build`,
el Code App desplegado) usan el backend real; dev y test (`npm run dev`, Vitest) usan el mock. El
cliente real se carga con un `import` dinámico *gated* por esa bandera, para que Vitest —que corre bajo
Node, donde el entrypoint ESM de `@microsoft/power-apps` no resuelve— nunca cargue el SDK. Las
pantallas importan `sigilApi` y no saben cuál implementación es.

**Conexión a Dataverse por Connection Reference.** En `power.config.json`, el data source de Dataverse
usa `xrmConnectionReferenceLogicalName` (`sanic_SigilConnDataverseSP`), **no** un `sharedConnectionId`.
Atar la conexión por *connection reference* la hace portable entre ambientes: el mismo Code App apunta
a la conexión correcta de cada ambiente sin reconfigurar el binding.

Publicar el Code App:

```bash
npm run build
pac code push --environment <env> --solutionName sigil_core_sigil
```

Los clientes tipados de `src/generated/` son autogenerados (`pac code add-data-source`) — no se
editan a mano.

---

## 6. Integración continua y ALM de la solución

Los tres workflows relevantes viven en `.github/workflows/`:

### 6.1 `ci.yml` — el gate de merge

Rojo bloquea el merge. Corre en cada `pull_request` y en push a `main`. Jobs:

| Job | Runner | Qué hace |
|-----|--------|----------|
| `frontend` | ubuntu | `npm ci` → `typecheck` → `lint` → `test` → `build` (el gate completo del Code App) |
| `core` | ubuntu | Compila y **corre** los tests del núcleo puro (net8) |
| `plugins` | **windows** | Corre los tests de la cáscara (net462, solo ejecutan en Windows) |
| `conformance-harness` | ubuntu | Compila la suite de conformidad y verifica que **se auto-omita** sin ambiente (sin secrets) — delata si alguien la rompe, en el PR |
| `lock-race-harness` | ubuntu | Compila el script de carrera de locks (no lo ejecuta) |
| `conformance` | ubuntu (`environment: dev`) | Solo `workflow_dispatch`: corre la conformidad **contra Dev** con los secrets del SP |

### 6.2 ALM de la solución (dos niveles)

Los `.zip` managed/unmanaged **no** viven en git: se publican como assets de **GitHub Releases**. La
metadata no-código se versiona en `solutions/unpacked/` (donde el diff de git es el changelog real).

- **`solution-sync.yml` (Nivel 1, semi-automático).** Se dispara al **publicar un Release**. Descarga
  el zip unmanaged, hace `pac solution unpack` (offline, sin secrets), **bota lo que ya es código**
  (`CanvasApps`, `pluginpackages`, `customapis`, `SdkMessageProcessingSteps`), y commitea el resto en
  `solutions/unpacked/`. Se mantienen: `Entities`, `OptionSets`, `environmentvariabledefinitions`,
  `Roles`, `Workflows`, `Other`.
- **`solution-release.yml` (Nivel 2, un botón).** `workflow_dispatch` con la versión. Con el Service
  Principal (secrets del environment `dev`) **exporta** managed + unmanaged de Dev, crea el Release
  con los dos zips y **reusa** el Nivel 1 (`solution-sync`) para el unpack + commit de metadata.

> El Code App no se reconstruye desde `solutions/unpacked/` (Solution Packager no soporta round-trip de
> Code Apps): su fuente es `src/frontend`. El unpack es para historia/diff de la metadata, no para
> re-empaquetar el Code App.

---

## Referencias externas

- **Formato de solución `.slnx`** — Microsoft Learn, *"XML-based solution file (.slnx)"*.
- **`global.json` / roll-forward del SDK** — Microsoft Learn, *".NET SDK overview: global.json"*.
- **Plugin packages de Dataverse (net462, dependent assemblies)** — Microsoft Learn, *"Dependent
  assembly plug-ins"*.
- **Power Platform CLI (`pac`) — comandos `solution` y `code`** — Microsoft Learn, *"Microsoft Power
  Platform CLI Command Groups"*.
- **Code Apps (`@microsoft/power-apps`, connection references)** — Microsoft Learn, documentación de
  Power Apps Code Apps.

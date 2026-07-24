# Frontend — el Code App

**Alcance.** Cómo está construido el frontend de Sigil: un **Power Apps Code App** (React + TypeScript +
Vite + Fluent UI v9) que corre embebido en el player de Power Apps. El **seam de datos** (`SigilApi`) con
sus dos implementaciones (mock y real), la selección mock/real por modo de build, el `PowerProvider` que
resuelve la identidad, la navegación por estado y los deep links, la política dura de binarios fuera del
caché, pdf.js con worker inline, y la i18n. El panorama de alto nivel está en la
[Guía del Desarrollador](../guias/04-guia-desarrollador.md); el árbol del repo y los comandos, en
[Estructura y build](02-estructura-y-build.md); el modelo de confianza (por qué la identidad del frontend
no es autoritativa), en [Arquitectura](01-arquitectura.md); el contrato de coordenadas que comparte con el
backend, en [Sellado y criptografía](04-sellado-y-cripto.md).

**Regla de oro, repetida acá porque es el eje de todo:** *el backend decide, el frontend orquesta.* No hay
lógica de negocio ni criptografía de decisión en el Code App. La UI muestra, propone y **oculta lo que el
backend igualmente rechazaría** — nunca es la fuente de verdad.

---

## 1. El stack y el bootstrap

| Pieza | Versión (de `package.json`) | Rol |
|-------|------------------------------|-----|
| **React + ReactDOM** | `18.3.1` | UI declarativa. `StrictMode` activo |
| **Vite** | `5.4.0` | Bundler/dev server. Build `target: es2022`, `base: './'`, sourcemaps |
| **TypeScript** | `5.4.5` | Todo el código tipado; el build es `tsc -b && vite build` |
| **Fluent UI v9** | `@fluentui/react-components` `9.54.17` | Sistema de componentes. Tema `webLightTheme` |
| **`@microsoft/power-apps`** | `1.2.7` | SDK del Code App: `getContext()` (identidad + query params) y `getClient()` (data client + `executeAsync` a las Custom APIs) |
| **TanStack Query** | `5.51.23` | Caché y polling de listas/estados — **jamás** binarios (§5) |
| **pdf.js** (`pdfjs-dist`) | `4.5.136` | Render del PDF, con el worker **inline** (§6) |
| **react-i18next** | `14.1.3` | i18n es/en, bundleado (§7) |
| **react-easy-crop** | `^6.2.2` | Editor de recorte de la firma maestra (onboarding) |

El bootstrap está en `src/main.tsx`: monta el árbol de providers en este orden — `I18nextProvider` →
`FluentProvider` → `QueryClientProvider` → `PowerProvider` → `App` + `Toaster`. El `FluentProvider` fija
`minHeight: '100dvh'` y el gris de shell (`colorNeutralBackground2`) para tapar el gap blanco que aparecía
en móvil (100vh vs viewport dinámico + overscroll). El `Toaster` tiene un id único (`TOASTER_ID` en
`app/toast.ts`) para que un toast **sobreviva la navegación** — la pantalla de firma dispara el toast del
resultado y navega a detalle sin que se pierda.

---

## 2. El seam de datos (`src/api/`)

Todo el acceso a datos pasa por **una interfaz**, `SigilApi` (`api/SigilApi.ts`), con **dos
implementaciones intercambiables**. Las pantallas importan `sigilApi` (la instancia) y **no saben cuál es**.

- **`api/powerApps.ts` — `PowerAppsSigilApi`**: la real. Traduce cada método a llamadas del SDK.
- **`api/mock.ts` — `MockSigilApi`**: un backend en memoria con datos sembrados y contratos idénticos.

`SigilApi` (`api/SigilApi.ts:139`) es un contrato explícito. Sus métodos agrupan:

- **Identidad:** `currentUser()` (sync, no autoritativa) y `getCurrentUserId(): Promise<string | undefined>` (resuelve
  el `systemuserid` del llamante — ver §4).
- **Acciones (Custom APIs):** `createTransaction`, `updateDraft`, `deleteDraft`, `sendTransaction`,
  `submitSignature` (→ `IsLastSigner`), `rejectTransaction`, `cancelTransaction`, `retrySealing`,
  `validateMasterSignature`/`saveMasterSignature` (preview vs commit), `getMasterSignature`,
  `getMasterSignatureHistory`, `verifyDocument`, `searchDocuments`.
- **Binarios:** `getDocumentContent` (→ `PdfBase64`) — fuera del caché (§5).
- **Lecturas de tabla (proyecciones):** `myPendingPage`, `myRequestsPage`, `myParticipationsPage`,
  `getTransaction`, `participantsOf`, `zonesOf`, `eventsOf`, `searchUsers`.

Las **vistas** (`TransactionView`, `ParticipantView`, `EventView`, `ZoneView`, `DocumentRow`,
`MasterSignatureVersion`…) son proyecciones que consumen las pantallas: mapean columnas y guardan el
`state` como **valor de choice numérico** (`state: number`) — la UI lo interpreta por nombre lógico con
`domain/states.ts`, nunca hardcodea números en la vista.

### 2.1 La implementación real (`powerApps.ts`)

Dos caminos distintos según la operación:

- **Acciones → clientes tipados generados.** Cada Custom API tiene un servicio en `src/generated/`
  (autogenerado por `pac code add-data-source`; **no se edita**). Cada servicio expone un método estático
  cuyos parámetros posicionales espejan los request parameters de la API y devuelve un `IOperationResult`
  (`{ success, data, error }`). El helper `ok()` (`powerApps.ts:75`) lo desenvuelve o lanza el mensaje de
  falla legible.
- **Lecturas → el data client de Dataverse.** `getClient(dataSourcesInfo).retrieveMultipleRecordsAsync(...)`
  (`powerApps.ts:61`) — **no** el connector `ListRecords` de bajo nivel, que en modo local devuelve
  "Invalid organization URL 'null'".

> **Gotcha verificado — nombres de entity set, no lógicos.** Las lecturas usan el nombre de **entity set
> plural** (`sanic_sigil_tbl_transactions`, `systemusers`, `powerApps.ts:66`), no el logical name singular:
> el executor del SDK lo mete **directo** en el path OData y lo usa como clave de data source. Los nombres
> de **columna** sí van en logical name. Los lookups se leen como `_<attr>_value` y los formatted values
> como `<attr>@OData.Community.Display.V1.FormattedValue` (helpers `lookup()`/`fmt()`, `powerApps.ts:127`).

### 2.2 Traducción de faults (`api/faults.ts`)

Un fault de Dataverse llega en formas anidadas variadas — y, crucialmente, el SDK suele meter **todo el
fault OData como un string JSON dentro de `.message`**. `dataverseFaultMessage(error)` (`faults.ts:46`)
desenvuelve objetos **y** parsea strings JSON recursivamente (hasta 6 niveles) para llegar al mensaje más
interno — el que un plugin puso con `InvalidPluginExecutionException`. Se mantiene como **módulo puro** (sin
importar `@microsoft/power-apps`) para que sea testeable bajo Vitest: `powerApps.ts` no carga bajo Node,
`faults.ts` sí.

### 2.3 Contratos JSON compartidos

Varias Custom APIs devuelven colecciones como **JSON en un string** (una Custom API no devuelve colecciones
nativas). El frontend hace `JSON.parse` y mapea a sus vistas: `SearchDocuments.ResultsJson` →
`DocumentRow[]` (`powerApps.ts:402`), `GetMasterSignatureHistory.HistoryJson` → `MasterSignatureVersion[]`
(tolerando backends viejos que no traen `documents`, `powerApps.ts:239`), `VerifyDocument.MetadataJson` +
`signerSummary` → el certificado de verificación (`buildCertificate`, `useVerify.ts:29`).

---

## 3. Mock vs real — lo decide el modo de build

La selección vive en **un solo lugar** (`api/index.ts:15`):

```ts
const USE_REAL_BACKEND = import.meta.env.PROD;

async function create(): Promise<SigilApi> {
  if (!USE_REAL_BACKEND) return new MockSigilApi();
  const { PowerAppsSigilApi } = await import('./powerApps'); // import DINÁMICO gated
  return new PowerAppsSigilApi();
}
export const sigilApi: SigilApi = await create();
```

- **Builds de producción** (`vite build`, el Code App desplegado por `pac code push`) → backend **real**.
- **Dev y test** (`npm run dev`, Vitest) → **mock**.

No es una bandera a mano: eso mata la clase de bug donde un `true` cometido rompe CI. El cliente real se
carga con un **`import` dinámico gated** por la bandera, para que Vitest —que corre bajo Node, donde el
entrypoint ESM de `@microsoft/power-apps` no resuelve— **nunca** cargue el SDK. Para ejercitar el backend
real localmente (`power-apps run`) se pone `true` temporalmente, sin cometerlo.

El **mock** (`api/mock.ts`) es un backend en memoria con la misma forma de respuesta que el real: **no valida
seguridad** (eso es del backend) — solo produce respuestas con la forma correcta para desarrollar la UI sin
ambiente y correr los tests unitarios sin red.

---

## 4. Identidad — `PowerProvider` y por qué no es autoritativa

`app/PowerProvider.tsx` resuelve el contexto de Power Apps **una sola vez** al arranque (`getContext()` es
async) y lo expone por React context (`useAppContext()`):

```ts
const { getContext } = await import('@microsoft/power-apps/app'); // import diferido
const c = await getContext();
setCtx({ user: { objectId, fullName, userPrincipalName }, queryParams: c.app.queryParams, ready: true });
```

Si el runtime de Power Apps no está (dev/local) cae a una **identidad mock** (`Test User`,
`test@sigil.local`) y a los query params de la URL del navegador. El flag `ready` distingue "contexto
resuelto" de "todavía cargando" — clave para el deep link (§4.1).

**Por qué la identidad del frontend nunca es autoritativa.** El `getContext()` sirve para que la UI oculte
lo que el backend rechazaría, no como fuente de verdad (el detalle del modelo de confianza está en
[Arquitectura](01-arquitectura.md)). Dos consecuencias concretas en el código:

- **Las lecturas filtran explícitamente por el llamante.** La conexión corre como Service Principal (sin
  trimming de seguridad por usuario), así que las lecturas filtran por el `systemuserid` del llamante,
  resuelto **una vez** del `objectId` de Entra (`me()`, `powerApps.ts:301`): busca en `systemusers` por
  `azureactivedirectoryobjectid eq {oid}` (o por email como fallback) y cachea el id. `getCurrentUserId()`
  del seam lo expone para que las pantallas comparen el llamante contra `participant.userId`.
- **El backend congela los snapshots de identidad del servidor**, no de lo que mande el cliente — un cliente
  manipulado no puede firmar en nombre de otro.

### 4.1 Navegación por estado y deep links

La navegación es **por estado interno**, no un router de URL (la app corre embebida en el iframe del host,
la barra de direcciones no es suya). El shell (`App.tsx`) mantiene un `Route { screen, txId?,
signatureVersion? }` en `useState`. Las 7 pantallas (`Screen` en `lib/navigation.ts:6`): `dashboard`,
`create`, `sign`, `detail`, `verify`, `onboarding`, `documents`. Todas se cargan **lazy** (`React.lazy`)
para que el bundle inicial no arrastre pdf.js — solo `create`/`sign`/`detail`/`verify` lo cargan.

El **único punto de entrada externo** es el par `screen`/`txId` de los query params, leído por
`parseRoute(params)` (`lib/navigation.ts:20`), que valida el `txId` contra un regex de GUID.

> **Gotcha de deep link verificado (`App.tsx:44`).** El player hosteado entrega los query params
> (`screen`/`txId` — p.ej. de un QR de verificación) vía `getContext()` **después** del primer render, así
> que el `parseRoute` inicial (de la URL del iframe) no los ve. El deep link se aplica en un **effect cuando
> `ready` es true**, una sola vez (`appliedDeepLink` ref), y **solo si apunta a algo distinto del default**
> (`r.screen !== 'dashboard' || r.txId`) para no pisar a un usuario que ya navegó. Sin esto, un enlace a la
> pantalla de firma abriría el dashboard.

El shell también maneja un **`returnTo`**: cuando se abre onboarding a mitad de flujo (p.ej. desde firmar sin
Firma Maestra), recuerda dónde volver para aterrizar de nuevo ahí tras configurar (`openOnboarding`/
`leaveOnboarding`, `App.tsx:62`).

---

## 5. Binarios fuera del caché (política dura)

**Regla:** los base64 de documentos **jamás** entran al caché de TanStack Query. Un PDF de 18 MB en base64 es
~36 MB de string UTF-16 + el `Uint8Array` + los buffers de pdf.js — inaceptable en móvil, y el `gcTime` de 5
minutos lo retendría (`queryClient.ts`). Por eso:

- El `QueryClient` (`app/queryClient.ts`) es solo para **estados y listas**: `staleTime: 30 s`,
  `gcTime: 5 min`, `refetchOnWindowFocus: true` (útil solo para queries de estado), `retry: 1`.
- Los binarios se piden por el seam directo (`getDocumentContent`), viven en **estado local de la pantalla**
  y se liberan al desmontar. Nunca en `localStorage`/`IndexedDB`.
- El **polling de sellado**: mientras hay transacciones visibles en *Sellando*, se refetchea cada `5 s`
  (`POLLING_SEALING_MS`) con tope de `3 min` (`POLLING_CAP_MS`), tras el cual muestra un mensaje + refresh
  manual.

**Decodificación con yields.** El decode base64↔bytes se hace **en pasos con `await`** para no congelar el
hilo principal en móvil (`api/binaries.ts`): `base64ToBytes` procesa en chunks de 1 MB cediendo el hilo
entre cada uno; `bytesToBase64` en chunks de 32 KB. El **hash del cliente** (verificación) usa **Web Crypto**
(`crypto.subtle.digest('SHA-256', ...)`) y devuelve **hex en mayúsculas** — el mismo formato canónico que el
ledger del backend. El archivo a verificar **nunca sube**: se hashea local y solo viaja el hash de 64 hex
(`useVerify.ts:73`).

---

## 6. pdf.js con worker inline (la historia de la CSP)

La CSP de los Code Apps bloquea por defecto workers y conexiones externas. Servir el worker de pdf.js desde
un CDN falla (el proxy de storage sirve el `.mjs` con MIME `application/octet-stream` y el navegador rechaza
módulos con MIME no-JS), y desde una URL externa lo bloquea la CSP. La solución (`pdf/pdfjs.ts`) es
**inyectar el worker inline como blob**:

```ts
import * as pdfjsLib from 'pdfjs-dist';
import PdfWorker from 'pdfjs-dist/build/pdf.worker.min.mjs?worker&inline';
pdfjsLib.GlobalWorkerOptions.workerPort = new PdfWorker();
```

El sufijo `?worker&inline` de Vite mete el worker **dentro del bundle** y lo corre desde un blob, evitando el
MIME del CDN por completo. Esto requiere que la CSP del ambiente permita `worker-src blob:` (en dev local no
hay CSP, así que funciona sin más). Este módulo es el **único** lugar que toca la config global de pdfjs; el
resto del visor vive en `pdf/` (`PdfViewer.tsx`, `PdfPage.tsx`, `usePdfDocument.ts`, `ZoneOverlay.tsx`,
`zoneGeometry.ts`).

### 6.1 El contrato de coordenadas (compartido con el backend)

Las zonas de firma usan **un solo sistema** en todo Sigil: origen **arriba-izquierda**, unidades **% del
área visible**, orientación **visual**. El editor del frontend (`pdf/zoneGeometry.ts`) y la incrustación del
backend (`TransformacionDeCoordenadas`, ver [Sellado y criptografía](04-sellado-y-cripto.md)) hablan
exactamente el mismo contrato — una zona dibujada en la UI cae en el píxel correcto del PDF sellado. La
geometría de zona preserva un **ratio visual 3:1** (el lienzo de la firma maestra es 600×200): al
redimensionar, el ancho es el único grado de libertad y la altura se deriva.

---

## 7. Internacionalización (`src/i18n/`)

Dos idiomas, **es/en**, **bundleados** — no hay carga remota (la CSP prohíbe el fetch). La config
(`i18n/index.ts`) registra ambos como `resources` de react-i18next. El **idioma inicial** sale de
`localStorage` (`sigil.lang`) o, en su ausencia, de `navigator.language` (default `en`) — hecho verificado:
`getContext()` **no** expone el idioma. El toggle (`useT().changeLang`, `i18n/useT.ts`) alterna es↔en y
persiste en `localStorage`. El hook `useT()` da `t` (con notación de punto e interpolación `{{...}}`), `lang`
y `changeLang`. **Regla:** todo texto por i18n, jamás hardcodeado; los tests de modelo asertan las **claves**
de i18n (p.ej. `'validation.titleRequired'`), no el texto renderizado.

---

## 8. Anatomía de una pantalla — el patrón contenedor/modelo/hook

Cada pantalla sigue el mismo reparto (visible recorriendo `src/screens/`):

- **Modelo puro** (`*Model.ts`) — lógica sin React ni red: gating del wizard, mapeos, geometría, orden. Se
  testea con Vitest sin DOM (p.ej. `createWizardModel.ts`, `dashboardModel.ts`, `documentsModel.ts`).
- **Hook de datos** (`use*.ts`/`use*.tsx`) — orquesta el seam `sigilApi` + TanStack Query, expone estado a la
  vista. Testeable con testing-library contra el mock (p.ej. `useDashboard`, `useSign`, `useVerify`).
- **Componente de presentación** (`*Screen.tsx` + subcomponentes) — solo Fluent UI, sin lógica de negocio.

Las 7 pantallas:

| Pantalla | Carpeta | Rol |
|----------|---------|-----|
| **dashboard** | `screens/dashboard/` | 3 pestañas (Pendientes / Mis solicitudes / Participaciones) con scroll infinito paginado server-side |
| **create** | `screens/create/` | Wizard de 4 pasos (PDF → participantes → zonas → revisión), cada paso con su `Step` |
| **sign** | `screens/sign/` | Firmar: visor PDF + overlay de zonas; deriva a onboarding si no hay Firma Maestra |
| **detail** | `screens/detail/` | Detalle: progreso de participantes + timeline de eventos |
| **verify** | `screens/verify/` | Verificar: hash local del PDF → `VerifyDocument`, o certificado por `txId` |
| **onboarding** | `screens/onboarding/` | Firma Maestra: editor con recorte, preview de la normalización, confirmación |
| **documents** | `screens/documents/` | Búsqueda avanzada server-side (texto, creador, estado, firmantes, versión) |

Ninguna pantalla llama el SDK directo: **siempre** por `sigilApi`. La decisión "¿es el último firmante?",
"¿puede firmar?", "¿el hash coincide?" es del backend — la pantalla solo muestra el veredicto.

---

## Referencias externas

- **Power Apps Code Apps (`@microsoft/power-apps`: `getContext`, `getClient`, connection references, CSP del
  host)** — Microsoft Learn, documentación de Power Apps Code Apps.
- **`pac code add-data-source` / clientes tipados de Custom APIs** — Microsoft Learn, *"Microsoft Power
  Platform CLI — `pac code`"*.
- **Fluent UI v9 (`@fluentui/react-components`, `FluentProvider`, `Toaster`)** — documentación de Fluent UI.
- **TanStack Query v5 (`QueryClient`, `staleTime`/`gcTime`)** — documentación de TanStack Query.
- **pdf.js (`GlobalWorkerOptions.workerPort`, worker como módulo)** — documentación de Mozilla pdf.js.
- **Web Crypto (`crypto.subtle.digest`)** — MDN Web Docs.

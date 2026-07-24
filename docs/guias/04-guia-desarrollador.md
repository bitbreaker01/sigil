# Sigil — Guía del Desarrollador

**Documento autónomo.** Explica la arquitectura, las convenciones y los mecanismos internos de Sigil con el detalle suficiente para que un desarrollador nuevo pueda leer, extender y mantener el sistema. Referencia el **código fuente real** (clases, archivos, namespaces), que está en el repositorio.

**Para quién:** desarrolladores que van a modificar el backend (plugins C# de Dataverse), el frontend (Power Apps Code App en React/TypeScript), o ambos.

**Fecha:** 2026-07-21.

---

## 1. Panorama de la arquitectura

Sigil es una aplicación de **Microsoft Power Platform** compuesta por dos mitades que viven en una **única solución de Dataverse** (`sigil_core_sigil`, publisher `sanic`):

```
┌──────────────────────────────┐        ┌──────────────────────────────────┐
│  FRONTEND                    │        │  BACKEND                          │
│  Power Apps Code App         │  HTTP  │  Plugins de Dataverse (C#)        │
│  React + TypeScript + Vite   │ ─────► │  Custom APIs (bound + unbound)    │
│  Fluent UI v9                │  SDK   │  + worker de sellado (async step) │
│  (corre en un iframe del     │        │                                   │
│   player de Power Apps)      │        │  Datos: 6 tablas de Dataverse     │
└──────────────────────────────┘        └──────────────────────────────────┘
```

- **El frontend no tiene lógica de negocio ni criptografía.** Solo orquesta pantallas y llama Custom APIs. La autorización real es del backend.
- **El backend es el único que decide.** Toda transición de estado, validación, hashing, sellado y escritura de evidencia ocurre en los plugins, bajo el contexto de sistema.
- **Comunicación:** el frontend llama Custom APIs vía el SDK `@microsoft/power-apps` — unas **bound** a la tabla de transacción (reciben un `Target`), otras **unbound** (globales). Los binarios (PDFs, imágenes de firma) viajan como **base64 por Custom API** en ambos sentidos (no hay acceso directo a columnas File desde el Code App).

---

## 2. Estructura del repositorio

```
src/
  backend/
    Sigil.Plugins.Core/        # NÚCLEO PURO (netstandard2.0) — sin dependencias de Dataverse
      Crypto/                  #   HashUtil (SHA-256), ClienteTsa (RFC 3161), TsaConfig
      Domain/                  #   SchemaNames, Choices, reglas puras, validación, contratos JSON
      Imaging/                 #   MotorDeFirmaMaestra (validación/normalización de firma)
      Pdf/                     #   Composición del documento, coordenadas, XObject manual
    Sigil.Plugins/             # CÁSCARA (net462) — plugins registrados en Dataverse
      Apis/                    #   17 Custom APIs + SigilApiPlugin (base) + LockDeFila + SealingWorker
      Data/                    #   Consultas, EnvVars, seams (IFileTransfer, ISelladorTsa)
    Sigil.Plugins.Core.Tests/  # Tests del núcleo puro (corren en cualquier plataforma)
    Sigil.Plugins.Tests/       # Tests de la cáscara (con el stub artesanal de IOrganizationService)
  frontend/
    sigil-app/                 # Code App (React + TS + Vite)
      src/
        api/                   #   El seam de datos: SigilApi (interfaz), powerApps.ts, mock.ts
        screens/               #   Las 7 pantallas
        pdf/                   #   Visor pdf.js + editor de zonas
        i18n/                  #   es.ts / en.ts
      e2e/                     #   Playwright (contra Dev)
tools/
  Sigil.Deploy/                # Herramienta de despliegue del backend por SDK
tests/
  conformance/                 # Suite de conformidad (CF-*) — verifica el ambiente real
  integration/                 # Script de carrera de locks
```

---

## 3. Backend — el motor

### 3.1 La separación núcleo / cáscara (la decisión arquitectónica central)

El backend está partido en dos proyectos con una frontera dura:

| Proyecto | Target | Depende de Dataverse | Contiene |
|----------|--------|----------------------|----------|
| **`Sigil.Plugins.Core`** | `netstandard2.0` | **No** | Toda la lógica pura: hashing, cliente TSA, composición de PDF, transformación de coordenadas, motor de imagen, reglas de estado, autorización, validación |
| **`Sigil.Plugins`** | `net462` | Sí (`Microsoft.Xrm.Sdk`) | Los plugins registrados: orquestación, acceso a datos, el pegamento con Dataverse |

**Por qué:** el 90% del motor (lo difícil y lo crítico — cripto, PDF, reglas) se testea como **clases puras** sin tocar Dataverse ni mocks pesados, y **corre en cualquier plataforma** (los tests del núcleo pasan en Linux, en CI, en segundos). La cáscara queda tan delgada que su lógica de orquestación se cubre con un stub liviano (§5.3).

> **Regla:** ninguna dependencia de `Microsoft.Xrm.Sdk` entra a `Sigil.Plugins.Core`. Si te tienta, es señal de que esa lógica pertenece a la cáscara, o de que hay que abstraer la dependencia detrás de una interfaz (como se hizo con `IFileTransfer` e `ISelladorTsa`).

### 3.2 El framework de plugins

Todos los handlers de Custom API heredan de **`SigilApiPlugin`** (`Sigil.Plugins/Apis/SigilApiPlugin.cs`), que resuelve todo el *plumbing* del contexto de Dataverse una sola vez:

```csharp
public abstract class SigilApiPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var contexto = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var trace    = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var factory  = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

        // null = contexto de SISTEMA (servicio elevado): TODA escritura la hace el sistema.
        var servicio = factory.CreateOrganizationService(null);

        // Seams: si el provider trae dobles (tests), se usan; en Dataverse real se crean los reales.
        var archivos     = serviceProvider.GetService(typeof(IFileTransfer)) as IFileTransfer ?? CrearFileTransfer(servicio);
        var selladorTsa  = serviceProvider.GetService(typeof(ISelladorTsa)) as ISelladorTsa   ?? new SelladorTsaReal();

        var entorno = new EntornoDeApi(contexto, servicio, trace, archivos, selladorTsa);
        try { Ejecutar(entorno); }
        catch (InvalidPluginExecutionException) { throw; }   // mensaje ya accionable para el usuario
        catch (Exception ex) {
            trace.Trace("...error inesperado: {0}", ex);      // el detalle técnico va al trace, JAMÁS PII
            throw new InvalidPluginExecutionException("...error inesperado — revisar el trace.", ex);
        }
    }
    protected abstract void Ejecutar(EntornoDeApi entorno);
}
```

Puntos clave:
- **Servicio elevado (`CreateOrganizationService(null)`):** todas las escrituras corren como sistema. El usuario que llama (`InitiatingUserId`) se usa **solo** para autorización y para congelar snapshots — nunca como quien escribe.
- **Seams inyectables:** `IFileTransfer` (subir/bajar columnas File) e `ISelladorTsa` (obtener el token de tiempo) se resuelven del `serviceProvider`. En Dataverse real vienen `null` y se instancian las implementaciones reales; en tests se inyectan dobles (§5.3).
- **Manejo de errores del patrón:** un `InvalidPluginExecutionException` con mensaje accionable se re-lanza tal cual (llega al usuario); cualquier otra excepción se envuelve y el detalle técnico va al trace **sin PII** (nunca nombres, correos, base64 ni hashes completos).

Cada handler recibe un **`EntornoDeApi`** con todo resuelto:

```csharp
public sealed class EntornoDeApi(...)
{
    public IPluginExecutionContext Contexto { get; }
    public IOrganizationService Servicio { get; }   // elevado
    public ITracingService Trace { get; }
    public IFileTransfer Archivos { get; }
    public ISelladorTsa SelladorTsa { get; }
    public Guid Llamante => Contexto.InitiatingUserId;          // SOLO autorización + snapshots

    public T? Input<T>(string nombre) where T : class;         // parámetro opcional de referencia
    public int? InputInt(string nombre);                       // Integer
    public int? InputOptionalInt(string nombre);               // Integer OPCIONAL — ver gotcha abajo
    public EntityReference Target { get; }                     // Target de una API bound
    public void Output(string nombre, object valor);
    public void Rechazar(IReadOnlyList<string> errores);       // corta con TODOS los errores juntos
}
```

> **Gotcha verificado — `InputOptionalInt`:** la plataforma materializa un parámetro Integer **opcional ausente** como `0`, indistinguible de un `0` explícito. Por eso `InputOptionalInt` trata `0` como "no provisto". Sin esto, todo llamante que omita el parámetro sería rechazado por la validación de dominio.

### 3.3 Anatomía de un handler (ejemplo: `SubmitSignaturePlugin`)

`SubmitSignature` es el handler más rico (registra una firma; es el crítico en concurrencia). El patrón, en orden:

```csharp
protected override void Ejecutar(EntornoDeApi e)
{
    var target = e.Target;
    LockDeFila.Tomar(e.Servicio, target.Id);          // 1. LOCK PRIMERO (§3.4)

    // 2. RE-LEER TODO post-lock: sobre estos datos serializados se decide.
    var tx = Consultas.Transaccion(e.Servicio, target.Id);
    var participantes = Consultas.ParticipantesDe(e.Servicio, target.Id);
    var mio = participantes.FirstOrDefault(p => ...UserId == e.Llamante);

    // 3. IDEMPOTENCIA ANTES del guard de estado (precedencia de guards):
    //    re-submit sobre Firmado = éxito sin efectos (doble click del último firmante).
    if (mio?.Status == Firmado) { e.Output("IsLastSigner", tx.Status == Sellando); return; }

    // 4. AUTORIZACIÓN — regla PURA del núcleo (no hay lógica de negocio en la cáscara):
    var motivo = ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(mio is not null, mio?.Status, tx.Status);
    if (motivo is not null) throw new InvalidPluginExecutionException(motivo);

    // 5. EFECTOS: firma maestra vigente (obligatoria), snapshot del PNG, documenthash SHA-256,
    //    snapshots de identidad SIEMPRE del contexto (jamás del cliente).
    var yo = e.Servicio.Retrieve(Usuario, e.Llamante, ...);   // nombre/email/upn/entraObjectId del servidor
    e.Archivos.Subir(participante, SignatureSnapshot, png);   // congela la imagen de firma

    // 6. DECISIÓN post-lock (T5/T6/T7 + P2') — otra regla PURA:
    var decision = ReglasDeFirma.Decidir(tx.RoutingType, e.Llamante, participantes);
    if (decision.SiguienteAActivar is Guid sig) { /* activar turno del siguiente (secuencial) */ }

    // 7. Transición — el status SOLO se escribe si CAMBIA (los triggers de los flows disparan
    //    aunque el valor sea idéntico; reescribir duplicaría notificaciones).
    var estadoNuevo = decision.EsUltimo ? Sellando : FirmadoParcialmente;
    if (estadoNuevo != tx.Status) e.Servicio.Update(...);

    // 8. Eventos (con la lista de lectores para GrantAccess) + Output.
    Consultas.CrearEvento(e.Servicio, target, FirmaRegistrada, ..., documentHash, lectores);
    e.Output("IsLastSigner", decision.EsUltimo);
}
```

Observá cómo **la lógica de decisión vive en el núcleo puro** (`ReglasDeAutorizacion`, `ReglasDeFirma`) y la cáscara solo orquesta lecturas/escrituras. Ese es el patrón de todos los handlers.

### 3.4 Concurrencia — el lock de fila

**El problema:** dos firmantes en paralelo llaman `SubmitSignature` a la vez; cada ejecución lee los participantes para decidir si es el último. Sin serialización: ambos ven al otro pendiente → nadie transiciona a Sellando → transacción zombi. O ambos se creen últimos → doble worker.

**La solución** (`Sigil.Plugins/Apis/LockDeFila.cs`): todo plugin que decide sobre el estado compartido ejecuta, **como primera operación**, un `Update` sobre la fila de la transacción usando **exclusivamente una columna técnica de no-op** (`sanic_sigil_locktoken`):

```csharp
public static void Tomar(IOrganizationService servicio, Guid transactionId)
{
    var fila = new Entity(SchemaNames.Tx.Entidad, transactionId);
    fila[SchemaNames.Tx.LockToken] = Environment.TickCount & int.MaxValue; // el VALOR no importa
    servicio.Update(fila);                                                  // toma el lock de SQL hasta el commit
}
```

Reglas derivadas:
- **PROHIBIDO lockear escribiendo el `status`:** los triggers de los flujos de notificación filtran por `status` y **disparan aunque el valor escrito sea idéntico** al existente. Un lock por status generaría notificaciones duplicadas. Por eso la columna técnica dedicada.
- **Siempre re-leer después del lock:** las ejecuciones concurrentes quedaron serializadas detrás del `Update`; recién ahí se lee el estado real y se decide.
- **Idempotencia por participante:** `SubmitSignature` sobre un participante ya `Firmado` retorna éxito sin efectos.
- Usan el mismo lock: `SendTransaction`, `RejectTransaction`, `CancelTransaction`, `UpdateDraft`, `DeleteDraft`, `RetrySealing` y el worker de sellado.

### 3.5 El núcleo puro (`Sigil.Plugins.Core`)

| Área | Archivo | Qué hace |
|------|---------|----------|
| **Reglas de estado** | `Domain/ReglasDeCicloDeVida.cs` | La máquina de estados pura: dado el enrutamiento y los participantes, decide quién sigue y si la firma es la última (T5/T6/T7, P2'). Sin efectos, testeable con tuplas |
| **Autorización** | `Domain/ReglasDeAutorizacion.cs` | Reglas puras de "¿este actor puede hacer esta acción en este estado?" — devuelven el motivo del rechazo o `null` |
| **Reglas de jobs** | `Domain/ReglasDeJobs.cs` | Elegibilidad de expiración/recordatorio/re-sellado |
| **Validación** | `Domain/ValidacionDeEntrada.cs` | Validación de entrada (longitud antes de decodificar, magic bytes, zonas, órdenes) — devuelve **todos** los errores juntos |
| **Nombres de esquema** | `Domain/SchemaNames.cs` | Constantes con los nombres `sanic_sigil_*` de tablas/columnas/choices — la única fuente de esos literales |
| **Choices** | `Domain/Choices.cs` | Los enums de estado (TransactionStatus, ParticipantStatus, RoutingType, TsaStatus, EventType) con sus valores numéricos |
| **Hash** | `Crypto/HashUtil.cs` | `Sha256Hex(bytes)` — la huella de todo archivo/contenido |
| **Cliente TSA** | `Crypto/ClienteTsa.cs` | RFC 3161: arma el `TimeStampRequest` con `CertReq=true` + nonce aleatorio, valida el token en dos niveles, rechaza `http://` |
| **Imagen de firma** | `Imaging/MotorDeFirmaMaestra.cs` | Valida y normaliza el PNG de la firma (alfa, contraste, tamaño estándar) |
| **Composición del PDF** | `Pdf/ComposicionDeDocumento.cs` | Incrusta firmas + hoja de cierre + QR + metadatos |
| **Coordenadas** | `Pdf/TransformacionDeCoordenadas.cs` | El contrato compartido con el frontend: % del área visible, origen arriba-izquierda, orientación visual (compensa rotación y CropBox) |
| **XObject manual** | `Pdf/XObjectManual.cs` | Incrusta PNG con transparencia como XObject manual (ver gotcha abajo) |

> **Gotcha verificado — incrustación de PNG bajo net462:** el importador de imágenes de PDFsharp (`XImage.FromStream`) **falla siempre** en el sandbox net462 ("Unsupported image format"), aunque el header PNG sea correcto. Por eso `XObjectManual` incrusta la imagen a mano: FlateDecode RGB + `/SMask` DeviceGray, con **zlib artesanal** (`0x78 0x9C` + Adler32) porque net462 no tiene `ZLibStream`. Regla: PDFsharp **pin `=6.2.4` exacto**, jamás 6.2.0.

### 3.6 El worker de sellado asíncrono (`SealingWorkerPlugin`)

Cuando la última firma transiciona la transacción a **Sellando**, se dispara un **step asíncrono** (post-operation, en Update de la transacción, filtrando por `status`). El worker ejecuta 9 pasos en orden estricto:

1. Descargar el PDF de contenido.
2. **Verificar `hash_contenido`** — SHA-256 de lo descargado debe coincidir con lo anclado en el envío; si no, Error de Sellado (jamás se sella contenido adulterado).
3. Incrustar las firmas (snapshots) en sus zonas.
4. Hoja de cierre + metadatos.
5. Calcular `hash_final`.
6. Obtener el token TSA (si la marca de tiempo está activa).
7. **Subir** el PDF final.
8. **Crear** el registro de ledger (la alternate key hace el insert idempotente).
9. Transicionar a Completado + eventos.

**Guards del disparador (precisos):**
- **Guard de post-image:** `status == Sellando` en la post-image → candidato; otro valor → return (neutraliza el auto-retrigger del paso 9).
- **Guard de estado ACTUAL bajo lock:** un reintento encolado conserva la post-image vieja; el worker toma el lock, **relee el estado actual** y aborta si ≠ Sellando; y **verifica la existencia del ledger antes del paso 1** — si existe, salta a completar los pasos restantes, jamás re-sube el archivo.
- **Depth guard:** el worker corre legítimamente con `Depth ≥ 2`; el guard correcto es anti-loop de umbral alto (`Depth > 8 → abortar`), no un filtro de negocio.

> **Por qué el orden 7 → 8 (idempotencia):** la serialización de un PDF no es determinística (IDs de objetos, metadata), así que recomponerlo en un reintento produce bytes distintos → hash distinto. Guardando el archivo durable **antes** que la huella que lo referencia, el hash del ledger siempre describe los bytes que existen. El orden inverso produciría un registro inmutable que apunta a bytes que ya no existen: transacción permanentemente inverificable. **Prohibido.**

**Semántica de fallos:** fallo transitorio (timeout, deadlock) → `InvalidPluginExecutionException` con `OperationStatus.Retry` (hasta 4 reintentos, re-entran por el flujo idempotente); fallo definitivo (PDF corrupto, mismatch de hash, retries agotados) → Error de Sellado. La única salida de Error de Sellado es `RetrySealing`.

### 3.7 Las 17 Custom APIs

Cada una tiene su **Execute Privilege** propio (autorización a nivel de plataforma). **8 son bound** a `sanic_sigil_tbl_transaction` (reciben un `Target` con la transacción sobre la que operan) y **9 son unbound** (globales, sin Target). Por categoría (🔗 = bound, 🌐 = unbound):

- **Borrador (CRUD):** `CreateTransaction` 🌐, `UpdateDraft` 🔗, `DeleteDraft` 🔗, `GetDocumentContent` 🔗.
- **Ciclo de vida** (todas 🔗, operan sobre una transacción): `SendTransaction`, `SubmitSignature`, `RejectTransaction`, `CancelTransaction`, `RetrySealing`.
- **Firma Maestra** (🌐, operan sobre el usuario): `ValidateMasterSignature`, `GetMasterSignature`, `GetMasterSignatureHistory`.
- **Verificación:** `VerifyDocument` 🌐 (toma un hash).
- **Documentos:** `SearchDocuments` 🌐.
- **Jobs (solo la identidad de servicio):** `ExpireTransactions` 🌐, `ProcessReminders` 🌐, `ResealPending` 🌐.

Los tres jobs llevan el **privilegio de ejecución de servicio** (`prvWrite` del ledger, que solo tiene el rol de servicio) — un usuario común no puede invocarlos aunque conozca su firma. `IsPrivate` **NO** es un control de seguridad (verificado); la protección es el Execute Privilege.

### 3.8 Modelo de datos (6 tablas)

| Tabla | Rol | Nota |
|-------|-----|------|
| `sanic_sigil_tbl_transaction` | La solicitud de firma | Columna técnica `locktoken`; `contentfile`/`finalfile` (File); `contenthash` |
| `sanic_sigil_tbl_participant` | Un firmante de una transacción | `signaturesnapshot` (File, congelado al firmar); snapshots de identidad |
| `sanic_sigil_tbl_signaturezone` | Dónde firma cada participante | Coordenadas en % (contrato §3.5) |
| `sanic_sigil_tbl_mastersignature` | La Firma Maestra de un usuario | Versionada; `signaturefile` (File) |
| `sanic_sigil_tbl_ledgerentry` | **El libro de registro (evidencia)** | **Org-owned**; **alternate key** en `transactionid` (idempotencia); columnas de evidencia con **column security** |
| `sanic_sigil_tbl_event` | Historial de negocio | Cada transición escribe su evento; `documenthash` por firma |

Choices globales: `transactionstatus` (9 valores), `participantstatus` (4), `routingtype` (2), `tsastatus` (3), `eventtype` (13). Los **valores numéricos** de cada choice se copian del portal (no se calculan) — los flujos comparan por número.

---

## 4. Frontend — la Code App

### 4.1 Stack

- **React 18 + TypeScript + Vite 5.** Bundle-first; los binarios pesados (pdf.js) se cargan *lazy* por pantalla.
- **Fluent UI v9** (`@fluentui/react-components`). Sin `data-testid`: los tests usan roles + texto.
- **`@microsoft/power-apps`** — el SDK del Code App: `getClient(dataSourcesInfo)` → `retrieveMultipleRecordsAsync(tabla, opciones)` / `executeAsync(...)` para las Custom APIs; `getContext()` para identidad y query params del deep link.
- **TanStack Query** para listas/estado (con una política especial para binarios — §4.3).
- **pdf.js** (`pdfjs-dist`) con el worker **inline** (§4.4).
- **react-i18next** (es/en), **react-easy-crop** (editor de firma).

### 4.2 El seam de datos (`src/api/`)

Todo el acceso a datos pasa por una **interfaz** (`SigilApi` en `api/SigilApi.ts`) con **dos implementaciones intercambiables**:

- **`api/powerApps.ts`** — la real: traduce cada método a llamadas del SDK (`retrieveMultipleRecordsAsync` para lecturas, `executeAsync` a las Custom APIs `sanic_sigil_capi_*` para acciones).
- **`api/mock.ts`** — un backend en memoria: mismos contratos, datos sembrados. Permite desarrollar el frontend **sin ambiente** y correr los tests unitarios (Vitest) sin red.

La pantalla nunca sabe cuál está usando. Se elige en `api/index.ts`.

### 4.3 Binarios fuera del caché (política dura)

Regla: **los base64 de documentos jamás entran al caché de TanStack Query.** Un PDF de 18 MB en base64 es ~36 MB de string UTF-16 + el `Uint8Array` + buffers de pdf.js — inaceptable en móvil, y el `gcTime` de 5 min lo retendría. Por eso los binarios se piden con el wrapper directo, viven en **estado local de la pantalla**, y se liberan al desmontar. Nunca en `localStorage`/`IndexedDB`. El decode base64 se hace **en pasos con yields** (`await`) para no congelar el hilo principal en móvil.

### 4.4 pdf.js con worker inline (la historia de la CSP)

La CSP de los Code Apps bloquea por defecto los workers y las conexiones externas. Servir el worker de pdf.js desde un CDN falla (MIME `.mjs` rechazado) y desde una URL externa lo bloquea la CSP. Solución: el worker se **inyecta inline como blob**:

```ts
import PdfWorker from 'pdfjs-dist/build/pdf.worker.min.mjs?worker&inline';
pdfjsLib.GlobalWorkerOptions.workerPort = new PdfWorker();
```

Esto requiere que la CSP del ambiente permita `worker-src 'self' blob:`, **`child-src 'self' blob:`** (indispensable para Safari, que no soporta `worker-src` y cae a `child-src`) y `connect-src 'self'`. El `blob:` es por el worker inline. Es config de ambiente (ver el Manual del Operador).

### 4.5 Navegación y pantallas

Navegación **por estado** (no router de URL): un `Route { screen, txId?, ... }` en el shell (`App.tsx`), con `parseRoute(queryParams)` para el arranque. Las 7 pantallas: dashboard (3 pestañas), crear (wizard 4 pasos), firmar, detalle, verificar, onboarding (firma maestra), documentos.

> **Gotcha de deep link:** el player hosteado entrega los query params (`screen`/`txId`) vía `getContext()` **después** del primer render. El deep link se aplica en un effect **cuando el contexto está listo** (`ready`), no en el render inicial — si no, un enlace a la pantalla de firma abriría el dashboard.

### 4.6 Contrato de coordenadas (compartido con el backend)

Las zonas de firma usan **un solo sistema** en todo Sigil: origen **arriba-izquierda**, unidades **% del área visible** (CropBox), orientación **visual** (compensa `/Rotate`). El editor del frontend y la incrustación del backend (`TransformacionDeCoordenadas`) hablan exactamente el mismo contrato — una zona dibujada en la UI cae en el píxel correcto del PDF sellado.

---

## 5. Testing — la pirámide

### 5.1 Strict TDD
Regla del proyecto: **ninguna línea de producción sin un test rojo que la exija** (red → green → refactor), backend y frontend por igual.

### 5.2 El núcleo puro (la base de la pirámide)
El 90% del motor se testea como clases puras en `Sigil.Plugins.Core.Tests` — sin Dataverse, sin mocks pesados, corriendo en cualquier plataforma en segundos. Hash, TSA (con respuestas RFC 3161 fabricadas por BouncyCastle), composición de PDF (fixtures rotados, CropBox≠MediaBox), reglas de estado (con tuplas), validación.

### 5.3 El stub artesanal de `IOrganizationService`
**Decisión:** no se usa FakeXrmEasy (su licencia comercial no aplica a uso interno cerrado). En su lugar, un **stub propio** de `IOrganizationService` en `Sigil.Plugins.Tests` cubre las ~6 operaciones usadas (Create/Retrieve/RetrieveMultiple/Update/Execute/Delete + tracking de llamadas). Honra `ColumnSet` y filtros `Equal`/`In`/`LessThan` con AND. **Límites declarados:** no simula locks de SQL (eso es §5.5) ni los mensajes de file blocks (por eso el seam `IFileTransfer`).

### 5.4 Suite de conformidad (`tests/conformance/`, `CF-*`)
**TDD de infraestructura:** tests xUnit que se conectan al **ambiente real** vía `ServiceClient` y verifican que cada componente exista y esté bien creado (publisher, solución, tablas, columnas, alternate keys, choices, roles, perfil FLS, env vars, Custom APIs, plugin steps). **Rojos hasta que el componente existe**, verdes cuando se creó bien; se auto-omiten si no hay ambiente configurado (jamás fingen verde). Son parte de los gates post-despliegue.

### 5.5 Carrera de locks (`tests/integration/`)
Un script que dispara N `SubmitSignature` concurrentes (impersonando a cada firmante, con una barrera para solaparlos) contra Dev, y verifica que **exactamente uno** resulte "último" (cero zombis, cero doble sellado). La prueba real del lock es `IsLastSigner == 1`.

### 5.6 Frontend: Vitest + Playwright
- **Vitest** — unit tests de hooks/modelos contra `api/mock.ts` (sin red).
- **Playwright** (`e2e/`) — E2E contra la app **real hosteada en Dev**. La app corre en un iframe llamado `fullscreen-app-host`; los tests lo enganchan con `frameLocator('iframe[name="fullscreen-app-host"]')`. Hay una **cadena autónoma crear→firmar** (self-seeding): la cuenta de prueba se agrega como firmante, dibuja la zona en el canvas con `page.mouse`, envía y firma — repetible sin datos sembrados a mano. Corre por workflow manual con secrets del ambiente `dev`.

---

## 6. Convenciones de nomenclatura

**Schema names de Dataverse:**
```
sanic_  +  sigil_  +  [marcador]  +  nombre
  │         │           │            └─ inglés técnico
  │         │           └─ tbl_ / choice_ / capi_ / env_  (columnas: sin marcador)
  │         └─ namespace del proyecto
  └─ prefijo del publisher (lo impone Dataverse)
```
Ejemplos: `sanic_sigil_tbl_transaction`, `sanic_sigil_choice_transactionstatus`, `sanic_sigil_capi_SubmitSignature`, `sanic_sigil_env_TsaEnabled`. Columnas sin marcador: `sanic_sigil_contenthash`.

**Display names** (roles, perfiles, flujos, solución): `Sigil | TIPO | Nombre` (siempre en inglés) — ej. `Sigil | SR | Service`, `Sigil | FLS | Evidence Writer`, `Sigil | Cloud Flow | Jobs - Daily`.

**Código:** los namespaces C# NO llevan `sanic` (`Sigil.Plugins.Core`, `Sigil.Plugins`). Las constantes que referencian schema viven en `Domain/SchemaNames.cs` con los nombres completos.

---

## 7. Build y despliegue del backend

```bash
# 1. Compilar y empaquetar el plugin package
dotnet build src/backend/Sigil.Plugins -c Release
dotnet pack  src/backend/Sigil.Plugins -c Release --no-build
#    → genera sanic_Sigil.<version>.nupkg

# 2. Desplegar por SDK (crea package + Custom APIs + step + valores de env var, idempotente)
source .env    # SIGIL_DATAVERSE_URL, SIGIL_CLIENT_ID, SIGIL_CLIENT_SECRET
dotnet run --project tools/Sigil.Deploy -c Release
```

> **REGLA CRÍTICA — bump de versión:** Dataverse **cachea el assembly del plugin package por versión**. Si cambiás código, **subí `<Version>` en `Sigil.Plugins.csproj`** antes de empaquetar. Si no, la plataforma sigue corriendo el código viejo aunque actualices el content (lección confirmada: un redeploy con la misma versión NO recargó el fix).

**Frontend:**
```bash
cd src/frontend/sigil-app
npm run build
pac code push --environment <env> --solutionName sigil_core_sigil
```

En **Test/Prod** el backend NO se pushea directo: viaja **en la solución** por el pipeline (export desde Dev → import). El despliegue directo por SDK es para **Dev** y para diagnóstico.

---

## 8. Cómo extender

### 8.1 Agregar una Custom API nueva
1. **Núcleo primero (TDD):** si hay lógica, escribila como clase pura en `Sigil.Plugins.Core/Domain` con su test rojo→verde.
2. **Handler:** creá `Sigil.Plugins/Apis/MiApiPlugin.cs : SigilApiPlugin`, implementá `Ejecutar(EntornoDeApi e)`. Usá el lock si decide sobre estado compartido.
3. **Contrato:** declará la API (nombre, params, response props, binding, execute privilege, IsPrivate) en `tools/Sigil.Deploy` (donde se authora el catálogo).
4. **Test de cáscara:** con el stub (§5.3), camino feliz + camino `InvalidPluginExecutionException`.
5. **Conformidad:** agregá el `CF-D*` que verifica su registro.
6. **Cliente frontend:** agregá el método a `SigilApi` + su implementación en `powerApps.ts` y `mock.ts`.
7. **Bump de versión** del package.

> **Gotcha verificado:** el `uniquename` de un request parameter DEBE ser el **nombre desnudo** (ej. `RoutingType`) — **es** la clave de `InputParameters` que lee el plugin. Y los file blocks (subir/bajar) usan un `blockid` en **base64 sin `+` ni `/`** (la plataforma no los url-encodea).

### 8.2 Agregar una pantalla
Nueva carpeta en `src/screens/`, agregala al `Screen` union y al `renderScreen` de `App.tsx`. Los datos siempre por el seam `SigilApi` (nunca llames el SDK directo desde una pantalla). Textos por `i18n` (es + en), jamás hardcodeados.

### 8.3 La regla de oro para extender
**El backend decide, el frontend orquesta.** Si te encontrás poniendo una validación de negocio o una decisión de estado en el frontend, va en el backend (el frontend solo oculta lo que el backend igualmente rechazaría). Y toda lógica pura va al núcleo, no a la cáscara.

---

*Documento autónomo. Complementa al Manual del Operador (despliegue y ambientes) y al Dossier de Evidencia (el modelo probatorio). El código fuente es la verdad última: esta guía te dice dónde mirar y por qué las cosas son como son.*

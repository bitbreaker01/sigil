# Sigil — Backend: Motor Criptográfico y Custom APIs

**Documento:** 04 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — visto bueno del equipo, con spikes ejecutados e integrados)
**Última actualización:** 2026-07-10
**Depende de:** [01-vision-y-alcance.md](01-vision-y-alcance.md), [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) (ADR-002/005/008/009/011), [03-modelo-datos-dataverse.md](03-modelo-datos-dataverse.md)

Todo el stack y los límites de plataforma citados fueron **verificados en julio 2026** (fuentes en §12). Este documento define el diseño; no contiene código de producción — contiene contratos, reglas y presupuestos que el código debe cumplir.

---

## 1. Stack técnico (verificado)

| Necesidad | Elección | Licencia | Justificación verificada |
|-----------|----------|----------|--------------------------|
| Runtime | Plugins C# **.NET Framework 4.6.2**, proyectos **SDK-style** | — | Requisito de plug-in packages |
| Empaquetado | **Plug-in package** (NuGet, **GA**) | — | ILMerge **explícitamente no soportado** por Microsoft; dependent assemblies es el enfoque recomendado "desde el día uno". Límite verificado: **16 MB por assembly** (límite del .nupkg completo: NO VERIFICADO — vigilar) |
| PDF (abrir existente, dibujar PNG en coordenadas, agregar página) | **PDFsharp ≥ 6.2.4** (jamás 6.2.0) | MIT | Puramente managed, netstandard2.0; `XGraphics.FromPdfPage` + `DrawImage` + `AddPage` cubren ADR-011. iText descartado (AGPL); QuestPDF descartado (no edita PDFs existentes); Aspose/Syncfusion descartados (costo sin necesidad). **Transparencia validada por spike (2026-07-10): ver §10** |
| SHA-256 | `System.Security.Cryptography.SHA256` (BCL) | — | Sin dependencias |
| Cliente RFC 3161 | **BouncyCastle.Cryptography 2.6.x** (`Org.BouncyCastle.Tsp`) + `HttpClient` | MIT-X11 | `TimeStampRequest/Response/Token`; targets net461/netstandard2.0. **`Rfc3161TimestampRequest` de la BCL NO existe en .NET Framework** (solo .NET Core 3+/5+) — descartado. Requisitos de uso en §6.4 |
| QR → PNG | **QRCoder 1.8.x**, renderer **`PngByteQRCode`** | MIT | Emite bytes PNG **sin System.Drawing**. El renderer clásico `QRCode` usa System.Drawing — **prohibido** |
| Decode/análisis/resize PNG | **SixLabors.ImageSharp 2.1.11+** (rama 2.1.x) | **Apache-2.0** (la 2.1.x; el Split License comercial aplica desde 3.0, que requiere .NET 6+) | 100% managed, netstandard2.0: decode de píxeles (alfa, contraste, nitidez) + resize. Fijar ≥ 2.1.11 y revisar advisories por release |
| System.Drawing / GDI+ | **PROHIBIDO** en todo el assembly | — | Sin soporte server-side de Microsoft; un fallo GDI+ mata el proceso del sandbox. (Prohibición explícita en docs de Dataverse: NO VERIFICADO — se prohíbe por decisión propia) |

**Restricciones del sandbox (verificadas):**
- HTTP saliente: **solo HTTP/HTTPS, solo por nombre DNS** ("IP addresses can't be used"). Endpoints TSA siempre por hostname.
- Sin file system, event log ni registry. Todo binario se procesa **en memoria** — consecuencia de diseño: **nada que deba sobrevivir a un reintento puede vivir solo en memoria** (§7).
- Límite duro de **2 minutos**, wording verificado: "There's a hard 2-minute time limit for a Dataverse message operation to complete. This limit includes executing the intended message operation and all registered synchronous plug-ins" — cubre **toda la operación síncrona**. Para plugins asíncronos, "2 minutos por ejecución" **NO está verificado** como wording actual; sí está documentado que Dataverse **mata cualquier extensión** que exceda umbrales de CPU/memoria/handles. **Decisión conservadora:** el worker se presupuesta como si los 2 minutos aplicaran (§7).
- **Reintentos de plugins asíncronos (verificado):** NO son automáticos ante una excepción común. El sistema reintenta **solo** si el plugin lanza `InvalidPluginExecutionException` con `OperationStatus.Retry` (hasta 4 intentos). Esto define la semántica de fallos de §7.

## 2. Estructura del proyecto

```
src/backend/
  Sigil.Plugins.Core/               # netstandard2.0 — el NÚCLEO PURO (sin SDK de Dataverse)
    Sealing/                        # pipeline de sellado (ADR-011): pasos puros
    Crypto/                         # SHA-256, cliente TSA (BouncyCastle), verificación
    Imaging/                        # validación y normalización de firma (ImageSharp)
    Pdf/                            # composición: firmas, hoja de cierre, QR, metadatos XMP
    Domain/                         # estados (nombres lógicos), reglas de transición, autorización
  Sigil.Plugins/                    # net462 — assembly REGISTRADO (≤16 MB); referencia a Core
    Apis/                           # un plugin por Custom API (entry points, orquestación)
    Data/                           # acceso a tablas sanic_sigil_tbl_*, env vars, seam de File blocks
  Sigil.Plugins.Core.Tests/         # net8 — corre en CUALQUIER plataforma (Linux dev/CI barato)
  Sigil.Plugins.Tests/              # net462 — FakeXrmEasy/stub (corre en runner Windows)
tests/conformance/
  Sigil.Conformance.Tests/          # net8 + ServiceClient — pruebas de EXISTENCIA de todo
                                    # componente del ambiente (doc 11 §1 regla 5)
```

**Precisión estructural (2026-07-13, reemplaza el "único assembly" de la versión anterior):** el núcleo puro se separa en `Sigil.Plugins.Core` (**netstandard2.0**) porque un test project moderno (.NET 8) no puede referenciar un assembly net462 — con el split, el 90% del motor se testea en cualquier sistema operativo y runner. El plugin package ya transporta dependent assemblies (PDFsharp, BouncyCastle…), así que Core viaja como una dependencia más — cero cambio en el modelo de despliegue.

Reglas:
- **Separación núcleo/cáscara:** los pasos del pipeline (hash, composición PDF, TSA, métricas) son clases puras que reciben `byte[]`/streams — sin `IOrganizationService` adentro. Los plugins de `Apis/` orquestan. El 90% del motor es testeable sin FakeXrmEasy.
- Assembly firmado; **todas** las dependencias del package firmadas (requisito verificado si el principal está firmado).
- `ITracingService` con tracing seguro: nunca PII, tokens, base64 ni hashes completos (solo primeros 8 hex para correlación); siempre IDs, longitudes y duraciones por paso.

## 3. Superficie completa de Custom APIs

**Convenciones generales:**
- Nomenclatura según [12-convenciones-nomenclatura.md](12-convenciones-nomenclatura.md): Custom APIs = `sanic_sigil_capi_<VerboPascalCase>`; backing plugin síncrono salvo el worker (§7). `IsPrivate = true` como higiene de metadata, **sabiendo que NO es control de acceso** (verificado: solo oculta del `$metadata`; cualquier usuario autenticado puede invocar una API si conoce su firma). El control real: **Execute Privileges (§3.2) + autorización en el plugin (§3.3)**.
- Los binarios viajan **base64 en parámetros String, en ambos sentidos** (upload y download) — el upload/download File del SDK de Code Apps sigue en preview y no dependemos de él. Verificado: el payload máximo por request a Dataverse es **128 MB**; un PDF de 20 MB ≈ 27 MB base64 entra holgado. **Orden de validación obligatorio: longitud del string ANTES de decodificar** (decodificar basura gigante para después medirla regala memoria del sandbox y arriesga el kill por recursos).
- Toda escritura ocurre vía **servicio elevado (contexto de sistema)**; la identidad del llamante se toma de `context.InitiatingUserId` para autorización y snapshots.

### 3.1 Catálogo

| Custom API | Binding | Rol |
|------------|---------|-----|
| `sanic_sigil_capi_CreateTransaction` | Unbound | Crea el borrador (RF-25/26). In: `Name`, `Message?`, `RoutingType`, `ExpirationDays?`, `PdfBase64`, `ParticipantsJson`, `ZonesJson?`. Out: `TransactionId` |
| `sanic_sigil_capi_UpdateDraft` | Bound | Edita un borrador (todos los inputs de Create, opcionales). **Si reemplaza el PDF, revalida las zonas persistidas contra el nuevo documento** (páginas inexistentes → error explícito listando las zonas afectadas, no borrado silencioso) |
| `sanic_sigil_capi_DeleteDraft` | Bound | Borra un borrador |
| `sanic_sigil_capi_SendTransaction` | Bound | Borrador → Pendiente de Firma: **valida que TODO participante tenga ≥1 zona (RF-28 — bloquea el envío si falta)**, calcula y persiste `senton`/`expireson` y **`sanic_sigil_contenthash`** (ancla temprana, §7 paso 2), comparte con participantes (GrantAccess Read + cascada), activa turnos con `turnactivatedon`, evento |
| `sanic_sigil_capi_SubmitSignature` | Bound | Registra la intención de firma (RF-04). Detalle crítico de concurrencia en §5. Copia el PNG de la versión vigente a `sanic_sigil_signaturesnapshot` **+ setea el lookup `mastersignatureid` a la versión exacta usada** (doc 03 §4.2/§4.5), snapshots de identidad, **crea el evento de firma con `documenthash` = SHA-256 del documento de contenido servido a ese firmante** (verificación cruzada — doc 03 §4.6) y lookup al participante; transición a Firmado Parcialmente si es la primera, activa siguiente turno o **Sellando** si es el último. Out: `IsLastSigner` |
| `sanic_sigil_capi_RejectTransaction` | Bound | Rechazo (RF-13): registra motivo, transacción → Rechazado, evento. In: `Reason` |
| `sanic_sigil_capi_CancelTransaction` | Bound | Cancelación por el creador (RF-30/Q-08): transacción → Cancelado, evento tipo 12, notificación vía cambio de estado. In: `Reason?`. Usa el lock de §5; estados elegibles: Pendiente de Firma, Firmado Parcialmente y **Error de Sellado** (cierra el ciclo de vida de fallos deterministas — doc 06 T13); jamás Sellando |
| `sanic_sigil_capi_GetDocumentContent` | Bound | **Lectura de binarios** (la operación más frecuente del sistema): devuelve `PdfBase64` del documento de contenido (para visualizar antes de firmar — RF-03) o del final (RF-05/24). In: `DocumentType` (`content`\|`final`) |
| `sanic_sigil_capi_GetMasterSignature` | Unbound | Devuelve el PNG normalizado **propio** (preview en onboarding y en el editor de zonas). Out: `ImageBase64?`, `ValidatedOn?` |
| `sanic_sigil_capi_ValidateMasterSignature` | Unbound | Valida (cómputo local — ADR-009) y normaliza la Firma Maestra; **crea una NUEVA versión y desactiva la anterior en la misma operación** (versionado 2026-07-13 — doc 03 §4.5; el historial jamás se pisa). In: `ImageBase64`. Out: `IsValid`, `FailureReasons?`, `MetricsJson`, `NormalizedImageBase64?` |
| `sanic_sigil_capi_VerifyDocument` | Unbound | Verificación (RF-20/21, ADR-007). Solo `TransactionId` → constancia; con `Sha256Hash` → además veredicto contra `sanic_sigil_finalhash`. **Verificación cruzada extendida (2026-07-13):** el veredicto incluye la comprobación del historial — todos los `documenthash` de eventos de firma iguales entre sí e iguales a `contenthash`, y columnas de sistema de los eventos sin modificación posterior (`modifiedon==createdon`) — con su límite declarado (doc 03 §4.6). **La constancia incluye `hash_final` en claro** para verificación manual independiente (`sha256sum`/`Get-FileHash` — no es secreto: describe un archivo ya distribuido). Escribe el evento tipo 11 (RNF-04). Out: `Found`, `IsIntact?`, `MetadataJson` (incl. `finalHashHex`, `historyIntact`), `TsaTokenBase64?` |
| `sanic_sigil_capi_RetrySealing` | Bound | **Error de Sellado → Sellando** (re-dispara el worker). Sin esta API el estado de error sería un callejón sin salida (doc 03 §6: nadie tiene Update directo) |
| `sanic_sigil_capi_ExpireTransactions` | Unbound (job) | RF-27: expira transacciones vencidas. **Estados elegibles: solo Pendiente de Firma y Firmado Parcialmente** — jamás Sellando ni Error de Sellado (expiraría una transacción con todas las firmas puestas por un fallo transitorio). **Segunda responsabilidad — saneamiento T14 (doc 06 R7):** transiciona a Error de Sellado toda transacción en Sellando > 24 h sin actividad del worker. Out: `ExpiredCount`, `SanitizedCount` |
| `sanic_sigil_capi_ProcessReminders` | Unbound (job) | RF-12: selecciona participantes en Turno Activo **cuya transacción está en Pendiente de Firma o Firmado Parcialmente** (filtro obligatorio — doc 06 §3: sin él, recordaría eternamente transacciones terminales) con `turnactivatedon` vencido según cadencia y sin recordatorio reciente, actualiza `lastreminderon`, crea eventos tipo 5, y devuelve la lista a notificar. Out: `RemindersJson` |
| `sanic_sigil_capi_ResealPending` | Unbound (job) | ADR-005: reintenta TSA sobre ledgers en `Re-sellado pendiente`. **Si `sanic_sigil_env_TsaEnabled` = false, transiciona esos ledgers a `Sin sello TSA`** (evita huérfanos eternos bajo una etiqueta que promete un reintento que no va a ocurrir) + evento. Out: `ResealedCount`, `MovedToNoTsaCount`, `StillPendingCount` |

**Disparadores de jobs (confirmado por el equipo, 2026-07-13):** los jobs se disparan **EXCLUSIVAMENTE por cloud flows** (el flow programado diario invoca `ExpireTransactions`, `ProcessReminders` y `ResealPending` bajo la conexión del Service Principal) — jamás otro mecanismo de scheduling. El flow solo dispara y notifica; toda lógica y transición vive en el plugin (ADR-003, doc 06 R3).

### 3.2 Execute Privileges (control de acceso real)

Cada Custom API declara `ExecutePrivilegeName`. Dos niveles:

| Nivel | Privilegio (`ExecutePrivilegeName`) | Lo tiene | APIs |
|-------|--------------------------------------|----------|------|
| Usuario | `prvReadsanic_sigil_tbl_transaction` (privilegio base del rol **Sigil \| SR \| User**) | Todos los usuarios de la app | Todas las APIs de negocio (Create/Update/Delete Draft, Send, Submit, Reject, Cancel, GetDocumentContent, GetMasterSignature, ValidateMasterSignature, VerifyDocument, RetrySealing) |
| Servicio | `prvWritesanic_sigil_tbl_ledgerentry` (solo el rol **Sigil \| SR \| Service** lo posee) | Únicamente el Service Principal | Los tres jobs: `ExpireTransactions`, `ProcessReminders`, `ResealPending` |

Con esto, un usuario común **no puede** invocar los jobs aunque conozca su firma. Verificado: `ExecutePrivilegeName` es el mecanismo soportado; `IsPrivate` no protege nada.

### 3.3 Autorización de negocio (en el plugin, contra `InitiatingUserId`)

| API | Regla de autorización |
|-----|----------------------|
| `UpdateDraft` / `DeleteDraft` / `SendTransaction` | El llamante **es el creador** (owner) y el estado es Borrador |
| `CancelTransaction` | El llamante **es el creador** y el estado es Pendiente de Firma o Firmado Parcialmente |
| `SubmitSignature` | El llamante **es el participante** de esa transacción y su estado es **Turno Activo** |
| `RejectTransaction` | Ídem Submit: participante con Turno Activo. (Un participante Pendiente en secuencial no puede rechazar — aún no le llegó el documento; si el negocio pide otra cosa, se registra como cambio) |
| `RetrySealing` | El llamante es el **creador** de la transacción y el estado es Error de Sellado |
| `GetDocumentContent` | El llamante es creador **o** participante de la transacción. `final` solo en Completado. **`content` para participantes solo desde Pendiente de Firma en adelante** (el creador siempre): un participante NO puede leer borradores no enviados — la existencia del registro de participante no implica que el documento ya le fue presentado |
| `GetMasterSignature` / `ValidateMasterSignature` | Solo operan sobre la firma **del propio llamante** (jamás aceptan un userId como parámetro) |
| `VerifyDocument` | Cualquier usuario autenticado con licencia. **Tradeoff declarado:** quien posee un `txId` (GUID no enumerable, impreso solo en documentos legítimamente distribuidos) obtiene metadatos de firmantes y el token TSA — es el propósito del QR (C-08); la contrapartida es el evento tipo 11 que registra cada verificación con actor |

**Regla general:** con el modelo "todo escribe el sistema", **cada validación de autorización que falte es una escalada de privilegios de facto**. Ninguna API se implementa sin su fila en esta tabla; los tests de FakeXrmEasy cubren el caso negativo de cada regla (doc 11).

### 3.4 Validaciones de entrada (todas las APIs que reciben contenido)

- `PdfBase64`: longitud del string ≤ límite derivado de `sanic_sigil_env_MaxPdfSizeKB` **antes de decodificar**; magic bytes `%PDF-`; apertura exitosa con PDFsharp; **documento cifrado/protegido → rechazo**; **documento con firmas digitales previas → rechazo** con mensaje claro (incrustar imágenes las invalidaría — y una "firma sobre firma" es una ambigüedad probatoria que no aceptamos). Postura PDF/A: se acepta, advirtiendo que el sellado rompe la conformidad PDF/A estricta (el documento final es un PDF estándar).
- `ParticipantsJson`: schema §4; usuarios existentes y habilitados; sin duplicados; el creador puede ser firmante; máximo de participantes según `sanic_sigil_env_MaxParticipants` (env var, default 20).
- `ZonesJson`: schema §4; página existente en el PDF; coordenadas 0–100; el userId de cada zona pertenece a un participante. **Completitud (RF-28): en `SendTransaction`, TODO participante debe tener ≥1 zona — error explícito listando a quiénes les falta.**
- `ImageBase64` (firma): longitud antes de decodificar; PNG válido decodificable por ImageSharp.

## 4. Contratos JSON (deuda del doc 03, saldada)

```jsonc
// ParticipantsJson (input de Create/UpdateDraft)
[ { "userId": "<guid>", "order": 1 } ]            // order solo en secuencial

// ZonesJson (input; 1..N por participante — obligatorias al enviar, RF-28)
[ { "userId": "<guid>", "page": 3, "x": 62.5, "y": 81.0, "w": 22.0, "h": 8.0 } ]

// ZonesJson: OBLIGATORIO al enviar — cada participante con ≥1 zona (RF-28, 2026-07-13);
// en borrador puede estar incompleto (SendTransaction valida la completitud)

// sanic_sigil_env_SignatureImageSpec (env var)
{ "targetWidthPx": 600, "targetHeightPx": 200, "maxKB": 150,
  "minAlphaRatio": 0.15, "minRmsContrast": 0.25, "minLaplacianVar": 80 }
// umbrales iniciales: calibrar con imágenes reales en la implementación
// contrato de salida de la normalización: PNG RGBA 8-bit NO entrelazado (ver §10, riesgo PDFsharp)

// sanic_sigil_env_TsaEndpoints (env var — orden = prioridad; hostnames obligatorios, jamás IPs)
{ "endpoints": [
    { "url": "https://timestamp.digicert.com", "timeoutSeconds": 10, "minIntervalSeconds": 0 },
    { "url": "https://timestamp.sectigo.com",  "timeoutSeconds": 10, "minIntervalSeconds": 15 } ] }
// minIntervalSeconds: rate limit por endpoint. Sectigo documenta ≥15 s entre requests
// automatizados — sanic_sigil_capi_ResealPending DEBE respetarlo al procesar lotes (ADR-005)

// RemindersJson (output de sanic_sigil_capi_ProcessReminders — insumo del flow de notificación)
// AUTOSUFICIENTE: el job ya tiene todo en memoria; el flow NO hace lookups para componer (doc 08 W3)
[ { "participantId": "<guid>", "userId": "<guid>", "transactionId": "<guid>",
    "transactionName": "...", "daysWaiting": 5,
    "recipientEmail": "...", "recipientName": "...", "recipientLanguage": "es|en",
    "senderName": "...", "creatorMessage": "...", "expiresOnUtc": "..." } ]

// sanic_sigil_signersummary (columna del ledger, escrita por el pipeline)
{ "signers": [ { "name": "...", "email": "...", "signedOnUtc": "..." } ],
  "routing": "sequential", "completedOnUtc": "...",
  "tsa": { "status": "sealed|none|pending", "tokenGenTimeUtc": "..." } }
// tokenGenTimeUtc: si el token llegó por re-sellado, esta fecha difiere de sealedon —
// el nivel de evidencia muestra AMBAS (el token prueba existencia a SU fecha, no antes)
```

*Los JSON de env vars se mantienen < 2.000 caracteres (límite verificado de `RetrieveEnvironmentVariableValue`).*

## 5. Concurrencia (diseño explícito — no opcional)

**El problema:** dos firmantes en paralelo envían `SubmitSignature` simultáneamente; cada ejecución lee los participantes para decidir si es el último. Sin serialización: ambos ven al otro pendiente → **nadie transiciona a Sellando → transacción zombi**. O ambos se creen últimos → doble worker. El mismo patrón afecta doble-click, UpdateDraft/Send concurrentes y Reject/Submit cruzados.

**La solución (patrón de lock de fila):** todo plugin que decide sobre el estado compartido de una transacción ejecuta, **como primera operación dentro de su transacción de BD**, un `Update` sobre la fila de `sanic_sigil_tbl_transaction` usando **exclusivamente la columna técnica `sanic_sigil_locktoken`** (doc 03 §4.1). **PROHIBIDO lockear escribiendo el status**: los triggers de los flows de notificación filtran por `sanic_sigil_status` y disparan **aunque el valor escrito sea idéntico al existente** (verificado — doc 08 §7); un lock por status generaría notificaciones duplicadas en cada operación. Ese update toma el **lock de fila de SQL** hasta el commit — las ejecuciones concurrentes se serializan detrás. Después del lock: re-leer el estado (participantes, status) y decidir sobre datos ya serializados.

Reglas derivadas:
- `SubmitSignature` es **idempotente por participante**: si al re-leer tras el lock el participante ya está Firmado, retorna éxito sin efectos (doble click resuelto).
- La decisión `IsLastSigner` se toma **después** del lock, contando participantes no-Firmado sobre la lectura serializada — exactamente uno verá cero pendientes.
- `SendTransaction`, `RejectTransaction`, `UpdateDraft`, `DeleteDraft` y `RetrySealing` usan el mismo lock + revalidación de estado (una `UpdateDraft` que llega durante el `Send` falla limpiamente con "la transacción ya no es un borrador").
- Los tests de concurrencia de estas reglas son parte del criterio de done (doc 11).

## 6. Especificaciones criptográficas y de composición

### 6.1 Contrato de coordenadas (compartido con el frontend — doc 05)
- Origen **arriba-izquierda**, unidades **% del ancho/alto visible de la página**. El PDF nativo usa origen abajo-izquierda; PDFsharp `XGraphics.FromPdfPage` abstrae a arriba-izquierda — la conversión %→puntos usa el tamaño de página que reporta PDFsharp.
- **Páginas rotadas (`/Rotate` 90/180/270):** las coordenadas refieren SIEMPRE a la orientación **visual** (lo que el usuario ve en el visor). **Evidencia de spike (2026-07-10, `spikes/RESULTADOS.md`): XGraphics trabaja en orientación RAW-MEDIA y NO compensa `/Rotate`** — `gfx.PageSize` no hace swap con Rotate=90 y un marcador en (0,0) renderiza arriba-derecha visual. La transformación manual es obligatoria, no opcional. **Caso de prueba obligatorio:** PDF escaneado apaisado.
- **`CropBox ≠ MediaBox`:** las coordenadas refieren al **CropBox** (área visible). Caso de prueba obligatorio.
- El editor visual del frontend (doc 05) usa este mismo contrato — un solo sistema de coordenadas en todo Sigil.

### 6.2 Hoja de cierre
- Una página consolidada, **con overflow a páginas adicionales** si los firmantes no entran (ADR-011 actualizado). Layout: estampa por firmante (imagen del **snapshot** congelado al firmar, nombre, correo, timestamp UTC), `hash_contenido` en texto claro, número de ledger, QR.
- El QR (`PngByteQRCode`) codifica `{sanic_sigil_env_AppPlayUrl}?screen=verify&txId={guid}` (~100 caracteres — muy por debajo de la capacidad de un QR de corrección M).

### 6.3 Timestamps
Todos los timestamps probatorios (estampa, ledger, eventos) se escriben y muestran en **UTC explícito** (sufijo "UTC" en la estampa). La conversión a hora local es responsabilidad exclusiva de la UI.

### 6.4 Cliente TSA (requisitos no negociables)
- `TimeStampRequest` con **`CertReq = true`** (el token DEBE incluir el certificado del firmante de la TSA — sin esto, la "validación independiente" de RF-16 puede volverse imposible años después, cuando el certificado ya no esté publicado) y **nonce aleatorio** (`RandomNumberGenerator`).
- Validación en dos niveles antes de persistir: `TimeStampResponse.Validate(request)` (correspondencia nonce/imprint/política — NO valida la firma) **y** `TimeStampToken.Validate(verifier)` con el certificado incluido (validez criptográfica de la firma del token). Token que no pasa ambas → se descarta y se intenta el siguiente endpoint.
- Rate limit por endpoint según config (§4) — crítico en `sanic_sigil_capi_ResealPending`.
- **HTTPS obligatorio:** la validación de `env_TsaEndpoints` **rechaza endpoints `http://`** (el sandbox permite HTTP saliente — verificado — pero un canal claro habilita MITM del token; doc 07). 
- **Límite honesto declarado:** la validación §6.4 verifica que el token está bien formado y firmado por el certificado que él mismo incluye — **no** valida la cadena hasta una raíz confiable. Un endpoint TSA comprometido podría emitir tokens "válidos" con un cert arbitrario; la verificación independiente (`openssl ts` contra el CA real) lo detectaría. Postura registrada en doc 07 (amenaza A14); endurecimiento futuro posible: pin de CAs esperadas por endpoint.

## 7. Pipeline de sellado (worker asíncrono — ADR-011)

**Disparador:** step **asíncrono** en Update de `sanic_sigil_tbl_transaction`, filtering attribute `sanic_sigil_status`, post-operation, contexto de sistema.

**Guards del disparador (precisos — un guard mal puesto mata el pipeline; uno de menos lo corrompe):**
- **Guard de post-image:** `sanic_sigil_status == Sellando` en la post-image → candidato a ejecutar; otro valor → return. Neutraliza el auto-retrigger (el paso 9 escribe Completado → re-disparo → sale).
- **Guard de estado ACTUAL bajo lock (obligatorio — la post-image NO basta):** un reintento encolado conserva la post-image vieja (`Sellando`) aunque el estado actual ya sea otro (saneamiento T14 + `RetrySealing` pudieron correr en el medio). El worker, como primera acción, toma el lock de fila (§5), **relee el estado actual** y aborta si ≠ Sellando; y **verifica la existencia del ledger ANTES del paso 1** — si existe, salta directo a completar los pasos restantes (9), jamás recompone ni re-sube el archivo. Sin estos dos checks, un reintento zombi que despierta tras T14+T10 subiría un segundo `finalfile` con bytes distintos al hash del ledger — el escenario prohibido de la sección "Idempotencia".
- **Depth guard (otro propósito):** el worker corre legítimamente con `Depth ≥ 2` (lo dispara el Update que hace `SubmitSignature`/`RetrySealing`). Un guard `Depth > 1 → return` **desactivaría el sellado por completo**. El depth guard correcto es un umbral alto anti-loop (`Depth > 8 → abortar con trace`), no un filtro de negocio.

**Pasos (orden estricto — corregido para idempotencia real):**

| # | Paso | Detalle | Presupuesto |
|---|------|---------|-------------|
| 1 | Descargar PDF de contenido | `InitializeFileBlocksDownload` + bloques 4 MB | ~2–6 s (20 MB) |
| 2 | Verificar `hash_contenido` | SHA-256 de los bytes descargados **debe coincidir** con el persistido en `SendTransaction` (doc 03). Mismatch → Error de Sellado + evento crítico: el archivo cambió entre el envío y el sellado — jamás se sella contenido adulterado | < 1 s |
| 3 | Incrustar firmas | Por participante: `sanic_sigil_signaturesnapshot` (imagen congelada al firmar) dibujada en **sus zonas obligatorias** (RF-28 — sin default; el guard de envío garantiza que existen) — contrato de coordenadas §6.1 | ~2–5 s |
| 4 | Hoja de cierre + **metadatos XMP** | §6.2 (con overflow) + escritura de metadatos del documento (marca "Firmado con Sigil", número de ledger, `hash_contenido`, URL de verificación — visibles en las propiedades del documento en Acrobat; posible porque ocurre ANTES del paso 5) | ~1–3 s |
| 5 | `hash_final` | SHA-256 del PDF final serializado (una sola serialización; los bytes quedan en memoria para 6 y 7) | < 1 s |
| 6 | Token TSA (si `sanic_sigil_env_TsaEnabled`) | §6.4 sobre `hash_final`; fallback en orden; si todos fallan → se continúa con estado `Re-sellado pendiente` | ~1–10 s (o skip) |
| 7 | **Subir PDF final** | `InitializeFileBlocksUpload` a `sanic_sigil_finalfile`. **Antes que el ledger, deliberadamente** — ver "Idempotencia" | ~2–6 s |
| 8 | **Crear registro de ledger** | Contexto de sistema; hashes + token + summary. El **alternate key** en `sanic_sigil_transactionid` hace el insert idempotente | < 1 s |
| 9 | Transicionar estados + eventos | Transacción → Completado, `completedon`; eventos; GrantAccess de eventos; el cambio de estado dispara los flows de notificación (ADR-003) | < 1 s |

**Total estimado: ~10–35 s** (PDF de 20 MB) — margen ≥ 3× dentro del presupuesto conservador. Plan B ya decidido (ADR-008): Azure Function vía Service Bus, mismo modelo de estados.

**Idempotencia (razonamiento completo — el orden 7→8 no es estético):**
El PDF final vive solo en memoria y **la serialización PDF no es determinística** (timestamps de metadata, IDs de objetos): recomponerlo en un reintento produce bytes distintos → hash distinto. Por eso el artefacto durable se escribe ANTES que el hash que lo referencia:
- Falla el paso 7 (upload): no hay ledger todavía. El reintento recompone desde el paso 1 — el nuevo `hash_final` será distinto, y no importa: no hay registro previo que contradiga. Consistente.
- Falla el paso 8 (ledger): el PDF final YA es durable en `sanic_sigil_finalfile`. El reintento detecta final file existente sin ledger → **re-descarga esos bytes exactos**, recalcula `hash_final` de lo durable, re-pide TSA si corresponde y crea el ledger. El hash del ledger siempre describe los bytes que existen. Consistente.
- Falla el paso 9: ledger existe (alternate key lo detecta) → el reintento solo completa transiciones y eventos. Consistente.
- El orden inverso (ledger antes que archivo) produce el desastre inverso: hash grabado en un registro inmutable cuyos bytes ya no existen — transacción **permanentemente inverificable**. Prohibido.

**Semántica de fallos (alineada con el comportamiento verificado de la plataforma):**
- **Fallo transitorio** (timeout HTTP del download/upload, deadlock de BD): el worker lanza `InvalidPluginExecutionException` con **`OperationStatus.Retry`** — el sistema reintenta (hasta 4). Los reintentos re-entran por el flujo idempotente de arriba.
- **Fallo definitivo** (PDF corrupto, mismatch de `hash_contenido`, agotados los retries): transacción → **Error de Sellado** + evento con detalle accionable + trace técnico. Sin ledger parcial.
- **Salida del error:** exclusivamente `sanic_sigil_capi_RetrySealing` (§3.1) — no existe "reintento automático" desde Error de Sellado; ese estado ES el fallo definitivo (coherente con ADR-008).

## 8. Reglas transversales

- **Env vars:** lectura con caché por ejecución (verificado: la plataforma no cachea).
- **Excepciones:** `InvalidPluginExecutionException` con mensajes accionables en APIs síncronas; en el worker, mensaje de negocio al evento, detalle técnico al trace.
- **Sin secretos:** el descarte de AI Vision (ADR-009) dejó al backend sin secretos ni managed identity en Fase 1. La TSA no requiere autenticación.
- **Eventos:** cada API que transiciona estado escribe su evento (doc 03 §4.6) — incluida la verificación (tipo 11).

## 9. Registro y despliegue

- Plugin package vía `pac plugin push` dentro de la solución Sigil (RNF-05); steps por Custom API + el step asíncrono del worker.
- Service Principal: rol **Sigil \| SR \| Service** + perfil **Sigil \| FLS \| Evidence Writer** + privilegio de los jobs (§3.2).
- **Nota de runbook (doc 09):** los índices de los alternate keys se crean **asíncronamente** al importar la solución — verificar su activación antes de habilitar tráfico; la idempotencia del paso 8 depende de ese índice.

## 10. Riesgos y NO VERIFICADOS registrados

**Spikes ejecutados el 2026-07-10** (adelantados por decisión del equipo — evidencia completa y artefactos en `spikes/RESULTADOS.md`; corridos en .NET 8/Linux: mismas librerías managed, pendiente solo la corrida en sandbox Dataverse real):

| Ítem | Estado | Resultado / Mitigación |
|------|--------|-----------|
| **PDFsharp + PNG con transparencia** (issue empira/PDFsharp#187) | **RESUELTO POR SPIKE — PASS** | El issue NO se reproduce en **6.2.4**: `/SMask` presente en el XObject y render verificado por píxeles (zona transparente muestra el fondo; alfa 128 da el blend exacto). Regla: **pin ≥ 6.2.4, jamás 6.2.0**. Contrato de normalización: PNG RGBA 8-bit no entrelazado (§4) |
| **BouncyCastle RFC 3161** | **RESUELTO POR SPIKE — PASS** (en .NET 8) | Contra Sectigo real: Granted en 815 ms, nonce verificado, **CertReq honrado (3 certs embebidos)**, `token.Validate()` OK, cruce con `openssl ts` OK. **Token: 6.633 bytes DER = 8.844 chars base64 → presupuesto de columna memo: ~12K chars** (doc 03). Pendiente: corrida en sandbox Dataverse cuando haya ambiente |
| **DigiCert inaccesible desde la red del spike** (TCP timeout; Sectigo respondió en 120 ms) | Finding de red, no de librería | Re-probar desde la red corporativa; refuerza la regla de **≥2 TSAs configuradas** (ADR-005) |
| Ejecución del stack dentro del **sandbox real de Dataverse** | PENDIENTE (requiere ambiente) | Primera tarea al provisionar el ambiente Dev; el riesgo residual es bajo (stack 100% managed + HTTPS por DNS permitido) |
| Límite del .nupkg del plugin package | NO VERIFICADO (16 MB por assembly sí) | Stack ~6–8 MB; medir en el primer push |
| "2 min por ejecución" en async | NO VERIFICADO como wording actual | Presupuesto conservador §7 + plan B Azure Function |
| ImageSharp 2.1.x sin backports futuros | Riesgo aceptado | ≥2.1.11, advisories por release; plan B BigGustave |
| Upload/download File desde Code App (preview) | Cerrado como dependencia | Base64 por Custom API en ambos sentidos (§3) |
| ~~Cancelación por el creador~~ | **Q-08 cerrada (2026-07-10): SÍ** | Resuelto: `sanic_sigil_capi_CancelTransaction` + estado *Cancelado* (RF-30); transiciones en doc 06 |

## 11. Trazabilidad

| RF/ADR | Sección |
|--------|---------|
| RF-01/02 | `ValidateMasterSignature` + `GetMasterSignature` + Imaging (§3, §4) |
| RF-03/05/24 (ver/descargar PDFs) | `GetDocumentContent` (§3.1) |
| RF-04 | `SubmitSignature` + worker (§5, §7) |
| RF-12 | `ProcessReminders` (§3.1) |
| RF-13 | `RejectTransaction` + autorización §3.3 |
| RF-14/15/16/19 | Pipeline §7 pasos 2–6 + §6.2/6.4 |
| RF-20/21 + evento de verificación (RNF-04) | `VerifyDocument` (§3.1, §3.3) |
| RF-25/26/27 | `CreateTransaction`/`SendTransaction`/`ExpireTransactions` |
| RF-28 | ZonesJson + contrato de coordenadas §6.1 |
| RF-29 | §7 paso 6 + `ResealPending` |
| ADR-011 | §7 (orden corregido + idempotencia razonada) |
| RNF-02 | Verificación de `hash_contenido` (paso 2), snapshots congelados, autorización §3.3 |
| RNF-03 | Presupuesto §7 |

## 12. Fuentes verificadas

- Plug-in packages (GA, 16 MB, ILMerge no soportado): learn.microsoft.com/power-apps/developer/data-platform/build-and-package
- Límite 2 min + kills por recursos: learn.microsoft.com/power-apps/developer/data-platform/analyze-performance
- Reintentos async solo con `OperationStatus.Retry`: learn.microsoft.com/power-apps/developer/data-platform/handle-exceptions
- Sandbox HTTP por DNS, sin file system: learn.microsoft.com/power-apps/developer/data-platform/access-web-services
- Payload máximo 128 MB por request: learn.microsoft.com/power-apps/maker/data-platform/api-limits-overview
- Custom API: `IsPrivate` no es seguridad; `ExecutePrivilegeName`: learn.microsoft.com/power-apps/developer/data-platform/custom-api
- PDFsharp (MIT, edición de existentes): nuget.org/packages/PDFSharp · docs.pdfsharp.net · issue de transparencia: github.com/empira/PDFsharp/issues/187
- iText AGPL: itextpdf.com/how-buy/AGPLv3-license · QuestPDF no edita: github.com/QuestPDF/QuestPDF/discussions/893
- BouncyCastle.Cryptography: nuget.org/packages/BouncyCastle.Cryptography · `Rfc3161TimestampRequest` sin .NET Framework: learn.microsoft.com/dotnet/api/system.security.cryptography.pkcs.rfc3161timestamprequest
- QRCoder `PngByteQRCode`: github.com/codebude/QRCoder · nuget.org/packages/qrcoder
- ImageSharp 2.1.x: nuget.org/packages/SixLabors.ImageSharp/2.1.10 · sixlabors.com/posts/license-changes · advisories
- Managed identity plugins (GA ago-2025, para futuro): learn.microsoft.com/power-platform/admin/set-up-managed-identity
- AI Vision sin métricas + deprecación: learn.microsoft.com/azure/ai-services/computer-vision/overview-image-analysis · image-analysis-characteristics-and-limitations

---

*Anterior: [03-modelo-datos-dataverse.md](03-modelo-datos-dataverse.md) · Siguiente: 05 — Frontend Code App.*

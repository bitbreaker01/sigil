# Referencia: Custom APIs de Sigil

Contrato de entrada/salida (I/O) **completo** de las **17 Custom APIs** de Sigil, extraído del código real (`tools/Sigil.Deploy/ApiSpec.cs` — la fuente única de verdad del despliegue). Los nombres, tipos, obligatoriedad y binding de cada parámetro son EXACTOS: reflejan lo que se registra en Dataverse y lo que las pruebas de conformidad verifican.

Este documento describe el **contrato**. Para el **comportamiento** (reglas de ciclo de vida, autorización, sellado criptográfico) ver [Backend (03-backend.md)](../desarrollo/03-backend.md) y [Sellado y cripto (04-sellado-y-cripto.md)](../desarrollo/04-sellado-y-cripto.md). Los valores numéricos de los estados aparecen en [Catálogo de choices](./catalogo-de-choices.md).

## Convenciones

- **Binding 🌐 Global (unbound)**: se invoca sin registro objetivo. La firma está compuesta solo por sus parámetros.
- **Binding 🔗 Entity (bound)**: está ligada a la tabla `sanic_sigil_tbl_transaction` y recibe un `Target` (`EntityReference` a la transacción sobre la que opera). El `Target` no aparece en la tabla de parámetros porque es implícito al binding.
- **Execute Privilege**: quién puede invocarla.
  - *Usuario* (default, `prvReadsanic_sigil_tbl_transaction`): lo tiene el rol `Sigil | SR | User`.
  - *Servicio* (`prvWritesanic_sigil_tbl_ledgerentry`): SOLO el rol `Sigil | SR | Service`. Un usuario común NO puede invocar los jobs aunque conozca la firma. Aplica a los tres jobs diarios (ExpireTransactions, ProcessReminders, ResealPending).
- **Tipos**: `String`, `Integer`, `Boolean`, `DateTime`, `Guid` — corresponden al option set de tipo de parámetro de la plataforma.
- **JSON**: los parámetros/propiedades terminados en `Json` transportan estructuras serializadas (una Custom API no devuelve colecciones nativas). Los nombres de propiedad JSON son **camelCase** por contrato con el frontend.

## Tabla resumen

| # | Custom API | Binding | Entrada → Salida |
|---|------------|---------|------------------|
| 1 | `sanic_sigil_capi_CreateTransaction` | 🌐 Global | Name, RoutingType, PdfBase64, ParticipantsJson (+opc) → TransactionId |
| 2 | `sanic_sigil_capi_UpdateDraft` | 🔗 Entity | todos opcionales (null = sin cambio) → *(vacío)* |
| 3 | `sanic_sigil_capi_DeleteDraft` | 🔗 Entity | *(solo Target)* → *(vacío)* |
| 4 | `sanic_sigil_capi_GetDocumentContent` | 🔗 Entity | DocumentType → PdfBase64 |
| 5 | `sanic_sigil_capi_SendTransaction` | 🔗 Entity | *(solo Target)* → *(vacío)* |
| 6 | `sanic_sigil_capi_SubmitSignature` | 🔗 Entity | *(solo Target)* → IsLastSigner |
| 7 | `sanic_sigil_capi_RejectTransaction` | 🔗 Entity | Reason → *(vacío)* |
| 8 | `sanic_sigil_capi_CancelTransaction` | 🔗 Entity | Reason? → *(vacío)* |
| 9 | `sanic_sigil_capi_ValidateMasterSignature` | 🌐 Global | ImageBase64, Persist? → IsValid, FailureReasons?, MetricsJson, NormalizedImageBase64? |
| 10 | `sanic_sigil_capi_RetrySealing` | 🔗 Entity | *(solo Target)* → *(vacío)* |
| 11 | `sanic_sigil_capi_GetMasterSignature` | 🌐 Global | *(sin params)* → ImageBase64, ValidatedOn |
| 12 | `sanic_sigil_capi_GetMasterSignatureHistory` | 🌐 Global | *(sin params)* → HistoryJson |
| 13 | `sanic_sigil_capi_SearchDocuments` | 🌐 Global | filtros/orden/paginación (todos opc) → ResultsJson, Total, NextPagingCookie |
| 14 | `sanic_sigil_capi_VerifyDocument` | 🌐 Global | TransactionId? / Sha256Hash? (≥1) → Found, IsIntact?, MetadataJson, TsaTokenBase64? |
| 15 | `sanic_sigil_capi_ExpireTransactions` | 🌐 Global (Servicio) | *(sin params)* → ExpiredCount, SanitizedCount |
| 16 | `sanic_sigil_capi_ProcessReminders` | 🌐 Global (Servicio) | *(sin params)* → RemindersJson |
| 17 | `sanic_sigil_capi_ResealPending` | 🌐 Global (Servicio) | *(sin params)* → ResealedCount, MovedToNoTsaCount, StillPendingCount, AnchorMismatchCount |

**Conteo: 17 Custom APIs = 8 bound (Entity) + 9 unbound (Global).**
Bound: UpdateDraft, DeleteDraft, GetDocumentContent, SendTransaction, SubmitSignature, RejectTransaction, CancelTransaction, RetrySealing.
Unbound: CreateTransaction, ValidateMasterSignature, GetMasterSignature, GetMasterSignatureHistory, SearchDocuments, VerifyDocument, ExpireTransactions, ProcessReminders, ResealPending.

---

## 1. `sanic_sigil_capi_CreateTransaction`

- **Binding**: 🌐 Global · **Execute Privilege**: Usuario
- **Descripción**: crea el borrador de una transacción de firma. Estado inicial: *Borrador*.

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `Name` | String | Sí | Nombre visible de la transacción. |
| `Message` | String | No | Mensaje del creador para los participantes (aparece en notificaciones/recordatorios). |
| `RoutingType` | String | Sí | Tipo de enrutamiento: `"sequential"` (secuencial, turnos por orden) o `"parallel"` (paralelo, todos a la vez). Otro valor = error de contrato. |
| `ExpirationDays` | Integer | No | Días de validez antes de expirar. Ausente ⇒ default por ambiente (`ExpirationDefaultDays`). Nota: `0` se interpreta como ausente (quirk de Custom API). |
| `PdfBase64` | String | Sí | PDF de contenido en base64. Validado por peso/páginas antes de decodificar. |
| `ParticipantsJson` | String | Sí | Lista de participantes (ver forma abajo). |
| `ZonesJson` | String | No | Zonas de firma por participante (ver forma abajo). Opcional al crear; **obligatorias al enviar** (`SendTransaction`). |

**Forma de `ParticipantsJson`** (array):
```json
[
  { "userId": "GUID-del-usuario", "order": 1 }
]
```
- `userId` (Guid): usuario firmante.
- `order` (int, opcional): orden de firma 1..N **solo en enrutamiento secuencial**; `null` en paralelo.

**Forma de `ZonesJson`** (array — 1..N por participante):
```json
[
  { "userId": "GUID-del-usuario", "page": 1, "x": 10.0, "y": 80.0, "w": 25.0, "h": 8.0 }
]
```
- `userId` (Guid): a qué participante pertenece la zona.
- `page` (int): página del PDF de contenido (1..N).
- `x`, `y`, `w`, `h` (double): rectángulo en **% del ancho/alto visible** de la página, origen arriba-izquierda.

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `TransactionId` | Guid | Id de la transacción creada. |

---

## 2. `sanic_sigil_capi_UpdateDraft`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: edita un borrador. **Todos los campos son opcionales**: `null` = sin cambio.

### Entrada

Mismos campos que Create, todos **opcionales**. Solo se aplican los presentes.

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `Name` | String | No | Nuevo nombre. |
| `Message` | String | No | Nuevo mensaje. |
| `RoutingType` | String | No | Nuevo enrutamiento (`"sequential"`/`"parallel"`). |
| `ExpirationDays` | Integer | No | Nuevos días de validez. |
| `PdfBase64` | String | No | Reemplazo del PDF de contenido. |
| `ParticipantsJson` | String | No | Reemplazo de participantes (misma forma que Create). |
| `ZonesJson` | String | No | Reemplazo de zonas (misma forma que Create). |

### Salida

*(sin propiedades)*

---

## 3. `sanic_sigil_capi_DeleteDraft`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: borra un borrador (elimina los eventos primero).

### Entrada

*(sin parámetros — solo el `Target`)*

### Salida

*(sin propiedades)*

---

## 4. `sanic_sigil_capi_GetDocumentContent`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: devuelve el PDF de contenido o el PDF final en base64. Operación de solo lectura. Autorización: creador O participante; el `final` solo en estado *Completado*; el `content` para participantes solo desde *Pendiente de Firma*.

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `DocumentType` | String | Sí | `"content"` (PDF original de contenido) o `"final"` (PDF sellado final). Otro valor = error de contrato. |

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `PdfBase64` | String | El PDF solicitado, en base64. |

---

## 5. `sanic_sigil_capi_SendTransaction`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: transición *Borrador → Pendiente de Firma*. Valida las zonas, ancla el `contenthash`, comparte el registro y activa los turnos.

### Entrada

*(sin parámetros — solo el `Target`)*

### Salida

*(sin propiedades)*

---

## 6. `sanic_sigil_capi_SubmitSignature`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: registra la intención de firma del participante en turno activo, con snapshot de la Firma Maestra vigente del llamante.

### Entrada

*(sin parámetros — solo el `Target`)*

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `IsLastSigner` | Boolean | `true` si esta firma fue la última pendiente (dispara el sellado). |

---

## 7. `sanic_sigil_capi_RejectTransaction`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: rechazo por un participante en turno activo. Motivo **obligatorio**.

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `Reason` | String | Sí | Motivo del rechazo (queda en el evento y en las notificaciones). |

### Salida

*(sin propiedades)*

---

## 8. `sanic_sigil_capi_CancelTransaction`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: cancelación por el creador. Válida desde *Pendiente de Firma*, *Firmado Parcialmente* o *Error de Sellado*.

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `Reason` | String | No | Motivo de la cancelación (opcional). |

### Salida

*(sin propiedades)*

---

## 9. `sanic_sigil_capi_ValidateMasterSignature`

- **Binding**: 🌐 Global · **Execute Privilege**: Usuario
- **Descripción**: valida (alfa/contraste/nitidez — cómputo local) y normaliza la Firma Maestra del **propio llamante** (jamás acepta un `userId` ajeno). Con `Persist=true` crea la nueva versión vigente y desactiva la anterior en la misma operación; sin él, solo valida (preview antes de confirmar el reemplazo irreversible).

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `ImageBase64` | String | Sí | Imagen de la firma en base64 (PNG con fondo transparente recomendado). |
| `Persist` | Boolean | No | `false` (default) = solo valida/preview; `true` = crea la nueva versión vigente. |

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `IsValid` | Boolean | Si la imagen pasó todos los umbrales. |
| `FailureReasons` | String | Motivos de rechazo, **uno por línea** (solo si `IsValid=false`). |
| `MetricsJson` | String | Métricas calculadas (ver forma abajo). |
| `NormalizedImageBase64` | String | PNG normalizado para el preview (siempre que sea válida, persista o no). |

**Forma de `MetricsJson`** (objeto):
```json
{
  "alphaRatio": 0.42,
  "rmsContrast": 0.31,
  "laplacianVariance": 152.7,
  "width": 600,
  "height": 200,
  "bytes": 48213
}
```
- `alphaRatio` (double): fracción de píxeles totalmente transparentes (alfa == 0).
- `rmsContrast` (double): RMS del apartamiento de la tinta respecto del fondo blanco.
- `laplacianVariance` (double): varianza del Laplaciano sobre la luminancia (nitidez).
- `width`, `height` (int): dimensiones del PNG normalizado, en px.
- `bytes` (int): peso del PNG normalizado.

Ver detalle de umbrales y algoritmo en [Firma Maestra (05-firma-maestra.md)](../desarrollo/05-firma-maestra.md).

---

## 10. `sanic_sigil_capi_RetrySealing`

- **Binding**: 🔗 Entity (bound a `sanic_sigil_tbl_transaction`, recibe `Target`) · **Execute Privilege**: Usuario
- **Descripción**: transición *Error de Sellado → Sellando*. Re-dispara el worker de sellado (idempotente).

### Entrada

*(sin parámetros — solo el `Target`)*

### Salida

*(sin propiedades)*

---

## 11. `sanic_sigil_capi_GetMasterSignature`

- **Binding**: 🌐 Global · **Execute Privilege**: Usuario
- **Descripción**: devuelve el PNG normalizado de la Firma Maestra vigente del **propio llamante**.

### Entrada

*(sin parámetros)*

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `ImageBase64` | String | PNG normalizado de la firma vigente, en base64. |
| `ValidatedOn` | DateTime | Fecha (UTC) en que se validó/creó esa versión. |

---

## 12. `sanic_sigil_capi_GetMasterSignatureHistory`

- **Binding**: 🌐 Global · **Execute Privilege**: Usuario
- **Descripción**: historial de versiones de la Firma Maestra del **propio llamante** (versionado inmutable: nunca se pisa). Sin firmas ⇒ `HistoryJson = "[]"` (no es error).

### Entrada

*(sin parámetros)*

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `HistoryJson` | String | Array de versiones (más nueva primero). |

**Forma de `HistoryJson`** (array):
```json
[
  {
    "version": 3,
    "imageBase64": "PNG-en-base64",
    "validatedOn": "2026-07-16T14:03:00.0000000Z",
    "isActive": true,
    "documents": [
      { "id": "GUID-de-la-transaccion", "name": "Contrato X", "status": 159460004 }
    ]
  }
]
```
- `version` (int): número de versión.
- `imageBase64` (string): PNG normalizado de esa versión.
- `validatedOn` (string ISO-8601): fecha de validación.
- `isActive` (bool): si es la versión vigente.
- `documents` (array): documentos firmados con esa versión — `id` (Guid), `name` (string), `status` (int — ver [Catálogo de choices](./catalogo-de-choices.md)).

---

## 13. `sanic_sigil_capi_SearchDocuments`

- **Binding**: 🌐 Global · **Execute Privilege**: Usuario
- **Descripción**: búsqueda paginada de los documentos en los que el llamante está involucrado (creados ∪ participados). Filtros, orden y paginación server-side; solo lo PROPIO.

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `Text` | String | No | Filtro por texto en el nombre (insensible a mayúsculas). |
| `CreatorId` | Guid | No | Filtra por creador. |
| `Status` | Integer | No | Filtra por estado (valor del choice — ver [Catálogo de choices](./catalogo-de-choices.md)). |
| `ParticipantIds` | String | No | CSV de GUIDs. El doc debe incluir **TODOS** (semántica AND). |
| `SignatureVersion` | Integer | No | Filtra por la versión de firma del llamante usada en el doc. |
| `Sort` | String | No | Orden. Valores: `nameAsc`, `nameDesc`, `sentAsc`, `sentDesc`, `completedAsc`, `completedDesc`, `createdAsc`, `createdDesc` (default `createdDesc`). |
| `PageSize` | Integer | No | Tamaño de página (default 25, máx 100). |
| `PagingCookie` | String | No | Offset opaco de la página anterior (`NextPagingCookie`). Vacío = primera página. |

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `ResultsJson` | String | Array de la página (ver forma abajo). |
| `Total` | Integer | Total de resultados filtrados (no solo la página). |
| `NextPagingCookie` | String | Offset de la siguiente página; `""` = era la última. |

**Forma de `ResultsJson`** (array):
```json
[
  {
    "id": "GUID-de-la-transaccion",
    "name": "Contrato X",
    "state": 159460001,
    "routing": 159460000,
    "creatorId": "GUID-del-creador",
    "creatorName": "Nombre Creador",
    "message": "Mensaje del creador",
    "sentOn": "2026-07-16T14:03:00.0000000Z",
    "expiresOn": "2026-07-23T14:03:00.0000000Z",
    "completedOn": null,
    "createdOn": "2026-07-15T09:00:00.0000000Z",
    "mySignatureVersion": 3,
    "participants": [
      { "userId": "GUID", "name": "Nombre Firmante" }
    ]
  }
]
```
- `state` (int): estado de la transacción (choice `transactionstatus`).
- `routing` (int): tipo de enrutamiento (choice `routingtype`).
- `sentOn`/`expiresOn`/`completedOn`/`createdOn` (string ISO-8601 o `null`).
- `mySignatureVersion` (int o `null`): versión de la firma del llamante en ese doc.
- `participants` (array): firmantes con `userId` (Guid) y `name` (string).

---

## 14. `sanic_sigil_capi_VerifyDocument`

- **Binding**: 🌐 Global · **Execute Privilege**: Usuario
- **Descripción**: verificación de un documento. Dos modos: (a) por `TransactionId` (llega del QR / detalle) → constancia + veredicto contra ese `finalhash`; (b) por `Sha256Hash` solo → búsqueda en el ledger por `finalhash` (soltás cualquier PDF sellado). Ambos parámetros son opcionales, pero **al menos uno es obligatorio** (lo valida el plugin). Cada verificación queda registrada como evento.

### Entrada

| Parámetro | Tipo | Oblig. | Significado |
|-----------|------|--------|-------------|
| `TransactionId` | Guid | No¹ | Id de la transacción (modo QR/detalle). |
| `Sha256Hash` | String | No¹ | SHA-256 hexadecimal (64 caracteres 0-9/A-F) del PDF final. Formato inválido = error de contrato. |

¹ Al menos uno de los dos debe estar presente.

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `Found` | Boolean | Si se encontró un ledger para la transacción/hash. |
| `IsIntact` | Boolean | Veredicto: `Sha256Hash` == `finalhash` del ledger. Solo se emite si se pasó `Sha256Hash`. |
| `MetadataJson` | String | Constancia (ver forma abajo). |
| `TsaTokenBase64` | String | Token TSA en base64 (solo si el ledger lo tiene). |

**Forma de `MetadataJson`** — no hallado:
```json
{ "found": false }
```

**Forma de `MetadataJson`** — hallado (objeto):
```json
{
  "found": true,
  "ledgerNumber": "LEDGER-0001",
  "sealedOnUtc": "2026-07-16T14:03:00.0000000Z",
  "finalHashHex": "abc...64hex",
  "contentHashHex": "def...64hex",
  "tsaStatus": "sealed",
  "historyIntact": true,
  "isIntact": true,
  "signerSummary": "resumen de firmantes",
  "verifiedOnUtc": "2026-07-24T10:00:00.0000000Z"
}
```
- `ledgerNumber` (string): número del asiento del ledger.
- `finalHashHex`/`contentHashHex` (string): hashes en claro (no son secretos — describen un archivo ya distribuido; permiten verificación manual con `sha256sum`).
- `tsaStatus` (string): `"sealed"` (sellado con TSA), `"pending"` (re-sellado pendiente) o `"none"` (sin sello TSA).
- `historyIntact` (bool): verificación cruzada del historial de eventos de firma (todos los `documenthash` iguales entre sí e iguales al `contenthash`, sin edición posterior).
- `isIntact` (bool o `null`): veredicto contra el hash provisto (`null` si no se pasó `Sha256Hash`).

Ver detalle de sellado, TSA y verificación cruzada en [Sellado y cripto (04-sellado-y-cripto.md)](../desarrollo/04-sellado-y-cripto.md).

---

## 15. `sanic_sigil_capi_ExpireTransactions`

- **Binding**: 🌐 Global · **Execute Privilege**: **Servicio** (`Sigil | SR | Service`)
- **Descripción**: job diario. Expira las transacciones vencidas + saneamiento de *Sellando* zombi (registros atascados en sellado).

### Entrada

*(sin parámetros)*

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `ExpiredCount` | Integer | Cuántas transacciones se expiraron. |
| `SanitizedCount` | Integer | Cuántos *Sellando* zombi se sanearon. |

---

## 16. `sanic_sigil_capi_ProcessReminders`

- **Binding**: 🌐 Global · **Execute Privilege**: **Servicio** (`Sigil | SR | Service`)
- **Descripción**: job diario. Selecciona participantes en turno activo con recordatorio vencido por cadencia (solo transacciones en *Pendiente de Firma* o *Firmado Parcialmente*), actualiza su marca de último recordatorio, crea eventos, y devuelve `RemindersJson` **autosuficiente** (el flow no hace lookups para componer la notificación).

### Entrada

*(sin parámetros)*

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `RemindersJson` | String | Array de recordatorios a enviar (ver forma abajo). |

**Forma de `RemindersJson`** (array):
```json
[
  {
    "participantId": "GUID-del-participante",
    "userId": "GUID-del-firmante",
    "transactionId": "GUID-de-la-transaccion",
    "transactionName": "Contrato X",
    "daysWaiting": 4,
    "recipientEmail": "firmante@dominio.com",
    "recipientName": "Nombre Firmante",
    "recipientLanguage": "es",
    "senderName": "Nombre Creador",
    "creatorMessage": "Mensaje del creador",
    "expiresOnUtc": "2026-07-23T14:03:00.0000000Z"
  }
]
```
- `daysWaiting` (int): días que el firmante lleva esperando desde la activación de su turno.
- `recipientEmail` (string): email (o UPN como fallback) del destinatario.
- `recipientLanguage` (string): idioma resuelto del firmante (o default del ambiente).
- `senderName` (string): nombre del creador.
- `creatorMessage` (string): mensaje del creador.
- `expiresOnUtc` (string ISO-8601 o `""`): vencimiento de la transacción.

---

## 17. `sanic_sigil_capi_ResealPending`

- **Binding**: 🌐 Global · **Execute Privilege**: **Servicio** (`Sigil | SR | Service`)
- **Descripción**: job diario. Reintenta la TSA sobre los ledgers con sello pendiente; con la TSA deshabilitada los mueve a *sin sello*.

### Entrada

*(sin parámetros)*

### Salida

| Propiedad | Tipo | Qué es |
|-----------|------|--------|
| `ResealedCount` | Integer | Ledgers re-sellados con éxito (TSA obtenida). |
| `MovedToNoTsaCount` | Integer | Ledgers movidos a *sin sello* (TSA off). |
| `StillPendingCount` | Integer | Ledgers que siguen pendientes tras el intento. |
| `AnchorMismatchCount` | Integer | Ledgers con discrepancia de anclaje (hash no coincide — anomalía). |

Ver detalle de TSA, re-sellado y anclaje en [Sellado y cripto (04-sellado-y-cripto.md)](../desarrollo/04-sellado-y-cripto.md).

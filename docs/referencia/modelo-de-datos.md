# Modelo de Datos

Referencia del modelo de datos de Sigil, generada a partir de la metadata real de la solución (definiciones `Entity.xml` y relaciones). Documenta las 6 tablas de negocio, sus columnas, claves y relaciones.

Cross-references:
- Valores de las columnas de tipo Choice: ver [Catálogo de Choices](catalogo-de-choices.md).
- Reglas de nombres lógicos, prefijos y convenciones: ver [Convenciones de Nomenclatura](convenciones-nomenclatura.md).

## Diagrama de relaciones

`transaction` es la tabla central. Cada flecha apunta desde la tabla que contiene el lookup (`N`) hacia la tabla referenciada (`1`).

```
                        systemuser (Dataverse)
                             ^         ^
                             |         |
                      (userid)|        |(userid)
                             |         |
          mastersignature ---+         |
                 ^ 1                    |
                 |                      |
   (mastersignatureid, opcional)       |
                 | N                    |
              participant --------------+
               ^   ^   | N
               |   |   |
        (participantid) (transactionid, requerido)
               |   |   |
   signaturezone|  |   v
      (N)-------+  |  ┌─────────────────┐
                   |  │   transaction   │  <--- TABLA CENTRAL
      event        |  └─────────────────┘
      (N)----------+     ^            ^
      participantid|     | N          | N
      (opcional)   +-----+            |
      transactionid                   |
      (N, requerido)------------------+
                                      |
                   ledgerentry (N, requerido, 1 por transacción)
                        transactionid ┘  [alternate key]
```

Resumen de lookups de negocio:

| Desde (N) | Columna lookup | Hacia (1) | Obligatorio | Borrado |
|-----------|----------------|-----------|-------------|---------|
| participant | `sanic_sigil_transactionid` | transaction | Sí | Cascade |
| participant | `sanic_sigil_mastersignatureid` | mastersignature | No | RemoveLink |
| participant | `sanic_sigil_userid` | systemuser | Sí | — |
| signaturezone | `sanic_sigil_participantid` | participant | Sí | Cascade |
| ledgerentry | `sanic_sigil_transactionid` | transaction | Sí | RemoveLink |
| event | `sanic_sigil_transactionid` | transaction | Sí | Cascade |
| event | `sanic_sigil_participantid` | participant | No | RemoveLink |
| mastersignature | `sanic_sigil_userid` | systemuser | Sí | — |

Todas las tablas incluyen además las columnas de sistema estándar de Dataverse (`createdon`, `createdby`, `modifiedon`, `modifiedby`, `ownerid`, `statecode`/`statuscode`, etc.), que no se detallan aquí por ser comunes a toda tabla.

---

## sanic_sigil_tbl_transaction

- **Display name:** Sigil | TBL | Transacción de Firma
- **Logical name:** `sanic_sigil_tbl_transaction`
- **Primary key:** `sanic_sigil_tbl_transactionid`
- **Primary name column:** `sanic_sigil_name` (Título de la solicitud)
- **Propósito:** Representa una solicitud de firma: el documento a firmar, su estado en el flujo y sus parámetros de envío/expiración.

| Columna lógica | Tipo | Obligatoria | Descripción / propósito |
|----------------|------|-------------|-------------------------|
| `sanic_sigil_name` | String (200) | Sí | Título de la solicitud. Columna de nombre primario. |
| `sanic_sigil_status` | Choice | Sí | Estado de la transacción en el flujo. Ver [Catálogo de Choices](catalogo-de-choices.md) (`sanic_sigil_choice_transactionstatus`). |
| `sanic_sigil_routingtype` | Choice | Sí | Tipo de enrutamiento entre firmantes (secuencial/paralelo). Ver [Catálogo de Choices](catalogo-de-choices.md) (`sanic_sigil_choice_routingtype`). |
| `sanic_sigil_contentfile` | File | Sí | Contenido del archivo inicial a firmar. |
| `sanic_sigil_contenthash` | String (200) | No | Hash del archivo inicial (integridad). |
| `sanic_sigil_finalfile` | File | No | Archivo final firmado/sellado. |
| `sanic_sigil_message` | Memo (2000) | No | Mensaje para los firmantes. |
| `sanic_sigil_senton` | DateTime | Sí | Fecha de envío a firma. |
| `sanic_sigil_expireson` | DateTime | Sí | Fecha de expiración de la solicitud. |
| `sanic_sigil_expirationdays` | Integer (1–180) | No | Días para expirar (parámetro de configuración). |
| `sanic_sigil_completedon` | DateTime | No | Fecha de completado del proceso. |
| `sanic_sigil_locktoken` | Integer | No | Token de bloqueo para control de concurrencia. |

**Relaciones (lookups):** ninguna saliente de negocio. Es la tabla referenciada por `participant`, `ledgerentry` y `event`.

---

## sanic_sigil_tbl_participant

- **Display name:** Sigil | TBL | Participante
- **Logical name:** `sanic_sigil_tbl_participant`
- **Primary key:** `sanic_sigil_tbl_participantid`
- **Primary name column:** `sanic_sigil_name` (ID del Participante)
- **Propósito:** Cada firmante de una transacción, con su estado, orden en el flujo y datos de identidad/firma.

| Columna lógica | Tipo | Obligatoria | Descripción / propósito |
|----------------|------|-------------|-------------------------|
| `sanic_sigil_name` | String (300) | Sí | ID del participante. Columna de nombre primario. |
| `sanic_sigil_status` | Choice | Sí | Estado del participante en el flujo. Ver [Catálogo de Choices](catalogo-de-choices.md) (`sanic_sigil_choice_participantstatus`). |
| `sanic_sigil_transactionid` | Lookup | Sí | Transacción a la que pertenece → `sanic_sigil_tbl_transaction`. |
| `sanic_sigil_userid` | Lookup | Sí | Usuario firmante → `systemuser`. |
| `sanic_sigil_mastersignatureid` | Lookup | No | Firma maestra aplicada → `sanic_sigil_tbl_mastersignature`. |
| `sanic_sigil_order` | Integer (≥0) | No | Orden del firmante en el enrutamiento. |
| `sanic_sigil_signername` | String (200) | No | Nombre del firmante. |
| `sanic_sigil_signeremail` | String (200) | No | Email del firmante. |
| `sanic_sigil_signerentraobjectid` | String (36) | No | Object ID de Entra ID del firmante. |
| `sanic_sigil_signedon` | DateTime | No | Fecha en que firmó. |
| `sanic_sigil_signaturesnapshot` | File | No | Snapshot de la firma aplicada. |
| `sanic_sigil_turnactivatedon` | DateTime | No | Fecha de activación de su turno (enrutamiento secuencial). |
| `sanic_sigil_lastreminderon` | DateTime | No | Fecha del último recordatorio enviado. |
| `sanic_sigil_rejectionreason` | Memo (2000) | No | Motivo de rechazo, si aplica. |

**Relaciones (lookups):**
- `sanic_sigil_transactionid` → `sanic_sigil_tbl_transaction` (obligatorio, Cascade al borrar la transacción).
- `sanic_sigil_userid` → `systemuser` (obligatorio).
- `sanic_sigil_mastersignatureid` → `sanic_sigil_tbl_mastersignature` (opcional, RemoveLink).

---

## sanic_sigil_tbl_signaturezone

- **Display name:** Sigil | TBL | Zon de firma
- **Logical name:** `sanic_sigil_tbl_signaturezone`
- **Primary key:** `sanic_sigil_tbl_signaturezoneid`
- **Primary name column:** `sanic_sigil_name` (ID de Zona de Firma)
- **Propósito:** Define la ubicación (página + coordenadas + tamaño) donde un participante debe estampar su firma en el documento.

| Columna lógica | Tipo | Obligatoria | Descripción / propósito |
|----------------|------|-------------|-------------------------|
| `sanic_sigil_name` | String (100) | No | ID de la zona de firma. Columna de nombre primario. |
| `sanic_sigil_participantid` | Lookup | Sí | Participante dueño de la zona → `sanic_sigil_tbl_participant`. |
| `sanic_sigil_page` | Integer (≥1) | Sí | Número de página del documento. |
| `sanic_sigil_posx` | Decimal | Sí | Posición X de la zona. |
| `sanic_sigil_posy` | Decimal | Sí | Posición Y de la zona. |
| `sanic_sigil_width` | Decimal | Sí | Ancho de la zona. |
| `sanic_sigil_height` | Decimal | Sí | Alto de la zona. |

**Relaciones (lookups):**
- `sanic_sigil_participantid` → `sanic_sigil_tbl_participant` (obligatorio, Cascade al borrar el participante).

---

## sanic_sigil_tbl_ledgerentry

- **Display name:** Sigil | TBL | Registro del Ledger
- **Logical name:** `sanic_sigil_tbl_ledgerentry`
- **Primary key:** `sanic_sigil_tbl_ledgerentryid`
- **Primary name column:** `sanic_sigil_name` (ID Ledger)
- **Propósito:** Registro inmutable de sellado de una transacción completada: hashes de inicio y fin, sello de tiempo y resumen de firmantes.

| Columna lógica | Tipo | Obligatoria | Descripción / propósito |
|----------------|------|-------------|-------------------------|
| `sanic_sigil_name` | String (100) | Auto | ID del registro de ledger (autonumber `SIGIL-{DATETIMEUTC:yyyy}-{SEQNUM:6}`, read-only). Columna de nombre primario. |
| `sanic_sigil_transactionid` | Lookup | Sí | Transacción sellada → `sanic_sigil_tbl_transaction`. |
| `sanic_sigil_contenthash` | String (200) | Sí | Hash del archivo inicial. |
| `sanic_sigil_finalhash` | String (200) | Sí | Hash del archivo final. |
| `sanic_sigil_sealedon` | DateTime | Sí | Fecha de sellado. |
| `sanic_sigil_signersummary` | Memo (100000) | Sí | Resumen serializado de los firmantes. |
| `sanic_sigil_tsastatus` | Choice | Sí | Estado del sellado TSA (Time Stamping Authority). Ver [Catálogo de Choices](catalogo-de-choices.md) (`sanic_sigil_choice_tsastatus`). |
| `sanic_sigil_tsatoken` | Memo (1048576) | No | Token TSA obtenido. |

> **Column security:** las columnas de evidencia `sanic_sigil_contenthash`, `sanic_sigil_finalhash`, `sanic_sigil_sealedon` y `sanic_sigil_tsatoken` están **field-secured** (`IsSecured`) — solo la identidad de servicio (vía el perfil `Sigil | FLS | Evidence Writer`) puede escribirlas. Ver [Roles y seguridad](roles-y-seguridad.md).

**Alternate key:**
- `sanic_sanic_sigil_ak_transaction` (display: "ID transaccion") sobre `sanic_sigil_transactionid` — garantiza **un único registro de ledger por transacción**.

**Relaciones (lookups):**
- `sanic_sigil_transactionid` → `sanic_sigil_tbl_transaction` (obligatorio, RemoveLink).

---

## sanic_sigil_tbl_mastersignature

- **Display name:** Sigil | TBL | Firma Maestra
- **Logical name:** `sanic_sigil_tbl_mastersignature`
- **Primary key:** `sanic_sigil_tbl_mastersignatureid`
- **Primary name column:** `sanic_sigil_name` (ID Firma Maestra)
- **Propósito:** Firma de referencia validada de un usuario, versionada, que puede reutilizarse al firmar transacciones.

| Columna lógica | Tipo | Obligatoria | Descripción / propósito |
|----------------|------|-------------|-------------------------|
| `sanic_sigil_name` | String (200) | Sí | ID de la firma maestra. Columna de nombre primario. |
| `sanic_sigil_userid` | Lookup | Sí | Usuario dueño de la firma → `systemuser`. |
| `sanic_sigil_signaturefile` | File | Sí | Archivo de imagen de la firma. |
| `sanic_sigil_version` | Integer (≥0) | Sí | Versión de la firma. |
| `sanic_sigil_isactive` | Boolean | No | Indica si la firma está activa. |
| `sanic_sigil_validatedon` | DateTime | Sí | Fecha de validación. |
| `sanic_sigil_validationdetails` | Memo (100000) | Sí | Detalles del proceso de validación. |

**Relaciones (lookups):**
- `sanic_sigil_userid` → `systemuser` (obligatorio).

---

## sanic_sigil_tbl_event

- **Display name:** Sigil | TBL | Historial
- **Logical name:** `sanic_sigil_tbl_event`
- **Primary key:** `sanic_sigil_tbl_eventid`
- **Primary name column:** `sanic_sigil_name` (ID Historial)
- **Propósito:** Bitácora / historial de eventos de auditoría de una transacción (creación, envío, firma, sellado, errores, etc.).

| Columna lógica | Tipo | Obligatoria | Descripción / propósito |
|----------------|------|-------------|-------------------------|
| `sanic_sigil_name` | String (100) | No | ID del evento de historial. Columna de nombre primario. |
| `sanic_sigil_type` | Choice | Sí | Tipo de evento registrado. Ver [Catálogo de Choices](catalogo-de-choices.md) (`sanic_sigil_choice_eventtype`). |
| `sanic_sigil_transactionid` | Lookup | Sí | Transacción del evento → `sanic_sigil_tbl_transaction`. |
| `sanic_sigil_participantid` | Lookup | No | Participante relacionado, si aplica → `sanic_sigil_tbl_participant`. |
| `sanic_sigil_occurredon` | DateTime | Sí | Fecha y hora en que ocurrió el evento. |
| `sanic_sigil_actorname` | String (200) | Sí | Nombre del actor que originó el evento. |
| `sanic_sigil_actoremail` | String (200) | Sí | Email del actor que originó el evento. |
| `sanic_sigil_details` | Memo (4000) | Sí | Detalles del evento. |
| `sanic_sigil_documenthash` | String (200) | No | Hash del documento al momento del evento. |

**Relaciones (lookups):**
- `sanic_sigil_transactionid` → `sanic_sigil_tbl_transaction` (obligatorio, Cascade al borrar la transacción).
- `sanic_sigil_participantid` → `sanic_sigil_tbl_participant` (opcional, RemoveLink).

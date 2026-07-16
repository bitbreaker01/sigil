# Sigil — Modelo de Datos Dataverse

**Documento:** 03 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — visto bueno del equipo; nomenclatura actualizada al doc 12)
**Última actualización:** 2026-07-10
**Depende de:** [01-vision-y-alcance.md](01-vision-y-alcance.md) (RF/RNF), [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) (ADR-004, 005, 006, 011)

Todos los límites de plataforma citados fueron **verificados contra Microsoft Learn en julio 2026** (fuentes en la sección 10).

---

## 1. Convenciones

Las convenciones completas de nomenclatura viven en [12-convenciones-nomenclatura.md](12-convenciones-nomenclatura.md) (aprobadas por el equipo el 2026-07-10). Resumen operativo para este documento:

- **Publisher:** "Sistemas Abiertos Nicaragua" (prefijo de personalización **`sanic_`**); todo componente de Sigil agrega el namespace **`sigil_`** + **marcador de tipo**: tablas `sanic_sigil_tbl_*`, choices `sanic_sigil_choice_*`, columnas `sanic_sigil_*` (sin marcador), Custom APIs `sanic_sigil_capi_*`, variables de entorno `sanic_sigil_env_*`.
- **Solución:** display "Sigil | Core | Sigil", logical `sigil_core_sigil` (unmanaged en Dev, managed hacia Test/Prod — doc 09).
- Nombres de schema en inglés técnico; display names en inglés (la UI bilingüe es responsabilidad de la Code App — RNF-06).
- Ninguna columna se crea "por si acaso": cada columna trazable a un RF o ADR.

## 2. Diagrama entidad-relación

```
                                    ┌─────────────────────┐
                                    │  systemuser (nativa) │
                                    └──────┬──────────────┘
                                           │ N:1 (firmante)
┌───────────────────────────────┐  1:N  ┌────────────┴──────────────────┐  1:N  ┌───────────────────────────────┐
│ sanic_sigil_tbl_transaction   │──────▶│ sanic_sigil_tbl_participant   │──────▶│ sanic_sigil_tbl_signaturezone │
│ (Transacción)                 │       │ (Participante)                │       │ (Zona de Firma)               │
└──────────────┬────────────────┘       └───────────────────────────────┘       └───────────────────────────────┘
               │ 1:1 (forzada por plugin, alternate key)
               ▼
┌───────────────────────────────┐  ┌─────────────────────────────────┐  ┌───────────────────────────────┐
│ sanic_sigil_tbl_ledgerentry   │  │ sanic_sigil_tbl_mastersignature │  │ sanic_sigil_tbl_event         │
│ (Ledger inalterable)          │  │ (Firma Maestra, 1 por usuario)  │  │ (Historial, N por transacción)│
└───────────────────────────────┘  └─────────────────────────────────┘  └───────────────────────────────┘
```

Relaciones y cascadas:

| Relación | Tipo | Cascadas |
|----------|------|----------|
| `sanic_sigil_tbl_transaction` → `sanic_sigil_tbl_participant` | 1:N parental | Share/Assign: Cascade All; Delete: Cascade All (solo alcanzable en Borrador — ver §6) |
| `sanic_sigil_tbl_participant` → `sanic_sigil_tbl_signaturezone` | 1:N parental | Share/Assign/Delete: Cascade All |
| `sanic_sigil_tbl_transaction` → `sanic_sigil_tbl_event` | 1:N | Share/Assign: **Cascade None**; Delete: **Restrict** (el historial no se borra; el acceso de los participantes a eventos se otorga explícitamente — ver advertencia abajo) |
| `sanic_sigil_tbl_transaction` → `sanic_sigil_tbl_ledgerentry` | 1:N (lookup) | Share/Assign/Reparent: **Cascade None**; Delete: **Restrict** — jamás cascada hacia el ledger. Unicidad 1:1 garantizada por **alternate key** sobre `sanic_sigil_transactionid` (§4.4). |
| `systemuser` → `sanic_sigil_tbl_participant` / `sanic_sigil_tbl_mastersignature` | 1:N | Sin cascadas |

**Advertencia de plataforma (verificada):** la cascada de Share se ejecuta **en el momento del share** sobre los registros relacionados *existentes* — NO se aplica retroactivamente a hijos creados después. Como la transacción se comparte al **enviar** (cuando participantes y zonas ya existen), la cascada los cubre; pero los `sanic_sigil_tbl_event` se crean continuamente *después* del envío, así que el plugin que crea cada evento debe otorgar acceso explícito (**GrantAccess Read** a los participantes de la transacción). Costo POA estimado: ~10–15 eventos × 3–5 participantes ≈ ≤75 filas POA por transacción — dentro de la guía de higiene POA para nuestro volumen. Fuente: learn.microsoft.com/power-apps/developer/data-platform/configure-entity-relationship-cascading-behavior.

## 3. Choices globales

Todas globales (se reutilizan entre tablas/APIs y la Code App las lee como formatted values).

**Sobre los valores numéricos:** los valores 1..N de las tablas siguientes son **índices lógicos de este documento**. Los valores reales se crean con el **Option Value Prefix del publisher Sanic** (número definido en el publisher; ej. prefijo 78216 → 782160001, 782160002…) para evitar colisiones entre soluciones. El doc 04 referencia los estados por nombre lógico, jamás por número mágico.

### `sanic_sigil_choice_transactionstatus` — Estado de Transacción (RF-08)
| Valor | Etiqueta | Origen |
|-------|----------|--------|
| 1 | Borrador | RF-08 |
| 2 | Pendiente de Firma | RF-08 |
| 3 | Firmado Parcialmente | RF-08 |
| 4 | Sellando | Operativo (ADR-008/ADR-011): entre la última firma y el fin del pipeline asíncrono |
| 5 | Completado | RF-08 |
| 6 | Rechazado | RF-08 |
| 7 | Expirado | RF-08 / RF-27 |
| 8 | Error de Sellado | Operativo (ADR-008): pipeline falló tras reintentos; re-disparable |
| 9 | Cancelado | RF-30 (Q-08): el creador retiró la transacción antes del sellado |

*Los valores 4 y 8 son **estados operativos** que precisan la matriz de RF-08; las transiciones válidas las define el doc 06.*

### `sanic_sigil_choice_participantstatus` — Estado de Participante
| Valor | Etiqueta | Nota |
|-------|----------|------|
| 1 | Pendiente | Aún no puede accionar (secuencial: no es su turno) |
| 2 | Turno Activo | **Puede firmar ahora.** En secuencial: el próximo en orden. En paralelo: TODOS los firmantes arrancan en Turno Activo — así el dashboard "pendientes por mi firma" (RF-22) es un filtro plano `userid = yo AND status = Turno Activo`, sin lógica de enrutamiento en el cliente |
| 3 | Firmado | Intención de firma registrada (RF-04) |
| 4 | Rechazado | RF-13 |

*Nota de reconciliación (ADR-008 vs ADR-011): la firma individual es un registro liviano síncrono — no hay estado "Procesando" por participante. El procesamiento pesado ocurre UNA vez, a nivel transacción (estado **Sellando**), cuando firma el último.*

### `sanic_sigil_choice_routingtype` — Enrutamiento (RF-09/RF-10)
| Valor | Etiqueta |
|-------|----------|
| 1 | Secuencial |
| 2 | Paralelo |

### `sanic_sigil_choice_tsastatus` — Estado del sello TSA (RF-16/RF-29, ADR-005)
| Valor | Etiqueta |
|-------|----------|
| 1 | Sellado con TSA |
| 2 | Sin sello TSA |
| 3 | Re-sellado pendiente |

*Las aclaraciones entre paréntesis de versiones anteriores eran notas editoriales, NO parte de la etiqueta (precisión 2026-07-15: la etiqueta REAL es la que valida CF-A16 contra el Apéndice A del doc 12).*

### `sanic_sigil_choice_eventtype` — Tipo de evento (RNF-04)
| Valor | Etiqueta |
|-------|----------|
| 1 | Transacción creada |
| 2 | Enviada a firma |
| 3 | Firma registrada |
| 4 | Rechazada |
| 5 | Recordatorio programado |
| 6 | Sellado iniciado |
| 7 | Sellado completado |
| 8 | Error de sellado |
| 9 | Re-sellado TSA obtenido |
| 10 | Expirada |
| 11 | Verificación realizada |
| 12 | Cancelada por el creador |
| 13 | TSA abandonada — *agregado por el negocio 2026-07-16: evento de `capi_ResealPending` al mover un ledger a Sin sello TSA con la TSA deshabilitada (cierra el gap de catálogo del doc 04 §3.1)* |

## 4. Tablas

**Convenciones de columnas:** (a) toda columna es *Business Required* salvo que se marque **(opcional)**; (b) los DateTime son **behavior User Local** (almacenan UTC, renderizan en zona del usuario) salvo indicación contraria; (c) los decimales de coordenadas tienen min 0, max 100, precisión 4; (d) no hay columnas de moneda en el modelo; (e) **ownership de hijos:** en Dataverse el owner NO se hereda al crear registros hijos — el plugin setea `ownerid` explícitamente en cada Create (owner = creador de la transacción), verificado: la cascada Assign solo aplica al *reasignar* el padre.

### 4.1 `sanic_sigil_tbl_transaction` — Transacción de Firma

**Ownership:** User/Team (owner = creador). **Auditoría: activada.**

| Columna | Tipo | Detalle | Trazabilidad |
|---------|------|---------|--------------|
| `sanic_sigil_name` (primaria) | Texto 200 | Título de la solicitud | RF-26 |
| `sanic_sigil_status` | Choice `sanic_sigil_choice_transactionstatus` | Default: Borrador | RF-08 |
| `sanic_sigil_routingtype` | Choice `sanic_sigil_choice_routingtype` | | RF-09/10 |
| `sanic_sigil_message` | Texto multilínea 2.000 **(opcional)** | Mensaje del creador a los firmantes | RF-26 |
| `sanic_sigil_expirationdays` | Número entero **(opcional)** | Plazo elegido por el creador en el borrador; si es null, aplica `sanic_sigil_env_ExpirationDefaultDays` al enviar | RF-27 |
| `sanic_sigil_senton` | Fecha/hora | Momento del envío (Borrador → Pendiente de Firma); base del cálculo de expiración y recordatorios | RF-27, RF-12 |
| `sanic_sigil_expireson` | Fecha/hora | Calculada al enviar: `senton + expirationdays` | RF-27 |
| `sanic_sigil_completedon` | Fecha/hora **(opcional)** | Escrita por el pipeline de sellado | RNF-04 |
| `sanic_sigil_locktoken` | Número entero **(opcional, técnico)** | Columna de **no-op para el lock de fila** (doc 04 §5): los plugins la actualizan para serializar. **Jamás** se usa el status para el lock — dispararía los triggers de notificación con valores repetidos (doc 08). Excluida de auditoría y de filtering attributes | doc 04 §5 |
| `sanic_sigil_contentfile` | **File**, MaxSizeInKB configurado al límite operativo (ver §9, `sanic_sigil_env_MaxPdfSizeKB`) | El PDF subido por el creador — los bytes que sella `hash_contenido` | RF-25, ADR-011 |
| `sanic_sigil_contenthash` | Texto 64 **(opcional)** | SHA-256 del PDF de contenido, calculado al **enviar** (no al sellar). El pipeline de sellado lo recalcula y **verifica el match** antes de sellar — detecta corrupción o manipulación del archivo entre el envío y el sellado. No se asegura con column security (es el hash que se imprime públicamente en la hoja de cierre) | ADR-011, RNF-02 |
| `sanic_sigil_finalfile` | **File**, ídem | El PDF final sellado — escrito una única vez por el pipeline (contexto de sistema). **No se asegura con column security**: los participantes deben poder descargarlo; su integridad la garantiza el hash del ledger, no su ocultamiento | RF-24, ADR-011 |

Límites File verificados: default 32 MB; máximo por designer 131.072 KB (131 MB); hasta 10 GB solo vía API. Los binarios cuentan contra capacidad **File**, no Database. Plugins leen/escriben con `InitializeFileBlocksUpload/Download` + bloques de ≤4 MB.

### 4.2 `sanic_sigil_tbl_participant` — Participante

**Ownership:** User/Team (owner = creador de la transacción, seteado explícitamente por el plugin en el Create). **Auditoría: activada.**

| Columna | Tipo | Detalle | Trazabilidad |
|---------|------|---------|--------------|
| `sanic_sigil_name` (primaria) | Texto 300 | Autogenerada por plugin: "{firmante} — {transacción}" | — |
| `sanic_sigil_transactionid` | Lookup → `sanic_sigil_tbl_transaction` (parental) | | RF-26 |
| `sanic_sigil_userid` | Lookup → `systemuser` | El firmante | RF-26 |
| `sanic_sigil_order` | Número entero | Solo secuencial (1..N); null en paralelo | RF-09 |
| `sanic_sigil_status` | Choice `sanic_sigil_choice_participantstatus` | Default: Pendiente | RF-08 |
| `sanic_sigil_turnactivatedon` | Fecha/hora **(opcional)** | Momento en que pasó a Turno Activo — base del cálculo "pendiente tras X días" de los recordatorios | RF-12 |
| `sanic_sigil_lastreminderon` | Fecha/hora **(opcional)** | Último recordatorio enviado a este participante — evita duplicados del flow diario | RF-12 |
| `sanic_sigil_signedon` | Fecha/hora **(opcional)** | Momento del registro de intención | RF-15, RNF-04 |
| `sanic_sigil_rejectionreason` | Texto multilínea 2.000 **(opcional)** | | RF-13 |
| `sanic_sigil_signaturesnapshot` | **File (opcional)** | **Copia congelada del PNG normalizado al momento de firmar** (escrita por `sanic_sigil_capi_SubmitSignature`). El pipeline incrusta ESTE snapshot: si el usuario cambia su Firma Maestra entre su firma y el sellado, el documento muestra la que existía al firmar | RF-15, RNF-02 |
| `sanic_sigil_mastersignatureid` | Lookup → `sanic_sigil_tbl_mastersignature` **(opcional)** | **La versión exacta de la Firma Maestra usada al firmar** (decisión 2026-07-13): linaje consultable que complementa al snapshot de bytes — el snapshot es la evidencia independiente; el lookup, la trazabilidad de versiones | RF-01, RNF-02 |
| `sanic_sigil_signername` | Texto 200 **(opcional)** | **Snapshot** del displayName de Entra al firmar | RF-15, RF-18 |
| `sanic_sigil_signeremail` | Texto 200 **(opcional)** | Snapshot del UPN/mail al firmar | RF-15, RF-18 |
| `sanic_sigil_signerentraobjectid` | Texto 36 **(opcional)** | Snapshot del objectId de Entra | RF-18, RNF-02 |

**Por qué snapshots:** el nombre/correo de un usuario puede cambiar en Entra después de firmar; la evidencia debe congelar la identidad **al momento de la firma** (los toma el plugin del contexto de ejecución — jamás del cliente).

### 4.3 `sanic_sigil_tbl_signaturezone` — Zona de Firma (RF-28)

**Ownership:** User/Team (parental desde participante). **Auditoría: no necesaria** (se congela al enviar; editable solo en Borrador).

| Columna | Tipo | Detalle |
|---------|------|---------|
| `sanic_sigil_name` (primaria) | Texto 100 | Autogenerada |
| `sanic_sigil_participantid` | Lookup → `sanic_sigil_tbl_participant` (parental) | |
| `sanic_sigil_page` | Número entero | Página (1..N) del PDF de contenido |
| `sanic_sigil_posx` / `sanic_sigil_posy` | Decimal (precisión 4) | Posición **en % del ancho/alto de la página** (esquina superior-izquierda de la zona) |
| `sanic_sigil_width` / `sanic_sigil_height` | Decimal (precisión 4) | Tamaño en % — independiente del tamaño físico de página; el plugin convierte a puntos PDF al incrustar |

Cardinalidad: **1..N zonas por participante — OBLIGATORIAS** (decisión 2026-07-13: no existe posición por defecto de Sigil; el creador define dónde firma cada uno, y el envío se bloquea si algún participante no tiene al menos una zona — guard de T4, doc 06). Coordenadas en porcentaje y no en puntos: el editor visual del frontend (doc 05) y el motor de incrustación (doc 04) no dependen del DPI/tamaño de render.

### 4.4 `sanic_sigil_tbl_ledgerentry` — Registro del Ledger (RF-17, ADR-006, ADR-011)

**Ownership:** **Organization** — sin semántica de dueño; el acceso lo dan los roles, no el ownership ni el sharing. **Auditoría: activada** (consume capacidad Log — verificado).

| Columna | Tipo | Column security | Detalle |
|---------|------|:---:|---------|
| `sanic_sigil_name` (primaria) | Texto 100, **autonumber** | — | Formato exacto: `SIGIL-{DATETIMEUTC:yyyy}-{SEQNUM:6}`. Verificado: la secuencia es **global y no se reinicia por año** (el año del formato es informativo); el seed no viaja en soluciones. El plugin NO pasa valor en esta columna (pisaría el autonumber) |
| `sanic_sigil_transactionid` | Lookup → `sanic_sigil_tbl_transaction` | — | Delete Restrict; **alternate key** sobre esta columna garantiza el 1:1 a nivel base de datos (elimina condiciones de carrera de sellados concurrentes — un ledger probatorio no confía su unicidad a convenciones de código) |
| `sanic_sigil_contenthash` | Texto 64 (hex SHA-256) | **SÍ** | ADR-011 |
| `sanic_sigil_finalhash` | Texto 64 | **SÍ** | ADR-011 — ancla de verificación |
| `sanic_sigil_tsatoken` | Texto multilínea **1.048.576** | **SÍ** | Token RFC 3161 DER en base64. Verificado: el máximo de memo es 1.048.576 caracteres. **Medido en spike (2026-07-10): token real de Sectigo = 8.844 chars base64** — presupuesto ~12K; sobra margen. Los attachments/Notes quedan descartados (su soporte de column security no está documentado — NO VERIFICADO — y no lo necesitamos) |
| `sanic_sigil_tsastatus` | Choice `sanic_sigil_choice_tsastatus` | — | Visible: es el **nivel de evidencia** (RF-29) |
| `sanic_sigil_sealedon` | Fecha/hora UTC | **SÍ** | Timestamp del sellado |
| `sanic_sigil_signersummary` | Texto multilínea 100.000 | — | JSON snapshot de firmantes (nombre, correo, fecha) para la pantalla de verificación — legible sin exponer columnas aseguradas |

**Decisión de acceso:** los usuarios NUNCA leen el ledger directamente — ni las columnas aseguradas ni la tabla (§6): toda la verificación pasa por la Custom API `sanic_sigil_capi_VerifyDocument` (contexto elevado), que devuelve veredicto y metadatos presentables (resumen de firmantes, nivel de evidencia TSA, fecha) sin exponer evidencia cruda ni permitir minería de actividad organizacional.

### 4.5 `sanic_sigil_tbl_mastersignature` — Firma Maestra (RF-01, **versionada** — decisión 2026-07-13)

**Ownership:** User/Team (owner = el usuario dueño de la firma). **Auditoría: activada.**

**Modelo de versionado:** cada carga validada crea un **registro NUEVO** (jamás se pisa ni se borra el anterior — historial inmutable); exactamente una versión por usuario está **vigente**. Al firmar, el participante queda vinculado a la versión exacta usada (lookup en §4.2) además del snapshot de bytes — linaje consultable + evidencia independiente.

| Columna | Tipo | Detalle |
|---------|------|---------|
| `sanic_sigil_name` (primaria) | Texto 200 | Autogenerada: "{UPN} v{N}" |
| `sanic_sigil_userid` | Lookup → `systemuser` | Redundante con owner a propósito: el owner puede reasignarse operativamente; el lookup es el vínculo probatorio. **Nota (2026-07-13):** el alternate key de unicidad por usuario se **elimina** (ya no hay 1 registro por usuario); la unicidad de la versión VIGENTE la garantiza el plugin (desactiva la anterior y crea la nueva en la misma operación; riesgo residual trivial: el único actor que puede "competir" es el propio usuario re-subiendo su firma) |
| `sanic_sigil_version` | Número entero | Secuencial por usuario, asignado por el plugin |
| `sanic_sigil_isactive` | Dos opciones | La versión vigente (única en true por usuario) |
| `sanic_sigil_signaturefile` | **File** (no Image) | El PNG normalizado con canal alfa. Por qué File y no Image: las columnas Image imponen semántica de thumbnail (144×144 convertido a .jpg) y su preservación byte a byte del full image no está garantizada por la documentación; **File preserva los bytes exactos** — condición necesaria para que la transparencia validada (RF-02) sobreviva intacta hasta la incrustación |
| `sanic_sigil_validatedon` | Fecha/hora UTC | |
| `sanic_sigil_validationdetails` | Texto multilínea 10.000 | JSON con métricas de la validación (transparencia, contraste, nitidez) — feedback y auditoría de calidad (ADR-009) |

### 4.6 `sanic_sigil_tbl_event` — Historial (RNF-04)

**Ownership:** User/Team (parental desde transacción — el share cascadea). **Auditoría: no** (la tabla ES un log; auditar el log es redundante).

| Columna | Tipo | Detalle |
|---------|------|---------|
| `sanic_sigil_name` (primaria) | Texto 300 | Autogenerada |
| `sanic_sigil_transactionid` | Lookup → `sanic_sigil_tbl_transaction` (parental, Delete Restrict) | |
| `sanic_sigil_type` | Choice `sanic_sigil_choice_eventtype` | |
| `sanic_sigil_actorname` / `sanic_sigil_actoremail` | Texto 200 | Snapshot del actor (o "Sistema") |
| `sanic_sigil_participantid` | Lookup → `sanic_sigil_tbl_participant` **(opcional)** | Ancla del evento al participante que actuó (eventos de firma/rechazo/recordatorio) — decisión 2026-07-13 | RNF-02 |
| `sanic_sigil_documenthash` | Texto 64 **(opcional)** | **SHA-256 del documento de contenido al momento del evento** (escrito por el sistema en eventos de firma): evidencia de QUÉ vio y aprobó exactamente ese firmante. **Verificación cruzada** (decisión 2026-07-13, implementada en `capi_VerifyDocument`): todos los hashes de eventos de firma deben ser iguales entre sí e iguales a `contenthash` — estrecha la ventana de manipulación entre firmas individuales | RNF-02 |
| `sanic_sigil_occurredon` | Fecha/hora UTC | |
| `sanic_sigil_details` | Texto multilínea 4.000 | Contexto adicional |

**Señales de integridad del historial (registradas con su límite honesto):** todos los eventos los crea el Service Principal (los usuarios no tienen privilegio de escritura — §6), por lo que `createdby` = SP siempre; la atribución humana vive en los snapshots de actor y el lookup al participante. La señal de no-modificación es **`modifiedon == createdon` y `modifiedby == createdby`** — delata cualquier edición posterior por cualquier actor que opere como sí mismo; NO detiene a quien toma la identidad del motor (doc 07 A13) ni al sysadmin (A4) — para esos actores, la defensa es la evidencia externa (TSA). `capi_VerifyDocument` incluye estos chequeos en su veredicto.

**Por qué una tabla propia además de la auditoría nativa:** la auditoría de Dataverse (a) no es consumible cómodamente desde la Code App, (b) audita cambios de columnas, no eventos de negocio. `sanic_sigil_tbl_event` alimenta la línea de tiempo visible en la UI; la auditoría nativa es la evidencia forense de bajo nivel. Ambas se activan.

## 5. Column security (FLS) — perfiles

Columnas aseguradas: `sanic_sigil_contenthash`, `sanic_sigil_finalhash`, `sanic_sigil_tsatoken`, `sanic_sigil_sealedon` (todas en `sanic_sigil_tbl_ledgerentry`).

| Perfil | Miembros | Read | Read unmasked | Create | Update |
|--------|----------|:---:|:---:|:---:|:---:|
| **Sigil \| FLS \| Evidence Writer** | Usuario de aplicación (Service Principal de los plugins) | ✔ | ✔ | ✔ | ✔ |
| *(ningún otro perfil)* | — | — | — | — | — |

Comportamiento resultante (verificado): sin perfil asignado, los usuarios no acceden a esas columnas. **Excepción inevitable y documentada: los System Administrators siempre ven y pueden editar columnas aseguradas** — la column security no les aplica (corrección verificada registrada en ADR-006; la capa anti-sysadmin es la TSA + auditoría).

## 6. Roles de seguridad

| Rol | Alcance | Privilegios |
|-----|---------|-------------|
| **Sigil \| SR \| User** | Todos los empleados que usan la app | `sanic_sigil_tbl_transaction`: Read (User — propias + compartidas al envío). `sanic_sigil_tbl_participant`, `sanic_sigil_tbl_signaturezone`: Read (User — cubiertos por la cascada del share al enviar). `sanic_sigil_tbl_event`: Read (User — acceso otorgado por GrantAccess explícito al crear cada evento, ver §2). `sanic_sigil_tbl_ledgerentry`: **sin acceso directo** — la verificación y sus metadatos (incluido `sanic_sigil_signersummary`, que contiene nombres y correos de firmantes) se sirven exclusivamente vía `sanic_sigil_capi_VerifyDocument`; dar Read org-wide expondría la actividad de firma de toda la organización a cualquier empleado (decisión de privacidad explícita). `sanic_sigil_tbl_mastersignature`: Read (User — solo la propia). **Sin Create/Update/Delete directos sobre ninguna tabla**: toda escritura (incluidos crear/editar/borrar borradores y el rechazo — RF-13) pasa por Custom APIs en contexto de sistema; la superficie completa de APIs se define en el doc 04. |
| **Sigil \| SR \| Service** | Usuario de aplicación (plugins **y conexión Dataverse de los flows** — doc 08 §6) | CRUD completo (Organization) sobre todas las tablas `sigil_` + miembro del perfil *Sigil \| FLS \| Evidence Writer* + **privilegios exigidos por los flows (verificados)**: CRUD user-level sobre **Callback Registration** (sin él, los triggers de Dataverse no se activan), Read sobre `systemuser` (resolver correo/nombre de destinatarios) y Read sobre `usersettings` (idioma del destinatario, RNF-06). |
| **Sigil \| SR \| Auditor** *(opcional, fase posterior)* | Compliance | Read (Organization) sobre transacciones, participantes, eventos y ledger. |

**Acceso de participantes a transacciones ajenas:** el plugin de envío comparte la transacción (**GrantAccess, solo Read**) con cada participante; las cascadas de la relación parental extienden el acceso a participantes, zonas y eventos. Verificado: el sharing programático desde plugins sigue plenamente soportado; higiene POA documentada (compartir solo lo necesario, solo Read, nunca revocar el acceso del firmante a un documento que firmó — es su derecho probatorio).

**Nota sobre el modelo "todo escribe el sistema":** la Custom API corre su operación principal bajo el usuario llamante, pero el plugin usa el servicio elevado (contexto de sistema) para las escrituras. La identidad del llamante se captura del contexto de ejecución para los snapshots — el privilegio de escritura del usuario es innecesario. Detalle de implementación en doc 04.

## 7. Auditoría nativa

- Activada a nivel organización + tablas: `sanic_sigil_tbl_transaction`, `sanic_sigil_tbl_participant`, `sanic_sigil_tbl_ledgerentry`, `sanic_sigil_tbl_mastersignature`.
- Verificado: registra operaciones **efectuadas** (no intentos bloqueados), con actor y timestamp, en la tabla `Audit`; **consume capacidad Log** — se incluye en la estimación de capacidad (§9).
- La retención de auditoría se configura por ambiente (doc 09 define el valor por política).

## 8. Variables de entorno (solution-aware — RNF-05)

| Variable | Tipo | Uso |
|----------|------|-----|
| `sanic_sigil_env_TsaEnabled` | Two options | Feature flag TSA (RF-29, ADR-005) |
| `sanic_sigil_env_TsaEndpoints` | JSON | Lista ordenada de endpoints RFC 3161 (primario → fallbacks) |
| `sanic_sigil_env_MaxPdfSizeKB` | Decimal | Límite operativo de PDF subido (validado por `sanic_sigil_capi_CreateTransaction`) |
| `sanic_sigil_env_MaxParticipants` | Decimal | Máximo de firmantes por transacción (default 20 — doc 04 §3.4). *Fila agregada 2026-07-13: existía en docs 04/09 pero faltaba en esta tabla canónica (hallazgo del antagonista de F1)* |
| `sanic_sigil_env_SignatureImageSpec` | JSON | Dimensiones estándar, formato y peso máximo de la Firma Maestra normalizada (ADR-009) |
| `sanic_sigil_env_ExpirationDefaultDays` | Decimal | Default de RF-27 |
| `sanic_sigil_env_ReminderCadenceDays` | Decimal | Cadencia de recordatorios (RF-12) |
| `sanic_sigil_env_AppPlayUrl` | Text | **URL base de la Code App del ambiente** (`https://apps.powerapps.com/play/e/{envId}/a/{appId}`) — environment ID y app ID cambian por ambiente. La usan: el plugin de sellado para construir la URL del QR (RF-19) y los flows para los deep links de notificaciones (RF-11). Sin esto, ni QR ni deep links son construibles |
| `sanic_sigil_env_DefaultLanguage` | Text | Idioma de fallback de notificaciones (`es` / `en`). El idioma del destinatario (RNF-06) se lee de `usersettings.uilanguageid` del firmante; si no está disponible, aplica este default |

**Restricción verificada:** las variables tipo *secret* (Key Vault) **no son legibles desde plugins**. Nota: tras el descarte de AI Vision (ADR-009, resolución 2026-07-10) el backend no necesita ningún secreto. Lectura desde plugins: `RetrieveEnvironmentVariableValue` (sin caché de plataforma, límite 2.000 caracteres por valor — los JSON de configuración deben mantenerse bajo ese tope).

## 9. Capacidad y límites

- **File:** cada transacción almacena 2 PDFs (contenido + final). Con `sanic_sigil_env_MaxPdfSizeKB` = 20 MB (valor inicial propuesto, ajustable), 1.000 transacciones/año ≈ ≤40 GB/año de capacidad File en el peor caso. La política de retención (archivado/purga de `sanic_sigil_contentfile` — el final NUNCA se purga) se define cuando haya volúmenes reales; el modelo la soporta sin cambios.
- **Log:** la auditoría de 4 tablas con este volumen es menor; se monitorea en Power Platform Admin Center.
- **Database:** despreciable (metadatos + hashes + JSON pequeños).

## 10. Riesgos registrados de plataforma

| Riesgo | Impacto | Mitigación |
|--------|---------|------------|
| Upload/download de columnas File desde Code Apps está en **preview** (verificado jul-2026) | RF-25 (subida) y visualización/descarga de PDFs desde la UI | **Decidido en doc 04 (2026-07-10):** los binarios viajan **base64 vía Custom APIs** en ambos sentidos (`PdfBase64` de entrada; `sanic_sigil_capi_GetDocumentContent` de salida) — no dependemos del preview. Si el upload/download File del SDK llega a GA, se re-evalúa como optimización en doc 05. |
| Render de columnas aseguradas vía code app no documentado | Ninguno con nuestro diseño | Los usuarios jamás leen columnas aseguradas: todo pasa por Custom APIs (§4.4). |
| Column security no aplica a sysadmins (verificado) | RNF-02 | Registrado en ADR-006; capa anti-sysadmin = TSA (ADR-005) + auditoría. |

### Deudas explícitas que este modelo delega (para que no se caigan entre sillas)

| Pendiente | Dueño |
|-----------|-------|
| **Mecanismo de expiración automática** (RF-27): el modelo soporta la query (`sanic_sigil_expireson`), pero nadie transiciona a *Expirado*. Candidato: job diario (flow programado que invoca una Custom API, o job asíncrono) — nota: ADR-003 limita los flows a notificaciones; si el flow solo *dispara* la API y la transición la hace el plugin, se respeta el espíritu | Doc 04 (API) + Doc 06 (transición) |
| Superficie de Custom APIs para gestión de borradores (crear/editar/borrar) y rechazo (RF-13) — ADR-002 solo lista la superficie inicial | Doc 04 |
| Estructura del JSON de `sanic_sigil_signersummary`, `sanic_sigil_env_TsaEndpoints`, `sanic_sigil_env_SignatureImageSpec` (contratos exactos) | Doc 04 |
| Idioma de notificaciones: mapeo `uilanguageid` → plantilla es/en | Doc 08 |

## 11. Trazabilidad RF → modelo

| RF/ADR | Elemento del modelo |
|--------|--------------------|
| RF-01/RF-02 (Firma Maestra + normalización) | `sanic_sigil_tbl_mastersignature` (File, no Image — preserva alfa) |
| RF-08 (estados) | `sanic_sigil_choice_transactionstatus`, `sanic_sigil_choice_participantstatus` |
| RF-09/10 (enrutamiento) | `sanic_sigil_routingtype`, `sanic_sigil_tbl_participant.sanic_sigil_order` |
| RF-12/RF-27 (recordatorios/expiración) | `sanic_sigil_senton`, `sanic_sigil_expirationdays`, `sanic_sigil_expireson`, `sanic_sigil_turnactivatedon`, `sanic_sigil_lastreminderon`, `sanic_sigil_env_ReminderCadenceDays`, `sanic_sigil_env_ExpirationDefaultDays` |
| RF-19 (URL del QR) / RF-11 (deep links) | `sanic_sigil_env_AppPlayUrl` |
| RNF-06 (idioma de notificaciones) | `usersettings.uilanguageid` + `sanic_sigil_env_DefaultLanguage` |
| RF-13 (rechazo) | `sanic_sigil_tbl_participant.sanic_sigil_rejectionreason` + estados |
| RF-15/RF-18 (identidad en la estampa) | Snapshots en `sanic_sigil_tbl_participant` |
| RF-16/RF-29 (TSA + flag) | `sanic_sigil_tbl_ledgerentry.sanic_sigil_tsatoken/sanic_sigil_tsastatus`, `sanic_sigil_env_TsaEnabled/Endpoints` |
| RF-17 (ledger inalterable) | `sanic_sigil_tbl_ledgerentry` org-owned + roles solo-Read + column security + auditoría |
| RF-24 (repositorio solo lectura) | `sanic_sigil_finalfile` + rol Usuario sin escritura |
| RF-25/RF-26 (ingesta/creación) | `sanic_sigil_contentfile`, `sanic_sigil_tbl_transaction`, `sanic_sigil_tbl_participant` |
| RF-28 (posiciones, obligatorias) | `sanic_sigil_tbl_signaturezone` (1..N por participante) |
| RNF-04 (trazabilidad) | `sanic_sigil_tbl_event` + auditoría nativa |
| RNF-07 (solo Dataverse) | Todo binario en columnas File; cero dependencias de almacenamiento externo |

## Fuentes verificadas

- File: learn.microsoft.com/power-apps/developer/data-platform/file-attributes · file-column-data
- Image (conversión a .jpg, 144×144 thumbnail): learn.microsoft.com/power-apps/developer/data-platform/image-attributes
- Texto/memo (máx. 1.048.576): learn.microsoft.com/power-apps/maker/data-platform/types-of-fields
- Column security (tipos asegurables, 4 permisos, sysadmin exento): learn.microsoft.com/power-platform/admin/field-level-security
- Auditoría (tabla Audit, capacidad Log): learn.microsoft.com/power-platform/admin/manage-dataverse-auditing
- Sharing/POA: learn.microsoft.com/power-apps/developer/data-platform/security-sharing-assigning · manage-principalobjectaccess-storage
- Variables de entorno (tipos, secrets no legibles desde plugins, RetrieveEnvironmentVariableValue): learn.microsoft.com/power-apps/maker/data-platform/environmentvariables · environmentvariables-azure-key-vault-secrets
- Code Apps + Dataverse (file upload/download preview): learn.microsoft.com/power-apps/developer/code-apps/how-to/connect-to-dataverse
- Cascadas de relaciones (Share en el momento, Assign solo al reasignar): learn.microsoft.com/power-apps/developer/data-platform/configure-entity-relationship-cascading-behavior · create-edit-entity-relationships
- Autonumber (formatos, seed no viaja en soluciones): learn.microsoft.com/power-apps/developer/data-platform/create-auto-number-attributes

---

*Anterior: [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) · Siguiente: 04 — Backend y motor criptográfico.*

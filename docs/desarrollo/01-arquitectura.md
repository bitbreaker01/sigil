# Arquitectura de Sigil

**Alcance.** Este documento describe cómo encajan las piezas de Sigil, el **ciclo de vida de una
transacción de firma** (estados y transiciones, con sus valores reales) y el **modelo de confianza**
que hace que una firma tenga valor probatorio. Es la referencia profunda; el panorama de alto nivel
está en la [Guía del Desarrollador](../guias/04-guia-desarrollador.md).

---

## 1. Las piezas y su interacción

Sigil vive en una única solución de Dataverse (`sigil_core_sigil`, publisher `sanic`). El frontend
no tiene lógica de negocio: orquesta pantallas y llama Custom APIs. El backend es la única autoridad.

```
   ┌────────────────────────────────────────────────────────────────────────┐
   │  PLAYER DE POWER APPS  (host — navegador / móvil)                        │
   │  ┌──────────────────────────────────────────────────────────────────┐  │
   │  │  CODE APP (iframe)  · React + TypeScript + Vite + Fluent UI v9     │  │
   │  │    pantallas → seam SigilApi → SDK @microsoft/power-apps           │  │
   │  │    getContext() = identidad + query params (deep link)            │  │
   │  └───────────────┬──────────────────────────────┬───────────────────┘  │
   └──────────────────┼──────────────────────────────┼──────────────────────┘
                      │ Custom API (executeAsync)     │ lecturas de tablas
                      │ binarios como base64          │ (retrieveMultipleRecordsAsync)
                      ▼                                ▼
   ┌────────────────────────────────────────────────────────────────────────┐
   │  DATAVERSE                                                               │
   │  ┌──────────────────────────────┐   ┌───────────────────────────────┐  │
   │  │  CUSTOM APIs (plugins C#)     │   │  6 TABLAS                     │  │
   │  │  17 handlers : SigilApiPlugin │──►│  transaction · participant    │  │
   │  │   8 bound (Target) · 9 unbound│   │  signaturezone · mastersign.  │  │
   │  │  contexto de SISTEMA          │   │  event · ledgerentry          │  │
   │  └──────────────┬───────────────┘   └───────────────────────────────┘  │
   │                 │ Update(status=Sellando) dispara step async            │
   │                 ▼                                                        │
   │  ┌──────────────────────────────┐        ┌──────────────────────────┐  │
   │  │  SealingWorkerPlugin (async) │───────►│  CLOUD FLOWS (Power       │  │
   │  │  compone PDF · doble hash    │        │  Automate) → notificación │  │
   │  │  · hoja de cierre · ledger   │        │  y recordatorios          │  │
   │  └──────────────┬───────────────┘        └──────────────────────────┘  │
   └─────────────────┼──────────────────────────────────────────────────────┘
                     │ TimeStampRequest (RFC 3161, sobre hash_final)
                     ▼
              ┌────────────────────┐
              │  TSA EXTERNA        │  sella el hash ante un tercero fuera
              │  (RFC 3161)         │  del alcance del tenant → tamper-evidence
              └────────────────────┘
```

**Reglas de la interacción:**

- **El frontend llama Custom APIs, no escribe tablas.** Las acciones (crear, enviar, firmar,
  rechazar, cancelar, sellar de nuevo, verificar) son Custom APIs. Las lecturas de listas van por la
  data client del SDK (`retrieveMultipleRecordsAsync`), filtrando explícitamente por el usuario.
- **Los binarios viajan como base64 por Custom API** en ambos sentidos. El Code App no accede a las
  columnas File de Dataverse directamente.
- **El worker de sellado se dispara por cambio de estado**, no por una llamada directa: cuando la
  última firma transiciona la transacción a *Sellando*, un step asíncrono (post-operation, sobre
  `Update` de la transacción, filtrando por `status`) arranca `SealingWorkerPlugin`.
- **Los Cloud Flows solo notifican.** Reaccionan a cambios de estado para mandar correos y
  recordatorios; no tocan binarios ni criptografía.
- **La TSA es un tercero externo** que estampa una marca de tiempo criptográfica sobre el hash final.

---

## 2. Ciclo de vida de una transacción de firma

Una transacción de firma es la unidad central. Su estado vive en la columna
`sanic_sigil_status` (choice `sanic_sigil_choice_transactionstatus`). Los estados y sus valores
numéricos **reales** están en el código, en `Sigil.Plugins.Core/Domain/Choices.cs` — el resto del
código referencia los nombres lógicos, nunca los números:

| Estado | Valor (`TransactionStatus`) | Significado |
|--------|-----------------------------|-------------|
| **Borrador** | `159460000` | Creado, sin enviar; editable y eliminable |
| **Pendiente de Firma** | `159460001` | Enviado; aguardando firmas |
| **Firmado Parcialmente** | `159460002` | Al menos uno firmó, pero no todos |
| **Sellando** | `159460003` | Entre la última firma y el fin del pipeline asíncrono; no acepta más firmas |
| **Completado** | `159460004` | Pipeline exitoso; PDF final con ledger sellado |
| **Rechazado** | `159460005` | Un participante rechazó |
| **Expirado** | `159460006` | Venció el plazo sin completarse |
| **Error de Sellado** | `159460007` | El pipeline asíncrono falló tras reintentos |
| **Cancelado** | `159460008` | El creador retiró la solicitud antes del sellado |

Estados **terminales** (sin salida): Completado, Rechazado, Expirado, Cancelado.
**Casi-terminal:** Error de Sellado → su única salida es re-sellar (vuelve a *Sellando*) o cancelar.

El participante tiene su propia máquina de estados (`ParticipantStatus`, mismos valores base):
**Pendiente** (`159460000`) → **Turno Activo** (`159460001`, puede firmar ahora) → **Firmado**
(`159460002`) o **Rechazado** (`159460003`).

### 2.1 Diagrama de estados de la transacción

```
              CreateTransaction
                     │
                     ▼
                ┌──────────┐  UpdateDraft
                │ Borrador │◄────────────┐   (se re-lee, se revalida)
                └────┬─────┘─────────────┘
                     │ SendTransaction         DeleteDraft ──► (eliminada)
                     ▼
            ┌───────────────────┐
            │ Pendiente de Firma│──────────────────┐
            └────────┬──────────┘                  │
     SubmitSignature │ (no es el último)           │
                     ▼                             │  RejectTransaction ──► Rechazado
        ┌─────────────────────────┐               │  ExpireTransactions ──► Expirado
        │  Firmado Parcialmente    │◄──────────────┤  CancelTransaction  ──► Cancelado
        └────────────┬─────────────┘               │
     SubmitSignature │ (es el último firmante) ────┘
                     ▼
                ┌──────────┐   worker OK          ┌────────────┐
                │ Sellando │─────────────────────►│ Completado │  (terminal)
                └────┬─────┘                       └────────────┘
                     │ worker falla (definitivo)
                     ▼
            ┌──────────────────┐  RetrySealing
            │ Error de Sellado │────────────────► (vuelve a Sellando)
            └──────────────────┘
```

### 2.2 Los cinco pasos, en detalle

**Paso 1 — Crear (Borrador).** El creador arma la solicitud: sube el PDF, define participantes y
sus zonas de firma, elige enrutamiento (secuencial o paralelo). Custom API `CreateTransaction`
(unbound). La transacción nace en *Borrador*; todos los participantes nacen *Pendiente*. Mientras
esté en *Borrador* es editable (`UpdateDraft`) y eliminable (`DeleteDraft`).

**Paso 2 — Enviar.** El creador envía. Custom API `SendTransaction` (bound). Valida que haya al menos
un participante, PDF presente, y que cada participante tenga al menos una zona. La transacción pasa a
*Pendiente de Firma*. Se activan los turnos: en **secuencial** solo el orden 1 pasa a *Turno Activo*;
en **paralelo**, todos.

**Paso 3 — Firmar.** Cada firmante en *Turno Activo* firma. Custom API `SubmitSignature` (bound). Es
la operación crítica en concurrencia (§3.2). Registra la firma con snapshots de identidad tomados del
contexto del servidor; congela la imagen de la firma maestra vigente; calcula el hash del contenido.
En secuencial, activa el turno del siguiente. Si no es el último, la transacción pasa (o queda) en
*Firmado Parcialmente*; si es el último, pasa a *Sellando* y devuelve `IsLastSigner = true`.

**Paso 4 — Sellar (asíncrono).** El paso a *Sellando* dispara `SealingWorkerPlugin` como step
**asíncrono**. El worker: descarga el PDF de contenido, **re-verifica el hash de contenido** (jamás
sella contenido adulterado), incrusta las firmas en sus zonas, agrega una **hoja de cierre** con
metadatos y QR, calcula el **hash final**, obtiene el **token TSA (RFC 3161)** sobre ese hash final,
sube el PDF final, crea el registro de ledger (evidencia) y transiciona a *Completado*. El sellado es
asíncrono porque el pipeline puede tomar decenas de segundos y libera la UI de inmediato.

**Paso 5 — Verificar.** En cualquier momento posterior, cualquiera puede verificar un documento.
Custom API `VerifyDocument` (unbound): recibe el hash SHA-256 del PDF (calculado en el cliente, el
archivo **no se sube**) y lo contrasta contra el ledger. El resultado es auténtico/íntegro o no.

### 2.3 Transiciones alternativas

- **Rechazo** (`RejectTransaction`): un participante en *Turno Activo* rechaza → *Rechazado*.
- **Expiración** (`ExpireTransactions`, job): vencido `expireson` → *Expirado*. Solo aplica a
  *Pendiente de Firma* y *Firmado Parcialmente*.
- **Cancelación** (`CancelTransaction`): el creador retira antes del sellado → *Cancelado*.
- **Reintento de sellado** (`RetrySealing`): la única salida de *Error de Sellado* → vuelve a
  *Sellando* y re-dispara el worker.

Cuando la transacción llega a un estado terminal, los participantes **conservan** su último estado
(verdad histórica): no se reescriben.

---

## 3. Concurrencia — por qué el sellado es asíncrono y cómo se serializa

### 3.1 El presupuesto de 2 minutos

Los plugins de Dataverse, síncronos y asíncronos, tienen un límite de ejecución del sandbox
(~2 minutos). El sellado no cabe con holgura ahí (render de PDF, incrustación, TSA con reintentos,
persistencia). La solución no es "hacerlo más rápido" sino separar responsabilidades: `SubmitSignature`
hace lo mínimo síncrono (validar, lockear, decidir, registrar la intención) en milisegundos y libera
al usuario; el trabajo pesado corre después, invisible, en el worker asíncrono.

### 3.2 El lock de fila (`LockDeFila`)

**El problema:** dos firmantes en paralelo llaman `SubmitSignature` a la vez. Cada ejecución lee los
participantes para decidir si es el último. Sin serialización, ambos pueden verse mutuamente
pendientes (nadie transiciona a *Sellando* → transacción zombi) o ambos creerse últimos (doble worker).

**La solución** (`Sigil.Plugins/Apis/LockDeFila.cs`): todo plugin que decide sobre el estado
compartido ejecuta, **como primera operación**, un `Update` sobre la fila de la transacción usando
**exclusivamente una columna técnica de no-op** (`sanic_sigil_locktoken`). Ese `Update` toma el lock
de fila de SQL hasta el commit, serializando las ejecuciones concurrentes. El valor escrito es
irrelevante; lo que importa es que sea un `Update` a esa fila.

Reglas derivadas:

- **Prohibido lockear escribiendo `status`.** Los triggers de los flujos de notificación filtran por
  `status` y disparan aunque el valor escrito sea idéntico al existente → lockear por status
  generaría notificaciones duplicadas. Por eso la columna técnica dedicada.
- **Siempre re-leer después del lock.** Recién con las ejecuciones serializadas se lee el estado real
  y se decide sobre datos consistentes.
- **Idempotencia por participante.** `SubmitSignature` sobre un participante ya *Firmado* retorna
  éxito sin efectos (protege del doble clic del último firmante).

Usan el mismo lock: `SendTransaction`, `RejectTransaction`, `CancelTransaction`, `UpdateDraft`,
`DeleteDraft`, `RetrySealing` y el worker de sellado.

---

## 4. Modelo de confianza y seguridad

El valor de Sigil es probatorio: una firma completada debe ser difícil de falsificar y fácil de
verificar. El modelo se sostiene en tres pilares.

### 4.1 El backend es la única autoridad

Toda transición de estado, validación, hashing, sellado y escritura de evidencia ocurre en los
plugins C#, bajo **contexto de sistema**. La base común `SigilApiPlugin` crea el servicio con
`factory.CreateOrganizationService(null)` — un servicio elevado. Los usuarios **no** tienen privilegio
directo de Create/Update sobre las tablas de transacción, participante o evento: toda escritura pasa
por una Custom API que corre como sistema. La autorización a nivel de plataforma la da el **Execute
Privilege** propio de cada Custom API; `IsPrivate` **no** es un control de seguridad.

### 4.2 La identidad del frontend no es autoritativa

El frontend resuelve la identidad con `getContext()` del SDK (`PowerProvider` la expone una vez al
arranque). Pero esa identidad **solo sirve para que la UI oculte lo que el backend igualmente
rechazaría** — nunca es la fuente de verdad. Cuando un participante firma, el plugin toma los
**snapshots de identidad (nombre, correo, UPN, objectId de Entra) del contexto del servidor**
(`InitiatingUserId`), no de lo que mande el cliente. Un cliente manipulado no puede firmar en nombre
de otro: la identidad que queda registrada es la que el backend observa, no la que el frontend afirma.

Esto se ve en el código: la implementación real (`src/api/powerApps.ts`) filtra las lecturas
explícitamente por el `systemuserid` del llamante (resuelto del `objectId` de Entra), porque la
conexión corre como Service Principal y no aplica trimming de seguridad por usuario.

### 4.3 Evidencia criptográfica y la TSA

La prueba de integridad se sostiene en **dos hashes SHA-256** y una **marca de tiempo externa**:

- **Hash de contenido** — se calcula al enviar; prueba *qué* aprobaron y leyeron los firmantes. Se
  imprime en texto claro en la hoja de cierre y se ancla en el ledger. El worker lo re-verifica antes
  de sellar: si el contenido descargado no coincide, aborta (jamás sella contenido adulterado).
- **Hash final** — se calcula sobre el PDF completo (con firmas y hoja de cierre) al terminar; prueba
  que el archivo distribuido es idéntico byte a byte al sellado. Es el ancla de la verificación.
- **Token TSA (RFC 3161)** — se obtiene sobre el hash final ante una autoridad de sellado de tiempo
  **externa al tenant**. El token es una firma criptográfica de tiempo: si alguien (incluido un
  administrador del tenant) reescribiera el ledger, el token viejo dejaría de coincidir con el nuevo
  hash → la manipulación se vuelve **evidente**. La TSA es configurable por ambiente; si está apagada
  o falla, el ledger lo indica con un estado explícito (`tsastatus`) y la protección restante es
  interna (roles de solo lectura + seguridad a nivel de columna + auditoría nativa).

El **registro de ledger** (`sanic_sigil_tbl_ledgerentry`) es la evidencia: es *organization-owned*,
tiene una **alternate key** sobre `transactionid` (hace el insert idempotente — un reintento del worker
no crea un segundo ledger) y sus columnas sensibles están protegidas con seguridad a nivel de columna.
La verificación de un documento no requiere subir el archivo: el cliente calcula el hash y solo se
contrasta contra el ledger.

> **En una frase:** el frontend propone y muestra; el backend, como sistema, decide, hashea y sella; y
> la TSA externa convierte cualquier manipulación posterior en algo detectable.

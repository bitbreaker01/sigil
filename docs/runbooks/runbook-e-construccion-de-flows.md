# Runbook E — Construcción de los Cloud Flows de Sigil

**Documento operativo** (el CÓMO de F4; el QUÉ y el porqué viven en el [doc 08](../fase-0/08-notificaciones-y-recordatorios.md) — Aprobado)
**Aplica a:** se **construye en Dev** (los flows viajan managed a Test/Prod dentro de `sigil_core_sigil`). Las diferencias por ambiente están marcadas.
**Depende de:** Runbook A (§A4 Service Principal, §A5 cuenta de servicio + conexiones, §A7 modelo/env vars), doc 08 (diseño), doc 04 §3–4 (Custom APIs y contratos), doc 12 Apéndice A (valores numéricos de choices), doc 09 (despliegue).
**Regla de la casa (doc 11 §1 regla 5):** un paso NO está terminado hasta que su verificación está en verde. La verificación integral de los flows es el **gate 3 del Runbook B** + el **heartbeat** (§E7).

> Ningún dato de plataforma se escribe de memoria: cada afirmación de Power Automate lleva cita `[n]` con las **fuentes al final**. Lo no confirmable se marca **NO VERIFICADO**.

---

## E0. Prerrequisitos duros (sin esto, NO se empieza)

Todo esto lo produce el Runbook A. Chequealo antes de tocar Power Automate:

| # | Prerrequisito | Origen | Por qué bloquea |
|---|---------------|--------|-----------------|
| 1 | **Solución `sigil_core_sigil`** con modelo de datos, las **9 env vars** y los **5 choices** creados | Runbook A §A3/§A7 | Los flows son componentes de esta solución |
| 2 | **Apéndice A del doc 12 COMPLETO** — tabla "etiqueta → número real" de los choices | Runbook A §A2/§A7 | Los flows filtran **por número**; un número mal = flow que nunca dispara, sin error [1] |
| 3 | **Service Principal `Sigil Service`** con rol `Sigil \| SR \| Service` (CRUD org + **Callback Registration CRUD user-level**) | Runbook A §A4 | Sin privilegios sobre Callback Registration, **los triggers de Dataverse no se activan** (doc 08 §2) |
| 4 | **Cuenta de servicio** `sigil-notifications@` con **Power Automate Premium** + buzón + Teams | Runbook A §A5 | El conector Dataverse es premium; será el **owner** de los flows (gate 3 Runbook B) |
| 5 | **Las 3 conexiones** en estado *Connected* en el ambiente: Dataverse (SP), Office 365 Outlook, Microsoft Teams | Runbook A §A5 | Las connection references se atan a ellas (§E1) |
| 6 | **La app Power Automate habilitada en Teams** (una vez por tenant) | Runbook A §A5.3 | Sin eso, "Post card" falla |
| 7 | **DLP:** Dataverse + Outlook + Teams juntos en el grupo Business | Runbook A §A9 | Si están separados, el flow no activa |

**Identidad de construcción:** iniciá sesión en [make.powerautomate.com](https://make.powerautomate.com), **seleccioná el ambiente Dev**, y trabajá **dentro de la solución** `sigil_core_sigil` (Solutions → abrir la solución → New). Un flow creado fuera de la solución no viaja en el pipeline.

---

## E1. Connection references (el puente solución ↔ conexión)

Una **connection reference es un componente de solución** que apunta a una conexión de un conector; los flows *solution-aware* **se atan a la connection reference**, no a la conexión directa [2]. Esto es lo que permite que en Test/Prod se reasocie la conexión sin editar el flow: **al importar la solución, se provee una conexión para cada connection reference y los flows se encienden automáticamente** [3].

**Crear las 3 (dentro de la solución):** en la solución → command bar → **New > More > Connection Reference** [4]. Una por conector, atada a la conexión de §E0-5:

| Connection reference (display) | Conector | Conexión (Runbook A §A5) |
|-------------------------------|----------|--------------------------|
| `Sigil \| Conn \| Dataverse (SP)` | Microsoft Dataverse | Conexión **Connect with service principal** (app id + tenant + secret/cert) [8] |
| `Sigil \| Conn \| Outlook` | Office 365 Outlook | Cuenta `sigil-notifications@` |
| `Sigil \| Conn \| Teams` | Microsoft Teams | Cuenta `sigil-notifications@` |

> **Recordá (doc 09 §5, Runbook A §A5.4):** en despliegue por Pipelines, las conexiones para las connection references **las provee quien solicita el run** — hay que **compartir las 3 conexiones** con los operadores que despliegan. El SP delegado solo ejecuta el import.

**Regla de higiene:** creá las 3 connection references **antes** de armar los flows y **atá cada acción a su connection reference** (no crees conexiones ad-hoc dentro del flow — quedarían fuera de la solución).

---

## E2. Flow 1 — `Sigil | Cloud Flow | Notifications - Participant turn`

**Objetivo:** avisar al firmante que es su turno (RF-11). Cubre primer firmante, todos en paralelo, y la activación del siguiente en secuencial (P2/P2').

### E2.1 Trigger
Acción **"When a row is added, modified or deleted"** (Microsoft Dataverse), atada a `Sigil | Conn | Dataverse (SP)`. Configuración [5]:

| Campo | Valor | Nota |
|-------|-------|------|
| **Change type** | *Modified* (Update) | Solo cambios de fila existente [5] |
| **Table name** | `sanic_sigil_tbl_participant` | La tabla observada [5] |
| **Scope** | **Organization** | Dispara para filas de cualquier dueño, no solo del owner del flow [5] |
| **Select columns** | `sanic_sigil_status` | **Filtering attribute**: el flow dispara **solo cuando cambia esa columna** [5] — evita runs por cualquier otro update |
| **Filter rows** | `sanic_sigil_status eq 159460001` | OData: **solo** filas que quedan en **Turno Activo** (159460001 — doc 12 Apéndice A) [5] |

> **Higiene de números (regla del doc 08 §2):** al lado del literal `159460001`, poné una acción **Compose** nombrada `status = Turno Activo (159460001)` para que el flow sea legible. El número sale del Apéndice A del doc 12, **no** se calcula.

> **Por qué el filtering attribute es el status y no otra cosa:** los triggers de Dataverse **disparan aunque el valor escrito sea idéntico** al existente (doc 08 §2); por eso el backend lockea con `sanic_sigil_locktoken`, **jamás** con el status (doc 04 §5). La idempotencia de la notificación se apoya en el filtering attribute + disciplina del backend, no en el trigger.

### E2.2 Resolver destinatario e idioma
1. **Get a row by ID** (Dataverse) sobre `sanic_sigil_tbl_transaction` = la transacción de la fila participante (para nombre del documento, quién envía, mensaje del creador, `expireson`).
2. **List rows** sobre `usersettings` del `userId` del participante → leer `uilanguageid` → mapa LCID→(`es`|`en`); sin match → `env_DefaultLanguage` [9] (doc 08 §5). Las env vars se leen desde el **dynamic content selector** de un solution flow [9].

### E2.3 Enviar (Teams + correo) — contenido explícito

Un solo destinatario (el firmante) → correo y card van en **SU idioma** (el resuelto en §E2.2). Mantené un `Compose` por idioma (doc 08 §5) y elegí con un **Condition** sobre el idioma resuelto. Los `@{...}` son *dynamic content* de §E2.1/§E2.2 (no texto literal).

**Correo — "Send an email (V2)" [11]** (sin adjuntos — doc 08 §5):

*ES* — Asunto: `[Sigil] Tu turno de firma: @{transactionName}`
```html
<p>Hola @{recipientName},</p>
<p><b>@{senderName}</b> te envió el documento <b>«@{transactionName}»</b> para firmar.</p>
<p><i>Mensaje del solicitante:</i> @{creatorMessage}</p>  <!-- omitir este bloque si creatorMessage viene vacío -->
<p>Vence el <b>@{expiresOnLocal}</b> (@{expiresOnUtc} UTC).</p>
<p><a href="@{env_AppPlayUrl}?screen=sign&txId=@{txId}">Revisar y firmar</a></p>
<p style="color:#666">El documento se abre y firma dentro de Sigil; este correo no lleva adjuntos.</p>
```
*EN* — Subject: `[Sigil] Your turn to sign: @{transactionName}`
```html
<p>Hi @{recipientName},</p>
<p><b>@{senderName}</b> sent you <b>"@{transactionName}"</b> to sign.</p>
<p><i>Message from the requester:</i> @{creatorMessage}</p>
<p>Due <b>@{expiresOnLocal}</b> (@{expiresOnUtc} UTC).</p>
<p><a href="@{env_AppPlayUrl}?screen=sign&txId=@{txId}">Review &amp; sign</a></p>
<p style="color:#666">You review and sign inside Sigil; this email has no attachments.</p>
```

**Teams — "Post card in a chat or channel" [10]** (`Post as`: Flow bot · `Post in`: **Chat with Flow bot** · `Recipient`: `@{recipientEmail}`). Pegá este JSON en el campo **Adaptive Card** [19] (ES; la EN cambia solo los textos):
```json
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.4",
  "body": [
    { "type": "TextBlock", "text": "Sigil — Tu turno de firma", "weight": "Bolder", "size": "Medium", "wrap": true },
    { "type": "TextBlock", "text": "@{transactionName}", "weight": "Bolder", "wrap": true, "spacing": "None" },
    { "type": "FactSet", "facts": [
      { "title": "Envía:", "value": "@{senderName}" },
      { "title": "Vence:", "value": "@{expiresOnLocal}" }
    ]},
    { "type": "TextBlock", "text": "@{creatorMessage}", "wrap": true, "isSubtle": true }
  ],
  "actions": [
    { "type": "Action.OpenUrl", "title": "Revisar y firmar", "url": "@{env_AppPlayUrl}?screen=sign&txId=@{txId}" }
  ]
}
```
> **EN:** mismos campos; textos `"Sigil — Your turn to sign"` / `"Sent:"` / `"Due:"` / `"Review & sign"`.

> **NO VERIFICADO (labels/campos exactos):** los strings literales de `Change type` ("Modified"), de `Post as` ("Flow bot"), y el campo **`Recipient`** con `Post in` = **"Chat with Flow bot"** (DM 1:1 por Flow bot — usado también en E3.2 y E4.3) NO los confirmé verbatim en la referencia del conector: son inputs que aparecen en el **diseñador** de Power Automate, no en la página [10]. Se usan según el diseño de doc 08 §3/§5. La página SÍ confirma la acción `Post card in a chat or channel` y `Post as`/`Post in` [10]; el trigger confirma `Change type` [5].

---

## E3. Flow 2 — `Sigil | Cloud Flow | Notifications - Transaction state`

**Objetivo:** un **Switch** por estado destino de la transacción → matriz de notificaciones (doc 08 §4). Un solo trigger para los 5 desenlaces.

### E3.1 Trigger
**"When a row is added, modified or deleted"** atada a `Sigil | Conn | Dataverse (SP)` [5]:

| Campo | Valor |
|-------|-------|
| **Change type** | *Modified* |
| **Table name** | `sanic_sigil_tbl_transaction` |
| **Scope** | **Organization** |
| **Select columns** | `sanic_sigil_status` |
| **Filter rows** | `sanic_sigil_status eq 159460004 or sanic_sigil_status eq 159460005 or sanic_sigil_status eq 159460006 or sanic_sigil_status eq 159460007 or sanic_sigil_status eq 159460008` |

Los **5 estados que notifican** (doc 08 §3, Apéndice A doc 12): Completado 159460004 · Rechazado 159460005 · Expirado 159460006 · Error de Sellado 159460007 · Cancelado 159460008. Filtrarlos en el trigger **evita runs no-op** de los estados que no notifican (Borrador, Sellando, etc.).

### E3.2 Switch por estado — acciones y mensajes explícitos

**Antes del Switch** (común a todos los casos):
1. **Get a row by ID** de la transacción (fila del trigger) → creador, `transactionName`, y el **motivo/actor** desde el evento correspondiente (`sanic_sigil_choice_eventtype` — doc 06/03).
2. **List rows** `sanic_sigil_tbl_participant` donde la transacción = fila del trigger → participantes + `userId`.
3. Resolvé el **email** de cada destinatario (systemuser) y **excluí al actor** de la transición (doc 08 §4).

**Regla de envío (decisión del equipo — 2026-07-21):**
- **Correo → UN SOLO correo** por evento: `Send an email (V2)` con **todos** los destinatarios en `To` (string separado por `;`, armado con un `Compose`/`join`). Como pueden tener idiomas distintos, el **cuerpo es BILINGÜE** (bloque ES + bloque EN en el mismo correo). Así cumple "un solo correo, no por separado" **y** sirve a ambos idiomas. *(Reconcilia con doc 08 §5: para correo multi-destinatario, bilingüe en vez de por-idioma. Excepción: si el case tiene UN solo destinatario —Error de Sellado— el correo va en el idioma de esa persona.)*
- **Teams → uno POR destinatario** (separado): un **Apply to each** sobre los destinatarios → `Post card in a chat or channel` (`Recipient` = email de cada uno) [10], cada card **en el idioma de ESE destinatario** (`usersettings.uilanguageid` — §E2.2).

Deep link de todos los cases: `@{env_AppPlayUrl}?screen=detail&txId=@{txId}`. Luego el **Switch** sobre `sanic_sigil_status`, un case por estado:

---

**Case `159460004` — Completado** · destinatarios: **creador + todos los participantes** · **Teams + correo**
- **Correo (1, bilingüe)** — Asunto: `[Sigil] Documento sellado · Document sealed: @{transactionName}`
  - *ES:* El documento **«@{transactionName}»** fue firmado por todos y quedó **sellado**. Nivel de evidencia: **@{evidenceLevel}**. → *Ver documento*: `@{env_AppPlayUrl}?screen=detail&txId=@{txId}`
  - *EN:* "**@{transactionName}**" has been signed by everyone and is now **sealed**. Evidence level: **@{evidenceLevel}**. → *View document*: (mismo link)
- **Teams (por destinatario):** título **"Documento sellado y disponible" / "Document sealed & available"**, `FactSet` (Documento, Nivel de evidencia), botón **"Ver documento" / "View document"** → deep link.

**Case `159460005` — Rechazado** · destinatarios: **creador + participantes MENOS el rechazante** · **Teams + correo**
- **Correo (1, bilingüe)** — Asunto: `[Sigil] Firma rechazada · Signature rejected: @{transactionName}`
  - *ES:* **@{rejecterName}** **rechazó** «@{transactionName}». **Motivo:** @{reason}. → *Ver detalle*: link
  - *EN:* **@{rejecterName}** **rejected** "@{transactionName}". **Reason:** @{reason}. → *View details*: link
- **Teams (por destinatario):** título **"Firma rechazada" / "Signature rejected"**, `FactSet` (Quién, Motivo), botón **"Ver detalle" / "View details"**.

**Case `159460008` — Cancelado** · destinatarios: **todos los participantes (incluidos los que ya firmaron)** · **solo correo** (sin Teams — doc 08 §4)
- **Correo (1, bilingüe)** — Asunto: `[Sigil] Solicitud cancelada · Request cancelled: @{transactionName}`
  - *ES:* **@{creatorName}** **canceló** la solicitud «@{transactionName}». **Motivo:** @{reason}. → *Ver detalle*: link
  - *EN:* **@{creatorName}** **cancelled** "@{transactionName}". **Reason:** @{reason}. → *View details*: link

**Case `159460006` — Expirado** · destinatarios: **creador + participantes que estaban en Turno Activo** · **solo correo** (sin Teams)
- **Correo (1, bilingüe)** — Asunto: `[Sigil] Solicitud expirada · Request expired: @{transactionName}`
  - *ES:* **«@{transactionName}»** **venció** sin completarse. Firmas que faltaron: **@{missingSigners}**. → *Ver detalle*: link
  - *EN:* "**@{transactionName}**" **expired** without completing. Missing signatures: **@{missingSigners}**. → *View details*: link

**Case `159460007` — Error de Sellado** · destinatario: **solo el creador** · **Teams + correo**
- **Correo (1, en el idioma del creador)** — Asunto: `[Sigil] Acción requerida — error de sellado: @{transactionName}`
  - *ES:* El **sellado** de «@{transactionName}» **falló** y requiere tu acción: **reintentá el sellado** desde el detalle. → *Ver detalle*: link
  - *EN:* Sealing of "@{transactionName}" **failed** and needs your action: **retry sealing** from the details. → *View details*: link
- **Teams:** título **"Requiere tu acción: reintentar el sellado" / "Action required: retry sealing"**, botón **"Ver detalle" / "View details"** → deep link.

> **Nota de idioma en las cards Teams:** cada case arma su card con un `Compose` ES y otro EN (doc 08 §5) y elige por el idioma del destinatario dentro del `Apply to each`.

> **No hace falta el estado origen:** cada destino que notifica es inequívoco en la máquina de estados (doc 06) — ej. Completado solo viene de Sellando (doc 08 §3).

---

## E4. Flow 3 — `Sigil | Cloud Flow | Jobs - Daily`

**Objetivo:** disparar los 3 jobs del backend en orden, mandar los recordatorios, y cerrar con el heartbeat.

### E4.1 Trigger
**Recurrence** — frecuencia diaria, **de madrugada** en la zona del negocio (doc 08 §3).

### E4.2 Invocar los 3 jobs EN ORDEN (el orden importa)
Tres acciones **"Perform an unbound action"** (Dataverse), atadas a `Sigil | Conn | Dataverse (SP)`, en secuencia [12]. Cada Custom API es una **unbound action** (opera sobre el ambiente, no sobre una fila) [12]; se elige en el campo **Action name** [12]:

1. `sanic_sigil_capi_ExpireTransactions` → outputs `ExpiredCount`, `SanitizedCount` (doc 04 §3).
2. `sanic_sigil_capi_ProcessReminders` → output `RemindersJson` (doc 04 §4).
3. `sanic_sigil_capi_ResealPending` → outputs `ResealedCount`, `MovedToNoTsaCount`, `StillPendingCount`, `AnchorMismatchCount` (doc 04 §3).

> **Por qué ESE orden (doc 08 §3):** expirar **primero** excluye de los recordatorios lo que vence hoy — nadie recibe "firmá" minutos antes del "expiró". `ResealPending` respeta su cap de lote por corrida (doc 04 §4).

> **Las transiciones que los jobs provocan** (Expirado, Error de Sellado por saneamiento) las notifica el **Flow 2** vía su trigger — **este flow NO duplica** esos envíos (doc 08 §3).

### E4.3 Enviar los recordatorios (RF-12)

**¿Qué es `RemindersJson`? (recordatorio).** Es el **output de `sanic_sigil_capi_ProcessReminders`** (doc 04 §4): un **array JSON autosuficiente** — el job ya seleccionó a quién recordarle y metió TODO lo necesario adentro, así que el flow **no hace ningún lookup de composición** (doc 08 §1). Un ítem por recordatorio a enviar. Campos:

| Campo | Qué es |
|-------|--------|
| `participantId` / `userId` / `transactionId` | GUIDs (referencia; no se muestran) |
| `transactionName` | Nombre del documento |
| `daysWaiting` | Hace cuántos días espera la firma |
| `recipientEmail` / `recipientName` | A quién y a qué correo |
| `recipientLanguage` | `es`\|`en` — el idioma YA resuelto (no hay que consultar `usersettings`) |
| `senderName` | Quién envió la solicitud |
| `creatorMessage` | Mensaje del creador (puede venir vacío) |
| `expiresOnUtc` | Cuándo vence |

**Pasos:**
1. **Parse JSON** [13][14]: `Content` = `RemindersJson`; `Schema` generado con *Use sample payload to generate schema* [15] a partir de:
   ```json
   [ { "participantId": "g", "userId": "g", "transactionId": "g",
       "transactionName": "s", "daysWaiting": 5,
       "recipientEmail": "s", "recipientName": "s", "recipientLanguage": "es",
       "senderName": "s", "creatorMessage": "s", "expiresOnUtc": "s" } ]
   ```
2. **Apply to each** sobre el array → cada recordatorio es a **una** persona, así que va **un correo + una card por ítem**, en `@{items('Apply_to_each')?['recipientLanguage']}` (sin lookup). Deep link: `@{env_AppPlayUrl}?screen=sign&txId=@{items('Apply_to_each')?['transactionId']}`.

**Correo — "Send an email (V2)" [11]** (`To` = `@{...recipientEmail}`):
- *ES* — Asunto: `[Sigil] Recordatorio de firma: @{...transactionName}`
  «Hola **@{...recipientName}**, seguimos esperando tu firma en **«@{...transactionName}»** (enviado por @{...senderName}). Esperando hace **@{...daysWaiting}** días. Vence el **@{...expiresOnUtc}** UTC. → *Firmar ahora*: link»
- *EN* — Subject: `[Sigil] Signature reminder: @{...transactionName}`
  «Hi **@{...recipientName}**, we're still waiting for your signature on **"@{...transactionName}"** (sent by @{...senderName}). Waiting for **@{...daysWaiting}** days. Due **@{...expiresOnUtc}** UTC. → *Sign now*: link»

**Teams — "Post card in a chat or channel" [10]** (`Recipient` = `@{...recipientEmail}`): título **"Recordatorio de firma" / "Signature reminder"**, `FactSet` (Documento, Esperando, Vence), botón **"Firmar ahora" / "Sign now"** → deep link `?screen=sign`.

### E4.4 Heartbeat (el control del canal muerto — doc 08 §7)

Al final del job, **Send an email (V2)** al operador. **El destinatario es PARAMÉTRICO** por ambiente: una env var nueva **`sanic_sigil_env_OperatorEmail`** (Dev/Test/Prod cada uno con su buzón). `To` = `@{env_OperatorEmail}` — **jamás** un correo hardcodeado.

- **Asunto:** `[Sigil] Heartbeat del job diario: OK`
- **Cuerpo (basta un idioma — es interno del operador):**
  ```html
  <p>El job diario se ejecutó <b>OK</b>.</p>
  <ul>
    <li>Expiradas: <b>@{ExpiredCount}</b> (saneadas por T14: @{SanitizedCount})</li>
    <li>Recordatorios enviados: <b>@{length(body('Parse_JSON'))}</b></li>
    <li>Re-sellados: <b>@{ResealedCount}</b> · Movidas a "Sin sello TSA": @{MovedToNoTsaCount} · Aún pendientes: @{StillPendingCount} · <b>Anclas rotas: @{AnchorMismatchCount}</b></li>
  </ul>
  <p style="color:#a00">Si mañana no llega este correo, el canal de notificaciones está caído (doc 08 §7).</p>
  ```
- **Recordatorios** = `length(body('Parse_JSON'))` (cantidad de ítems del array — §E4.3), **no** el largo del string `RemindersJson`.
- **`AnchorMismatchCount > 0`** = integridad catastrófica (archivo ≠ hash del ledger, doc 04 §4) — resaltarlo para que el operador reaccione.

> ⚠️ **Ripple de `env_OperatorEmail` (env var nueva):** hoy el modelo tiene **9** env vars (tabla del doc 03 §8; el conteo lo verifica `CF-A09` del Runbook A). Agregar esta la lleva a **10** → hay que: (a) crearla en el modelo (Runbook A §A7) sin *current value* en la solución, (b) registrarla en la tabla canónica del doc 03 §8 y actualizar el test **`CF-A09`** (9→10), (c) darle valor por ambiente en el doc 09 §6 y en el **gate 4** del Runbook B. **Alternativa sin env var nueva:** mandar el heartbeat a `sigil-notifications@` y dejar que la **regla de reenvío** (A5.5) lo lleve al operador — pero eso NO es paramétrico por ambiente (el reenvío es el parámetro). Recomendado: la env var, porque es lo que pediste (paramétrico y pulcro por ambiente).

---

## E5. Reglas transversales (aplican a los 3)

- **Owner del flow = cuenta de servicio** `sigil-notifications@` (gate 3 Runbook B); edición restringida — un flow editado a mano en Prod es una puerta trasera del canal (doc 07 §6).
- **Idioma por destinatario** (doc 08 §5): reactivos → `usersettings.uilanguageid` [9]; recordatorios → `recipientLanguage` del JSON. Fechas → zona del destinatario con la UTC cruda entre paréntesis.
- **Plantillas es/en dentro del flow**, un `Compose` por idioma (la estructura fuerza a mantener ambos). Sin traducción remota (doc 08 §5).
- **Env vars, jamás hardcode:** los deep links salen de `env_AppPlayUrl` [9]. Ojo (doc 08 / plataforma): un cambio de valor de env var **fuera de ALM** no lo toma el flow hasta que se **guarda o se apaga/prende** [9].

## E6. Errores, reintentos y concurrencia

- **Retry policy** en las acciones de envío: **Default** (exponencial, hasta 4 reintentos) [16][17]; se setea en *Settings > Networking > Retry policy* de la acción [17]. (Motor compartido con Logic Apps — ver NO VERIFICADO.)
- **`Apply to each` de destinatarios con *continue on error*** — un destinatario caído no corta el resto. Los `Apply to each` corren **secuencial por defecto** [18]; **dejalos secuenciales** (concurrency OFF) — simplicidad, el job es de madrugada (doc 08 §7/§8). Precisión (doc 08 §7): el run resultante queda **Failed** (no existe "PartiallySucceeded") — correcto para diagnóstico.
- **Monitoreo real (doc 08 §7):** las alertas nativas van al buzón del owner (la cuenta de servicio, que nadie lee) → **regla de reenvío** al buzón del operador (Runbook A §A5.5) + revisión semanal de runs fallidos + el **heartbeat** (§E4.4).

## E7. Verificación (gate)

| Qué | Cómo |
|-----|------|
| Los 3 flows **encendidos**, owner = cuenta de servicio | **Gate 3 del Runbook B** (connection references + flows on + ownership reconciliado) |
| Trigger que dispara | Cambiar un participante a Turno Activo en datos semilla → llega el aviso |
| Cadena completa punta a punta | **Salida de F4** (doc 10): una transacción completa notificando de punta a punta + **heartbeat diario recibido** |
| DLP no bloquea | El primer run del flow se activa sin error de DLP (Runbook A §A9) |

## E8. Despliegue a Test/Prod (no se re-construyen)

Los flows **viajan managed** en `sigil_core_sigil` — **jamás se re-arman a mano** en Test/Prod. En el import (Pipelines o manual), se **provee una conexión para cada connection reference** y los flows se encienden solos [3]. Requisitos por ambiente (Runbook A §A5, doc 09 §7): las 3 conexiones deben **existir ANTES del primer run del pipeline**, y estar **compartidas** con quien solicita el despliegue. Post-import: **gate 3** del Runbook B.

---

## Fuentes

Verificadas contra Microsoft Learn el 2026-07-20.

1. Trigger de Dataverse dispara con valores idénticos; filtering attributes; privilegios de Callback Registration (doc 08 §2, ya verificado): https://learn.microsoft.com/en-us/power-automate/dataverse/create-update-delete-trigger
2. Connection reference = componente de solución; los flows solution-aware se atan a ella: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-connection-reference
3. En el import se provee una conexión por cada connection reference y los flows se encienden automáticamente: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-connection-reference
4. Crear connection reference en la solución (New > More > Connection Reference): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-connection-reference
5. Trigger de Dataverse (Change type, Table name, Scope, Select columns, Filter rows OData, Run as): https://learn.microsoft.com/en-us/power-automate/dataverse/create-update-delete-trigger
6. Trigger conditions (expresiones @ en Settings; AND por defecto): https://learn.microsoft.com/en-us/power-automate/customize-triggers
7. (reservado)
8. Conexión Dataverse con service principal (Connect with service principal: app id, secret, tenant): https://learn.microsoft.com/en-us/power-automate/dataverse/manage-dataverse-connections
9. Env vars en solution flows (dynamic content selector; no se re-lee hasta guardar/togglear): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-power-automate
10. Teams "Post card in a chat or channel" (Post as / Post in): https://learn.microsoft.com/en-us/connectors/teams/
11. Office 365 Outlook "Send an email (V2)": https://learn.microsoft.com/en-us/connectors/office365/
12. "Perform an unbound action" para invocar Custom APIs (unbound) desde el flow; campo Action name: https://learn.microsoft.com/en-us/power-automate/dataverse/bound-unbound
13. Parse JSON (Content + Schema): https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-perform-data-operations
14. Parse JSON crea tokens de las propiedades para pasos posteriores: https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-perform-data-operations
15. Parse JSON "Use sample payload to generate schema": https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-perform-data-operations
16. Retry policy: Default es exponencial, hasta 4 reintentos: https://learn.microsoft.com/en-us/azure/logic-apps/error-exception-handling
17. Retry policy se setea en Settings > Networking > Retry policy: https://learn.microsoft.com/en-us/azure/logic-apps/error-exception-handling
18. Apply to each corre secuencial por defecto; la concurrencia lo paraleliza: https://learn.microsoft.com/en-us/power-automate/guidance/coding-guidelines/implement-parallel-execution
19. Adaptive Cards — introducción + Schema Explorer (TextBlock, FactSet.facts[].title/value, Action.OpenUrl.title/url): https://learn.microsoft.com/en-us/adaptive-cards/authoring-cards/getting-started · https://adaptivecards.io/explorer/

**NO VERIFICADO (declarado):**
- Labels literales de dropdown: `Change type = "Modified"` y `Post as = "Flow bot"` no se confirmaron verbatim en una página oficial; se usan según el diseño ya aprobado en doc 08 §3/§5. Los **campos** (`Change type`, `Post as`/`Post in`) sí están confirmados [5][10].
- **Retry policy** y **Parse JSON** están citados desde las páginas de **Azure Logic Apps** (motor compartido con Power Automate). El path de menú exacto del diseñador de Power Automate cloud puede variar; lo estable es el concepto y los tipos [16][17][13].

---

*Runbook operativo de F4. Autoridad de diseño: doc 08. Anterior en la cadena de despliegue: Runbook A (aprovisionamiento) · Runbook B (gates post-import).*

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

### E2.3 Enviar (Teams + correo)
- **Teams:** acción **"Post card in a chat or channel"** (`Post as`: Flow bot; `Post in`: chat 1:1 con el firmante) [10] con una **Adaptive Card**: título, hechos (documento, quién envía, vence el `{expireson}`), **un solo botón** `Action.OpenUrl` con el deep link `{env_AppPlayUrl}?screen=sign&txId={txId}` [9].
- **Correo:** **"Send an email (V2)"** (Office 365 Outlook) [11] — asunto `[Sigil] Tu turno de firma: {documento}`, HTML simple, mismo CTA. **Jamás adjuntos** (doc 08 §5).

> **NO VERIFICADO (labels exactos):** los strings literales de `Change type` ("Modified") y de `Post as` ("Flow bot") no los confirmé verbatim en una página oficial; se usan según el diseño ya fijado en doc 08 §3/§5. La página del conector confirma la acción `Post card in a chat or channel` con sus parámetros `Post as`/`Post in` [10], y el trigger confirma el campo `Change type` [5].

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

### E3.2 Switch por estado y matriz de destinatarios
**Switch** sobre `sanic_sigil_status` de la fila. **Regla general: todos los indicados MENOS el actor de la transición** (doc 08 §4). Para armar destinatarios: **List rows** sobre `sanic_sigil_tbl_participant` donde la transacción = fila del trigger, + **Get a row by ID** de la transacción (creador, y el motivo/actor desde el evento correspondiente — `sanic_sigil_choice_eventtype`, doc 06/doc 03).

| Case (valor) | Destinatarios | Canales | Contenido | Deep link |
|--------------|---------------|---------|-----------|-----------|
| **159460004** Completado | Creador + todos los participantes | Teams + correo | "Documento sellado y disponible" + nivel de evidencia | `?screen=detail&txId=` |
| **159460005** Rechazado | Creador + participantes **menos el rechazante** | Teams + correo | Quién rechazó y el motivo | `?screen=detail&txId=` |
| **159460008** Cancelado | Todos los participantes (incluidos los que ya firmaron) | Correo | Quién canceló y el motivo | `?screen=detail&txId=` |
| **159460006** Expirado | Creador + participantes que estaban en **Turno Activo** | Correo | Venció sin completarse; firmas que faltaron | `?screen=detail&txId=` |
| **159460007** Error de Sellado | Creador | Teams + correo | "Requiere tu acción: reintentar el sellado" | `?screen=detail&txId=` |

Para cada case: **Apply to each** sobre la lista de destinatarios (resolver idioma por destinatario vía `usersettings.uilanguageid` — §E2.2), **excluyendo al actor**, y enviar Teams/correo como en §E2.3. Deep link `{env_AppPlayUrl}?screen=detail&txId={txId}`.

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
`RemindersJson` es **autosuficiente**: trae todo lo necesario, el flow **no hace lookups de composición** (doc 08 §1). 
1. **Parse JSON** [13][14]: `Content` = `RemindersJson`; `Schema` generado con *Use sample payload to generate schema* [15] a partir de (doc 04 §4):
   ```json
   [ { "participantId": "g", "userId": "g", "transactionId": "g",
       "transactionName": "s", "daysWaiting": 5,
       "recipientEmail": "s", "recipientName": "s", "recipientLanguage": "es",
       "senderName": "s", "creatorMessage": "s", "expiresOnUtc": "s" } ]
   ```
2. **Apply to each** sobre el array parseado → por cada ítem, Teams + correo (idioma directo de `recipientLanguage` — sin lookup), contenido "esperando hace `{daysWaiting}` días", deep link `{env_AppPlayUrl}?screen=sign&txId={transactionId}`.

### E4.4 Heartbeat (el control del canal muerto — doc 08 §7)
Al final, **Send an email (V2)** al buzón del operador: `[Sigil] Job diario OK: {ExpiredCount} expiradas, {N recordatorios} recordatorios, {ResealedCount} re-sellados` — donde **N = cantidad de ítems del array parseado** (`length(body('Parse_JSON'))`), **no** el largo del string `RemindersJson`. **Si un día no llega ese correo → el canal está muerto.** Barato y suficiente.

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

**NO VERIFICADO (declarado):**
- Labels literales de dropdown: `Change type = "Modified"` y `Post as = "Flow bot"` no se confirmaron verbatim en una página oficial; se usan según el diseño ya aprobado en doc 08 §3/§5. Los **campos** (`Change type`, `Post as`/`Post in`) sí están confirmados [5][10].
- **Retry policy** y **Parse JSON** están citados desde las páginas de **Azure Logic Apps** (motor compartido con Power Automate). El path de menú exacto del diseñador de Power Automate cloud puede variar; lo estable es el concepto y los tipos [16][17][13].

---

*Runbook operativo de F4. Autoridad de diseño: doc 08. Anterior en la cadena de despliegue: Runbook A (aprovisionamiento) · Runbook B (gates post-import).*

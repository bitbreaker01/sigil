# Conexiones y flujos

Referencia de las **connection references** y los **cloud flows** (Power Automate) de Sigil, extraída de la metadata real de la solución (`Other/Customizations.xml` y los tres JSON de `Workflows/`).

Sigil delega en tres cloud flows toda la mensajería asíncrona y el mantenimiento diario: notificar a los participantes cuando es su turno de firmar, notificar a creador y firmantes cuando una transacción llega a un estado terminal, y correr el barrido diario de expiraciones, recordatorios y re-sellados TSA. Los flows no contienen lógica de negocio pesada: esa vive en las [Custom APIs de Sigil](custom-apis.md). Los flows orquestan (disparar, obtener datos, componer mensajes) y llaman a esas APIs cuando hace falta.

---

## Connection references

Una connection reference es un puntero simbólico a una conexión concreta. Los flows referencian la connection reference por su **logical name**; la conexión física (credenciales, cuenta) se enlaza en cada ambiente. Esto es lo que permite mover la solución entre ambientes sin recablear cada acción a mano.

Sigil declara **tres** connection references, una por cada conector que usan los flows:

| Logical name | Display name | Conector | Para qué |
| --- | --- | --- | --- |
| `sanic_SigilConnDataverseSP` | Sigil \| Conn \| Dataverse (SP) | `shared_commondataserviceforapps` (Microsoft Dataverse) | Trigger de los flows por webhook sobre filas de Dataverse, lectura de transacciones/usuarios/participantes, y llamada a las Custom APIs vía acción "unbound". El sufijo **(SP)** indica que la conexión física se resuelve con un **Service Principal**, no con una cuenta de usuario. |
| `sanic_sigil_conn_outlook` | Sigil \| Conn \| Outlook | `shared_office365` (Office 365 Outlook) | Envío de correos: aviso de turno de firma, recordatorios, avisos de estado terminal, y el heartbeat diario al operador. |
| `sanic_sigil_conn_teams` | Sigil \| Conn \| Teams | `shared_teams` (Microsoft Teams) | Publicación de tarjetas adaptativas (Adaptive Cards) en el chat del Flow bot: turno de firma, recordatorios y estados terminales. |

### Typo conocido en el logical name de Dataverse

Las tres connection references **no siguen la misma convención de nomenclatura**:

- Outlook y Teams usan `snake_case` con el patrón completo del publisher: `sanic_sigil_conn_outlook`, `sanic_sigil_conn_teams`.
- Dataverse rompe el patrón: `sanic_SigilConnDataverseSP` — mezcla el prefijo `sanic_` con un tramo en `PascalCase` (`SigilConnDataverseSP`), sin los separadores `_sigil_conn_`.

Es un **typo/inconsistencia conocida**. No afecta el funcionamiento (el logical name es un identificador opaco y los tres flows lo referencian tal cual, con esa grafía exacta), pero rompe la [convención de nomenclatura](convenciones-nomenclatura.md) del resto de la solución. Queda registrado también en [Typos conocidos](typos-conocidos.md). Si algún día se corrige, hay que actualizar el logical name en las tres definiciones de flow que lo referencian, no solo en el XML de la solución.

---

## Cloud flows

Son tres. Todos comparten el parámetro de ambiente `sanic_sigil_env_AppPlayUrl` (URL de la app, usada para armar los deep links de "Firmar ahora" / "Revisar y firmar"). El de Jobs-Daily además usa `sanic_sigil_env_emailoperador` para el heartbeat.

### SigilCloudFlowNotifications-Participantturn

- **Trigger** — Webhook de Dataverse (`SubscribeWebhookTrigger`) sobre la tabla `sanic_sigil_tbl_participant`, en modificación (`message: 3`) con scope de organización (`scope: 4`), filtrando por el atributo `sanic_sigil_status` con la expresión `sanic_sigil_status eq 159460001`. Es decir: se dispara cuando un **participante** pasa a estado **Turno Activo** (`159460001` de `participantstatus`).
- **Propósito** — Avisarle al firmante que le llegó su turno de firmar, por correo y por tarjeta de Teams.
- **Acciones (resumen)**:
  1. `Obtener_transaccion` — lee la transacción asociada (`sanic_sigil_tbl_transactions`) por el lookup del participante, para tomar nombre, dueño, vencimiento y mensaje del solicitante.
  2. `Obtener_Usuario` — lee el `systemuser` del participante (`userid`) para obtener nombre y correo interno.
  3. `Post_card_in_a_chat_or_channel` — publica una Adaptive Card en Teams (Flow bot) con los datos de la transacción y un botón "Revisar y firmar" que abre el deep link a la app.
  4. `Send_an_email_(V2)` — manda el mismo aviso por Outlook, con enlace a firmar. El correo aclara que el documento se firma dentro de Sigil y no lleva adjuntos.
- **Connection references usadas** — `sanic_SigilConnDataverseSP` (trigger + lecturas), `sanic_sigil_conn_teams` (tarjeta), `sanic_sigil_conn_outlook` (correo).

### SigilCloudFlowNotifications-Transactionstate

- **Trigger** — Webhook de Dataverse (`SubscribeWebhookTrigger`) sobre la tabla `sanic_sigil_tbl_transaction`, en modificación (`message: 3`), scope de organización (`scope: 4`), filtrando por `sanic_sigil_status` con la expresión que cubre los **cinco estados terminales**: `159460004 or 159460005 or 159460006 or 159460007 or 159460008` — es decir **Completado, Rechazado, Expirado, Error de Sellado y Cancelado** (choice `transactionstatus`).
- **Propósito** — Notificar a creador y firmantes cuando una transacción cierra su ciclo de vida, con un mensaje distinto según el desenlace.
- **Acciones (resumen)**:
  1. `Obtener_transaccion` y `Obtener_creador` — leen la transacción y el `systemuser` dueño/creador.
  2. `Apply_to_each` — recorre los participantes y arma la lista de correos en la variable `arrParticipantes`.
  3. `Switch` sobre `sanic_sigil_status` con un caso por estado terminal:
     - **Completado** (`159460004`) — "Documento sellado": avisa a creador + firmantes que quedó firmado por todos y disponible.
     - **Rechazado** (`159460005`) — avisa el rechazo (obtiene el participante que rechazó).
     - **Cancelado** (`159460008`) — avisa la cancelación por el creador.
     - **Expirado** (`159460006`) — avisa que la transacción venció sin completarse.
     - **Error de Sellado** (`159460007`) — avisa la falla de sellado.
     - `default` — sin acciones (los estados no terminales no llegan por el filtro del trigger).
     Cada caso compone el mensaje y lo envía por **correo (Outlook)** y, según el estado, publica **tarjeta en Teams**.
- **Connection references usadas** — `sanic_SigilConnDataverseSP` (trigger + lecturas), `sanic_sigil_conn_outlook` (correos), `sanic_sigil_conn_teams` (tarjetas).

### SigilCloudFlowJobs-Daily

- **Trigger** — Recurrencia diaria (`Recurrence`): una vez por día, a las **05:00** hora de **Central America Standard Time**.
- **Propósito** — Barrido de mantenimiento del sistema: expirar transacciones vencidas, enviar recordatorios de firma pendiente, y re-sellar lo que requiere nuevo sello TSA. Delega la lógica en las [Custom APIs de jobs](custom-apis.md) y usa el resultado para notificar y reportar.
- **Acciones (resumen)** — en secuencia, cada paso corre tras el éxito del anterior:
  1. `Expirar` — llama a la Custom API `sanic_sigil_capi_ExpireTransactions` (acción unbound sobre Dataverse). Devuelve conteos (`ExpiredCount`, `SanitizedCount`).
  2. `Recordatorios` — llama a `sanic_sigil_capi_ProcessReminders`. Devuelve `RemindersJson` con los recordatorios a enviar.
  3. `Resellar` — llama a `sanic_sigil_capi_ResealPending`. Devuelve conteos de re-sellado (`ResealedCount`, `MovedToNoTsaCount`, `StillPendingCount`, `AnchorMismatchCount`).
  4. `Parsear_recordatorios` — parsea el `RemindersJson` a un array de destinatarios (transacción, firmante, correo, días esperando, vencimiento, etc.).
  5. `Recorrer_recordatorios` — por cada recordatorio: manda **correo** (Outlook) y **tarjeta de Teams** con enlace "Firmar ahora".
  6. `Heartbeat` — manda un correo de "job diario: OK" al **operador** (`sanic_sigil_env_emailoperador`) con el resumen de conteos de los tres pasos. Sirve de señal de vida: si no llega el correo, el canal de notificaciones está caído.
- **Connection references usadas** — `sanic_SigilConnDataverseSP` (llamadas a las Custom APIs), `sanic_sigil_conn_outlook` (recordatorios + heartbeat), `sanic_sigil_conn_teams` (tarjetas de recordatorio).

---

## Ver también

- [Custom APIs de Sigil](custom-apis.md) — las acciones unbound que invoca el flow diario (`ExpireTransactions`, `ProcessReminders`, `ResealPending`) y qué devuelven.
- [Convenciones de nomenclatura](convenciones-nomenclatura.md) — el patrón `sanic_sigil_*` que respetan Outlook y Teams pero no la de Dataverse.
- [Typos conocidos](typos-conocidos.md) — registro del logical name inconsistente `sanic_SigilConnDataverseSP`.

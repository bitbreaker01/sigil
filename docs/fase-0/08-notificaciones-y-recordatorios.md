# Sigil — Notificaciones y Recordatorios

**Documento:** 08 — Fase 0
**Estado:** **Aprobado** (2026-07-12 — visto bueno del equipo)
**Última actualización:** 2026-07-10
**Depende de:** [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) (ADR-003), [03-modelo-datos-dataverse.md](03-modelo-datos-dataverse.md), [04-backend-motor-criptografico.md](04-backend-motor-criptografico.md), [06-maquina-de-estados-y-flujos.md](06-maquina-de-estados-y-flujos.md), [07-seguridad-y-cumplimiento.md](07-seguridad-y-cumplimiento.md) (A13), [12-convenciones-nomenclatura.md](12-convenciones-nomenclatura.md)

Los hechos de Power Automate citados fueron verificados contra documentación oficial (fuentes en §10).

---

## 1. Principios (heredados, no negociables)

1. **El motor transiciona; la mensajería observa** (ADR-003, doc 06 R3): los flows reaccionan a cambios de estado. Ningún flow contiene lógica de negocio, cripto ni binarios.
2. **Best-effort declarado:** una notificación fallida NO afecta evidencia ni estados — el dashboard es la fuente de verdad (doc 05). Los fallos se monitorean (§7), no se compensan con lógica.
3. **Los jobs deciden, los flows disparan y envían** (doc 04 §3.1): el output de las APIs es autosuficiente — el flow **no hace lookups de composición**.
4. Nomenclatura: `Sigil | Cloud Flow | <Dominio> - <Acción>` (doc 12).

## 2. Realidad de plataforma que condiciona el diseño (verificada)

- **Los triggers de Dataverse disparan aunque el valor escrito sea idéntico al existente** ("The flow runs even when the values included are the same as existing values"). Consecuencias: (a) el lock de fila del backend usa SOLO la columna técnica `sanic_sigil_locktoken`, jamás el status (regla endurecida en doc 04 §5); (b) la idempotencia de notificaciones es **por transición real gracias al filtering attribute + disciplina del backend**, no por magia del trigger.
- **Los flows comparan choices por VALOR NUMÉRICO**, no por nombre lógico (el Filter rows es OData: `sanic_sigil_status eq <número>`; el formatted value no sirve en condiciones de trigger). Los valores reales nacen del Option Value Prefix del publisher Sanic al crearlo: **el runbook del doc 09 produce la tabla canónica nombre lógico → valor numérico y la registra como apéndice del doc 12** — ningún flow se construye antes de que exista esa tabla. Regla de higiene: junto a cada literal numérico en un flow, un Scope/acción nombrada con el nombre lógico del estado.
- La identidad del trigger necesita privilegios **user-level CRUD sobre Callback Registration** (sin ellos los triggers no se activan) + Read sobre `systemuser` y `usersettings` — incorporados al rol Sigil \| SR \| Service (doc 03 §6, actualizado).
- Variables de entorno: soporte nativo en flows de solución (panel Parameters) — los deep links salen de `env_AppPlayUrl`, jamás hardcodeados.

## 3. Catálogo de flows (3 — deliberadamente pocos)

| Flow | Trigger | Responsabilidad |
|------|---------|-----------------|
| **Sigil \| Cloud Flow \| Notifications - Participant turn** | Dataverse *When a row is modified* sobre `sanic_sigil_tbl_participant`; **Scope: Organization**; filtering attribute `sanic_sigil_status`; Filter rows: status = Turno Activo (valor numérico) | Notifica al firmante que es su turno (RF-11) — cubre primer firmante, todos en paralelo y activación del siguiente en secuencial (P2/P2') |
| **Sigil \| Cloud Flow \| Notifications - Transaction state** | Dataverse *When a row is modified* sobre `sanic_sigil_tbl_transaction`; **Scope: Organization**; filtering attribute `sanic_sigil_status`; **Filter rows con SOLO los 5 estados que notifican** (Completado, Rechazado, Cancelado, Expirado, Error de Sellado — evita runs no-op de T4/T6/T7) | Switch por estado destino → matriz §4. **No necesita el estado origen**: cada destino que notifica es inequívoco en la máquina de doc 06 (ej. Completado solo viene de Sellando) |
| **Sigil \| Cloud Flow \| Jobs - Daily** | Recurrencia diaria **de madrugada** (zona del negocio) | Invoca en orden: `capi_ExpireTransactions` → `capi_ProcessReminders` → `capi_ResealPending` (acción **Perform an unbound action**; outputs → Parse JSON). **El orden importa**: expirar primero excluye de los recordatorios a las transacciones que vencen HOY (nadie recibe "firmá" minutos antes del "expiró"). Con `RemindersJson` (autosuficiente — doc 04 §4) envía los recordatorios. Las transiciones que los jobs provocan (Expirado, Error de Sellado por saneamiento) notifican vía el flow de *Transaction state* — este flow NO duplica esos envíos |

**Por qué 3 y no 7:** cada flow extra = una connection reference, un punto de fallo y una fila de ALM más. El switch mantiene un solo trigger para los desenlaces de transacción; *Participant turn* se separa porque su trigger es otra tabla; *Jobs* porque es programado, no reactivo.

## 4. Matriz de notificaciones

**Regla general de destinatarios: todos los indicados MENOS el actor de la transición** (quien rechaza no recibe "X rechazó"; quien cancela no se auto-notifica).

| Evento | Destinatarios | Canales | Contenido mínimo | Deep link |
|--------|---------------|---------|------------------|-----------|
| Turno activo (P2/P2') | El firmante | Teams + correo | Documento, quién lo envía, mensaje del creador, vence el {expireson} | `?screen=sign&txId=` |
| Recordatorio (RF-12) | Firmantes de `RemindersJson` | Teams + correo | Ídem + "esperando hace {daysWaiting} días" (todo viene en el JSON — sin lookups) | `?screen=sign&txId=` |
| Completado (T8) | Creador + todos los participantes | Teams + correo | "Documento sellado y disponible"; nivel de evidencia | `?screen=detail&txId=` |
| Rechazado (T11) | Creador + participantes (menos el rechazante) | Teams + correo | Quién rechazó y el motivo | `?screen=detail&txId=` |
| Cancelado (T13) | Todos los participantes (incluidos los que ya firmaron — les cambia el estatus de algo que firmaron) | Correo | Quién canceló y el motivo (si hay) | `?screen=detail&txId=` |
| Expirado (T12) | Creador **+ participantes que estaban en Turno Activo** (su card de "pendiente" desaparece del dashboard — sin este aviso, el documento se les esfuma sin explicación) | Correo | Venció sin completarse; firmas que faltaron | `?screen=detail&txId=` |
| Error de Sellado (T9/T14) | Creador | Teams + correo | "Requiere tu acción: reintentar el sellado" | `?screen=detail&txId=` |

**Omisiones deliberadas (declaradas):** envío T4 (la notificación útil es el turno), firma intermedia T5 (el creador lo ve en su dashboard con polling — anti-spam), re-sellado TSA obtenido (detalle técnico visible en el detalle), verificaciones (registro, no noticia).

## 5. Idioma y plantillas (RNF-06)

- Idioma por destinatario: en recordatorios viene en `RemindersJson.recipientLanguage`; en los flows reactivos se lee `usersettings.uilanguageid` del destinatario → mapa LCID→(`es`\|`en`), no mapeado → `env_DefaultLanguage`. **Tradeoff declarado:** `uilanguageid` es el mejor proxy disponible server-side; el toggle de idioma de la Code App vive en `localStorage` del navegador (doc 05 §7) y NO es legible por los flows — un usuario puede ver la app en un idioma y recibir correos en otro. Aceptado; si duele, la mejora futura es persistir la preferencia en Dataverse.
- Fechas: convertidas a la zona del destinatario (`usersettings.timezonecode` → `timezonedefinitions` → `convertTimeZone`), con la fecha UTC cruda entre paréntesis (coherente con doc 04 §6.3). Si la resolución de zona falla → UTC explícito solamente.
- **Plantillas dentro de los flows**, en pares es/en por mensaje (un Compose por idioma — la estructura fuerza a mantener ambos). Sin servicios de traducción ni contenido remoto.
- **Teams:** acción **"Post card in a chat or channel"**, *Post as: Flow bot*, *Post in: Chat with Flow bot* (1:1, sin licencias extra; requiere la app Power Automate habilitada en el admin de Teams — nota de runbook; no alcanza a invitados — irrelevante: sin firmantes externos). Adaptive Card: título, hechos, **un solo botón** `Action.OpenUrl` con el deep link.
- **Correo:** asunto `[Sigil] {acción}: {documento}`; HTML simple, mismo contenido y único CTA. **Jamás adjuntos** — el documento se descarga autenticado desde la app; el correo no es canal de evidencia.

## 6. Conexiones, cuentas e identidad (hereda A13 — doc 07)

| Conexión | Identidad | Notas |
|----------|-----------|-------|
| Dataverse (triggers + jobs) | **Service Principal** (rol Sigil \| SR \| Service con los privilegios de §2) | Credencial crítica (A13): certificado con rotación, dueño único, jamás compartida |
| Office 365 Outlook + Microsoft Teams | **Cuenta de servicio** `sigil-notifications@…` (buzón licenciado) | Los conectores Outlook/Teams exigen **OAuth interactivo de una cuenta real** — verificado: una cuenta con sign-in interactivo bloqueado NO puede crear ni re-consentir las conexiones ni reasociar connection references en despliegues. Postura realista: cuenta con **MFA + Conditional Access restringido** (ubicación/dispositivo administrado); sesión interactiva solo para el ritual de conexión/re-consent, ejecutado por el operador según runbook (doc 09) |

- **Connection references** para las tres conexiones, dentro de la solución (RNF-05); reasociación por ambiente en el despliegue (doc 09).
- Flows en la solución, **propietario = cuenta de servicio**, edición restringida (un flow editado a mano en Prod es una puerta trasera del canal — doc 07 §6).

## 7. Errores, reintentos y monitoreo

- Retry policy default (exponencial) en las acciones de envío.
- `foreach` de destinatarios con *continue on error* — un destinatario caído no corta el resto. Precisión verificada: el run resultante queda **Failed** (no existe "PartiallySucceeded" en cloud flows) — correcto para diagnóstico.
- **Semántica del evento 5 (corregida en doc 03):** "Recordatorio **programado**" — el job lo registra al seleccionar; la entrega por el canal es best-effort del flow. La línea de tiempo no afirma entregas que no puede garantizar. Un envío fallido se recupera en la próxima cadencia (`lastreminderon` no se revierte — costo aceptado: un ciclo de espera).
- **Monitoreo real (no el default):** las alertas nativas de fallo van al buzón del OWNER — que es la cuenta de servicio, un buzón que nadie lee. Mecanismo obligatorio: **regla de reenvío del buzón de servicio al buzón del equipo operador** + revisión semanal de runs fallidos como rutina (se integra con la observabilidad del doc 11). **Escalation path del canal muerto:** si la conexión Outlook/Teams expira o el buzón se deshabilita, TODOS los envíos fallan a la vez — el síntoma es el silencio; el control es un **heartbeat**: el flow de Jobs termina enviando un correo diario de resumen al operador ("job OK: X expiradas, Y recordatorios, Z re-sellados") — si un día no llega, el canal está muerto. Barato y suficiente.

## 8. Límites y volumen

Volumen esperado (doc 03 §9: ~1.000 transacciones/año) ≈ decenas de runs/día — muy por debajo de los límites por conexión/plan. El `foreach` de recordatorios corre secuencial (sin concurrencia — simplicidad; el job es de madrugada). Si el volumen crece 10×, vigilar el flow de *Transaction state*; sin acción preventiva hoy.

## 9. Trazabilidad

| RF/RNF | Sección |
|--------|---------|
| RF-05 | §4 (Completado) |
| RF-11 | §3, §4, §5 |
| RF-12 | §3 (Jobs - Daily), §4, §7 (semántica del evento 5) |
| RF-27/RF-30 | §4 (Expirado con aviso a firmantes activos / Cancelado a todos) |
| RNF-05 | §6 |
| RNF-06 | §5 (con tradeoff declarado) |
| ADR-003 / doc 06 R3 | §1, §3 |
| Doc 07 A13 | §6 |

## 10. Fuentes verificadas

- Trigger Dataverse (dispara con valores idénticos; Scope; filtering attributes; privilegios de Callback Registration): learn.microsoft.com/power-automate/dataverse/create-update-delete-trigger
- Teams "Post card in a chat or channel" / Flow bot / Adaptive Cards: learn.microsoft.com/power-automate/teams/send-a-message-in-teams · /overview-adaptive-cards · learn.microsoft.com/connectors/teams
- Perform unbound action (Custom APIs desde flows): learn.microsoft.com/power-automate/dataverse/bound-unbound
- Env vars en flows de solución: learn.microsoft.com/power-apps/maker/data-platform/environmentvariables-power-automate
- `usersettings` (uilanguageid, timezonecode): learn.microsoft.com/power-apps/developer/data-platform/reference/entities/usersettings
- Conexiones OAuth de conectores: learn.microsoft.com/power-automate/dataverse/manage-dataverse-connections

---

*Anterior: [07-seguridad-y-cumplimiento.md](07-seguridad-y-cumplimiento.md) · Siguiente: 09 — ALM, entornos y despliegue.*

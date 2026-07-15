# Sigil — Máquina de Estados y Flujos

**Documento:** 06 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — visto bueno del equipo)
**Última actualización:** 2026-07-10
**Depende de:** [03-modelo-datos-dataverse.md](03-modelo-datos-dataverse.md) (choices §3), [04-backend-motor-criptografico.md](04-backend-motor-criptografico.md) (APIs, concurrencia §5, worker §7)

Este documento es la **autoridad única sobre transiciones de estado**. Los docs 03 (valores), 04 (APIs) y 05 (UI) referencian esta matriz; ninguna transición existe si no está acá. Estados referidos por **nombre lógico** (doc 03 §3).

---

## 1. Máquina de estados de TRANSACCIÓN

```
                                 ┌──────────────────────────────────────────┐
                                 │ (rechazo / expiración / cancelación)     │
                                 ▼                                          │
 Borrador ──Send──▶ Pendiente de Firma ──1ª firma──▶ Firmado Parcialmente ──┤
    │                     │                                  │              │
    │ DeleteDraft         │ (firmante único                  │ última firma │
    ▼                     │  firma)                          ▼              │
 (eliminada)              └──────────────────────────▶   Sellando ◀──Retry── Error de Sellado
                                                             │                    ▲
                                                             │ worker OK          │ worker falla
                                                             ▼                    │ (definitivo)
                                                         Completado          (desde Sellando)

 Terminales sin salida: Completado · Rechazado · Expirado · Cancelado
 Casi-terminal: Error de Sellado (única salida: Retry → Sellando)
```

### 1.1 Matriz de transiciones (exhaustiva)

| # | Desde | Hacia | Disparador | Actor autorizado | Guards (además del lock de fila — doc 04 §5) | Efectos y evento |
|---|-------|-------|-----------|------------------|----------------------------------------------|------------------|
| T1 | — | Borrador | `capi_CreateTransaction` | Cualquier usuario con rol Sigil \| SR \| User | Validaciones doc 04 §3.4 | Crea transacción + participantes + zonas; evento 1 |
| T2 | Borrador | Borrador | `capi_UpdateDraft` | Creador | Estado = Borrador; revalidación de zonas si cambia el PDF | Sin evento (edición de borrador no es hito) |
| T3 | Borrador | *(eliminada)* | `capi_DeleteDraft` | Creador | Estado = Borrador | **Orden obligatorio**: el plugin borra PRIMERO los eventos del borrador en contexto elevado (el Delete **Restrict** de la relación transacción→evento bloquea el delete del padre mientras exista CUALQUIER hijo — verificado; el evento 1 siempre existe), luego borra la transacción (cascada elimina participantes/zonas). Excepción acotada a R6: los borradores son pre-historia |
| T4 | Borrador | Pendiente de Firma | `capi_SendTransaction` | Creador | ≥1 participante; PDF presente; secuencial: órdenes 1..N sin huecos; **todo participante con ≥1 zona de firma (RF-28, 2026-07-13)** | `senton`, `expireson`, `contenthash`; share a participantes; activa turnos (§2 P1/P2); evento 2; dispara notificaciones (doc 08) |
| T5 | Pendiente de Firma | Firmado Parcialmente | `capi_SubmitSignature` (no es el último) | Participante en Turno Activo | Decisión "último" DESPUÉS del lock (doc 04 §5) | Ver §2 P3; evento 3 |
| T6 | Pendiente de Firma | Sellando | `capi_SubmitSignature` (único/último firmante) | Participante en Turno Activo | Ídem T5 | Evento 3 + evento 6 (sellado iniciado); dispara worker |
| T7 | Firmado Parcialmente | Sellando | `capi_SubmitSignature` (último) | Participante en Turno Activo | Ídem T5 | Ídem T6 |
| T8 | Sellando | Completado | Worker (doc 04 §7) éxito | Sistema | Pipeline completo (ledger creado, archivo final subido) | `completedon`; evento 7; notificación final (RF-05) |
| T9 | Sellando | Error de Sellado | Worker — fallo definitivo (incl. reintentos agotados de `OperationStatus.Retry`) | Sistema | — | Evento 8 con detalle accionable; visible con alerta en dashboard del creador (doc 05 §4.1) |
| T10 | Error de Sellado | Sellando | `capi_RetrySealing` | Creador | Estado = Error de Sellado | Evento 6 con `details` = "reintento manual" (distingue las entradas de la línea de tiempo); re-dispara worker (idempotente — doc 04 §7) |
| T11 | Pendiente de Firma \| Firmado Parcialmente | Rechazado | `capi_RejectTransaction` | Participante en Turno Activo | Motivo obligatorio | Participante → Rechazado (§2 P4); evento 4; notifica a creador y participantes |
| T12 | Pendiente de Firma \| Firmado Parcialmente | Expirado | `capi_ExpireTransactions` (job diario) | Service Principal | `expireson` < ahora; **solo estos dos estados** (doc 04 §3.1) | Evento 10; notifica al creador |
| T13 | Pendiente de Firma \| Firmado Parcialmente \| **Error de Sellado** | Cancelado | `capi_CancelTransaction` | Creador | Motivo opcional | Evento 12; notifica a participantes. Error de Sellado se incluye para cerrar el ciclo de vida de fallos **deterministas** (ej. mismatch de `contenthash`): sin esta salida, una transacción irreparable viviría eternamente con alerta en el dashboard |
| T14 | Sellando | Error de Sellado | Job diario (saneamiento — regla R7) | Sistema | Sellando hace > **24 horas** sin actividad del worker | Evento 8 con `details` = "saneamiento: worker sin actividad" |

**Transiciones prohibidas por diseño (los guards las rechazan explícitamente):**
- Ninguna salida de **Completado, Rechazado, Expirado, Cancelado** — jamás, ni por admin (no existe API que lo haga; recordar que nadie tiene Update directo — doc 03 §6).
- Nada interrumpe **Sellando** por acción de usuario: ni cancelación, ni expiración, ni rechazo (el lock de fila serializa; una cancelación concurrente al último SubmitSignature pierde la carrera y recibe error limpio). La única salida no-worker es el saneamiento T14 (regla R7).
- Error de Sellado no expira (T12 lo excluye): una transacción con todas las firmas puestas no se pierde por un fallo técnico. Sus dos salidas: reintento (T10) o cancelación por el creador (T13).

## 2. Máquina de estados de PARTICIPANTE

```
 Pendiente ──(turno)──▶ Turno Activo ──SubmitSignature──▶ Firmado
                             │
                             └──RejectTransaction──▶ Rechazado
```

| # | Desde | Hacia | Disparador | Regla |
|---|-------|-------|-----------|-------|
| P1 | *(creación)* | Pendiente | `capi_CreateTransaction` / `capi_UpdateDraft` | Todos nacen Pendiente |
| P2 | Pendiente | Turno Activo | `capi_SendTransaction` | **Secuencial:** solo orden 1. **Paralelo:** todos (doc 03 §3). Sella `turnactivatedon`; dispara notificación de turno |
| P2' | Pendiente | Turno Activo | `capi_SubmitSignature` del anterior | Solo secuencial: al firmar el orden N, el orden N+1 se activa (`turnactivatedon`, notificación) |
| P3 | Turno Activo | Firmado | `capi_SubmitSignature` | `signedon`, snapshots de identidad, copia de firma a `signaturesnapshot` (doc 04 §3.1). **Idempotente**: re-submit sobre Firmado = éxito sin efectos |
| P4 | Turno Activo | Rechazado | `capi_RejectTransaction` | `rejectionreason`; la transacción transiciona T11 |

**Reglas de coherencia participante ↔ transacción:**
- Cuando la transacción alcanza un estado terminal (Rechazado/Expirado/Cancelado), los participantes **conservan su último estado** — es la verdad histórica (quién había firmado, quién no llegó a actuar).
- **Precedencia de guards en `SubmitSignature` (resuelve el doble-click del ÚLTIMO firmante):** (1º) **idempotencia por participante** — si el participante ya está *Firmado*, éxito sin efectos, **sin importar** el estado de la transacción (el primer click pudo haberla movido a Sellando); (2º) guard de estado de transacción — aplica solo si el participante sigue accionable (Turno Activo). Para el resto de las APIs, el guard de transacción va primero.
- `SubmitSignature` exige **Firma Maestra configurada** — guard explícito con error accionable ("configurá tu firma primero"); la redirección de la UI (doc 05 §4.3) es cortesía, no control.
- Un participante *Pendiente* en secuencial **no puede rechazar** (doc 04 §3.3): aún no recibió el documento.
- No existe "des-firmar": P3 no tiene inversa. El arrepentimiento pre-sellado se canaliza como pedido de cancelación al creador (T13).

### 2.1 Sub-máquina del sello TSA (ledger — `tsastatus`)

También bajo la autoridad de este documento (la autoridad cubre transacción + participante + ledger):

| Desde | Hacia | Disparador | Evento |
|-------|-------|-----------|--------|
| *(creación del ledger)* | Sellado con TSA | Worker, paso 6 exitoso | — (implícito en evento 7) |
| *(creación del ledger)* | Sin sello TSA | Worker con `env_TsaEnabled` = false | — (nivel de evidencia lo registra) |
| *(creación del ledger)* | Re-sellado pendiente | Worker: TSA inaccesible tras fallbacks | — (detalle en evento 7) |
| Re-sellado pendiente | Sellado con TSA | `capi_ResealPending` obtiene token | Evento 9 |
| Re-sellado pendiente | Sin sello TSA | `capi_ResealPending` con `env_TsaEnabled` = false | Evento propio con detalle (doc 04 §3.1) |

Sin más transiciones: `Sellado con TSA` y `Sin sello TSA` son finales (el toggle no es retroactivo — ADR-005).

## 3. Semántica de enrutamiento (RF-09/RF-10)

| Aspecto | Secuencial | Paralelo |
|---------|-----------|----------|
| Activación inicial (T4) | Solo orden 1 | Todos |
| Tras cada firma | Se activa el siguiente orden | Nada que activar |
| "Último firmante" (decisión post-lock) | Firmó el orden N máximo | Cuenta de no-Firmado llega a 0 |
| Rechazo de uno | Detiene la cadena: los siguientes jamás se activan | Detiene igual: transacción → Rechazado (T11); los que no firmaron quedan como estén |
| Recordatorios (RF-12) | Solo al Turno Activo vigente | A todos los Turno Activo |

**Filtro obligatorio de recordatorios:** `capi_ProcessReminders` selecciona participantes en Turno Activo **cuya transacción está en Pendiente de Firma o Firmado Parcialmente** — jamás sobre estados terminales (los participantes conservan Turno Activo como verdad histórica tras Rechazado/Expirado/Cancelado; sin este filtro, el job recordaría eternamente transacciones muertas).

Decisión explícita: **un solo rechazo mata la transacción completa** en ambos enrutamientos (no hay quórums ni firmas opcionales en esta fase — extensión futura si el negocio lo pide).

## 4. Reglas transversales

- **R1 — Toda transición emite su evento** (doc 03 §4.6) con actor real (`InitiatingUserId` o "Sistema") — RNF-04. La tabla §1.1 es exhaustiva: transición sin evento listado = bug.
- **R2 — Toda transición ocurre bajo el lock de fila** de la transacción (doc 04 §5) y revalida estado tras el lock. Las carreras pierden limpio, jamás corrompen.
- **R3 — Las notificaciones NUNCA las emite el motor**: los flows (doc 08) reaccionan a los cambios de estado/eventos. El motor transiciona; la mensajería observa.
- **R4 — Los guards de estado se validan en el backend** aunque la UI ya oculte la acción (doc 05 §9).
- **R5 — Expiración y recordatorios derivan de datos, no de timers**: `expireson` y `turnactivatedon`/`lastreminderon` consultados por el job diario. No hay timeouts en memoria.
- **R6 — La historia es permanente DESDE EL ENVÍO** (T4). Los borradores son pre-historia: T3 los elimina junto con sus eventos (excepción acotada y deliberada). **Gap aceptado y registrado:** los borradores no expiran ni se limpian automáticamente (`expireson` nace en T4) — política de retención de borradores abandonados pendiente de decisión de negocio (impacta capacidad File, doc 03 §9).
- **R7 — Saneamiento de Sellando zombi (T14)**: el job diario mueve a Error de Sellado toda transacción en Sellando por más de **24 horas** — umbral deliberadamente holgado porque el intervalo entre reintentos de `OperationStatus.Retry` **no está documentado** (verificado): un umbral corto podría pisar un worker legítimamente reintentando. Complemento obligatorio en el worker (doc 04 §7): revalidar el **estado ACTUAL** bajo lock al arrancar (no confiar solo en la post-image) y verificar existencia de ledger ANTES de tocar el archivo final — un reintento zombi que despierta tras T14+T10 debe abortar, jamás subir un segundo archivo (evitaría el escenario prohibido de doc 04 §7). Se implementa dentro de `capi_ExpireTransactions` (contrato ampliado en doc 04 §3.1).

## 5. Trazabilidad

| RF | Cobertura |
|----|-----------|
| RF-08 (matriz de estados) | §1 completa (7 estados de negocio, incluido Cancelado/RF-30 + 2 operativos: Sellando y Error de Sellado — consistente con doc 03 §3) |
| RF-09/10 (enrutamiento) | §3 |
| RF-12 (recordatorios) | §3 + R5 (sin cambio de estado) |
| RF-13 (rechazo) | T11 + P4 + §3 |
| RF-27 (expiración) | T12 + R5 + guard de estados elegibles |
| RF-30 (cancelación) | T13 |
| RNF-04 (trazabilidad) | R1 |

---

*Anterior: [05-frontend-code-app.md](05-frontend-code-app.md) · Siguiente: [07-seguridad-y-cumplimiento.md](07-seguridad-y-cumplimiento.md).*

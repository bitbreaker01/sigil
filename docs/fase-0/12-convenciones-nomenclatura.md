# Sigil — Convenciones de Nomenclatura

**Documento:** 12 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — convenciones definidas por el equipo, tipo por tipo)
**Última actualización:** 2026-07-10

Referencia canónica de nombres para TODOS los componentes de Sigil. Ningún componente se crea con un nombre que no siga estas reglas. Los documentos 03 y 04 ya aplican estas convenciones.

---

## 1. Estructura general

**Schema names (componentes de Dataverse):**

```
sanic_  +  sigil_  +  [marcador de tipo]  +  nombre
  │         │              │                  │
  │         │              │                  └─ inglés técnico
  │         │              └─ tbl_ / choice_ / capi_ / env_  (columnas: sin marcador)
  │         └─ namespace del proyecto: distingue lo de Sigil dentro del publisher
  └─ prefijo de personalización del publisher (lo impone Dataverse)
```

**Display names (roles, perfiles, flows, solución):**

```
Sigil | <TIPO> | <Nombre>        — siempre en inglés
```

**Racional:** el publisher `sanic_` es de la organización y lo compartirán otros proyectos; el segmento `sigil_` permite distinguir de un vistazo qué componentes pertenecen a Sigil dentro del mismo publisher. El marcador de tipo permite saber qué ES un componente con solo leer su nombre.

## 2. Publisher y solución

| Elemento | Valor |
|----------|-------|
| Publisher (display) | **Sistemas Abiertos Nicaragua** |
| Publisher (unique name) | `Sistemas_Abiertos_Nicaragua` (autogenerado por el portal — aceptado como convención el 2026-07-14, publisher ya creado en Dev; la identidad operativa es el prefix) |
| Publisher (prefix) | `sanic` → prefijo `sanic_` de todos los schema names |
| Option Value Prefix | **15946** (registrado del portal el 2026-07-14 — semilla de los valores del Apéndice A) |
| Solución (display) | **Sigil \| Core \| Sigil** |
| Solución (logical) | `sigil_core_sigil` |

**Patrón del nombre de solución:** `<abreviatura> | <módulo> | <nombre completo>`. "Core" porque el proyecto puede separarse en varias soluciones a futuro (ej. `Sigil | Notifications | Sigil`). Como "Sigil" es corto, funciona de abreviatura y de nombre completo a la vez.

## 3. Componentes de schema

| Tipo | Patrón | Ejemplos |
|------|--------|----------|
| **Tablas** | `sanic_sigil_tbl_` + inglés singular minúsculas | `sanic_sigil_tbl_transaction`, `sanic_sigil_tbl_participant`, `sanic_sigil_tbl_signaturezone`, `sanic_sigil_tbl_ledgerentry`, `sanic_sigil_tbl_mastersignature`, `sanic_sigil_tbl_event` |
| **Columnas** | `sanic_sigil_` + minúsculas concatenadas, **sin marcador** | `sanic_sigil_contenthash`, `sanic_sigil_turnactivatedon`, `sanic_sigil_signaturesnapshot` |
| **Choices globales** | `sanic_sigil_choice_` + entidad/concepto + atributo | `sanic_sigil_choice_transactionstatus`, `sanic_sigil_choice_tsastatus` |
| **Custom APIs** | `sanic_sigil_capi_` + Verbo en PascalCase | `sanic_sigil_capi_CreateTransaction`, `sanic_sigil_capi_VerifyDocument` |
| **Variables de entorno** | `sanic_sigil_env_` + PascalCase | `sanic_sigil_env_TsaEnabled`, `sanic_sigil_env_AppPlayUrl` |

Notas:
- El designer de Dataverse impone `sanic_` automáticamente (viene del publisher); lo que se escribe a mano es `sigil_tbl_transaction` etc.
- Los **valores** de los choices usan el Option Value Prefix del publisher — los documentos referencian estados por **nombre lógico**, jamás por número.
- Los privilegios derivan del schema de la tabla: `prvReadsanic_sigil_tbl_transaction`, `prvWritesanic_sigil_tbl_ledgerentry` (relevante para Execute Privileges de las Custom APIs — doc 04 §3.2).
- Relaciones: nombre autogenerado por Dataverse a partir de las tablas — no se personaliza salvo colisión.

## 4. Display names (siempre en inglés)

| Tipo | Patrón | Valores definidos |
|------|--------|-------------------|
| **Roles de seguridad** | `Sigil \| SR \| <Nombre>` | `Sigil \| SR \| User` · `Sigil \| SR \| Service` · `Sigil \| SR \| Auditor` |
| **Perfil de column security** | `Sigil \| FLS \| <Nombre>` | `Sigil \| FLS \| Evidence Writer` |
| **Flows de Power Automate** | `Sigil \| Cloud Flow \| <Dominio> - <Acción>` | Definidos en doc 08: `Sigil \| Cloud Flow \| Notifications - Participant turn` · `Sigil \| Cloud Flow \| Notifications - Transaction state` · `Sigil \| Cloud Flow \| Jobs - Daily` |

## 5. Código

| Elemento | Nombre |
|----------|--------|
| Proyectos/namespaces backend | `Sigil.Plugins.Core` (núcleo puro, netstandard2.0) · `Sigil.Plugins` (assembly registrado, net462) · tests: `Sigil.Plugins.Core.Tests`, `Sigil.Plugins.Tests` (doc 04 §2, actualizado 2026-07-13) |
| Suite de conformidad | `Sigil.Conformance.Tests` bajo `tests/conformance/` (doc 11 §1 regla 5); IDs de tests: `CF-<runbook><nn>` (ej. `CF-A06`) |
| Package npm frontend | `sigil-app` |
| Carpetas del repo | `src/backend/`, `src/frontend/`, `tests/conformance/`, `docs/`, `spikes/`, `solutions/snapshots/` |

El código NO lleva el prefijo `sanic` — ese pertenece al schema de Dataverse. Las constantes de C# que referencian schema names (tabla `Domain/`) usan los nombres completos `sanic_sigil_*`.

## 6. Otros artefactos

| Elemento | Convención |
|----------|-----------|
| Autonumber del ledger | `SIGIL-{DATETIMEUTC:yyyy}-{SEQNUM:6}` (formato de registro, no schema — doc 03 §4.4) |
| Query params de deep links | camelCase: `?screen=verify&txId=<guid>` |
| Documentos de Fase 0 | `NN-nombre-con-guiones.md` bajo `docs/fase-0/` |

## Apéndice A — Valores canónicos de choices

**Option Value Prefix del publisher `sanic`: `15946`** *(registrado del portal el 2026-07-14 — COTEJADO: CF-A02 imprimió 15946 el 2026-07-15)*

Los valores reales de cada opción se **copian del portal al crearse** (Runbook A §A7) — jamás se predicen: los flows comparan por número (doc 08 §2) y un valor adivinado mal es un bug silencioso. Tabla a completar durante A7 (una fila por opción, los 5 choices — ~30 filas):

| Choice | Etiqueta lógica | Valor real (copiado del portal) |
|--------|-----------------|--------------------------------|
| sanic_sigil_choice_transactionstatus | Borrador | 159460000 |
| sanic_sigil_choice_transactionstatus | Pendiente de Firma | 159460001 |
| sanic_sigil_choice_transactionstatus | Firmado Parcialmente | 159460002 |
| sanic_sigil_choice_transactionstatus | Sellando | 159460003 |
| sanic_sigil_choice_transactionstatus | Completado | 159460004 |
| sanic_sigil_choice_transactionstatus | Rechazado | 159460005 |
| sanic_sigil_choice_transactionstatus | Expirado | 159460006 |
| sanic_sigil_choice_transactionstatus | Error de Sellado | 159460007 |
| sanic_sigil_choice_transactionstatus | Cancelado | 159460008 |
| sanic_sigil_choice_participantstatus | Pendiente | 159460000 |
| sanic_sigil_choice_participantstatus | Turno Activo | 159460001 |
| sanic_sigil_choice_participantstatus | Firmado | 159460002 |
| sanic_sigil_choice_participantstatus | Rechazado | 159460003 |
| sanic_sigil_choice_routingtype | Secuencial | 159460000 |
| sanic_sigil_choice_routingtype | Paralelo | 159460001 |
| sanic_sigil_choice_tsastatus | Sellado con TSA | 159460000 |
| sanic_sigil_choice_tsastatus | Sin sello TSA | 159460001 |
| sanic_sigil_choice_tsastatus | Re-sellado pendiente | 159460002 |
| sanic_sigil_choice_eventtype | Transacción creada | 159460000 |
| sanic_sigil_choice_eventtype | Enviada a firma | 159460001 |
| sanic_sigil_choice_eventtype | Firma registrada | 159460002 |
| sanic_sigil_choice_eventtype | Rechazada | 159460003 |
| sanic_sigil_choice_eventtype | Recordatorio programado | 159460004 |
| sanic_sigil_choice_eventtype | Sellado iniciado | 159460005 |
| sanic_sigil_choice_eventtype | Sellado completado | 159460006 |
| sanic_sigil_choice_eventtype | Error de sellado | 159460007 |
| sanic_sigil_choice_eventtype | Re-sellado TSA obtenido | 159460008 |
| sanic_sigil_choice_eventtype | Expirada | 159460009 |
| sanic_sigil_choice_eventtype | Verificación realizada | 159460010 |
| sanic_sigil_choice_eventtype | Cancelada por el creador | 159460011 |

---

*Aprobado tipo por tipo por el equipo el 2026-07-10 (sesión de convenciones). Cualquier tipo de componente nuevo (ej. custom connectors, PCF) define su marcador ANTES de crear el primero, y se agrega acá.*

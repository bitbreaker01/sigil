# Convenciones de Nomenclatura

Referencia canónica de nombres para TODOS los componentes de Sigil. Ningún componente se crea con un
nombre que no siga estas reglas.

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
| Publisher (unique name) | `Sistemas_Abiertos_Nicaragua` (autogenerado por el portal; la identidad operativa es el prefix) |
| Publisher (prefix) | `sanic` → prefijo `sanic_` de todos los schema names |
| Option Value Prefix | **15946** (semilla de los valores de los choices; ver [Catálogo de Choices](catalogo-de-choices.md)) |
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
- Los **valores** de los choices usan el Option Value Prefix del publisher — la documentación referencia estados por **nombre lógico**, jamás por número.
- Los privilegios derivan del schema de la tabla: `prvReadsanic_sigil_tbl_transaction`, `prvWritesanic_sigil_tbl_ledgerentry` (relevante para los Execute Privileges de las Custom APIs).
- Relaciones: nombre autogenerado por Dataverse a partir de las tablas — no se personaliza salvo colisión.

## 4. Display names (siempre en inglés)

| Tipo | Patrón | Valores definidos |
|------|--------|-------------------|
| **Roles de seguridad** | `Sigil \| SR \| <Nombre>` | `Sigil \| SR \| User` · `Sigil \| SR \| Service` · `Sigil \| SR \| Auditor` |
| **Perfil de column security** | `Sigil \| FLS \| <Nombre>` | `Sigil \| FLS \| Evidence Writer` |
| **Flows de Power Automate** | `Sigil \| Cloud Flow \| <Dominio> - <Acción>` | `Sigil \| Cloud Flow \| Notifications - Participant turn` · `Sigil \| Cloud Flow \| Notifications - Transaction state` · `Sigil \| Cloud Flow \| Jobs - Daily` |

## 5. Código

| Elemento | Nombre |
|----------|--------|
| Proyectos/namespaces backend | `Sigil.Plugins.Core` (núcleo puro, netstandard2.0) · `Sigil.Plugins` (assembly registrado, net462) · tests: `Sigil.Plugins.Core.Tests`, `Sigil.Plugins.Tests` |
| Suite de conformidad | `Sigil.Conformance.Tests` bajo `tests/conformance/`; IDs de tests de conformidad: `CF-<área><nn>` (ej. `CF-A06`, `CF-D09`) |
| Package npm frontend | `sigil-app` |
| Carpetas del repo | `src/backend/`, `src/frontend/`, `tools/`, `tests/`, `docs/`, `solutions/` |

El código NO lleva el prefijo `sanic` — ese pertenece al schema de Dataverse. Las constantes de C# que referencian schema names (`Domain/SchemaNames.cs`) usan los nombres completos `sanic_sigil_*`.

## 6. Otros artefactos

| Elemento | Convención |
|----------|-----------|
| Autonumber del ledger | `SIGIL-{DATETIMEUTC:yyyy}-{SEQNUM:6}` (formato de registro, no schema) |
| Query params de deep links | camelCase: `?screen=verify&txId=<guid>` |
| Documentos de referencia/desarrollo | `nombre-con-guiones.md` bajo `docs/referencia/` y `docs/desarrollo/` |

## Valores de choices

Los valores canónicos de los choices (option sets) viven en su propio documento — la **fuente única de verdad**, verificada por tests: **[Catálogo de Choices](catalogo-de-choices.md)**.

---

*Convenciones aprobadas tipo por tipo por el equipo. Cualquier tipo de componente nuevo (ej. custom connectors, PCF) define su marcador ANTES de crear el primero, y se agrega acá.*

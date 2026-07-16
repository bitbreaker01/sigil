# Sigil — Fase 1: Checklist Vivo (Fundaciones)

**Estado:** En ejecución (arrancada 2026-07-13)
**Plan:** [../fase-0/10-hoja-de-ruta.md](../fase-0/10-hoja-de-ruta.md) §2 F1 · **Runbooks operativos:** [Runbook A — aprovisionamiento](../runbooks/runbook-a-aprovisionamiento-ambiente.md) (paso a paso con clicks, comandos y verificación CF-*) · [Runbook B — gates post-import](../runbooks/runbook-b-gates-post-import.md). El doc 09 conserva el qué/por qué; los runbooks son la autoridad del cómo (extraídos 2026-07-14).

## Reglas de trabajo de la fase (permanentes — doc 11 §1, reglas 5 y 6)

1. **Prueba de existencia para TODO:** cada componente — creado a mano en Power Platform o por código — tiene su(s) test(s). Los pasos manuales de este checklist referencian el **ID del test de conformidad** que los prueba (`tests/conformance/`): el paso NO está terminado hasta que su test pasa de rojo a verde.
2. **El antagonista, siempre:** todo artefacto (código, config, docs) pasa revisión adversarial antes de darse por terminado.
3. **Strict TDD** en todo el código: red → green → refactor.

## Cómo correr la suite de conformidad

```bash
# Local (con el ambiente Dev ya creado):
export SIGIL_DATAVERSE_URL="https://<org>.crm.dynamics.com"
export SIGIL_CLIENT_ID="..." SIGIL_CLIENT_SECRET="..."
# (el tenant se descubre desde la URL — TenantId no es parámetro del connection string, verificado)
dotnet test tests/conformance/Sigil.Conformance.Tests

# Sin variables: los tests se OMITEN con motivo. Con URL pero credencial incompleta/mala: FALLAN ruidoso.
# CI: el job "conformance-harness" prueba en cada PR que la suite existe y compila;
#     el job "conformance" corre a demanda contra Dev (secrets del environment 'dev').
```

## Checklist de F1

| # | Paso | Quién | Prueba que lo garantiza | Estado |
|---|------|-------|--------------------------|--------|
| 1 | Repo Git + estructura + proyectos + CI (versiones pineadas, `global.json`, warnings-as-errors) | Claude | Canarios de harness (`HarnessTests`) verdes local y en CI; build net462 + net8 en verde | ✅ 2026-07-13 — antagonista aplicado (1 crítico + 8 warnings corregidos) |
| 2 | Suite de conformidad operativa (CF-A01..A09) con **gate de PR propio** (`conformance-harness`: compila y se auto-omite en cada PR — la prueba de existencia de la propia suite) | Claude | El job `conformance-harness` en cada PR + corrida local verificada (23 casos omitidos con motivo) | ✅ 2026-07-13 — antagonista aplicado |
| 3 | Ambiente **Dev** creado (sandbox, Dataverse, idiomas es/en) | **Usuario** (Runbook A §7.1) | Conexión de la suite establecida (el fixture FALLA si la conexión configurada no conecta) | ✅ 2026-07-15 — la suite conecta |
| 4 | **Publisher** "Sistemas Abiertos Nicaragua" (`sanic`) + Option Value Prefix | **Usuario** (§7.2) | **CF-A01** (nombre y prefijo exactos) + **CF-A02** (no es el Default Publisher; loguea el prefix). **La garantía completa del paso es la tabla canónica de choices** registrada en el apéndice del doc 12 con ese prefix — el test solo acompaña (límite declarado en el propio test) | ✅ 2026-07-15 — CF-A01/A02 verdes; prefix 15946 cotejado |
| 5 | **Solución** `sigil_core_sigil` ("Sigil \| Core \| Sigil") bajo el publisher sanic | **Usuario** (§7.3) | **CF-A03** | ✅ 2026-07-15 — CF-A03 verde |
| 6 | **App registration + Service Principal** (application user + rol Service + perfil FLS + secreto para el runner de conformidad) | **Usuario** (Runbook A §A4) | La suite conecta CON esa identidad + **CF-A07/CF-A08** (rol y perfil existen) + **CF-A10** (membresía del perfil — se escribe antes de ejecutar A4c, que ocurre tras el paso 7) | ✅ 2026-07-15 — CF-A07/A08/A10 verdes (membresía FLS confirmada vía systemuserprofiles) |
| 7 | **Modelo de datos** completo en la solución (6 tablas, columnas, choices, alternate keys — doc 03) | Usuario + Claude (guiado) | **CF-A04** (6 tablas), **CF-A05** (ledger org-owned), **CF-A06** (alternate key ACTIVO) + tests de columnas/choices que se agregan AL construir cada tabla (regla: tabla sin test de columnas = tabla no terminada) | ✅ 2026-07-15 — CF-A04/A05/A06/A16/A17/A18 verdes (51 columnas con binding; 30 valores de choices cotejados; hubo 3 remediaciones cazadas por la suite: ownership del ledger, contenthash Memo, choice eventtype mal nombrado) |
| 8 | **Variables de entorno** (9 definiciones — doc 03 §8) | **Usuario** (§7) | **CF-A09** | ✅ 2026-07-15 — CF-A09 verde (9/9) |
| 9 | Cuenta de servicio + conexiones + CSP + auditoría + DLP | **Usuario** (§7.5–7.8) | Tests CF-A10+ a escribir ANTES de ejecutar esos pasos (regla 1) | 🟡 Casi completo 2026-07-15 — auditoría ✅ (CF-A13), grupo+team+rol ✅ (CF-A14), **A5 cuenta de servicio+conexiones+licencia ✅ (verificación manual — sin test posible: las conexiones viven fuera de Dataverse; CF-A11 las cubre indirectamente en F4)**, **A6 CSP ✅ (verificación definitiva: gate 5 con la app real en F3)**; pendientes: A9 DLP, A11 backups |
| 10 | **Spike residual en sandbox real** (stack completo + TSA desde el sandbox) | Claude + Usuario | Plugin de spike con resultado observable + registro en `spikes/` | ✅ 2026-07-15 — 3 veredictos en verde (`spikes/spike-sandbox/RESULTADOS-SANDBOX.md`): stack ✔, TSA Sectigo ✔ (fallback ADR-005 vindicado), alfa ✔ vía **XObject manual** — finding: importer PNG de PDFsharp roto bajo net462 sandbox (doc 04 §10); antagonista aplicado (1 crítico + 4 advertencias corregidas) |
| 11 | Datos semilla en Dev + exclusión CA cuentas de prueba | **Usuario** | Test de conformidad de datos semilla (a escribir) | 🟡 Parcial 2026-07-15 — 3 usuarios creados y con rol vía grupo; falta: export SIGIL_SEED_UPNS (→ CF-A15 verde), exclusión CA, firmas maestras (requiere APIs de F2) |
| 12 | Decisión FakeXrmEasy comercial vs stub (doc 11 §2) | **Usuario** decide, Claude implementa | El harness de `Sigil.Plugins.Tests` con su primer test real en verde | ✅ 2026-07-15 — DECIDIDO: **stub propio** (FakeXrmEasy descartado por licenciamiento); el stub se construye TDD con las primeras APIs (paso 13) |
| 13 | Primeras Custom APIs (`CreateTransaction`, `UpdateDraft`, `DeleteDraft`, `GetDocumentContent`) — TDD | Claude | Suites M1/M7 (doc 11 §4) + test de conformidad de registro de las APIs en el ambiente | ✅ 2026-07-15/16 — **núcleo puro** (Domain/) con **88 tests verdes** (M1/M7); **cáscara net462** (4 plugins + stub artesanal + seam IFileTransfer); **DESPLEGADO en Dev** por SDK (`tools/Sigil.Deploy`, idempotente); **CF-D01..D05 verdes** (registro + smoke E2E). Antagonista aplicado en 3 rondas. Runbook D escrito y revisado |

**Salida de F1** (doc 10): spike de sandbox en verde + smoke por API (crear borrador con PDF real y volver a leerlo) + todos los pasos de arriba con su prueba en verde.

> **✅ SALIDA DE F1 LOGRADA (2026-07-16):** spike de sandbox verde (paso 10); **smoke E2E verde** (`CF-D05`: crear borrador con PDF real → `GetDocumentContent` round-trip byte a byte → borrar); backend desplegado en Dev (Runbook D); **88 Core + 105 conformidad verdes**. Hallazgos de F2 cerrados: cache de assembly por versión (bump obligatorio), `uniquename` de request param = clave de `InputParameters`, `version` de pluginpackage no editable por SDK (solo content), Integer opcional de Custom API llega como 0 (`InputOptionalInt`), env vars necesitan VALOR además de definición.

## F2 en curso — Backend del ciclo de vida

| # | Paso | Quién | Prueba que lo garantiza | Estado |
|---|------|-------|--------------------------|--------|
| F2.1 | APIs del ciclo de vida: `SendTransaction` (T4), `SubmitSignature` (T5/T6/T7), `RejectTransaction` (T11), `CancelTransaction` (T13) — TDD | Claude | Core 119 verdes (M1/M2/M3/M9 + reglas de enrutamiento doc 06 §3); tests de cáscara (lock-primero, idempotencia M3, no-reescritura de status doc 08 §7, sharing M13); **CF-D01..D06 verdes contra Dev** (113/113 conformidad), package v1.0.3 | ✅ 2026-07-16 — antagonista aplicado (1 crítico real: ColumnSet sin `expirationdays` — el stub ahora HONRA ColumnSet; 6 advertencias corregidas) |
| F2.2 | Firma Maestra: `ValidateMasterSignature` + `GetMasterSignature` + motor de Imaging (M8) | Claude | Suites M8 + CF-D ampliado; con esto el smoke podrá firmar de verdad (SubmitSignature E2E) | ✅ 2026-07-16 — motor M8 (134 Core verdes, con casos CERCA del umbral); **CF-D07** (validar/versionar/leer) y **CF-D08** (E2E de FIRMA: crear→enviar→firmar→Sellando con `documenthash==contenthash` verificado contra Dev) verdes; package v1.0.6. Antagonista: 1 crítico (bomba de descompresión PNG → techo 4096² sobre header) + 5 advertencias corregidas; **enmienda a ADR-009** (métrica de contraste = apartamiento de la tinta, no RMS global); fix de plataforma: blockid de file blocks debe ser base64 sin `+`/`/` (la plataforma no url-encodea) |
| F2.3 | Pipeline de sellado (worker asíncrono, doc 04 §7) + motor Pdf/Crypto (M4/M5/M6/M9) + `RetrySealing` | Claude | Suites M4/M5/M6/M9 + step asíncrono registrado (CF-D09) + **CF-D10 E2E TOTAL** | ✅ 2026-07-16 — **155 Core verdes** (coordenadas 4 rotaciones+CropBox, composición con XObject manual+hoja+QR+overflow, TSA con doble validación); worker con guards+idempotencia doc 04 §7; **CF-D10 VERDE: firmar→worker→Completado→ledger con TSA REAL de Sectigo→finalhash==SHA-256 del final descargado**. Package v1.0.8. Antagonista: 4 críticos corregidos (clasificación de faults por ErrorCode, ancla post-ledger, sonda del final durable, catch de duplicado); enmiendas doc 04 §6.2/§7 (sin nº de ledger en la hoja; Info dictionary) y doc 06 T9 (retries agotados = T14). **Nota:** `VerifyDocument` pasa a F2.4 con los jobs |
| F2.4 | `VerifyDocument` + Jobs (`ExpireTransactions` T12+T14, `ProcessReminders`, `ResealPending`) — M10 | Claude | Suite M10 + CF-D02 con privilegio de SERVICIO por job + smokes CF-D11/D12/D13 | ✅ 2026-07-16 — **177 Core verdes** (M10 + verificación cruzada del historial); **CF-D11 VERDE: sellar real→verificar hash correcto VERDE→hash alterado ROJO→historial íntegro→evento 11**; CF-D12 (expiración real) y CF-D13 (recordatorio autosuficiente + no-duplicación) verdes. Package v1.0.9, **15 Custom APIs** + worker. 134/134 conformidad. Revisión adversarial como self-review (agente antagonista caído por límite de sesión/529 — re-corrida completa pendiente como deuda). **Decisión de negocio pendiente:** valor de choice para "TSA abandonada" (evento de ResealPending con TSA off — runbook D) |
| F2.5 | Script de carrera de locks contra Dev (doc 11 §3) | Claude + Usuario | N `SubmitSignature` concurrentes → exactamente un Sellando | ⬜ |

## Decisiones tomadas durante F1

| Fecha | Decisión | Registro |
|-------|----------|----------|
| 2026-07-13 | Núcleo puro separado en `Sigil.Plugins.Core` (netstandard2.0) para testear en cualquier plataforma | doc 04 §2 actualizado |
| 2026-07-13 | Suite de conformidad como TDD de infraestructura (skip-si-no-hay-ambiente, jamás verde fingido) | doc 11 §1 regla 5 |

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
| 13 | Primeras Custom APIs (`CreateTransaction`, `UpdateDraft`, `DeleteDraft`, `GetDocumentContent`) — TDD | Claude | Suites M1/M7 (doc 11 §4) + test de conformidad de registro de las APIs en el ambiente | ⬜ |

**Salida de F1** (doc 10): spike de sandbox en verde + smoke por API (crear borrador con PDF real y volver a leerlo) + todos los pasos de arriba con su prueba en verde.

## Decisiones tomadas durante F1

| Fecha | Decisión | Registro |
|-------|----------|----------|
| 2026-07-13 | Núcleo puro separado en `Sigil.Plugins.Core` (netstandard2.0) para testear en cualquier plataforma | doc 04 §2 actualizado |
| 2026-07-13 | Suite de conformidad como TDD de infraestructura (skip-si-no-hay-ambiente, jamás verde fingido) | doc 11 §1 regla 5 |

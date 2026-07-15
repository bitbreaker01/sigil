# Sigil — ALM, Entornos y Despliegue

**Documento:** 09 — Fase 0
**Estado:** **Aprobado** (2026-07-12 — visto bueno del equipo)
**Última actualización:** 2026-07-12
**Depende de:** [12-convenciones-nomenclatura.md](12-convenciones-nomenclatura.md), docs 03/04/05/07/08 (consolida TODOS sus requisitos de ambiente y despliegue)

Claims de plataforma verificados contra Microsoft Learn (fuentes en §13).

---

## 1. Ambientes

| Ambiente | Tipo | Soluciones | Quién toca qué |
|----------|------|-----------|----------------|
| **Dev** | Sandbox | `sigil_core_sigil` **unmanaged** | Developers (pac/npm CLI, maker portal). Único ambiente editable |
| **Test** | Sandbox | Managed | UAT + matriz doc 05 §8 + gates §8. Solo el pipeline escribe |
| **Prod** | Production | Managed | Solo el pipeline escribe; sin roles maker/customizer (doc 07 §6) |

Reglas: jamás unmanaged en Test/Prod; jamás edición directa fuera de Dev. **Refresh de Test (copy Prod→Test): NO se practica** — un copy pisa application users, conexiones y env vars; si alguna vez se necesita, exige re-ejecutar el Runbook A completo (decisión explícita, no default).

**Multi-developer en un Dev compartido (riesgo declarado):** Git protege el código; tablas/flows/roles se editan en vivo. Reglas mínimas: (a) dueño por tipo de componente (quién toca el modelo, quién los flows), (b) **freeze de edición** durante el export del snapshot de release, (c) los pushes de app/plugin se anuncian en el canal del equipo. Ambientes personales de developer: no en Fase 1 (costo/beneficio), re-evaluable.

## 2. Estrategia de soluciones

**Decisión: UNA solución (`sigil_core_sigil`) para la Fase 1** — tablas, choices, roles, perfil FLS, Custom APIs, plugin package, 3 flows, connection references, env vars y la code app.

**Desviación declarada del patrón de segmentación** (Core/Plugins/Flows/Config separados): con un equipo, un producto y una unidad de despliegue, segmentar multiplica pipelines y dependencias sin beneficio hoy. **Criterio de segmentación futuro:** segundo módulo con cadencia propia, o config desplegándose más seguido que el código → se extrae `Sigil | Config | Sigil` primero (la separación de mayor valor del patrón).

## 3. Fuente de verdad y control de versiones

| Artefacto | Fuente de verdad | Nota |
|-----------|------------------|------|
| Código backend/frontend/docs | **Git** | Lo desplegado se construye SIEMPRE desde Git |
| Componentes solo-Dataverse | **Ambiente Dev** + export de solución como **snapshot** versionado en Git (`solutions/snapshots/`) | **Desviación verificada** del patrón "source control de soluciones desempaquetadas": las code apps **no soportan solution packager ni git integration** — el ciclo unpack→pack no es completo para nuestra solución. El zip es snapshot auditable, NO fuente editable |

Regla del patrón que SÍ se respeta a rajatabla: cero config hardcodeada — env vars y connection references para todo.

## 4. Flujo de desarrollo (hacia Dev)

1. Backend: rama → PR → build + tests (doc 11) → `pac plugin push` a Dev. **Paso una-única-vez (verificado que `pac plugin push` NO acepta solución destino):** tras el primer registro, **asociar el plugin package a `sigil_core_sigil`** (`pac solution add-solution-component` o registro inicial hecho dentro de la solución) — sin esto, el backend NO viaja en el pipeline.
2. Frontend: rama → PR → build + tests → **`pac code push --solutionName sigil_core_sigil`** — targeting **explícito y determinista**. (La "preferred solution" existe pero es configuración **por maker**, no del ambiente — verificado; no dependemos del estado personal de cada developer.)
3. Tablas/roles/flows/env vars: en el maker portal de Dev, **dentro de la solución** (nunca Default).
4. Regenerar clientes tipados tras cada cambio de contrato (doc 05 §10).

## 5. Despliegue Dev → Test → Prod

**Mecanismo: Power Platform Pipelines** (verificado: despliegan code apps en soluciones con preflight; preguntan conexiones y valores de env vars sin valor durante el despliegue; convierten a managed). **Con delegated deployments (Service Principal)** para que el despliegue no corra con identidad personal — refuerza "solo el pipeline escribe" y evita ownership personal de componentes en el destino.

**Aprobaciones (control humano sobre el control técnico):** Dev→Test la aprueba el **lead técnico**; Test→Prod la aprueban **lead técnico + dueño de producto**. Registradas en el pipeline (soporta approvals — verificado).

**Secuencia de promoción:**
1. CI verde + Dev probado. Freeze de edición en Dev.
2. **Higiene de env vars ANTES de exportar (verificado y CRÍTICO):** los *current values* presentes en la solución **viajan en el zip y aterrizan en el destino en silencio** — el despliegue solo pregunta por vars SIN valor. Regla: **remover todos los current values de la solución** (*Remove from this solution*) — los valores viven en cada ambiente, jamás en el artefacto. Sin esta higiene, `env_TsaEndpoints` = FreeTSA de Dev llega a Prod y el gate ingenuo no lo ve.
3. Incrementar versión (§10), exportar snapshot a Git.
4. Ejecutar pipeline Dev→Test: proveer conexiones (deben existir de antemano — Runbook A) y valores de env vars del ambiente.
5. **Gates post-import (§8) — nada se prueba antes de pasarlos.**
6. UAT + matriz de dispositivos. 7. Pipeline Test→Prod + gates + smoke.

**Rollback de Prod (decidido ANTES del primer despliegue, no durante el incidente):**
- **Default: fix-forward.** El downgrade de una solución managed no está soportado, y el **restore de ambiente retrocede el ledger** — pérdida probatoria directa (doc 07 A15/A16). Un despliegue malo se corrige con hotfix hacia adelante (§9).
- **Restore de ambiente: última instancia**, solo con aprobación del dueño de producto + registro escrito de la pérdida probatoria aceptada (qué transacciones/evidencia quedan fuera de la ventana). Los tokens TSA en poder de verificadores externos sobreviven y delatan la discrepancia — exactamente A15.

**Hotfix (§9 lo versiona; este es el camino):** los stages se recorren en orden SIEMPRE (verificado — no se puede saltar Test). Hotfix = mismo pipeline con **gate set reducido definido**: gates 1–4 + smoke E2E (gate 9); la matriz completa de dispositivos se omite salvo que el fix toque frontend. Aprobación: las mismas dos firmas de Test→Prod.

## 6. Configuración por ambiente

**Los valores NUNCA viajan en la solución (§5 paso 2). Tabla de valores esperados por ambiente — el gate 4 verifica contra ESTA tabla, no "que haya algo":**

| Variable | Dev | Test | Prod |
|----------|-----|------|------|
| `env_TsaEnabled` | **false** en el día a día; **true** en sesiones de prueba de integración TSA (dos modos declarados) | true | **true** (doc 07 §6) |
| `env_TsaEndpoints` | FreeTSA (solo dev — ADR-005) | DigiCert + Sectigo | DigiCert + Sectigo |
| `env_AppPlayUrl` | URL tras el **primer `pac code push`** en Dev | URL tras el **primer despliegue del pipeline** a Test (en Test/Prod NO hay push — la app viaja en la solución; el appId del destino se lee después del primer deployment y se setea esta variable como paso post-import) | ídem Prod |
| `env_MaxPdfSizeKB` / `env_MaxParticipants` / specs de imagen y posición | Valores de prueba | = Prod | Valores de negocio |
| `env_ExpirationDefaultDays` / `env_ReminderCadenceDays` / `env_DefaultLanguage` | Cortos (probar expiración/recordatorios rápido) | = Prod | Valores de negocio |

Cambio en Prod = cambio controlado con registro (doc 07 §4).

## 7. Runbook A — Aprovisionamiento de ambiente (resumen — el CÓMO vive en el runbook)

**Autoridad operativa: [docs/runbooks/runbook-a-aprovisionamiento-ambiente.md](../runbooks/runbook-a-aprovisionamiento-ambiente.md)** (extraído de esta sección el 2026-07-14: paso a paso con rutas de portal, comandos pac/Web API, valores exactos y la verificación CF-* de cada paso).

Resumen de alcance y orden (el detalle y el racional de cada punto viven en el runbook):
- **Solo Dev:** ambiente (base language **English — decisión irreversible** + pack de español) → publisher `sanic` + **apéndice canónico de choices en el doc 12** → solución `sigil_core_sigil` → modelo de datos completo (doc 03), construido test-first.
- **Todos los ambientes:** app registration + application user + rol Service + **membresía del perfil FLS (no viaja — se repite por ambiente)**; cuenta de servicio + ritual de conexiones; CSP de code apps (reporting → enforced); auditoría org-level + retención; DLP; usuarios por grupo de Entra; backups (política del doc 07 §6); datos semilla (Dev/Test).
- **Orden crítico en Test/Prod:** identidades y conexiones **ANTES del primer run del pipeline** (el despliegue las pide); publisher/solución/modelo **JAMÁS a mano** (viajan managed).

## 8. Runbook B — Gates post-import (resumen — el CÓMO vive en el runbook)

**Autoridad operativa: [docs/runbooks/runbook-b-gates-post-import.md](../runbooks/runbook-b-gates-post-import.md)** (extraído de esta sección el 2026-07-14: cada gate con su procedimiento exacto, criterio de éxito y qué hacer si falla).

Los 10 gates (después de CADA despliegue, antes de habilitar tráfico; hotfix = gates 1–4 + 9):
1. Alternate keys ACTIVOS (CF-A06; ante Failed → ReactivateEntityKey) · 2. Plugin steps presentes y activos · 3. Connection references + flows encendidos + ownership reconciliado · 4. Env vars con los **valores correctos del ambiente** (tabla §6 — jamás "que haya valor") · 5. CSP con la app real (escaneados/CJK) · 6. Deep link completo desde Teams en dispositivo sin sesión · 7. Descargas desde el iframe (desktop + Safari iOS) · 8. TSA desde el sandbox · 9. **Smoke E2E** (protocolo SMOKE-{fecha} entre operadores, Verde/Rojo) · 10. Heartbeat del job diario.

Con cualquier gate en rojo el ambiente NO se declara utilizable (fix-forward — §5). El resultado de los gates se archiva en el registro del despliegue.

## 9. Versionado

- **Solución:** `major.minor.patch.0` — minor por promoción planificada, patch por hotfix (camino §5).
- **Assembly:** alineado al minor; el trace del worker lo registra.
- **Frontend:** versión en el footer (doc 05 §10); tag de Git `sigil/vX.Y.Z` = snapshot de solución + código en el mismo punto.

## 10. Gobernanza y calendario operativo (consolida docs 03/04/07/08)

| Rutina | Cadencia | Dueño |
|--------|----------|-------|
| Rotación del certificado del SP (alerta 30 días antes) | Anual | Operador designado |
| Rotación del client secret del runner de conformidad (Runbook A §A4) | Cada 180 días | Operador designado |
| Re-consent de conexiones Outlook/Teams | Al expirar/revocar (ritual §7.5) | Operador designado |
| **Capacidad File/Log** (doc 03 §9) | Mensual (PPAC) | Operador designado |
| **Advisories de dependencias** (ImageSharp 2.1.x — doc 04; pdf.js; npm CLI en preview — doc 05) | En cada release + revisión mensual | Lead técnico |
| Revisión de runs fallidos de flows | Semanal (doc 08 §7) | Operador designado |
| Verificación de restore point de Prod | Semanal | Operador designado |
| Custodia del `.snk` | — (no rota; fuera de Git) | Lead técnico |

## 11. Riesgos y NO VERIFICADOS

| Ítem | Estado | Mitigación |
|------|--------|-----------|
| Pipelines con el mix exacto code app + plugin package + flows en UNA solución | NO VERIFICADO como combinación (cada pieza sí por separado) | Gates §8; plan B: `pac solution export/import --activate-plugins` guiado por runbook |
| Code apps por pipeline: ¿appId nuevo en el destino? | Consistente con canvas apps; para code apps NO documentado | El paso post-import de `env_AppPlayUrl` (gate 4) lo absorbe en ambos casos |
| `power-apps push` (npm) y solución destino | El targeting documentado es `pac code push --solutionName` — adoptado §4 | — |

## 12. Trazabilidad

| Requisito heredado | Sección |
|--------------------|---------|
| RNF-05 | §2, §5 |
| CSP por ambiente (doc 05) | §7.6, gate 5 |
| Gate alternate keys (docs 04/07) | Gate 1 (mecanismo nombrado) |
| Tabla canónica de choices (doc 08) | §7.2 — solo Dev, una vez |
| Gobernanza SP + cuenta de servicio (docs 07/08) | §7.4/§7.5, §10 |
| Backups (doc 07 §6 — antes dropeado, ahora operacionalizado) | §7.10, §5 (rollback) |
| Verificaciones de primer deploy (doc 05 §11) | Gates 5–7 |
| TSA desde sandbox (doc 04 §10) | Gate 8 + modo de integración Dev (§6) |
| Auditoría (doc 03 §7) | §7.7 (con precisión verificada de qué viaja y qué no) |
| Monitoreo/ownership de flows (doc 08 §7) | Gate 3, §10 |

## 13. Fuentes verificadas

- Pipelines (conexiones + env vars al desplegar; stages en orden; approvals; delegated deployments): learn.microsoft.com/power-platform/alm/run-pipeline · /alm/pipelines
- Code apps ALM (pipelines soportados; sin solution packager/git integration): learn.microsoft.com/power-apps/developer/code-apps/how-to/alm
- Env vars (los valores en la solución viajan y prevalecen; remover antes de exportar): learn.microsoft.com/power-apps/maker/data-platform/environmentvariables
- `pac plugin push` (sin parámetro de solución) / `pac code push --solutionName`: learn.microsoft.com/power-platform/developer/cli/reference/plugin · /reference/code
- Preferred solution (por maker): learn.microsoft.com/power-apps/maker/data-platform/preferred-solution
- FLS profiles (membresía por ambiente): learn.microsoft.com/power-platform/admin/field-level-security
- Alternate keys (EntityKeyIndexStatus, ReactivateEntityKey): learn.microsoft.com/power-apps/developer/data-platform/define-alternate-keys-entity
- Auditoría (IsAuditEnabled viaja; switch org por ambiente): learn.microsoft.com/power-apps/developer/data-platform/auditing/configure

---

*Anterior: [08-notificaciones-y-recordatorios.md](08-notificaciones-y-recordatorios.md) · Siguiente: 10 — Hoja de ruta.*

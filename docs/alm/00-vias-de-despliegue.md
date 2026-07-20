# Sigil — Vías de Despliegue (Playbook ALM reutilizable)

**Documento:** ALM/00 — Cluster de vías de despliegue
**Estado:** Borrador (pendiente de verificación antagonista)
**Última actualización:** 2026-07-20
**Relación con Fase 0:** Extiende, sin contradecir, [`../fase-0/09-alm-entornos-y-despliegue.md`](../fase-0/09-alm-entornos-y-despliegue.md) (Aprobado). El doc 09 fija la decisión **específica de Sigil** (Pipelines). Este cluster documenta las **tres vías** como playbook reutilizable en cualquier tenant.

> **Regla de este cluster (heredada del índice de Fase 0, regla #2):** ningún dato de plataforma se escribe de memoria. Cada afirmación de plataforma lleva cita `[n]` a documentación oficial, con las **fuentes al final del archivo**. Lo no confirmable se marca **NO VERIFICADO**.

---

## 1. Para qué sirve este documento

Existen **tres vías** para mover una solución de Power Platform entre ambientes:

1. **Exportar/Importar soluciones** (manual, "packages") → [`01-via-export-import.md`](01-via-export-import.md)
2. **Power Platform Pipelines** (ALM in-product) → [`02-via-pipelines.md`](02-via-pipelines.md)
3. **GitHub Actions** (CI/CD sobre git) → [`03-via-github-actions.md`](03-via-github-actions.md)

Y una capa transversal a las tres:

4. **Variables de entorno y secretos** → [`04-env-vars-y-secretos.md`](04-env-vars-y-secretos.md)

Este documento (00) es el **mapa**: qué es cada vía, ventajas/desventajas, cuándo elegir cuál, y el invariante que las tres respetan.

## 2. El invariante (vale para las TRES vías)

Independientemente de la vía, estas reglas no cambian — son propiedad del modelo de soluciones, no de la herramienta:

- **Unmanaged solo en Dev.** Las soluciones unmanaged se usan en ambientes de desarrollo mientras se hacen cambios; son la fuente de la verdad de los assets [1]. **Managed** se despliega a todo ambiente que **no** sea el de desarrollo de esa solución — test, UAT, SIT, producción [1].
- **Managed = artefacto de build.** La práctica ALM es **generar** la managed exportando la unmanaged **como managed**, y tratarla como artefacto de build [1]. No se puede exportar una solución que ya es managed: se exporta la unmanaged marcando el tipo Managed [1][2].
- **Test y Prod son ambientes separados del Dev.** No se puede importar una managed en el mismo ambiente que contiene su unmanaged de origen [1].
- **Los valores de config NO viajan en el artefacto.** Las *definiciones* de variables de entorno van en la solución, pero los **valores** se proveen por ambiente destino en el despliegue [3] (detalle en el doc 04).

Esto es exactamente la política que ya fijó el doc 09 §1 para Sigil ("jamás unmanaged en Test/Prod"). Las tres vías la cumplen; cambian en **cómo** llevan el artefacto y **quién/qué** lo empuja.

## 3. Las tres vías de un vistazo

| | **1. Export/Import** | **2. Pipelines** | **3. GitHub Actions** |
|---|---|---|---|
| **Qué es** | Exportar zip managed e importarlo a mano (portal o `pac`) | ALM in-product que automatiza el deploy entre ambientes [4] | Workflows de CI/CD que ejecutan las acciones oficiales de Power Platform [7] |
| **Dónde vive el proceso** | En la cabeza del operador + comandos `pac` | En un **host environment** de Dataverse [5] | En GitHub (`.github/workflows`) [7] |
| **Automatización** | Manual (scriptable con `pac`) | Media–alta (in-product, con validación previa) [4] | Alta (dispara por push/PR/release) [8] |
| **Infra extra** | Ninguna (solo `pac` CLI) | Host env + **managed environments** en los destinos [4] + licencias premium [4] | Repo GitHub + runners + App Registration |
| **Aprobación humana** | Fuera de banda (la pone el proceso, no la herramienta) | **Sí, integrada** (pre-deployment steps / delegated deployments con aprobaciones) [6][9] | Vía *environments* + reviewers de GitHub (fuera del alcance de Learn — ver doc 03) |
| **Identidad del deploy** | La del operador (o un SP si scripteás `pac`) | Maker, o **service principal** vía delegated deployments [9] | **Service principal** (secret) u **OIDC/FIC** sin secreto [10][11] |
| **Nativo a git** | No | No | **Sí** — el artefacto y el YAML viven en git |
| **Orden de stages forzado** | No (disciplina del operador) | **Sí** — el mismo artefacto pasa por stages en orden, sin saltos ni manipulación [12] | Lo definís vos en el YAML |
| **Mejor para** | Bootstrap, tenants chicos, un primer deploy, plan B | Equipos Power Platform que quieren gobernanza sin montar CI/CD | Equipos con cultura git/DevOps y múltiples tenants |

## 4. Ventajas y desventajas (resumen — el detalle vive en cada doc)

**1. Export/Import**
- ✅ Cero infraestructura; funciona en cualquier tenant desde el minuto cero; ideal como **plan B** cuando algo del pipeline falla.
- ✅ Transparente: ves exactamente el zip que subís.
- ❌ Manual y propenso a error humano; no fuerza el orden de stages; la trazabilidad depende de la disciplina (guardar el zip versionado).
- ❌ La aprobación y los gates son externos a la herramienta.

**2. Power Platform Pipelines**
- ✅ Gobernanza integrada: orden de stages inviolable [12], validación previa (preflight) contra el destino [13], aprobaciones y delegated deployments con SP [6][9].
- ✅ No exige saber CI/CD ni git; lo maneja el maker desde la solución.
- ❌ Exige **managed environments** en los destinos [4] y **licencias premium** para ellos [4]; monta un **host environment** [5].
- ❌ Atado al tenant: **no** despliega entre tenants distintos (ver doc 02).

**3. GitHub Actions**
- ✅ Un solo lugar (git) para código + artefacto + pipeline; reutilizable y versionado; **OIDC sin secretos** [11].
- ✅ Corre en Windows y Linux [7]; se integra con PRs, releases y reviewers.
- ❌ Curva de CI/CD, App Registration y manejo de secretos; más piezas que mantener.
- ❌ Las acciones de **pack/unpack asumen componentes empaquetables** — y ahí aparece la nota de las code apps (§5).

## 5. La nota que colorea TODO para Sigil: las Code Apps

Sigil incluye una **Power Apps Code App**. Y la documentación oficial es explícita:

- Las code apps **no soportan solution packager** (pack/unpack) [14].
- Las code apps **no soportan integración de código fuente (git)** [15][16].
- El camino documentado: `pac code push` deja la app en la solución preferida, y **una vez en la solución se usan Power Platform Pipelines** para promover Dev → Test → Prod [16].

**Implicancia honesta:**
- La vía **2 (Pipelines)** es la única con soporte documentado end-to-end para la solución de Sigil **con** la code app adentro. Aun así, doc 09 §11 ya marcó como **NO VERIFICADO** la combinación exacta code app + plugin package + flows en UNA sola solución vía pipeline — se valida con los gates del Runbook B.
- Las vías **1 (export/import)** y **3 (GitHub Actions)** son perfectamente válidas para todo lo **empaquetable** (tablas, choices, plugin package, flows, env vars). Pero el **componente code app** por unpack/pack **no está soportado** [14][15]; y no encontré página oficial que confirme que un export-as-managed → import manual de una solución que **contiene** una code app funcione limpio → **NO VERIFICADO**.

Por eso este playbook documenta las tres, pero para **Sigil** la recomendación operativa sigue siendo la del doc 09 (Pipelines para la solución completa), dejando export/import y GitHub Actions como vías plenas para otros tenants o para las partes empaquetables.

## 6. Cómo elegir (árbol de decisión)

1. **¿Tu solución tiene una Code App?** → Pipelines es la vía con soporte documentado para moverla dentro de la solución [16]. (Sigil cae acá.)
2. **¿Equipo con cultura git/DevOps y/o varios tenants?** → GitHub Actions (con OIDC) da el mejor control de versiones y portabilidad.
3. **¿Equipo Power Platform que quiere gobernanza sin montar CI/CD?** → Pipelines.
4. **¿Tenant chico, primer deploy, o necesitás un plan B siempre a mano?** → Export/Import manual.

No son excluyentes: es común usar **Pipelines** como vía principal y **Export/Import** como plan B (doc 09 §11 ya lo nombra como plan B con `pac solution import --activate-plugins`).

## 7. Estado en Sigil y reutilización

- **Sigil hoy:** doc 09 eligió **Pipelines**. Este ambiente puede operar por packages (export/import) en el arranque, y migrar a Pipelines cuando el host env y los managed environments estén provisionados.
- **Otro tenant:** puede tomar este mismo cluster y elegir Pipelines o GitHub Actions según su cultura. Los ejemplos concretos usan los nombres de Sigil (`sigil_core_sigil`, publisher `sanic_`) como referencia traducible.

## 8. Trazabilidad

| Tema | Documento |
|------|-----------|
| Decisión específica de Sigil (Pipelines, env vars, rollback) | `../fase-0/09-alm-entornos-y-despliegue.md` |
| Aprovisionamiento de identidades/conexiones por ambiente | `../runbooks/runbook-a-aprovisionamiento-ambiente.md` |
| Gates post-import | `../runbooks/runbook-b-gates-post-import.md` |
| Vía manual paso a paso | `01-via-export-import.md` |
| Pipelines paso a paso | `02-via-pipelines.md` |
| GitHub Actions paso a paso | `03-via-github-actions.md` |
| Env vars y secretos (propuestas) | `04-env-vars-y-secretos.md` |

---

## Fuentes

Todas verificadas contra documentación oficial (Microsoft Learn) el 2026-07-20.

1. Solution concepts (managed vs unmanaged; managed a no-dev; managed = build artifact; no importar managed en el ambiente de su unmanaged): https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm
2. Exportar soluciones (elegir Managed en "Export as"): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/export-solutions
3. Variables de entorno (las definiciones van en la solución; los valores se proveen por ambiente en el deploy): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
4. Pipelines overview (ALM in-product; managed a no-dev; managed environments requeridos; licencias premium): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
5. Configurar host/pipelines personalizados (host environment; instalar la app solo en el host): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
6. Custom host pipelines (pre-deployment step / aprobaciones): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
7. GitHub Actions for Power Platform (automatiza import/export/deploy; Windows y Linux): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-actions
8. Tutorial deploy con GitHub Actions (export unmanaged de Dev → artefacto managed → deploy a Prod): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-deploy
9. Delegated deployments (deploy como service principal; permisos por identidad del SP; aprobaciones): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
10. Available GitHub Actions (auth service principal + client secret): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
11. GitHub Actions OIDC/FIC (autenticar sin client secret): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-oidc-fic
12. Pipelines (orden de stages secuencial; sin manipulación del artefacto): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
13. Pipelines (prevalidación/preflight contra el destino): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
14. Code apps ALM (no soportan solution packager): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
15. Code apps ALM (no soportan integración de código fuente): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
16. Code apps ALM (`pac code push` a la solución; luego Pipelines para Dev→Test→Prod): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm

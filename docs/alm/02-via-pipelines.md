# Sigil — Vía 2: Power Platform Pipelines

**Documento:** ALM/02
**Estado:** Borrador (pendiente de verificación antagonista)
**Última actualización:** 2026-07-20
**Ver también:** [`00-vias-de-despliegue.md`](00-vias-de-despliegue.md) · [`04-env-vars-y-secretos.md`](04-env-vars-y-secretos.md) · [`../fase-0/09-alm-entornos-y-despliegue.md`](../fase-0/09-alm-entornos-y-despliegue.md) (§5/§7 — **la decisión de Sigil**)

> Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final del archivo**. Lo no confirmado se marca **NO VERIFICADO**.

---

## 1. Qué es

Power Platform Pipelines es la vía **in-product** de ALM: automatiza los despliegues de soluciones entre ambientes con "mínimo esfuerzo" [1], sin montar CI/CD externo. La página oficial de ALM de code apps recomienda **Pipelines** como camino de deploy una vez que la app está en la solución [2]. Es la vía elegida por Sigil (doc 09 §5); el que sea *la única* opción end-to-end para mover la solución **con la code app adentro** es razonamiento de Sigil (doc 00 §5), no una afirmación de la fuente [2].

Los pipelines despliegan la **solución** más la **configuración del destino**: conexiones, connection references y variables de entorno [3].

## 2. Prerrequisitos (lo que exige la plataforma)

- **Managed environments:** todos los ambientes destino (no-dev) del pipeline deben estar habilitados como **managed environments** [4]. Host y Dev no hace falta [4].
- **Licencias premium:** se requieren licencias con derechos de uso premium para **todos** los managed environments del pipeline [4].
- **Managed a no-dev:** los pipelines despliegan **managed** a los ambientes no-dev; no se recomienda desplegar unmanaged [5].
- **Roles de seguridad** (se instalan con la app): **Deployment Pipeline User** (correr pipelines) y **Deployment Pipeline Administrator** (control total de la configuración) [6]. Además, el maker necesita privilegios de **export** en el origen y de **import** en los destinos [7].

## 3. Configuración (setup, en orden)

Cada paso citado en §Fuentes.

1. **Elegí los ambientes:** un **host** dedicado (plano de almacenamiento y gestión de toda la config, seguridad e historial de runs), el/los de desarrollo y el/los destino [8].
2. **Habilitá los destinos como managed environments** [9].
3. **Instalá la app "Power Platform Pipelines" SOLO en el host** — no hace falta en los demás ambientes [10].
4. En el host, **ejecutá la app "Deployment Pipeline Configuration"** [11].
5. **Creá los registros de ambiente**, tipando cada uno como **Development Environment** o **Target Environment** (Target para QA y producción, donde aterrizan las managed) [12].
6. **Creá el pipeline** (Name, Description) en *Pipelines > New* [13].
7. **Vinculá el/los ambiente(s) de desarrollo** al pipeline (*Add Existing Development Environment*) [14].
8. **Creá los stages ordenados**, seteando *Target Deployment Environment* y encadenándolos con *Previous Deployment Stage* (ej.: Prod después de Test) [15]. Mínimo 1 stage, hasta **7** [16].
9. **(Opcional pero recomendado en Sigil) Delegated deployment:** en el stage, marcá *Is delegated deployment*, elegí **Service Principal**, poné el **Client ID** y guardá [17].
10. **(Delegated) Prerrequisitos del SP:** agregá la enterprise app como usuario **S2S** en el host y en cada destino; asigná **Deployment Pipeline Administrator** en el host y **System Administrator** en los destinos [18]. **Ojo:** quien habilita o modifica la config de service principal debe ser **owner de la enterprise application (el SP) en Microsoft Entra ID** [33] — si no, la habilitación falla por ownership.
11. **Compartí el pipeline** con los makers vía rol **Deployment Pipeline User** + **Read** en el registro del pipeline [19].

## 4. Cómo se usa (correr un deploy)

1. En el ambiente de **desarrollo**, abrí la solución **unmanaged** — los pipelines **solo** se ven/corren desde una unmanaged en un dev vinculado, **no** desde la default, ni desde managed, ni en los destinos [20].
2. Elegí **Deploy** → el stage (ej. *Deploy to Test*) → *Deploy here* [21].
3. **Validación previa (preflight):** el deploy se prevalida contra el destino para detectar dependencias faltantes antes de desplegar [22].
4. Si hay **connection references** o **environment variables**, te los **pide** (igual que en un import manual) [23].
5. **Aprobaciones/gates:** si el admin configuró pre-deployment steps o delegated approvals, el request queda **pendiente** hasta aprobarse [24][25]. En delegated deployments, al aprobarse, los permisos se asignan con la **identidad del SP** [26], y **todos** los delegated deployments quedan pendientes hasta aprobarse por un flow de aprobación configurado [27].

## 5. Gobernanza que te da (y que las otras vías no)

- **Orden inviolable:** el **mismo** artefacto pasa por los stages en orden secuencial; el sistema **impide saltar stages o manipular** el artefacto exportado [28].
- **Identidad no personal:** con delegated deployments el stage despliega como el **service principal** (o stage owner), no como el maker que pide [17] — refuerza "solo el pipeline escribe" (doc 09 §5).
- **Preflight** contra el destino [22].

## 6. Limitaciones (honestas)

- **Solo intra-tenant:** los pipelines **no** despliegan a un tenant distinto [29]. Relevante solo si los ambientes de Sigil cruzaran tenants (no es el caso).
- **Delegated con stage owner + OAuth:** un delegated deployment de tipo *stage owner* **no** puede desplegar soluciones con connection references para conexiones **OAuth** [30]. (Sigil usa SP como delegado, no stage owner; aun así, tenerlo presente para las conexiones Outlook/Teams del doc 08.)
- **Plugins / código:** desplegar plugins y otros componentes de código exige **System Administrator** en los destinos; roles menores no pueden [31] — por eso el SP delegado lleva System Administrator (§3.10).
- **Code App en pipeline — NO VERIFICADO:** ninguna página oficial que haya leído confirma *por su nombre* que una **code app** se despliegue por Pipelines sin fricción; el overview de code apps confirma que **no** soportan git integration [32] y la página de ALM de code apps recomienda Pipelines como camino [2], pero doc 09 §11 ya marca como **NO VERIFICADO** la combinación exacta *code app + plugin package + flows en UNA solución* vía pipeline. **Se valida con los gates del Runbook B**; plan B: export/import manual (doc 01, doc 09 §11).

## 7. Ejemplo Sigil

- **Host:** un ambiente dedicado (Runbook A lo provisiona).
- **Pipeline:** `sigil_core_sigil` · stages `Deploy to Test` → `Deploy to Prod` (encadenados) [15].
- **Delegado:** service principal de Sigil con **System Administrator** en Test/Prod [18][31].
- **Aprobaciones:** Dev→Test la aprueba el lead técnico; Test→Prod lead técnico + dueño de producto (doc 09 §5), implementadas como delegated approvals [25][27].
- **Env vars:** al correr, proveés los valores de la tabla del doc 09 §6 (jamás heredar los de Dev — doc 04 §3).
- **Post-deploy:** los 10 gates del Runbook B.

## 8. Ventajas y desventajas

**Ventajas**
- ✅ Gobernanza integrada: orden inviolable [28], preflight [22], aprobaciones y delegated SP [17][25].
- ✅ No exige CI/CD ni git; lo maneja el maker desde la solución.
- ✅ Camino documentado para mover la solución **con** la code app (doc 00 §5) [2].

**Desventajas**
- ❌ Exige **managed environments** [4] y **licencias premium** en los destinos [4]; monta un **host environment** [8].
- ❌ **Intra-tenant** solamente [29].
- ❌ La combinación exacta de componentes de Sigil queda **NO VERIFICADA** hasta pasar los gates (§6).

---

## Fuentes

Verificadas contra Microsoft Learn el 2026-07-20.

1. Pipelines overview (ALM in-product, mínimo esfuerzo): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
2. Code apps ALM (`pac code push` + Pipelines para Dev→Test→Prod): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
3. Pipelines (despliegan solución + config del destino: conexiones, conn refs, env vars): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
4. Pipelines (destinos = managed environments; licencias premium): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
5. Pipelines (managed a no-dev; no se recomienda unmanaged): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
6. Custom host pipelines (roles Deployment Pipeline User / Administrator): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
7. Custom host pipelines (maker necesita export en origen, import en destino): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
8. Custom host pipelines (host environment = plano de gestión): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
9. Custom host pipelines (destinos como managed environments): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
10. Custom host pipelines (instalar la app solo en el host): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
11. Custom host pipelines (ejecutar Deployment Pipeline Configuration app): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
12. Custom host pipelines (Environment Type: Development / Target): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
13. Custom host pipelines (crear pipeline, Name): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
14. Custom host pipelines (Linked Development Environments): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
15. Custom host pipelines (stages, Previous Deployment Stage): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
16. Custom host pipelines (mínimo 1, hasta 7 stages): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
17. Delegated deployments (Is delegated deployment; Service Principal; Client ID): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
18. Delegated deployments (S2S user; roles en host y destinos): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
19. Custom host pipelines (compartir con Deployment Pipeline User + Read): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
20. Pipelines (solo se ven/corren desde una unmanaged en un dev vinculado): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
21. Run a pipeline (Deploy → stage → Deploy here): https://learn.microsoft.com/en-us/power-platform/alm/run-pipeline
22. Pipelines (prevalidación/preflight): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
23. Run a pipeline (pide conn refs y env vars, como en el import manual): https://learn.microsoft.com/en-us/power-platform/alm/run-pipeline
24. Custom host pipelines (PreDeployment Step Required): https://learn.microsoft.com/en-us/power-platform/alm/custom-host-pipelines
25. Run a pipeline (request pendiente por procesos/aprobaciones del admin): https://learn.microsoft.com/en-us/power-platform/alm/run-pipeline
26. Delegated deployments (al aprobar, permisos con la identidad del SP): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
27. Delegated deployments (todos pendientes hasta aprobarse por un flow de aprobación): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
28. Pipelines (orden secuencial; sin manipulación del artefacto): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
29. Pipelines (no despliega a otro tenant): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
30. Delegated deployments (stage owner no puede desplegar conn refs de conexiones OAuth): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
31. Delegated deployments (System Administrator requerido para desplegar plugins/código): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
32. Code apps overview (no soportan Power Platform Git integration): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/overview
33. Delegated deployments (quien habilita/modifica la config de SP debe ser owner de la enterprise application en Entra ID): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup

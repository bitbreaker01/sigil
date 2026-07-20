# Sigil — Vía 3: GitHub Actions para Power Platform

**Documento:** ALM/03
**Estado:** Borrador (pendiente de verificación antagonista)
**Última actualización:** 2026-07-20
**Ver también:** [`00-vias-de-despliegue.md`](00-vias-de-despliegue.md) · [`04-env-vars-y-secretos.md`](04-env-vars-y-secretos.md)

> Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final del archivo**. Lo no confirmado se marca **NO VERIFICADO**.

---

## 1. Qué es

Los **GitHub Actions for Microsoft Power Platform** (repo oficial `microsoft/powerplatform-actions`) automatizan importar/exportar soluciones, desplegar a ambientes downstream, aprovisionar ambientes y correr el solution checker [1]. Corren en runners **Windows y Linux** [2] y solo aplican a un ambiente Dataverse **con base de datos** [3].

Es la vía indicada cuando el equipo tiene cultura **git/DevOps** y/o opera **varios tenants**: el artefacto, el YAML y el código viven juntos y versionados.

## 2. Autenticación (elegí UNA)

Dos formas soportadas de conectar a Power Platform:

| Forma | Cómo | Nota |
|-------|------|------|
| **Usuario/contraseña** | user + password | **No** soporta MFA [4] — evitar |
| **Service Principal + client secret** | App ID + Tenant ID como variables; el **secret** como **GitHub Secret** [5][6] | Soporta MFA [4]. El secret se referencia como `${{ secrets.CLIENT_SECRET_... }}` [6] |
| **OIDC / Federated Identity Credential** | `id-token: write` + app-id/tenant-id/environment-url, **sin** secret [7][8] | **Recomendado**: autentica sin almacenar client secret [7]. Detalle de identidades en doc 04 |

El **environment-url** del destino se pasa por el input `environment-url` (ej. `https://YourOrg.crm.dynamics.com`) [9].

## 3. Las acciones (nombres exactos)

Del catálogo oficial [10] (cada una citada en §Fuentes). **La primera task siempre** debe ser instalar las herramientas:

| Acción | Para qué |
|--------|----------|
| `actions-install` *(Install Power Platform Tools)* | Instala la CLI/tools; **debe ir primera** en el workflow [11] |
| `who-am-i` | Verifica la conexión con un WhoAmI [12] |
| `export-solution` | Exporta la solución del origen; input **`managed`** (requerido): `true` = managed, `false` = unmanaged [13] |
| `unpack-solution` | Descompone el zip en XML para control de fuente; `solution-type` (Unmanaged recomendado) [14] |
| `pack-solution` | Empaqueta el árbol versionado en un `solution.zip`; `solution-type` (Unmanaged/Managed/Both) [15] |
| `import-solution` | Importa la solución al destino [16] |
| `publish-solution` | Publica las customizations [17] |
| `upgrade-solution` | Hace upgrade de la solución en el destino [18] |
| `check-solution` | Corre el Power Platform checker sobre el zip [19] |
| `clone-solution` | Clona la solución de un ambiente [20] |
| `deploy-package` | Despliega un package dll/zip — **solo Windows** [21] |
| `branch-solution` | Crea una rama para guardar la solución exportada [22] |

> **Notas de precisión:** `branch-solution` está documentada en el **tutorial** [22], no en el catálogo de acciones [10]. En `pack-solution`, `solution-type` es **opcional** (default Unmanaged); en `unpack-solution` es **requerido** [15].

## 4. Cómo se produce una MANAGED

No hay un flag "importar como managed": el tipo lo determina el **zip** que producís [23]:
- **`export-solution` con `managed: true`** → exporta directamente el zip managed [13], o
- **`pack-solution` con `solution-type: Managed`** → empaqueta el árbol versionado como managed [15].

El tutorial oficial de deploy hace exactamente esto: exporta la app como **unmanaged** de Dev, genera un **artefacto managed** y lo despliega a Producción **como managed** [24][25].

## 5. Workflow de ejemplo (Sigil, con OIDC)

Dos flujos típicos. Los nombres (`sigil_core_sigil`, secrets) son de referencia.

### 5.1 Export desde Dev → commit a git (fuente de la verdad del artefacto)

```yaml
name: export-from-dev
on: workflow_dispatch
permissions:
  id-token: write          # OIDC/FIC — autenticar sin client secret [7][8]
  contents: write
jobs:
  export:
    runs-on: ubuntu-latest # las acciones corren en Linux [2]
    env:
      DEV_URL: https://sigil-dev.crm.dynamics.com
    steps:
      - uses: actions/checkout@v4
      - uses: microsoft/powerplatform-actions/actions-install@v1   # PRIMERA task [11]
      - uses: microsoft/powerplatform-actions/who-am-i@v1          # verifica conexión [12]
        with:
          environment-url: ${{ env.DEV_URL }}
          app-id: ${{ secrets.PP_APP_ID }}
          tenant-id: ${{ secrets.PP_TENANT_ID }}
      - uses: microsoft/powerplatform-actions/export-solution@v1   # [13]
        with:
          environment-url: ${{ env.DEV_URL }}
          app-id: ${{ secrets.PP_APP_ID }}
          tenant-id: ${{ secrets.PP_TENANT_ID }}
          solution-name: sigil_core_sigil
          managed: false                # unmanaged para versionar el fuente [24]
          solution-output-file: out/sigil_core_sigil.zip
      - uses: microsoft/powerplatform-actions/unpack-solution@v1   # [14]
        with:
          solution-file: out/sigil_core_sigil.zip
          solution-folder: solutions/sigil_core_sigil
          solution-type: Unmanaged
      - run: |
          git config user.name "sigil-ci"
          git add solutions/ && git commit -m "chore(alm): export sigil_core_sigil desde Dev" && git push
```

### 5.2 Release a Test/Prod (pack managed → import)

```yaml
name: release
on:
  push:
    tags: ['sigil/v*']
permissions:
  id-token: write
  contents: read
jobs:
  deploy-test:
    runs-on: ubuntu-latest
    environment: Test          # gate de reviewers de GitHub (ver §7 — NO VERIFICADO en Learn)
    env:
      TEST_URL: https://sigil-test.crm.dynamics.com
    steps:
      - uses: actions/checkout@v4
      - uses: microsoft/powerplatform-actions/actions-install@v1        # [11]
      - uses: microsoft/powerplatform-actions/pack-solution@v1          # empaqueta MANAGED [15]
        with:
          solution-folder: solutions/sigil_core_sigil
          solution-file: out/sigil_core_sigil_managed.zip
          solution-type: Managed
      - uses: microsoft/powerplatform-actions/import-solution@v1        # [16]
        with:
          environment-url: ${{ env.TEST_URL }}
          app-id: ${{ secrets.PP_APP_ID }}
          tenant-id: ${{ secrets.PP_TENANT_ID }}
          solution-file: out/sigil_core_sigil_managed.zip
          # NB: el catálogo oficial de acciones [10] NO documenta un input `activate-plugins`
          # para import-solution → NO VERIFICADO como input nativo (ver §7). Para activar
          # plugins/workflows con certeza, invocar `pac solution import --activate-plugins` en el job.
  # deploy-prod: idéntico, environment: Prod, needs: [deploy-test]
```

> **Valores de env vars / conn refs en Actions — NO VERIFICADO:** el tutorial oficial de deploy que leí **no** muestra explícitamente un input de *deployment settings file* para `import-solution`. El settings file (`{ "EnvironmentVariables": [...], "ConnectionReferences": [...] }`) está documentado para **`pac solution import --settings-file`** y para Power Platform **Build Tools** [26]. Si necesitás setear valores por ambiente en Actions, la ruta confirmada es **invocar `pac`** dentro del job (doc 04 §4), no un input nativo confirmado de `import-solution`.

## 6. Secretos en GitHub

- El **client secret** del SP se guarda como **GitHub Secret** y se referencia en el workflow [6]. El tutorial lo nombra `PowerPlatformSPN` [27].
- **OIDC/FIC**: no guardás secret; el job intercambia el token de GitHub por uno de Entra vía **federated identity credential** [7][8]. Detalle en doc 04.

## 7. Limitaciones (honestas)

- **Code App con pack/unpack — no soportado:** las code apps **no** soportan solution packager [28] ni integración de código fuente [29]; su camino es `pac code push` + Pipelines [30]. Por lo tanto, el loop `export → unpack → pack` de esta vía **no aplica al componente code app** de Sigil → **combinar code app con las acciones clásicas de pack/unpack es NO VERIFICADO y probablemente no soportado**.
- **Plugin packages:** viajan dentro del zip por export/pack/import, pero **no** encontré guía específica de GitHub Actions para plugin packages → los detalles finos quedan **NO VERIFICADO**; se validan con los gates del Runbook B.
- **`set-solution-version` — NO VERIFICADO:** no confirmé una acción con ese nombre exacto en el catálogo oficial de GitHub Actions (existe en Azure DevOps Build Tools, otro producto).
- **`activate-plugins` en `import-solution` — NO VERIFICADO:** el catálogo oficial [10] no lista ese input para `import-solution`. Para activar plugins/workflows con certeza, invocar `pac solution import --activate-plugins` dentro del job (doc 01 §5).
- **Gates/reviewers de GitHub Environments:** el control de aprobación por *environment protection rules* es de **GitHub**, no de Learn — fuera del alcance citable de Microsoft; documentarlo aparte si se adopta.

## 8. Ventajas y desventajas

**Ventajas**
- ✅ Un solo lugar (git) para código + artefacto + pipeline; versionado y reutilizable entre tenants.
- ✅ **OIDC sin secretos** [7]; runners Windows y Linux [2].
- ✅ Se integra con PRs, tags y reviewers de GitHub.

**Desventajas**
- ❌ Curva de CI/CD + App Registration + manejo de secretos/OIDC.
- ❌ El loop pack/unpack **no** cubre la **code app** de Sigil (§7) — para Sigil, esta vía sirve para las partes empaquetables, no para mover la solución completa.
- ❌ Setear valores por ambiente exige invocar `pac` en el job (settings file no confirmado como input nativo — §5).

---

## Fuentes

Verificadas contra Microsoft Learn el 2026-07-20.

1. GitHub Actions for Power Platform (automatiza import/export/deploy/provisioning/checker): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-actions
2. GitHub Actions (corren en Windows y Linux): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-actions
3. GitHub Actions (solo Dataverse con base de datos): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-actions
4. GitHub Actions (user/pass sin MFA; SP + client secret con MFA): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-actions
5. Available GitHub Actions (App ID y Tenant ID como variables): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
6. Available GitHub Actions (client secret como GitHub Secret, referenciado en el workflow): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
7. Tutorial OIDC/FIC (autenticar sin client secret): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-oidc-fic
8. Tutorial OIDC/FIC (`permissions: id-token: write`): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-oidc-fic
9. Available GitHub Actions (input `environment-url`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
10. Available GitHub Actions (catálogo de acciones): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
11. Available GitHub Actions (`actions-install` debe ir primera): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
12. Available GitHub Actions (`who-am-i` verifica con WhoAmI): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
13. Available GitHub Actions (`export-solution`, `managed` requerido): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
14. Available GitHub Actions (`unpack-solution`, `solution-type`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
15. Available GitHub Actions (`pack-solution`, `solution-type` Unmanaged/Managed/Both): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
16. Available GitHub Actions (`import-solution`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
17. Available GitHub Actions (`publish-solution`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
18. Available GitHub Actions (`upgrade-solution`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
19. Available GitHub Actions (`check-solution`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
20. Available GitHub Actions (`clone-solution`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
21. Available GitHub Actions (`deploy-package`, solo Windows): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
22. Tutorial deploy (`branch-solution`): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-deploy
23. Available GitHub Actions (el tipo lo determina export `managed` / pack `solution-type`): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
24. Tutorial deploy (export unmanaged de Dev → artefacto managed): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-deploy
25. Tutorial deploy (validar que se desplegó como managed en Prod): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-deploy
26. Conn refs + env vars con Build Tools / settings file JSON: https://learn.microsoft.com/en-us/power-platform/alm/conn-ref-env-variables-build-tools
27. Tutorial deploy (secret `PowerPlatformSPN`): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-deploy
28. Code apps ALM (no soportan solution packager): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
29. Code apps ALM (no soportan integración de código fuente): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
30. Code apps ALM (`pac code push` + Pipelines): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm

# Sigil — Variables de Entorno y Secretos (propuestas + guía)

**Documento:** ALM/04
**Estado:** Borrador (pendiente de verificación antagonista)
**Última actualización:** 2026-07-20
**Ver también:** [`00-vias-de-despliegue.md`](00-vias-de-despliegue.md) · [`../fase-0/09-alm-entornos-y-despliegue.md`](../fase-0/09-alm-entornos-y-despliegue.md) §6 · [`../fase-0/07-seguridad-y-cumplimiento.md`](../fase-0/07-seguridad-y-cumplimiento.md)

> Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final del archivo**. Lo no confirmado se marca **NO VERIFICADO**. Este doc **propone** el manejo de secretos con ventajas/desventajas; la decisión es del equipo.

---

## 1. Dos cosas distintas que se confunden

| | **Variables de entorno** | **Secretos** |
|---|---|---|
| Qué es | Config por ambiente (URLs, tamaños, flags) | Credenciales (client secrets, API keys, cert) |
| Dónde vive | Definición en la solución; **valor por ambiente** [1] | **Nunca** en git ni en el artefacto [2] |
| Ejemplo Sigil | `env_AppPlayUrl`, `env_TsaEndpoints`, `env_MaxPdfSizeKB` | secret del SP de deploy; secret de la API TSA (si aplica) |

Se cruzan en un solo punto: una **variable de entorno tipo Secret** que **referencia** un secreto en Azure Key Vault [3]. Ese es el puente, y lo cubre la Propuesta A.

## 2. Variables de entorno — tipos y anatomía

**Tipos disponibles:** Decimal number, Text, JSON, **Two options** (booleano Sí/No), **Data source**, y **Secret** [4]. (El tipo *Data source* es para conectores con autenticación como Microsoft Entra, donde la conexión sola no identifica el servicio/tabla [5].)

**Valor por defecto vs valor actual:**
- **Current Value** (el "valor"): opcional, vive en la tabla de valores [6]. Si está definido, **se usa aunque haya default** (el current tiene precedencia) [7].
- **Default Value**: vive en la definición, no es obligatorio, y se usa **solo si no hay current value** [8].

Límite: 2.000 caracteres por variable [9].

## 3. Higiene (la regla de oro — vale para las 3 vías)

**Las definiciones van en la solución; los valores se proveen por ambiente destino en el deploy** [10]. La política oficial es **no** incluir el valor en la solución [10].

**Por qué importa (y por qué doc 09 §5 lo marca CRÍTICO):** un current value incluido en la solución **se exporta con ella** salvo que lo remuevas [11]. En el import, las variables **sin** valor te **piden** uno; las que ya traen valor **no** muestran esa pantalla [12] — es decir, un valor olvidado **aterriza en silencio**. Ejemplo real de Sigil: el `env_TsaEndpoints` = FreeTSA de Dev llegaría a Prod sin que nadie lo note.

**Cómo removerlo (antes de exportar):** en la variable, bajo *Current Value* → `...` → **Remove from this solution** [13]. Esto deja el valor en Dev pero lo saca del artefacto [11].

**Dónde termina el valor:** en el zip exportado, los valores se separan en **archivos JSON** que se pueden editar offline [14].

## 4. Cómo se setean los valores por ambiente (por vía)

| Vía | Mecanismo | Cita |
|-----|-----------|------|
| **Import manual (portal)** | La UI moderna de import permite ingresar los valores; setea el value en la tabla `environmentvariablevalue` [15] | [15] |
| **CLI `pac`** | **deployment settings file** JSON: `pac solution create-settings` lo genera; `pac solution import --settings-file` lo aplica [16][17] | [16][17] |
| **Pipelines** | Los valores se **proveen upfront y se validan antes** de desplegar [18]; visibles al desplegar igual que en un import [19] | [18][19] |
| **GitHub Actions** | **NO VERIFICADO** como input nativo de `import-solution`; ruta confirmada: invocar `pac ... --settings-file` en el job [20] | [20] |

**Estructura del deployment settings file** (documentada para Build Tools / `pac`) [16]:
```json
{
  "EnvironmentVariables": [
    { "SchemaName": "sanic_sigil_env_AppPlayUrl", "Value": "https://.../play/e/<env>/app/<id>" }
  ],
  "ConnectionReferences": [
    { "LogicalName": "sanic_...", "ConnectionId": "<id>", "ConnectorId": "/providers/Microsoft.PowerApps/apis/shared_office365" }
  ]
}
```
En Sigil: un `settings.test.json` y un `settings.prod.json` versionados (sin secretos), con los valores de la tabla del doc 09 §6.

## 5. Secretos — el principio

**Regla dura (Well-Architected):** no hardcodear secretos en flows, canvas apps, archivos de config **ni en pipelines de build-deploy**; mantenerlos fuera del código, en un sistema como Key Vault [2]. Y **claves distintas por consumidor y por ambiente** — la misma clave **no** se comparte entre preproducción y producción [21].

En Sigil hay (potencialmente) **dos familias** de secretos:
1. **Identidad de deploy** — el SP que empuja la solución (Pipelines delegado o GitHub Actions).
2. **Secreto de runtime** — un secreto que la app/plugin consume (ej. credencial de la API TSA, si la hubiera).

Las propuestas de abajo cubren ambas. **No** son excluyentes: lo normal es combinar A (runtime) + C o D (deploy).

## 6. Propuestas de manejo de secretos

### Propuesta A — Azure Key Vault + variable de entorno tipo Secret *(para secretos de RUNTIME)*

La variable de entorno **referencia** el secreto; el secreto real vive en Key Vault [3]. Disponible para **Power Automate flows, Copilot Studio agents y custom connectors** (no en general por la API) [3].

**Setup requerido:**
- Key Vault en el **mismo tenant** Entra que el ambiente Power Platform [22].
- La suscripción Azure con el resource provider **`Microsoft.PowerPlatform`** registrado [23].
- Rol RBAC **Key Vault Secrets User** otorgado al **service principal de Dataverse** (app ID `00000007-0000-0000-c000-000000000000`) [24][25]. Microsoft recomienda pasar el vault al modelo **Azure RBAC** [26].
- Quien **crea/usa** la variable Secret necesita **Key Vault Secrets User** vía IAM [27].
- Si el **Key Vault Firewall** está activo, hay que permitir las **IPs de Power Platform** (no entra en "Trusted Services Only") [28].
- La variable Secret se define con Subscription ID + Resource Group + Key Vault Name + Secret Name [29].

**Ventajas:** el secreto nunca toca la solución ni git [2][3]; rotás en Key Vault sin re-desplegar; RBAC y auditoría de Azure.
**Desventajas:** más piezas (Azure sub, RBAC, firewall); **alcance limitado** (flows/custom connectors) [3] — **NO VERIFICADO** que un plugin C# lea la variable Secret directamente (ver §8).

### Propuesta B — Client secret del SP en el store del pipeline *(para IDENTIDAD de deploy)*

El SP usa **client secret**; se guarda en **GitHub Secrets** (Actions) [30] o se referencia en la conexión Dataverse del delegated deployment de Pipelines [31].

**Ventajas:** simple, universal, funciona en cualquier runner/tenant.
**Desventajas:** el secret se muestra **una sola vez** y hay que copiarlo [32]; **expira** y exige rotación [33]; es un secreto que hay que custodiar y rotar (riesgo declarado) [34].

### Propuesta C — OIDC / Workload Identity Federation *(para IDENTIDAD de deploy en GitHub Actions)* — **RECOMENDADA para Actions**

GitHub Actions autentica a Entra vía **federated identity credential**, **sin** almacenar client secret [35][36]. El job declara `id-token: write` y usa app-id/tenant-id/environment-url [35].

**Ventajas:** **cero secretos** que rotar o filtrar [36][34]; menos superficie de ataque; alineado con Well-Architected [2].
**Desventajas:** setup inicial de FIC en Entra; atado a GitHub como IdP (no aplica a Pipelines ni a `pac` local).

### Propuesta D — Certificado en vez de client secret *(para IDENTIDAD de deploy)*

El application user de Dataverse admite **key secret** *o* **certificado X.509** [37].

**Ventajas:** el certificado evita el riesgo/rotación/downtime que cargan los secretos [34].
**Desventajas — NO VERIFICADO:** no encontré una página oficial de Power Platform/Dataverse que declare *explícitamente* "el certificado es más seguro que el client secret" para el caso S2S; el argumento sale de la página de Workload Identity Federation de Entra [34], que compara secretos/certificados como riesgo a gestionar, no cert-vs-secret en una misma app. Además, **NO VERIFICADO** si el SP **delegado de Pipelines** admite certificado — la doc de delegated deployments solo menciona "client ID and secret" para la conexión del flow de aprobación [31].

## 7. Recomendación (matriz)

| Secreto | Propuesta recomendada | Por qué |
|---------|----------------------|---------|
| Identidad de deploy — **GitHub Actions** | **C (OIDC/FIC)** [35] | Cero secretos a custodiar/rotar [36] |
| Identidad de deploy — **Pipelines delegado** | **B (client secret)**, evaluar **D** | La doc solo confirma client ID + secret para el flow [31]; cert = **NO VERIFICADO** para el delegado |
| Identidad de deploy — **`pac` local / bootstrap** | **B** o **D** | Simple; rotación disciplinada (doc 09 §10) |
| Secreto de **runtime** (ej. TSA) consumido por **flow/custom connector** | **A (Key Vault + env var Secret)** [3] | El secreto nunca toca solución ni git [2] |
| Secreto de **runtime** consumido por **plugin C#** | **Ver §8 — NO VERIFICADO**; hoy Sigil no depende de esto | La lectura de env var Secret desde plugin no está confirmada |

## 8. NO VERIFICADO / límites honestos

- **Env var Secret desde plugin C#:** la doc dice que las variables Secret quedan disponibles para **flows y custom connectors** [3]; **no** confirma lectura directa desde un plugin/assembly. Si Sigil necesitara un secreto de runtime en el backend C#, hay que verificar el mecanismo (posible: el plugin llama a Key Vault con su propia identidad) antes de diseñarlo.
- **Certificado en el SP delegado de Pipelines:** **NO VERIFICADO** (§6-D) [31].
- **"El current value siempre pisa el valor del destino en el import":** confirmé que el current value se **exporta** salvo que lo remuevas [11] y que el current tiene precedencia sobre el **default** [7]; la UI etiqueta el origen del valor (solución / destino / default) [12], pero **no** encontré una frase oficial que rankee explícitamente *solución sobre destino* en el import. La higiene del §3 (remover el valor) hace el punto **irrelevante**: si no viaja, no puede pisar nada.
- **Tags de Key Vault (`AllowedBots`/`AllowedEnvironments`):** documentados específicamente para **Copilot Studio** [38]; **no** se requieren para el caso de flows/custom connectors de Sigil.
- **GitHub Secrets (docs de GitHub):** el mecanismo de *encrypted secrets* de GitHub se cita aquí vía la doc de Microsoft [30]; los detalles propios de GitHub (docs.github.com) quedan fuera del alcance citable de Learn.

## 9. Rotación (calendario)

- Correr un proceso de rotación **regular y confiable**, con capacidad de rotación de **emergencia** [39]; retirar/reemplazar secretos tan seguido como se pueda, y ante fin de vida/compromiso [40].
- Ojo la **ventana** en que el viejo ya no vale y el nuevo no está puesto: mitigar con retry/credenciales concurrentes [41].
- En Sigil, esto ya está calendarizado (doc 09 §10): cert del SP anual (alerta 30 días antes); client secret del runner cada 180 días.

---

## Fuentes

Verificadas contra Microsoft Learn el 2026-07-20.

1. Env vars (definición en la solución; valor por ambiente): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
2. Well-Architected — application secrets (no hardcodear en flows/apps/config/pipelines; usar Key Vault): https://learn.microsoft.com/en-us/power-platform/well-architected/security/application-secrets
3. Env vars Secret + Key Vault (el secreto vive en KV; la env var lo referencia; disponible para flows y custom connectors): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
4. Env vars (tipos: Decimal, Text, JSON, Two options, Data source, Secret): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
5. Env vars (Data source: conectores con autenticación como Entra): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
6. Env vars (Current Value opcional, en la tabla de valores): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
7. Env vars (el current value se usa aunque haya default): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
8. Env vars (default solo si no hay current value): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
9. Env vars (límite 2.000 caracteres): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
10. Env vars (política: definiciones en la solución, valores por ambiente destino): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
11. Env vars (remover el valor antes de exportar para que no viaje): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
12. Env vars (sin valor → prompt; con valor → prefilled con etiqueta de origen): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
13. Env vars (Current Value → ... → Remove from this solution): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
14. Env vars (valores en archivos JSON separados dentro del zip, editables offline): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
15. Env vars (UI moderna de import ingresa valores; setea environmentvariablevalue): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
16. Conn refs + env vars (deployment settings file JSON: EnvironmentVariables + ConnectionReferences): https://learn.microsoft.com/en-us/power-platform/alm/conn-ref-env-variables-build-tools
17. `pac solution import --settings-file` / `pac solution create-settings`: https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution
18. Pipelines (conexiones y env vars provistas upfront y validadas antes del deploy): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
19. Env vars (valores visibles al importar o al usar Pipelines): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
20. Conn refs + env vars con `pac` / Build Tools (settings file; en Actions se invoca `pac`): https://learn.microsoft.com/en-us/power-platform/alm/conn-ref-env-variables-build-tools
21. Well-Architected (clave distinta por consumidor y por ambiente; no compartir pre/prod): https://learn.microsoft.com/en-us/power-platform/well-architected/security/application-secrets
22. Env vars Secret (Key Vault en el mismo tenant): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
23. Env vars Secret (resource provider Microsoft.PowerPlatform registrado): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
24. Env vars Secret (rol Key Vault Secrets User al SP de Dataverse): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
25. Env vars Secret (app ID de Dataverse 00000007-0000-0000-c000-000000000000): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
26. Env vars Secret (recomendación: modelo Azure RBAC): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
27. Env vars Secret (quien crea/usa la Secret necesita Key Vault Secrets User vía IAM): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
28. Env vars Secret (Key Vault Firewall → permitir IPs de Power Platform): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
29. Env vars Secret (Subscription ID + Resource Group + Vault Name + Secret Name): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
30. Available GitHub Actions (client secret como GitHub Secret): https://learn.microsoft.com/en-us/power-platform/alm/devops-github-available-actions
31. Delegated deployments (conexión Dataverse del SP: client ID y secret): https://learn.microsoft.com/en-us/power-platform/alm/delegated-deployments-setup
32. Authenticate OAuth (el secret se muestra una sola vez): https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth
33. Authenticate OAuth (el secret requiere período de expiración): https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth
34. Workload identity federation (secretos/certificados son riesgo: almacenar, rotar, downtime al expirar): https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation
35. Tutorial OIDC/FIC (autenticar sin client secret; id-token: write): https://learn.microsoft.com/en-us/power-platform/alm/tutorials/github-actions-oidc-fic
36. Workload identity federation (intercambio de token sin gestionar secretos): https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation
37. Authenticate OAuth (application user con key secret o certificado X.509): https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth
38. Env vars Secret (tags AllowedBots/AllowedEnvironments para Copilot Studio): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets
39. Well-Architected (rotación regular + emergencia — SE:07): https://learn.microsoft.com/en-us/power-platform/well-architected/security/application-secrets
40. Well-Architected (retirar/reemplazar seguido; ante fin de vida/compromiso): https://learn.microsoft.com/en-us/power-platform/well-architected/security/application-secrets
41. Well-Architected (ventana de rotación; mitigar con retry): https://learn.microsoft.com/en-us/power-platform/well-architected/security/application-secrets

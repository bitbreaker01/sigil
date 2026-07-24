# Runbook A — Aprovisionamiento de un Ambiente de Sigil

**Documento operativo** (extraído del doc 09 §7 el 2026-07-14 — este runbook es la autoridad del CÓMO; el doc 09 conserva el qué y el porqué)
**Aplica a:** Dev, Test, Prod (las diferencias por ambiente están marcadas)
**Regla de la casa (doc 11 §1 regla 5):** un paso NO está terminado hasta que su verificación está en verde. Cada paso indica su test de conformidad (`CF-*`) o su verificación manual.

**Nota sobre rutas de portal:** los portales de Microsoft cambian de menú con frecuencia; las rutas indicadas son las vigentes al escribir esto. Lo estable son los **comandos pac/Web API y los nombres de settings** — ante un menú movido, buscá el nombre exacto del setting indicado.

---

## Parte 1 — Solo Dev

### A1. Crear el ambiente Dev

**Quién:** admin de Power Platform. **Dónde:** [admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com) → *Manage* → *Environments* → **+ New**.

1. Name: `Dev` · Region: la de la organización · Type: **Sandbox**.
2. **Add a Dataverse data store: Yes.**
3. **Language (¡decisión IRREVERSIBLE!):** el *base language* del ambiente **no se puede cambiar después**. Elegir **English** (nuestros schema/display names son en inglés — doc 12); el español se agrega como language pack en A1.5.
4. Currency: la organizacional. Security group: el grupo de Entra del equipo (recomendado — limita quién ve el ambiente).
5. Crear y esperar el provisioning (minutos).
6. **Habilitar español:** dentro del ambiente → *Settings* → *Product* → **Languages** → marcar **Spanish** → Apply (tarda; correr en segundo plano).

**Prerequisitos:** rol Power Platform admin (o Dynamics 365 admin) + **≥1 GB de capacidad Dataverse disponible** en el tenant (sin eso el provisioning falla sin explicación clara).

**Alternativa CLI** (requiere `pac auth create` previo con una identidad admin):
```bash
pac admin create --name "Dev" --type Sandbox --region <region> \
  --language English --currency <la organizacional> \
  --domain <slug-unico-en-el-tenant> --security-group-id <guid-del-grupo>
```
(La doc de `pac admin create` usa nombres de idioma, no LCID.)

**Verificación:** el ambiente aparece *Ready* en PPAC; `pac org who` contra la URL responde. La suite de conformidad **conecta** (paso A4 mediante): toda corrida posterior re-prueba esto.

---

### A2. Publisher "Sistemas Abiertos Nicaragua" — SOLO EN DEV

> **Jamás crear este publisher a mano en Test/Prod**: viaja dentro de la solución managed. Crearlo allá es en el mejor caso inútil y en el peor un prefijo divergente (doc 09).

**Quién:** developer. **Dónde:** [make.powerapps.com](https://make.powerapps.com) (ambiente Dev) → *Solutions* → pestaña **Publishers** → **+ New publisher**.

1. Display name: `Sistemas Abiertos Nicaragua`
2. Name: **queda el autogenerado `Sistemas_Abiertos_Nicaragua`** (decisión del equipo 2026-07-14, con el publisher ya creado — la identidad operativa de Sigil es el **Prefix**, no el Name; CF-A01 valida contra este Name exacto).
3. Description (opcional): `Sistemas Abiertos - BacCredomatic NIC`.
4. Prefix: `sanic` ← este es el campo que importa: define todos los schema names `sanic_*`.
5. **Choice value prefix:** el portal autogenera un número de 5 dígitos (10000–99999) — **REGISTRALO**: es la semilla de todos los valores de choices. *(Registrado: `15946` — confirmado con captura del portal, 2026-07-14.)*
6. Save.

**Inmediatamente después — el Apéndice A del doc 12 (bloqueante para los flows):**

*Por qué existe:* las opciones de un choice guardan **números** en la base (las etiquetas son decoración), y los cloud flows comparan **por número** ("dispará cuando `status eq 782160004`" — doc 08 §2). Quien construya los flows necesita la tabla escrita "etiqueta → número real". Y como Microsoft NO documenta la aritmética de esos números, **se COPIAN del portal, jamás se calculan**: un número adivinado mal = un flow que nunca dispara, sin error visible.

*Qué hacer:*
1. Del formulario del publisher (arriba), anotar el **Choice value prefix** (5 dígitos). Comprobación cruzada: `CF-A02` lo imprime al correr la suite.
2. Agregar al final de `docs/fase-0/12-convenciones-nomenclatura.md` la sección `## Apéndice A — Valores canónicos de choices` con ese prefix y la tabla de abajo, vacía.
3. **Durante A7**, al crear cada opción de cada choice: el portal muestra su campo **Value** autogenerado (expandir "View more" en el panel de la opción si no se ve) → copiar ese número a su fila. Una fila por opción, los 5 choices completos (~30 filas).

Formato de la tabla (números de EJEMPLO — copiar los reales):

| Choice | Etiqueta lógica | Valor real (copiado del portal) |
|--------|-----------------|--------------------------------|
| sanic_sigil_choice_transactionstatus | Borrador | 782160000 *(ejemplo)* |
| sanic_sigil_choice_transactionstatus | Pendiente de Firma | 782160001 *(ejemplo)* |
| … | … | … |

*Quién la consume:* los 3 flows (F4 — sus Filter rows y switches), los tests de conformidad de choices (A7), y cualquier integración futura.

**Verificación:** `CF-A01` (nombre, prefijo) y `CF-A02` (no es el Default Publisher; **loguea el prefix** — cotejalo con el apéndice). ⚠️ **La suite conecta recién después de A4** (entra con el Service Principal): hasta entonces estos tests se OMITEN con motivo — es lo esperado, no un error. Ejecutar esta verificación al completar A4:
```bash
# Variables de TU TERMINAL (no confundir con las environment variables de Dataverse de A7);
# guardalas en un .env en la raíz (gitignoreado) y cargalas con: set -a; source .env; set +a
export SIGIL_DATAVERSE_URL="https://<org>.crm.dynamics.com"   # PPAC → ambiente → Environment URL
export SIGIL_CLIENT_ID="<application-client-id>"              # del A4a
export SIGIL_CLIENT_SECRET="<client-secret>"                  # del A4a
dotnet test tests/conformance/Sigil.Conformance.Tests --filter "CF_A01|CF_A02" --logger "console;verbosity=detailed"
```

---

### A3. Solución `sigil_core_sigil` — SOLO EN DEV

**Dónde:** make.powerapps.com → *Solutions* → **+ New solution**.

1. Display name: `Sigil | Core | Sigil`
2. Name: `sigil_core_sigil`
3. Publisher: **Sistemas Abiertos Nicaragua (sanic)** ← el paso más fácil de errar: el default es CDS Default Publisher.
4. Save.
5. Regla permanente: **todo componente se crea DESDE ADENTRO de esta solución** (abrir la solución → New → …), jamás desde el área global (caería en la Default Solution con el publisher equivocado).

**Verificación:** `CF-A03` (existe, display name exacto, publisher = `Sistemas_Abiertos_Nicaragua`).

---

## Parte 2 — Todos los ambientes

> **Orden crítico en Test/Prod:** A4 y A5 van **ANTES del primer run del pipeline** — el despliegue pide las conexiones durante el import; si no existen, los flows llegan apagados (doc 09 §7).

### A4. App registration + Service Principal (la identidad del motor)

**Quién:** admin de Entra + admin de Power Platform. **Amenaza asociada: A13 (doc 07)** — esta credencial atraviesa todas las capas internas; su gobernanza no es opcional.

**4a. App registration (una sola para todos los ambientes):**
1. [portal.azure.com](https://portal.azure.com) → *Microsoft Entra ID* → *App registrations* → **+ New registration**.
2. Name: `Sigil Service`. Single tenant. Sin redirect URI. Register.
3. Anotar **Application (client) ID** y **Directory (tenant) ID**.
4. Credenciales — dos, con propósitos distintos:
   - **Certificado** (la credencial operativa — doc 09 §10): *Certificates & secrets* → *Certificates* → upload del certificado emitido por el proceso de la organización. Vencimiento calendarizado con alerta a 30 días.
   - **Client secret** (solo para el runner de conformidad y desarrollo): *Certificates & secrets* → **+ New client secret**, expiración 180 días. Copiar el valor YA (no se vuelve a mostrar). Va a los secrets del environment `dev` de GitHub (`SIGIL_CLIENT_ID`, `SIGIL_CLIENT_SECRET`, `SIGIL_DATAVERSE_URL`) y a las variables locales del runner — **jamás al repo**. **Rotación: cada 180 días, calendarizada** (fila agregada al calendario del doc 09 §10 — un secret vencido = la suite de conformidad deja de conectar en silencio).
5. **Sin permisos de API de Dataverse en el app registration** (no hace falta `user_impersonation` para S2S): el acceso lo da el application user del paso 4b.

**4b. Application user en el ambiente:**
1. PPAC → *Manage* → *Environments* → Dev → *Settings* → *Users + permissions* → **Application users** → **+ New app user**.
2. *+ Add an app* → buscar `Sigil Service` → Add. Business unit: la raíz. 
3. Security roles: asignar **Sigil | SR | Service** — ⚠️ el rol existe recién después de A7 (Dev) o del primer import (Test/Prod). **En Dev, mientras tanto: asignar el rol estándar `System Customizer`** — un app user sin NINGÚN rol no puede leer nada y la suite de conformidad conectaría pero fallaría por privilegios; System Customizer le da la lectura de publisher/solución/metadata/definiciones que la suite necesita. Al terminar A7: **SUMAR** `Sigil | SR | Service` — ⚠️ **sin quitarle System Customizer en Dev, jamás**: la suite de conformidad lee publisher/solución/perfiles con esos privilegios (quitarlo pone en rojo CF-A01/A02/A03/A08/A10 con "missing prvRead..."). **Y para registrar plugins/packages vía SDK o CLI (el spike, `pac plugin push`), System Customizer NO alcanza** (hallazgo verificado: falta `prvCreatePluginAssembly`) — en **Dev**, sumar también **System Administrator** al app user (aceptable: es el ambiente de desarrollo, y en Test/Prod el SP de despliegue igualmente requiere SysAdmin en los targets según los prerequisitos de pipelines — Runbook A §A13.3). En Test/Prod el app user de Sigil lleva solo los roles del diseño.

**Alternativa CLI (asignación de rol):**
```bash
pac admin assign-user --environment <env-id> --user <application-client-id> --role "Sigil | SR | Service" --application-user
```

**4c. Perfil de column security:** make.powerapps.com → solución → **Column security profiles** (o PPAC → environment → *Users + permissions* → *Column security profiles*) → `Sigil | FLS | Evidence Writer` → *Users* → agregar el application user. (La **membresía NO viaja en la solución** — verificado; este sub-paso se repite en CADA ambiente.)

**Verificación:** la suite de conformidad **conecta usando esta identidad** (toda corrida verde re-prueba el paso) + `CF-A07`/`CF-A08` (rol y perfil existen). La membresía del perfil se verifica con el test `CF-A10` (a escribir antes de ejecutar 4c — regla 5).

---

### A5. Cuenta de servicio de notificaciones + ritual de conexiones

**Quién:** admin de M365 + operador designado. **Contexto:** los conectores Outlook/Teams exigen OAuth **interactivo** de una cuenta real (verificado — doc 08 §6); por eso existe este ritual.

1. **Crear la cuenta:** [admin.microsoft.com](https://admin.microsoft.com) → *Users* → *Active users* → **Add a user**: `sigil-notifications@<dominio>`, display "Sigil Notifications". Licencia: buzón Exchange + Teams **+ Power Automate Premium en esta cuenta** (DECIDIDO 2026-07-14: los flows tendrán owner = esta cuenta de servicio — gate 3 del Runbook B; el conector Dataverse es premium y el Power Automate incluido en Office 365 NO alcanza — bloqueador verificado). **Comprar la licencia antes de F4** — sin ella, los flows se suspenden y el canal de notificaciones muere en silencio (doc 08 §7).
2. **Protección (doc 08 §6):** MFA obligatoria + política de Conditional Access restringida (solo desde ubicación/dispositivo administrado de TI). **NO bloquear el sign-in interactivo** — rompería las conexiones.
3. **Habilitar la app Power Automate en Teams** (una vez por tenant): Teams admin center → *Teams apps* → *Manage apps* → buscar "Power Automate" → Allowed.
4. **Ritual de conexiones** (sesión interactiva controlada del operador, EN CADA AMBIENTE):
   a. [make.powerautomate.com](https://make.powerautomate.com) → seleccionar el ambiente → *Connections* → **+ New connection**.
   b. Crear: **Office 365 Outlook** y **Microsoft Teams** — iniciando sesión como `sigil-notifications@…`.
   c. Crear la conexión **Microsoft Dataverse** con el **Service Principal** (Connect with service principal → client id + tenant + secret/cert).
   d. **Compartir las tres conexiones con los makers/operadores que SOLICITAN despliegues** (corrección verificada: en pipelines, las conexiones para las connection references las provee quien solicita el run — el SP delegado solo ejecuta el import; compartir una conexión OAuth "con un SP" ni siquiera es un flujo soportado). Sin esto, el solicitante no puede asociarlas durante el despliegue.
5. Configurar la **regla de reenvío** del buzón `sigil-notifications@` al buzón del equipo operador (el mecanismo de alertas del doc 08 §7).

**Verificación:** las 3 conexiones en estado *Connected* en el ambiente; test `CF-A11` (a escribir: connection references de la solución con conexión asociada — verificable vía tabla `connectionreference`); el reenvío probado con un correo manual.

---

### A6. CSP de Code Apps del ambiente (sin esto, el visor PDF NO funciona — doc 05 §6.1)

**Quién:** admin de Power Platform. Settings verificados: `PowerApps_CSPEnabledCodeApps`, `PowerApps_CSPConfigCodeApps`, `PowerApps_CSPReportingEndpoint`. Ruta vigente: PPAC → environment → *Settings* → *Product* → *Privacy + Security* → *Content security policy* → pestaña **App**.

**Premisas correctas (corregidas por el antagonista contra la doc oficial):**
- La CSP de code apps **ya viene ENFORCED por defecto** (enforcement general desde ene-2026) con `worker-src 'none'` y `connect-src 'none'` — o sea, el visor PDF está roto DESDE EL DÍA CERO hasta ejecutar este paso. "Modo reporting" no es un paso previo inocuo: significa **apagar el enforcement** (bajar la seguridad del ambiente) — aceptable brevemente en Dev para diagnosticar, **jamás en Prod**.
- Regla real de las directivas custom: se **AGREGAN** (append) al default; **solo cuando el default es `'none'` lo reemplazan**. Para nuestras dos directivas el resultado es el buscado (ambas parten de `'none'`), pero NO generalizar esta regla a `script-src`/`img-src`.
- Vía REST/PowerShell, actualizar directivas **reemplaza la colección completa**: siempre leer → modificar → escribir.

**Procedimiento:**
1. En la pestaña App, agregar **TRES** directivas:
   - `worker-src` = **`'self' blob:`** (el worker de pdf.js se inyecta **inline como blob** — `?worker&inline` en el frontend, F3; sin `blob:` el visor NO arranca).
   - `child-src` = **`'self' blob:`** ← **indispensable para Safari** (ver ⚠️ abajo).
   - `connect-src` = `'self'` (cMaps/fonts/wasm para PDFs escaneados y CJK).

   Enforcement queda ACTIVO.
   > ⚠️ **`child-src` es obligatorio (corrección 2026-07-22):** **Safari NO soporta `worker-src`** y cae en cascada a `child-src` (y el default lo trae en `'none'`). Sin `child-src 'self' blob:`, en Safari/iOS **no carga ningún PDF** aunque `worker-src` esté bien — verificado en producción en un tenant con usuarios de iPhone. Chrome/Edge sí leen `worker-src`, por eso el bug es intermitente por navegador/tenant.
   > ⚠️ *(Corrección previa 2026-07-21: este paso decía `worker-src 'self'` sin `blob:`; el worker inline de F3 requiere `blob:`.)*
2. Validar con la app real cuando exista (gate 5 del Runbook B: PDF normal + escaneado + CJK, cero violaciones en consola).
3. Si hay que diagnosticar violaciones inesperadas: reporting endpoint configurado + apagar enforcement SOLO en Dev, el tiempo mínimo, y re-encender.

**Verificación:** gate 5 del Runbook B (visor renderizando PDF normal + escaneado JBIG2/JPX + CJK con CSP enforced). En F1, antes de que exista la app: registrar la configuración aplicada + `CF-A12` si el setting es legible por API (a investigar al escribir el test — si no es legible, la verificación es el gate 5 y se documenta el límite).

---

### A7. Modelo de datos — SOLO EN DEV (en Test/Prod viaja en la solución)

**Quién:** developer, guiado por el doc 03 (la especificación completa). **Regla:** cada tabla se construye Y su test de conformidad de columnas se escribe/pasa — tabla sin test verde = tabla no terminada.

Orden dentro de la solución `sigil_core_sigil`:
1. **Choices globales primero** (doc 03 §3): las 5 (`transactionstatus` 9 valores, `participantstatus` 4, `routingtype` 2, `tsastatus` 3, `eventtype` 12). **Registrar cada valor real en el apéndice del doc 12** (A2).
   ⚠️ **El campo Name del choice se autogenera desde el display name** (mismo gotcha del publisher): **sobrescribirlo a mano** con `sigil_choice_<nombre>` (el portal antepone `sanic_`). El Name NO se puede editar después; si nace mal: crear el choice correcto, copiar sus valores NUEVOS al apéndice, recrear las columnas que lo usaban y borrar el equivocado (CF-A16/CF-A17 cazan ambos extremos).
2. **Tablas** en orden de dependencias: `transaction` → `participant` → `signaturezone` → `mastersignature` → `ledgerentry` → `event` — cada una con sus columnas EXACTAS del doc 03 §4 (tipos, tamaños, required/opcional, File columns con su MaxSizeInKB).
   ⚠️ **El "Record ownership" se elige AL CREAR y es IRREVERSIBLE** (está en *+ New table → Advanced options*): `ledgerentry` = **Organization** (doc 03 §4.4); las demás = User/Team. Si quedó mal (CF-A05 lo caza): borrar la tabla y recrearla — con datos adentro ya no es borrar, es migrar. Costo de equivocarse hoy: 10 minutos; en la semana 6: un incidente.
3. **Relaciones y cascadas** según doc 03 §2 (¡las cascadas exactas importan: Delete Restrict del ledger y eventos!).
4. **Alternate key**: `sanic_sigil_tbl_ledgerentry` sobre `sanic_sigil_transactionid`. Esperar a que el índice pase a **Active**.
5. **Roles**: `Sigil | SR | User` (solo Read según doc 03 §6) y `Sigil | SR | Service` (CRUD org + Callback Registration CRUD user-level + Read `systemuser`/`usersettings`). Crear DESDE la solución.
6. **Perfil de column security** `Sigil | FLS | Evidence Writer` + asegurar las 4 columnas del ledger (doc 03 §5) → volver a A4c para la membresía.
7. **Variables de entorno**: las **9** definiciones de la tabla canónica del doc 03 §8 (exactamente las 9 de `CF-A09`) — **sin current value en la solución** (los valores se setean por ambiente — doc 09 §5 paso 2).

**Verificación:** `CF-A04` (6 tablas), `CF-A05` (ledger org-owned), `CF-A06` (alternate key ACTIVE), `CF-A07`–`CF-A09` + los tests de columnas/choices por tabla (se escriben durante este paso, uno por tabla, antes de crearla — TDD de infraestructura).

---

### A8. Auditoría del ambiente

1. PPAC → environment → *Settings* → *Audit and logs* → **Audit settings**: marcar **Start auditing** (+ *Log access* si la política lo pide).
2. Retención: según política de la organización (el default "Forever" consume Log — decidir y registrar).
3. El `IsAuditEnabled` de cada tabla viaja con la solución (verificado — doc 09): no se reconfigura a mano.

**Verificación:** `CF-A13` (a escribir: `organization.isauditenabled == true` — legible por query a la tabla `organization`).

---

### A9. DLP

1. PPAC → *Policies* → *Data policies*: confirmar qué política cubre el ambiente.
2. En esa política, **Dataverse, Office 365 Outlook y Microsoft Teams** deben estar juntos en el grupo Business — y nada más de lo necesario para Sigil.

**Verificación:** manual (las policies no son consultables desde la suite con el SP estándar): captura de la política + registro en el checklist. Prueba práctica: el primer flow de F4 se activa sin error de DLP.

---

### A10. Usuarios finales por grupo de Entra

1. Entra → crear grupo de seguridad `sigil-users` con los empleados usuarios de Sigil.
2. PPAC → environment → *Settings* → *Users + permissions* → **Teams** → **+ Create team**: tipo **Entra ID Security group**, vincular `sigil-users`, y asignarle el rol **Sigil | SR | User**.
3. ⚠️ **En Test/Prod este paso va DESPUÉS del primer import** — el rol `Sigil | SR | User` llega con la solución managed (misma regla que A4.3).

**Verificación:** `CF-A14` (a escribir: existe el team vinculado al grupo con el rol correcto — tablas `team`/`teamroles`).

---

### A11. Backups (política — doc 09 §7.10)

1. Prod: los backups automáticos de plataforma vienen incluidos; verificar en PPAC → environment → *Backups* que existen restore points.
2. Rutina semanal del operador: confirmar el último restore point (calendario del doc 09 §10).
3. **Regla de uso del restore:** SOLO bajo el procedimiento de rollback (doc 09 §5) — un restore retrocede el ledger (doc 07 A15/A16).

### A12. Datos semilla (Dev y Test)

1. Usuarios de prueba licenciados (mínimo 3 — para enrutamiento secuencial de 3 firmantes) en el grupo `sigil-users`.
2. **Exclusión de Conditional Access SOLO-DEV** para las cuentas de prueba de Playwright (doc 11 §3) — jamás en Test/Prod.
3. Firma Maestra configurada para cada usuario de prueba (vía la app cuando exista; en F1/F2, vía la Custom API). ⚠️ **Requiere la solución desplegada** — en Test, este sub-paso va después del primer import.
4. Transacciones de ejemplo para UAT (Test).

**Verificación:** `CF-A15` (a escribir: los usuarios semilla existen, habilitados, con el rol vía team).

---

### A13. Infraestructura de Power Platform Pipelines (se ejecuta en F4 — doc 10; prerequisitos verificados)

Sin runbook detallado todavía (se escribe en F4, con su antagonista); los **prerequisitos duros** quedan registrados YA porque condicionan decisiones de A4:

1. **Pipelines host**: un ambiente production dedicado donde vive la app de Pipelines.
2. **Los ambientes destino (Test/Prod) deben ser Managed Environments** (conversión automática por la plataforma) — con el requisito de licencias premium que eso implica.
3. **SP de delegated deployments**: application user en el host (rol **Deployment Pipeline Administrator**) y en cada target (rol **System Administrator** — roles menores no despliegan plugins). **Decisión a registrar acá cuando se ejecute:** ¿se reutiliza `Sigil Service` o se crea un SP dedicado al despliegue? (Separarlos = menor blast radius; recomendación preliminar: SP dedicado.)
4. Quien configura el stage con delegated deployment debe ser **owner de la Enterprise Application** del SP.
5. Aprobaciones de dos niveles (doc 09 §5) configuradas en los stages.

---

## Resumen de orden por ambiente

| Paso | Dev | Test/Prod |
|------|-----|-----------|
| A1 ambiente | 1º | 1º |
| A2 publisher + apéndice choices | 2º | ❌ JAMÁS (viaja managed) |
| A3 solución | 3º | ❌ (viaja managed) |
| A4 app registration/app user/perfil | 4º (rol al terminar A7; perfil A4c tras A7.6) | **2º — ANTES del primer pipeline** (rol/perfil tras el primer import, antes de gates) |
| A5 cuenta de servicio + conexiones (+ licencia premium de flows) | 5º | **3º — ANTES del primer pipeline** |
| A6 CSP | 6º | 4º |
| A7 modelo de datos | 7º | ❌ (viaja managed) |
| A8 auditoría · A9 DLP · A11 backups | 8º | 5º |
| A13 infraestructura de pipelines | — (se hace una vez, en F4) | 6º (host + managed envs + SP delegado) |
| Primer despliegue del pipeline | — | 7º → **Runbook B (gates)** |
| A10 usuarios por grupo (rol) · A12 semilla (Test) | 9º (Dev: tras A7) | **8º — DESPUÉS del primer import** (el rol y las APIs llegan con la solución) |

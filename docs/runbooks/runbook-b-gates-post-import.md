# Runbook B — Gates Post-Import (cada despliegue, antes de habilitar tráfico)

**Documento operativo** (extraído del doc 09 §8 el 2026-07-14 — autoridad del CÓMO; el doc 09 conserva el qué y el porqué)
**Cuándo:** después de CADA run del pipeline (Dev→Test, Test→Prod) y antes de declarar el ambiente utilizable. **Hotfix:** gates 1–4 + 9 (doc 09 §5).
**Formato de cada gate:** Cómo verificar → Criterio de éxito → Si falla. El resultado de los 10 gates se archiva en el registro del despliegue (fecha, versión de solución, quién, evidencia).

---

## Gate 1 — Alternate keys ACTIVOS

**Por qué:** el índice se crea **asíncronamente** al importar; hasta Active, la idempotencia del ledger no existe (docs 04 §7, 07 §2).

**Cómo:**
```bash
# Vía suite de conformidad (preferido):
export SIGIL_DATAVERSE_URL=... SIGIL_CLIENT_ID=... SIGIL_CLIENT_SECRET=...
dotnet test tests/conformance/Sigil.Conformance.Tests --filter "CF_A06"
```
Manual (fallback): make.powerapps.com → solución → `sanic_sigil_tbl_ledgerentry` → **Keys** → columna Status; o Web API:
```
GET {url}/api/data/v9.2/EntityDefinitions(LogicalName='sanic_sigil_tbl_ledgerentry')/Keys
   → cada key con "EntityKeyIndexStatus": "Active"
     (el Web API serializa el enum como STRING — "Active" es el valor 2; si ves texto, es lo esperado)
```
**Éxito:** `CF-A06` verde (key sobre `sanic_sigil_transactionid`, estado Active).
**Si falla:** estado *Pending/InProgress* → esperar y reintentar (minutos). Estado *Failed* → ejecutar `ReactivateEntityKey`:
```
POST {url}/api/data/v9.2/ReactivateEntityKey
{ "EntityLogicalName": "sanic_sigil_tbl_ledgerentry", "EntityKeyLogicalName": "<nombre lógico del key>" }
```
y volver a esperar Active. No avanzar sin esto.

## Gate 2 — Plugin steps presentes y activos

**Por qué:** con pipelines los steps llegan con el estado que tenían en Dev — se **verifica**, no se activa (`--activate-plugins` es del plan B con `pac solution import`).

**Cómo:** consulta a `sdkmessageprocessingstep` (suite `CF-B02`, a escribir en F2 cuando existan los steps — hasta entonces, manual):
- **Web API (funciona desde cualquier estación — preferido):**
  ```
  GET {url}/api/data/v9.2/sdkmessageprocessingsteps?$select=name,statecode,mode,stage
      &$filter=contains(name,'sanic_sigil')
     → inventario contra el catálogo del doc 04 §3.1; cada step con statecode = 0 (Enabled)
  ```
- Plugin Registration Tool (`pac tool prt` — **solo Windows**): assembly `Sigil.Plugins` → un step por Custom API + el **step asíncrono del worker** (Update de `sanic_sigil_tbl_transaction`, filtering attribute `sanic_sigil_status`, post-operation, async).

**Éxito:** inventario completo y habilitado. **Si falla:** step faltante = el package no viajó completo (revisar asociación a la solución — Runbook A / doc 09 §4); step deshabilitado = habilitarlo desde PRT y registrar por qué llegó así.

## Gate 3 — Connection references, flows encendidos, ownership reconciliado

**Cómo:**
1. make.powerapps.com → solución → **Cloud flows**: los 3 flows (doc 08 §3) en estado **On**.
2. Cada **connection reference** de la solución con conexión asociada (solución → Connection references → columna Connection).
3. **Ownership — DECIDIDO (2026-07-14, decisión del equipo): owner = cuenta de servicio `sigil-notifications@`**, con licencia **Power Automate Premium** en esa cuenta (una licencia cubre los flows que posee — Runbook A §A5.1). Procedimiento en cada despliegue: el delegated deployment deja los flows con owner SP → **reasignar el owner a la cuenta de servicio** (detalles del flow → Edit owners) como paso de este gate. Coherente con el monitoreo del doc 08 §7 (las alertas nativas van al buzón del owner → el reenvío de A5.5 las hace visibles).

**Éxito:** 3/3 On, referencias asociadas, decisión de ownership registrada. **Si falla:** flow Off por conexión faltante → el ritual A5 no se completó ANTES del pipeline (ejecutarlo y re-asociar: editar el flow → aceptar el mapeo de conexiones → encender).

## Gate 4 — Variables de entorno con los valores CORRECTOS del ambiente

**Por qué:** los current values presentes en el zip viajan EN SILENCIO (verificado — doc 09 §5); "que haya valor" no alcanza — tiene que ser EL valor de la tabla doc 09 §6.

**Cómo:**
1. Suite: `CF-B04` (a escribir antes del primer despliegue: lee `environmentvariablevalue` + `environmentvariabledefinition` y compara contra la tabla esperada del ambiente — parametrizada por `SIGIL_EXPECTED_ENV`, `dev|test|prod`).
2. Manual: solución → Environment variables → revisar **Current value** una por una contra doc 09 §6. Ejemplos de lo que NO puede aparecer en Prod: `TsaEnabled=false`, endpoints FreeTSA.
3. **`env_AppPlayUrl` (primer despliegue):** make.powerapps.com → *Apps* → Sigil → *Details* → copiar el **Web link**. (Ruta documentada para canvas apps; para code apps, si el Details no lo muestra: componer `https://apps.powerapps.com/play/e/{environmentId}/a/{appId}` con el appId leído por API o el que imprime `pac code push` en Dev.) Pegarlo como current value de la variable EN ESTE AMBIENTE (config del ambiente, no un re-deploy — doc 09 §6).

**Éxito:** todas las variables = tabla del ambiente. **Si falla:** corregir el current value en el ambiente destino Y ejecutar la higiene "Remove from this solution" sobre esa variable en Dev antes del próximo export.

## Gate 5 — CSP verificada con la app real

**Cómo:** en el ambiente destino, abrir la app y probar con los **fixtures del repo** (`tests/fixtures/pdf/` — *a crear en F2 a partir de los PDFs del spike + los casos de M9*):
1. PDF normal ≥10 MB → el visor renderiza (worker funcionando).
2. PDF escaneado (JBIG2/JPX) → renderiza (los `fetch` auxiliares pasan `connect-src 'self'`).
3. PDF con fuentes CJK → renderiza.
4. DevTools → Console: **cero violaciones CSP** (`Refused to ...`).

**Éxito:** 3 PDFs renderizando sin violaciones. **Si falla:** revisar la config del A6 (¿reporting vs enforced? ¿directivas exactas `worker-src 'self'` / `connect-src 'self'`?); si la CSP es correcta y el worker no arranca → plan B fake worker (doc 05 §6.1) y registrar el hallazgo.

## Gate 6 — Deep link completo desde Teams

**Cómo (dispositivo móvil SIN sesión previa — el caso del QR):**
1. Generar una notificación real (transacción de prueba → turno activo) o enviarse la card manualmente con el link `{AppPlayUrl}?screen=sign&txId=<guid-de-prueba>`.
2. En el móvil (sesión cerrada): tocar el botón de la card → login de Entra → **verificar que la app aterriza en la pantalla de firma de ESA transacción** (no en el dashboard).

**Éxito:** aterrizaje correcto post-login. **Si falla:** confirmar que los query params se pierden en el redirect (comparar URL final vs enviada) → aplicar la mitigación del emisor (param único compacto — doc 05 §3); si aún así se pierden → defecto de plataforma: abrir caso a Microsoft y registrar workaround.

## Gate 7 — Descargas desde el iframe

**Cómo:**
1. Desktop (Edge/Chrome): en Detalle de una transacción completada → descargar PDF final → el archivo baja y su SHA-256 coincide con el esperado (`sha256sum`).
2. En Verificar → descargar token TSA → llega como `SIGIL-<numero>.tsr` y `openssl ts -reply -in <archivo>.tsr -text` lo parsea.
3. Safari iOS: repetir la descarga del PDF (comportamiento esperado: tab nueva o share sheet — documentar cuál).

**Éxito:** ambos artefactos descargables en desktop + comportamiento iOS documentado. **Si falla** (bloqueo del sandbox del iframe): fallback "abrir en nueva pestaña" (doc 05 §11) y registrar.

## Gate 8 — TSA alcanzable desde el sandbox

**Cómo:**
1. Con `env_TsaEnabled=true` y los endpoints del ambiente: ejecutar un sellado real (la transacción SMOKE del gate 9 sirve).
2. Verificar en el ledger: `sanic_sigil_tsastatus = Sellado con TSA` y el trace del worker con la duración del paso 6.
3. Si el primario falla y el fallback responde: también es éxito — registrar cuál respondió (el finding del spike: DigiCert bloqueado desde algunas redes).

**Éxito:** token obtenido y validado desde el sandbox. **Si falla** (todos los endpoints): el sellado queda `Re-sellado pendiente` (comportamiento correcto — ADR-005); investigar la salida de red del sandbox, probar endpoints alternativos, registrar. El gate NO bloquea el despliegue si la degradación funciona como diseñada — bloquea si el pipeline se rompe en vez de degradar.

## Gate 9 — Smoke E2E (el propósito del sistema, de punta a punta)

**Protocolo (en Prod: transacción `SMOKE-{yyyy-MM-dd}` entre CUENTAS DE OPERADOR — las notificaciones reales les llegan a ellos; no se borra jamás, R6):**
1. Operador A crea la transacción `SMOKE-...` con un PDF de prueba, operadores B y C como firmantes (secuencial), zonas definidas.
2. Enviar → B recibe card/correo → firma vía deep link → C recibe turno → firma.
3. Esperar el sellado (< 2 min) → estado Completado, notificación final recibida.
4. Descargar el PDF final → verificar visualmente: firmas en sus zonas, hoja de cierre completa (estampas, hash de contenido, número de ledger, QR).
5. Escanear el QR con un móvil → constancia correcta (firmantes, nivel de evidencia, hash final visible).
6. Drag & drop del PDF descargado → **Verde**.
7. Alterar UN byte de una copia y verificar:
   ```bash
   cp SMOKE.pdf SMOKE-alterado.pdf && printf '\x00' >> SMOKE-alterado.pdf
   ```
   Drag & drop del alterado → **Rojo**.
8. Verificación manual independiente: `sha256sum SMOKE.pdf` == hash final mostrado en la constancia.
9. Archivar en el registro del despliegue: número de ledger, hashes, capturas del Verde y el Rojo.

**Éxito:** los 9 pasos. **Si falla cualquiera:** NO habilitar tráfico; diagnóstico con el ID de correlación + trace; el despliegue queda en estado "gates fallidos" hasta fix-forward.

## Gate 10 — Heartbeat del job diario

**Cómo:** esperar la primera corrida programada (o ejecutar el flow `Sigil | Cloud Flow | Jobs - Daily` manualmente: make.powerautomate.com → flow → *Run*). Verificar que el correo de resumen ("job OK: X expiradas, Y recordatorios, Z re-sellados") llega **al buzón del equipo operador** (vía el reenvío de A5.5).

**Éxito:** heartbeat recibido donde alguien lo lee. **Si falla:** revisar la corrida del flow (historial de runs), la conexión Outlook y la regla de reenvío — este gate es el detector del "canal muerto" (doc 08 §7); si el heartbeat no llega hoy, ninguna notificación llegará mañana.

---

## Cierre del despliegue

Con los 10 gates en verde: registrar el despliegue (versión de solución, fecha, aprobadores, evidencia de gates, número de ledger del SMOKE) en `docs/despliegues/` (*carpeta que nace con el primer despliegue*) y habilitar el tráfico (asignación del grupo de usuarios / anuncio). Con CUALQUIER gate en rojo: el ambiente NO se declara utilizable — fix-forward (doc 09 §5).

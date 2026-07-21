# Sigil — F3 Cierre 03: Gates 6 y 7 (deep link desde Teams + descargas desde el iframe)

**Documento operativo** (cierre de F3 — cómo ejecutar y pasar los gates 6 y 7, adelantados a Dev)
**Estado:** Parcial — gate 6 tocado (el link de verify aterrizó bien en una prueba); falta el caso canónico y el gate 7
**Depende de:** [Runbook B gates 6/7](../runbooks/runbook-b-gates-post-import.md) (autoridad canónica), [doc 05 §3/§5/§11](../fase-0/05-frontend-code-app.md), [doc 10 §3](../fase-0/10-hoja-de-ruta.md) (por qué se adelantan a F3)
**Leyenda:** 🧑 **acción tuya** · 📱 en el dispositivo · ✅ éxito · 🔧 si falla

> Estos son **gates del Runbook B**; el doc 10 §3 los **adelanta a F3** porque son el mayor riesgo frontend — se validan en Dev antes de construir encima. Este doc es la guía de ejecución en F3; el Runbook B queda como autoridad canónica. Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final**; lo no confirmable, **NO VERIFICADO**.

---

## Gate 6 — Deep link completo desde Teams (móvil SIN sesión previa)

### Qué prueba
Que un usuario que toca el link de una notificación en el celu, **sin sesión abierta**, tras el login de Entra **aterrice en la pantalla de firma de ESA transacción** — no en el dashboard. Es el caso del QR / la card de Teams (Runbook B gate 6).

### Por qué es riesgoso
Los **query params** (`?screen=sign&txId=…`) pueden **perderse en el redirect de Entra**, y —lo que ya flagueamos— **no está confirmado** que el player hosteado reenvíe esos params al `getContext()` del code app (doc 05 §3/§11).

### 🧑 Cómo ejecutarlo
> No necesitás los flows de F4 para esto: podés **enviarte la card/link a mano** (Runbook B gate 6).
1. Armá el link con una tx de prueba real: `{env_AppPlayUrl}?screen=sign&txId=<guid-de-una-tx-en-turno>`.
2. Mandátelo por Teams (card) o como link simple.
3. 📱 En el celu, **cerrá la sesión** de la app / usá una ventana privada.
4. Tocá el link → login de Entra → observá **dónde aterriza**.

**✅ Éxito:** abre **directo en la pantalla de firma** de esa tx.
**🔧 Si falla (cae en dashboard):**
1. Comparar la **URL final vs la enviada** (¿sobrevivieron los params al redirect?).
2. Con remote debugging (doc 02 §2.3), inspeccionar qué trae `getContext().app.queryParams` en el player.
3. Si los params se pierden en el redirect → aplicar la mitigación del emisor: **un solo param compacto** (doc 05 §3).
4. Si el player **no** los reenvía a `getContext` → es defecto/limitación de plataforma: abrir caso a Microsoft y registrar el workaround (Runbook B gate 6).

### Estado actual (declarado)
- **Parcial:** el fix de routing de la app ya aplica el deep link cuando el contexto está listo, y una prueba con un link de **verify** aterrizó bien. **Falta** el caso canónico: **card de Teams, móvil, sin sesión, pantalla de firma**. Y sigue **NO VERIFICADO** si el player reenvía los params al `getContext` en el caso general.

---

## Gate 7 — Descargas desde el iframe

### Qué prueba
Que desde la app (que corre **dentro de un iframe** en apps.powerapps.com) se puedan **descargar** los dos artefactos probatorios, y que en móvil el comportamiento sea usable.

### Por qué es riesgoso
El **sandbox** del iframe puede bloquear descargas, y iOS Safari maneja los blob URLs distinto. Mecánica de Sigil (doc 05 §5.3): base64 → `Blob` → `URL.createObjectURL()` → `<a download>` → `revokeObjectURL()` [D2].

### 🧑 Cómo ejecutarlo
**Desktop (Edge/Chrome):**
1. Detalle de una tx **completada** → **descargar el PDF final** → el archivo baja y su hash coincide:
   ```bash
   sha256sum <descargado>.pdf   # == hash final mostrado en la constancia
   ```
2. Verificar → **descargar el token TSA** → llega como `SIGIL-<numero>.tsr` (MIME `application/timestamp-reply`) y `openssl ts -reply -in <archivo>.tsr -text` lo parsea (Runbook B gate 7).

**📱 Safari iOS:**
3. Repetir la descarga del PDF y **documentar el comportamiento** (tab nueva / preview / Files / share sheet).

**✅ Éxito:** ambos artefactos descargables en desktop (hash correcto) + comportamiento iOS **documentado**.

**🔧 Si falla (bloqueo del sandbox):**
1. Inspeccionar el atributo **`sandbox`** del iframe del player: las descargas en un iframe con `sandbox` requieren el token **`allow-downloads`** [D1]. Si el iframe tiene `sandbox` sin ese token, la descarga se bloquea.
2. Recordá que `<a download>` solo aplica a **same-origin o esquemas `blob:`/`data:`** [D3] — Sigil usa `blob:`, que está permitido.
3. Fallback documentado (doc 05 §11): **"abrir en nueva pestaña"** en vez de descarga directa; registrar el hallazgo.
4. Ojo el **timing de `revokeObjectURL()`**: revocar demasiado pronto puede romper la descarga (best-practice conocida, no citada aquí) — asegurar que la descarga arrancó antes de revocar. (La mecánica `createObjectURL`/`revokeObjectURL` está en [D2].)

### Estado actual (declarado)
- **Pendiente:** no verificado sistemáticamente en desktop ni iOS.

---

## Registro y salida

- Ambos gates archivados en el registro de cierre de F3 (fecha, dispositivo/navegador, evidencia: capturas, hash del PDF, salida de `openssl ts`).
- **Salida:** gate 6 ✅ (aterrizaje en firma desde card en móvil sin sesión) + gate 7 ✅ (descargas desktop con hash correcto + comportamiento iOS documentado).
- Estos son la versión **F3/Dev** de los gates; se **repiten** en cada despliegue (Test/Prod) vía Runbook B.

---

## NO VERIFICADO (declarado)
- **Reenvío de query params por el player al `getContext`** en el caso general (gate 6) — es lo que el propio gate mide; si se pierde, workaround del emisor o caso a Microsoft.
- **Comportamiento exacto de descarga en iOS Safari** (gate 7) — ninguna fuente autoritativa lo documenta (WebKit anunció `download` solo para macOS [D4]); medir en dispositivo.
- **Si el iframe del player setea `sandbox` y si incluye `allow-downloads`** — inspeccionar en vivo [D1].

## Fuentes

Verificadas contra documentación autoritativa el 2026-07-20.

- D1. Descargas en iframe con `sandbox` requieren `allow-downloads`: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe
- D2. `URL.createObjectURL()` / `revokeObjectURL()` (no revocar antes de tiempo): https://developer.mozilla.org/en-US/docs/Web/API/URL/createObjectURL_static
- D3. `<a download>` solo same-origin o esquemas `blob:`/`data:`: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/a
- D4. WebKit `download` (Safari 10.1) — scope macOS, silencio sobre iOS: https://webkit.org/blog/7477/new-web-features-in-safari-10-1/
- Runbook B (definición canónica de los gates 6 y 7): ../runbooks/runbook-b-gates-post-import.md

# Sigil — F3 Cierre 02: Matriz de dispositivos móviles reales

**Documento operativo** (cierre de F3 — qué tenés que hacer para barrer la matriz móvil)
**Estado:** ✅ Hecho (2026-07-21) — probado en Safari iOS + Chrome Android reales
**Depende de:** [doc 05 §8](../fase-0/05-frontend-code-app.md) (matriz obligatoria), [doc 05 §5](../fase-0/05-frontend-code-app.md) (binarios/descargas), Runbook B gates 5/6/7
**Leyenda:** 🧑 **acción tuya** · 📱 en el dispositivo · ✅ criterio de éxito

> Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final**. Lo no confirmable se marca **NO VERIFICADO**.

---

## 1. Objetivo y por qué dispositivos REALES

Móvil es **primera clase** en Sigil (RF-23) y el flujo crítico es **firmar** (doc 05 §8). La emulación de Playwright (doc 01) simula viewport/UA [E1], pero **no** el motor real ni el hardware: el `decode` de 27 MB en un procesador de gama baja, cómo Safari iOS maneja una descarga, o una violación de CSP real **solo se ven en el dispositivo**. Esta es una **pasada manual**, guiada por checklist (doc 11 §2 la define como manual con cuentas reales).

## 2. 🧑 Preparación

### 2.1 Dispositivos objetivo (mínimo)
- **Safari en iOS** (iPhone real).
- **Chrome en Android** (idealmente **uno de gama baja/media** — es donde el decode de 27 MB duele).
- (El **mobile player NO soporta code apps** — todo por **navegador**, verificado — doc 05 §8.)

### 2.2 Cuentas y datos
- Cuentas reales con **Firma Maestra** configurada (las semilla de Runbook A §A12 sirven).
- Al menos una transacción **en turno** para el usuario de prueba (para firmar) y una **completada** (para descargar/verificar).

### 2.3 Remote debugging (para ver consola y CSP)
Sin la consola no ves las violaciones de CSP ni los errores. Conectá el dispositivo:
- **iOS:** habilitar **Web Inspector** en el iPhone (*Ajustes → Safari → Avanzado*), conectar por USB a una Mac, y usar el **menú *Desarrollar*** de Safari en la Mac [D1].
- **Android:** habilitar USB debugging y abrir **`chrome://inspect#devices`** en Chrome de escritorio (*Discover USB devices*) [D2].

## 3. La matriz (casos obligatorios — doc 05 §8)

Ejecutá **cada caso en cada dispositivo**. Registrá el resultado en la tabla del §4.

| # | Caso | 📱 Cómo | ✅ Éxito |
|---|------|---------|----------|
| M-1 | **Render de PDF ~20 MB** | Abrir una tx con un PDF grande en el visor | Renderiza completo; scroll/zoom fluido; **cero violaciones CSP** en consola (gate 5) |
| M-2 | **Decode base64 de ~27 MB** (main thread) | Abrir/subir el documento más grande soportado | La UI **no se congela** de forma perceptible; el decode va **en pasos con yields** (doc 05 §5.2) — las tareas largas (>50 ms) degradan la respuesta de la UI [D3] |
| M-3 | **Selector de archivo en Verify** | Pantalla Verify → elegir un PDF del dispositivo | El file picker abre y toma el archivo; arranca la verificación |
| M-4 | **Descarga del PDF final** | Detalle de una tx completada → descargar | El archivo baja (o abre según iOS — §5); **documentar el comportamiento** |
| M-5 | **Deep link desde card de Teams** | Tocar el botón de una card (o el link) en el móvil, **sin sesión previa** | Login Entra → aterriza en la **pantalla de firma de ESA tx** (no dashboard) — es el **gate 6** (doc 03 de este cluster) |

**Estados transversales** (doc 05 §4.6) — verificalos de paso en cada dispositivo:
- Cargando / vacío con CTA / error accionable / error técnico con ID de correlación / **sin red** (cortar datos → el flujo firmar detecta el fallo y ofrece reintento manual visible).

## 4. 🧑 Registro de resultados

Una tabla por dispositivo (guardala en el registro del cierre de F3):

```
Dispositivo: iPhone 15 / iOS 18 / Safari
| Caso | Resultado | Nota (comportamiento observado) |
|------|-----------|---------------------------------|
| M-1  | ✅/❌      | …                               |
| M-2  | ✅/❌      | tiempo aprox., ¿jank?           |
| M-3  | ✅/❌      | …                               |
| M-4  | ✅/❌      | ¿tab nueva? ¿share sheet? ¿Files? |
| M-5  | ✅/❌      | URL final vs enviada            |
```

## 5. Lo que hay que OBSERVAR y documentar (no está garantizado por doc)

- **Descarga en iOS Safari (M-4) — NO VERIFICADO:** ninguna fuente autoritativa documenta si un `<a download>`/blob en iOS abre **tab nueva, preview, Files o share sheet**, ni si respeta el nombre sugerido. El `<a download>` solo funciona para **same-origin o esquemas `blob:`/`data:`** [D4], y WebKit anunció `download` para **macOS** sin describir iOS [D5]. **Comportamiento a MEDIR en el dispositivo** y documentar (define el fallback del gate 7 — doc 03).
- **`decode` de 27 MB (M-2) — sin umbral documentado:** no hay número oficial; el único ancla es que las tareas largas (>50 ms) degradan la respuesta de la UI [D3]. **Medilo** en el equipo de gama baja; si congela, es señal para chunking más fino (doc 05 §5.2).
- **`sandbox`/`allow-downloads` del iframe del host:** las descargas dentro de un iframe con `sandbox` requieren el token **`allow-downloads`** [D6]; si M-4 falla, **inspeccioná el atributo `sandbox`** del iframe del player (vía Web Inspector) para ver si lo tiene — no está documentado que apps.powerapps.com lo setee (**NO VERIFICADO**).

## 6. Relación con los gates

- **M-1** es la validación móvil del **gate 5** (CSP con la app real).
- **M-4** alimenta el **gate 7** (descargas desde el iframe) — doc 03 de este cluster.
- **M-5** ES el **gate 6** (deep link desde Teams) — doc 03.

Esta matriz cubre el **lado móvil**; el doc 03 formaliza los gates 6/7 incluyendo el lado desktop.

## 7. Salida (cuándo está terminado)

- Los 5 casos (M-1..M-5) ✅ en **Safari iOS real** y **Chrome Android real** (mínimo).
- El comportamiento de descarga iOS (M-4) **documentado**.
- Cero violaciones CSP en M-1 en ambos.
- Resultados archivados en el registro de cierre de F3.

---

## Fuentes

Verificadas contra documentación autoritativa el 2026-07-20.

- E1. Los `devices[...]` de Playwright son **emulación** (viewport/UA), no dispositivos reales: https://playwright.dev/docs/emulation
- D1. Habilitar Web Inspector en iOS + menú Desarrollar en Safari (macOS): https://webkit.org/web-inspector/enabling-web-inspector/
- D2. Remote debugging de Android vía `chrome://inspect`: https://developer.chrome.com/docs/devtools/remote-debugging
- D3. El main thread procesa una tarea a la vez; >50 ms bloquea la UI: https://web.dev/articles/optimize-long-tasks
- D4. `<a download>` solo para same-origin o esquemas `blob:`/`data:`: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/a
- D5. WebKit `download` (Safari 10.1) — scope macOS, silencio sobre iOS: https://webkit.org/blog/7477/new-web-features-in-safari-10-1/
- D6. Descargas en iframe con `sandbox` requieren `allow-downloads`: https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/iframe

**NO VERIFICADO (declarado):**
- Comportamiento exacto de descarga en iOS Safari (tab/preview/Files/share sheet + nombre) — **medir en dispositivo** (§5).
- Umbral de congelamiento del decode de 27 MB — **medir** (§5).
- Si el iframe del player de apps.powerapps.com setea `sandbox` y si incluye `allow-downloads` — **inspeccionar en vivo** (§5).

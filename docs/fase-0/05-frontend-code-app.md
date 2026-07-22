# Sigil — Frontend: Code App

**Documento:** 05 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — visto bueno del equipo)
**Última actualización:** 2026-07-10
**Depende de:** [01-vision-y-alcance.md](01-vision-y-alcance.md), [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) (ADR-001/007/010), [04-backend-motor-criptografico.md](04-backend-motor-criptografico.md) (contratos de APIs y coordenadas), [12-convenciones-nomenclatura.md](12-convenciones-nomenclatura.md)

Los hechos de plataforma citados provienen de investigación verificada en julio 2026 (fuentes en ADR-001/ADR-010 y §13). **Shorthand de este documento:** `capi_X` = `sanic_sigil_capi_X`, `env_X` = `sanic_sigil_env_X` (nombres completos en doc 12).

---

## 1. Stack

| Necesidad | Elección | Justificación |
|-----------|----------|---------------|
| Base | **React 18 + TypeScript + Vite** (`sigil-app`) | ADR-001; template oficial de Code Apps |
| SDK plataforma | **`@microsoft/power-apps`** + npm CLI `power-apps` para init/run/push y clientes de **operaciones** (`add-dataverse-api`); **`pac code add-data-source`** para clientes de **tablas** (el npm CLI aún no genera datasources tabulares — precisión a ADR-010) | Única vía soportada para Custom APIs tipadas. Versiones fijadas exactas en `package.json` (CLI en preview — riesgo aceptado) |
| UI | **Fluent UI v9** (`@fluentui/react-components`) | Consistencia con Power Platform, accesibilidad de fábrica, DataGrid/Drawer/Toast. Descartado Tailwind puro: construir design system desde cero no aporta a un producto interno |
| Server state | **TanStack Query** — **prohibido para binarios** (§5.2) | Caché + polling declarativo para estados y listas; los PDFs/base64 tienen política propia |
| Visor PDF | **pdfjs-dist** (pdf.js, Apache-2.0) | Render canvas por página. Requiere CSP de ambiente personalizada — ver §6.1, decisión con impacto en doc 09 |
| Hash client-side | **Web Crypto API** (`crypto.subtle.digest`) | Nativo (ADR-007); contexto seguro garantizado (HTTPS) |
| i18n | **react-i18next**, recursos `es`/`en` empaquetados | RNF-06; sin carga remota (CSP) |
| Fechas | `Intl.DateTimeFormat` | Timestamps llegan UTC (doc 04 §6.3); la UI convierte a local mostrando la zona |

**Prohibiciones derivadas de plataforma (verificadas):** ninguna llamada a orígenes externos (CDNs, fuentes, telemetría — todo se bundlea); sin routing por path; el fuente vive en nuestro Git (`src/frontend/sigil-app/`) — `push` publica artefactos de build.

## 2. Estructura del proyecto

```
src/frontend/sigil-app/
  power.config.json
  src/
    generated/        # clientes tipados (tablas + capi_*) — NO editar a mano
    api/              # wrappers: base64↔Blob, validaciones espejo, mapeo de errores, descargas
    screens/          # una carpeta por pantalla (§4)
    components/
      pdf/            # visor pdf.js, overlay de zonas, editor de posiciones
      verify/         # drag&drop + hash Web Crypto
    domain/           # tipos, estados por NOMBRE LÓGICO (espejo doc 03 §3), reglas de UI
    i18n/             # recursos es/en
    lib/              # navegación por query params, polling configs, format helpers
  tests/              # Vitest — E2E Playwright en doc 11
```

**Regla de labels (RNF-06):** los formatted values de Dataverse se usan **solo como clave de mapeo** hacia el nombre lógico — **toda label visible sale de los recursos i18n** indexados por nombre lógico. Mostrar un formatted value directo en pantalla es un bug de i18n (el toggle de idioma no lo afectaría).

## 3. Navegación y deep links

**Contrato de URL** (único punto de entrada externo):

| Query param | Valores | Origen |
|-------------|---------|--------|
| `screen` | `dashboard` (default) · `sign` · `verify` · `detail` · `create` · `onboarding` | Notificaciones (RF-11), QR (RF-19) |
| `txId` | GUID | `sign`, `verify`, `detail` |

- Lectura inicial vía `getContext()` → `app.queryParams` (verificado). Desde ahí la navegación es **estado interno de React**. Hecho asumido y verificado por review: la app corre **embebida por el host de Power Apps** (`frame-ancestors 'self' https://*.powerapps.com`) — manipular `history` dentro del iframe no afecta la URL visible del navegador; **no se usa** `history.pushState/replaceState` para nada funcional.
- Deep links emitidos por flows y QR: `{env_AppPlayUrl}?screen=sign&txId={guid}` (contrato compartido con docs 04 §6.2 y 08).
- **Verificación obligatoria en primer deploy:** que los query params sobrevivan al redirect de Entra en dispositivo sin sesión (QR escaneado). Nota honesta: si el redirect los pierde, `app.queryParams` también llegará vacío (mismo mecanismo) — el plan B real es **del lado del emisor**: consolidar el destino en UN solo parámetro compacto (`?d=sign.<guid>`) para minimizar superficie de pérdida, y si aún así se pierde, escalar a Microsoft (sería un defecto de plataforma, no nuestro). No hay plan B client-side honesto.
- `hideNavBar=true` (verificado) se evalúa en implementación para maximizar área útil móvil.

## 4. Pantallas

### 4.1 Dashboard (`dashboard`) — RF-22 + RF-24

Tres pestañas (las dos de RF-22 + la vista que materializa RF-24/ADR-004):

| Pestaña | Contenido |
|---------|-----------|
| **Pendientes por mi firma** | Participaciones propias en *Turno Activo* **cruzado con transacción en Pendiente de Firma o Firmado Parcialmente** (los participantes conservan Turno Activo como verdad histórica en estados terminales — doc 06 §2; sin el cruce, quedarían cards "Firmar" muertas de transacciones expiradas/canceladas). Cada card: nombre de transacción, creador, **fecha de vencimiento** (`expireson`, RF-27) con urgencia visual, CTA Firmar |
| **Mis solicitudes** | Transacciones creadas por mí, **todos los estados**. **Error de Sellado se muestra arriba con alerta prominente** (es el estado accionable más urgente — CTA Reintentar). *Sellando* con spinner y polling. Terminales (Completado/Rechazado/Expirado/Cancelado) con badge y motivo |
| **Mis participaciones** | Transacciones donde soy firmante, cualquier estado — resuelve el caso "ya firmé pero sigue en curso" (seguimiento) y el acceso post-completado a documentos que firmé (RF-24): filtro rápido "Completados" con descarga directa |

**First-run (usuario nuevo):** si `capi_GetMasterSignature` devuelve vacío → banner persistente "Configurá tu Firma Maestra" con CTA a onboarding; empty states de pestañas con CTA "Crear tu primera solicitud". Nadie aterriza en dos listas vacías sin guía.

**Nota de datos:** las cards de "Pendientes" necesitan datos de la transacción además del participante — verificar si el cliente tabular delega el join/expand; si no, segunda query batcheada (decisión de implementación registrada).

### 4.2 Crear solicitud (`create`) — RF-25/26/28

Wizard: (1) PDF, (2) firmantes + enrutamiento, (3) **zonas de firma OBLIGATORIAS** (§6.3 — cada firmante ≥1 zona; RF-28, 2026-07-13: no hay posición por defecto; el paso no se puede saltar y "Enviar" queda bloqueado con indicador de a quién le falta zona; "guardar borrador" sí permite incompleto), (4) revisión → enviar o guardar borrador.

**Validaciones client-side (espejo explícito de doc 04 §3.4 — todo lo barato se valida ANTES de subir 27 MB):** tamaño vs `env_MaxPdfSizeKB` y extensión **antes** de encodear base64; firmantes sin duplicados; máximo `env_MaxParticipants`; orden secuencial 1..N sin huecos; zonas solo de firmantes existentes, página válida, coordenadas 0–100. Quedan solo-backend (imposibles client-side de forma confiable): magic bytes, PDF cifrado, firmas digitales previas — sus errores se muestran tal cual.

**Borrador en dos pestañas/dispositivos:** el lock del backend (doc 04 §5) produce error limpio → la UI refetchea el estado actual y el usuario re-aplica sus cambios sobre lo último (jamás silent overwrite).

### 4.3 Firmar (`sign`) — RF-03/04/13/28

- Visor PDF completo (§6.1). **Precondición dura: "Aprobar y Firmar" no se habilita sin render exitoso del documento** (RF-03).
- **El firmante VE dónde quedará su firma** (decisión de esta revisión): overlay con sus zonas resaltadas (o la posición estándar indicada) y las de otros firmantes en neutro — información material para el consentimiento; nadie firma sin saber dónde se estampa.
- Acciones: **Aprobar y Firmar** → éxito navega a `detail` con toast diferenciado por `IsLastSigner` ("tu firma quedó registrada" / "eras el último: sellando el documento"); **Rechazar** con motivo obligatorio.
- Sin Firma Maestra configurada → redirección a onboarding con retorno automático a esta pantalla.

### 4.4 Detalle (`detail`) — RF-05/24/27/30, RNF-04

Estado + vencimiento (`expireson` visible), progreso de participantes, línea de tiempo (`sanic_sigil_tbl_event`), motivo de rechazo/cancelación visible para creador y participantes (todos comparten la transacción). Acciones por rol/estado: descarga del PDF final (Completado), **Cancelar** (creador — RF-30), **Reintentar sellado** (creador, Error de Sellado). Polling mientras *Sellando* (§5.1).

### 4.5 Verificar (`verify`) — RF-20/21

Aterrizaje del QR: constancia con metadatos, **nivel de evidencia TSA con ambas fechas** (sellado y genTime si difieren), **el `hash_final` en claro con guía de verificación manual** ("verificalo vos mismo: `sha256sum archivo.pdf`" — 2026-07-13) y el resultado de la **verificación cruzada del historial** (`historyIntact` — doc 04 §3.1). Drag & drop (desktop) / selector de archivo (móvil) → SHA-256 en el navegador → veredicto **Verde/Rojo** a pantalla completa. Token TSA descargable (§5.3).

### 4.6 Onboarding de firma (`onboarding`) — RF-01/02

Subida de imagen → `capi_ValidateMasterSignature` → si falla: motivos específicos (`FailureReasons`); si pasa: **preview inmediato de la versión normalizada** (`NormalizedImageBase64` del output — contrato actualizado en doc 04). Muestra la firma vigente actual (`capi_GetMasterSignature`) si existe.

**Estados transversales:** cargando / vacío con CTA / error accionable (mensajes del backend, diseñados para usuarios — doc 04 §8) / error técnico genérico con ID de correlación / **sin red** (móvil): detección de fallo de red con reintento manual visible — crítico en el flujo firmar.

**Fuera de alcance de esta fase:** UI para el rol `Sigil | SR | Auditor` (doc 03 §6 lo define como fase posterior — registrado para que no se caiga entre sillas).

## 5. Acceso a datos

### 5.1 Listas y estados (TanStack Query)
Lecturas de tablas vía cliente generado, con permisos del usuario. Polling: `refetchInterval` 5 s **solo** cuando hay transacciones en *Sellando* visibles; tope 3 min → mensaje "sigue en proceso, te llegará la notificación" + **botón de refresco manual** (sin reanudación automática; `refetchOnWindowFocus` habilitado únicamente para queries de estado, jamás de binarios).

### 5.2 Binarios (política propia — FUERA de TanStack Query)
Regla dura de esta revisión: **los base64 de documentos jamás entran al caché de Query** (el default `gcTime` de 5 min retendría ~54 MB de string UTF-16 + el `Uint8Array` + buffers de pdf.js — inaceptable en móvil; y un `refetchOnWindowFocus` re-descargaría 27 MB). Los binarios se piden con el wrapper de `api/` directo, viven en **estado local de la pantalla**, y se liberan al desmontar. Nunca en localStorage/IndexedDB (el documento no queda en el dispositivo fuera de la descarga explícita). El wrapper valida tamaño antes de encodear (upload) y decodifica en pasos con yields (`await`) para no congelar el main thread en móvil.

### 5.3 Descargas (PDF final, token TSA)
Mecánica: base64 → `Blob` → `URL.createObjectURL` → `<a download>` (+ `revokeObjectURL`). Token TSA: nombre `SIGIL-{numero}.tsr`, MIME `application/timestamp-reply` (consumible por `openssl ts`). **Verificación de primer deploy:** que el iframe del host permita descargas (sandbox `allow-downloads`) y el comportamiento de Safari iOS con blob URLs (tab/share sheet) — agregado a la lista §11.

## 6. Visor PDF, editor de zonas y verificación

### 6.1 Visor (pdf.js) — decisión de CSP con impacto en ambiente

**Hecho verificado (corrige la versión anterior de este doc):** la CSP por defecto de Code Apps trae **`worker-src 'none'` y `child-src 'none'` explícitos** — el fallback a `script-src` NO aplica (solo opera cuando `worker-src` está ausente). Consecuencia: **el worker de pdf.js no puede crearse ni siquiera como asset same-origin**. Además, `connect-src 'none'` bloquea los `fetch` auxiliares de pdf.js (cMaps para fuentes CJK, standard fonts, wasm de JPEG2000/JBIG2 — **típicos de PDFs escaneados**, pan de cada día en firma de documentos).

**Decisión (Plan A):** personalizar la CSP del ambiente — mecanismo oficial (`PowerApps_CSPConfigCodeApps` / admin center, verificado que los valores custom **reemplazan** el default): **`worker-src 'self' blob:`** y `connect-src 'self'`. *(Enmienda 2026-07-21: al adoptar el worker de pdf.js **inline como blob** — `?worker&inline` en F3 —, `worker-src` requiere `blob:`; sin él el visor no arranca. Confirmado en el primer despliegue a Test. `blob:` no habilita orígenes externos: son URLs same-origin generadas en runtime.)* Esta configuración es **por ambiente** y entra al runbook de ALM (**doc 09 la hereda como requisito**: Dev/Test/Prod deben configurarla antes del primer deploy; se valida con el modo reporting de CSP primero).
**Plan B** (si la política organizacional no permite tocar la CSP): pdf.js en modo **fake worker** (main thread, `script-src 'self'` sí lo permite — más lento, funcional) + factories custom para cMaps/fonts/wasm empaquetados como módulos importados (sin `fetch`). Costo: UX degradada en móviles y complejidad de bundling — por eso es plan B.

- Worker (plan A) como asset propio de Vite (`?url`), same-origin. Render por página con lazy loading; zoom y paginación táctiles.
- pdf.js aplica `/Rotate` y usa el CropBox en su viewport → el canvas muestra **orientación visual**: coincide con el contrato de coordenadas (doc 04 §6.1, validado por spike — el backend compensa la rotación; el frontend reporta coordenadas visuales).
- **Casos de prueba obligatorios bajo CSP enforced:** PDF escaneado (JBIG2/JPX), PDF con fuentes CJK, PDF de 20 MB.

### 6.2 Verificación client-side
`File` → `ArrayBuffer` → `crypto.subtle.digest('SHA-256')` → hex 64 → `capi_VerifyDocument`. El archivo **jamás sale del navegador** (ADR-007).

### 6.3 Editor de zonas (RF-28)
Overlay por página: rectángulos arrastrables/redimensionables por firmante (color + etiqueta). **Accesibilidad:** además del drag, **entrada numérica de página/x/y/ancho/alto** por zona (teclado y precisión); el canvas lleva estado textual por página para lectores de pantalla. Coordenadas en el contrato compartido (% del CropBox, arriba-izquierda, orientación visual), independientes del zoom. **Las zonas son obligatorias** (RF-28): el editor muestra el checklist de firmantes con/sin zona y bloquea el avance hasta completarlo. Móvil: funciona con pointer events, pero el flujo móvil de primera clase es **firmar**, no crear (§8).

## 7. Internacionalización (RNF-06)

- `es`/`en` empaquetados; idioma inicial `navigator.language` (default `en`) + toggle persistido en `localStorage` (nota verificada: Safari particiona localStorage en iframes cross-site — persiste por top-site; suficiente para una preferencia de UI).
- Hecho verificado: `getContext()` **no expone idioma** — por eso browser language. Correos/cards usan `uilanguageid` de Dataverse (doc 03 §8); puede diferir del idioma de la UI — aceptado.
- Cero strings hardcodeados; labels de estados vía nombre lógico (§2); fechas/números con `Intl`.

## 8. Responsive y móvil (RF-23 — primera clase)

- Mobile-first; breakpoints `<640` / `640–1024` / `>1024`; targets táctiles ≥ 44 px; acciones primarias en bottom bar móvil.
- Flujo crítico móvil: **firmar** — deep link → login → visor → aprobar, presupuesto ≤ 3 interacciones. **Crear con PDFs grandes se recomienda en desktop** (aviso suave en móvil): encodear y subir 27 MB en un POST único desde red móvil no tiene chunking ni reanudación — un fallo al 90% reinicia el upload (limitación aceptada del transporte por Custom API).
- **Matriz de prueba obligatoria en dispositivos reales** (Safari iOS, Chrome Android): render de 20 MB, decode base64 de 27 MB (main thread), selector de archivo en verify, descarga de PDF final, deep link completo desde tarjeta de Teams. (Mobile player NO soporta code apps — todo navegador, verificado.)

## 9. Identidad y seguridad en el cliente

- Identidad de UI vía `getContext()`; **jamás autoritativa** — la autorización real es del backend (doc 04 §3.3); la UI solo oculta lo que el backend igualmente rechazaría.
- Sin secretos; sin persistencia de documentos (§5.2); binarios solo en memoria de pantalla y liberados al desmontar.
- Errores del backend se muestran tal cual; técnicos → genérico + ID de correlación.

## 10. Build, push y versionado

- `power-apps run` (desarrollo local, conexiones reales) / `power-apps push` asociado a la solución `sigil_core_sigil` (detalle en doc 09).
- Versión de `package.json` visible en el footer.
- `generated/` se regenera ante cada cambio de contrato de Custom API — el build roto ES la alarma de contrato roto.

## 11. Riesgos y verificaciones de primer deploy

| Ítem | Estado | Mitigación |
|------|--------|-----------|
| CSP de ambiente personalizada (`worker-src 'self' blob:`, `connect-src 'self'`) | **Requisito nuevo para doc 09** — verificado que el default bloquea el plan ingenuo | Plan B fake-worker + factories bundleadas (§6.1) |
| Query params tras redirect de Entra (QR sin sesión) | NO VERIFICADO — prueba obligatoria | Emisor consolida destino en un param; si se pierde igual → defecto de plataforma, escalar |
| Descargas desde el iframe del host (sandbox `allow-downloads`, Safari iOS + blob) | NO VERIFICADO — prueba obligatoria | Fallback: abrir en nueva pestaña |
| Decode/encode base64 de 27 MB en móviles de gama baja | A medir en matriz §8 | Decode con yields; crear-en-desktop recomendado; último recurso: bajar `env_MaxPdfSizeKB` (decisión de negocio con datos) |
| npm CLI en preview | Riesgo aceptado (ADR-010) | Versiones fijadas; upgrades deliberados |
| pdf.js: PDFs escaneados (JBIG2/JPX) y CJK bajo CSP | Prueba obligatoria (§6.1) | Cubierto por plan A de CSP (`connect-src 'self'`) |

## 12. Trazabilidad

| RF/RNF | Sección |
|--------|---------|
| RF-01/02 | §4.6 Onboarding |
| RF-03 | §4.3 (precondición dura de render) |
| RF-04 | §4.3 + §5.1 |
| RF-05 | §4.4 (descarga final) + notificaciones doc 08 |
| RF-11/19 | §3 |
| RF-13 | §4.3 (rechazo con motivo) |
| RF-18 | §9 (identidad UI no autoritativa) |
| RF-20/21 | §4.5 + §6.2 |
| RF-22 | §4.1 (pestañas 1 y 2) |
| RF-23 | §8 |
| RF-24 | §4.1 (pestaña Mis participaciones + filtro Completados) + §4.4 |
| RF-25/26 | §4.2 |
| RF-27 | §4.1/§4.4 (vencimiento visible) |
| RF-28 | §6.3 + §4.3 (el firmante ve sus zonas) |
| RF-30 | §4.4 (Cancelar) |
| RNF-04 | §4.4 (timeline) |
| RNF-06 | §7 + regla de labels §2 |

## 13. Fuentes

Las de ADR-001/ADR-010 (overview, architecture, retrieve-context, connect-to-dataverse, add-dataverse-action-function, npm-quickstart, ALM) más: learn.microsoft.com/power-apps/developer/code-apps/how-to/content-security-policy (worker-src/child-src/connect-src 'none' por defecto; personalización por ambiente) · developer.mozilla.org (worker-src y su fallback; SubtleCrypto) · tanstack.com/query (caching/gcTime) · mozilla.github.io/pdf.js · react.i18next.com.

---

*Anterior: [04-backend-motor-criptografico.md](04-backend-motor-criptografico.md) · Siguiente: 06 — Máquina de estados y flujos.*

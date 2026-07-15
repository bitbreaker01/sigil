# Sigil — Hoja de Ruta de Ejecución

**Documento:** 10 — Fase 0
**Estado:** **Aprobado** (2026-07-13 — visto bueno del equipo)
**Última actualización:** 2026-07-12
**Depende de:** todos los documentos de Fase 0 (esta hoja de ruta los convierte en secuencia de construcción)

---

## 1. Remapeo de la hoja de ruta de V2 (trazabilidad)

La hoja de ruta de `Sigil_GuiaDelForo.md` §5 quedó obsoleta en partes por las decisiones de Fase 0. Mapeo explícito:

| V2 decía | Qué pasó en Fase 0 | Destino real |
|----------|--------------------|--------------|
| Fase 1 (S1-2): Plugin C# + librería PDF + API TSA + tablas con FLS | Vigente, enriquecida (spikes ya ejecutados adelantaron riesgo) | Nuestras F1+F2 |
| Fase 2 (S3): Azure AI Vision + Custom API de validación de firma | **AI Vision eliminado con evidencia** (ADR-009: sin métricas de calidad + deprecado) — la validación es local | Absorbida en F2 (sin servicio externo, sin semana propia) |
| Fase 3 (S4-5): Canvas App, visor, onboarding, asincronía | Canvas → **Code App** (ADR-001); los "gatilladores asíncronos" son el worker (backend) | Nuestras **F2** (asincronía) **+ F3** (UI) |
| Fase 4 (S6): generador QR + endpoint de validación | El QR es un paso del pipeline (ADR-011); la verificación es pantalla + Custom API — no una fase | Absorbida en F2 (backend) y F3 (pantalla) |
| — | Notificaciones (V2 §2 las **mencionaba** — "se envía una notificación al usuario" — pero sin asignarles fase ni semanas), ALM/gates, endurecimiento y UAT | F4 y F5 (con entidad propia) |

Estimación V2: 6 semanas. **Estimación de esta hoja de ruta: 8 semanas de trabajo — declaradamente AGRESIVA (§2.1)** — la diferencia con V2 es lo no contemplado (F4/F5) más el alcance que la Fase 0 agregó (cancelación RF-30, editor de zonas RF-28, saneamiento T14, dashboard de 3 pestañas, i18n estructural).

### 2.1 Supuestos y disparadores de re-plan (honestidad de estimación)

- **Supuesto de capacidad:** 2 desarrolladores full-time (uno con foco backend, uno frontend, ambos compartiendo Fase 1). Con 1 solo desarrollador, la estimación NO vale: F2/F3 dejan de solaparse → ~11-12 semanas.
- **Sin colchón interno:** las 8 semanas no tienen holgura. **Compromiso hacia afuera: 10 semanas** (colchón de 2) — el colchón absorbe los disparadores de abajo sin re-negociar.
- **Disparadores de re-plan (se evalúan al ocurrir, no se improvisan):** (a) spike de sandbox de F1 falla → plan B Azure Function (ADR-008) = +2 semanas de infraestructura y ajuste del worker; (b) el mix de pipelines falla (F4) → plan B export/import manual = +3 días de runbook; (c) la CSP no es personalizable (F3) → plan B fake worker = +1 semana de bundling.
- **Qué se recorta si F2 desborda:** F4 (notificaciones) es lo deslizable — dependencias mínimas y el sistema es operable sin recordatorios los primeros días. El sellado y la verificación NO se recortan jamás (son el producto).

## 2. Fases de ejecución

Regla transversal: **Strict TDD desde el primer commit** (doc 11) y **los riesgos van primero** — cada NO VERIFICADO de plataforma se ataca en la fase más temprana posible, no se arrastra.

### F1 — Fundaciones (semanas 1–2)

| Entregable | Fuente |
|------------|--------|
| Ambiente Dev aprovisionado (Runbook A completo: publisher, **tabla canónica de choices → apéndice doc 12**, SP, cuenta de servicio, CSP, auditoría) | doc 09 §7 |
| Repos + CI (build + tests backend y frontend en PR) | doc 09 §4, doc 11 |
| **Spike residual en sandbox REAL**: plugin de prueba con PDFsharp+BouncyCastle+QRCoder+ImageSharp ejecutando en el sandbox de Dataverse + TSA alcanzable desde ahí (gate 8) | doc 04 §10 — el único riesgo técnico abierto de la Fase 0 |
| Modelo de datos completo en la solución (6 tablas, choices, roles, perfil FLS, alternate keys) | doc 03 |
| Esqueleto del plugin package + primeras APIs: `CreateTransaction`, `UpdateDraft`, `DeleteDraft`, `GetDocumentContent` + harness TDD (incl. decisión de licencia FakeXrmEasy vs stub — doc 11 §2) | docs 04, 11 |
| **Datos semilla en Dev**: usuarios de prueba licenciados con Firma Maestra + exclusión de Conditional Access para cuentas de prueba solo-Dev (Playwright — doc 11 §3) | doc 11 — F2/F3 los necesitan desde la semana 3 |

**Salida de F1:** el spike de sandbox en verde (si falla, el plan B Azure Function de ADR-008 se activa ANTES de construir el motor encima) + smoke por API: crear borrador con PDF real y volver a leerlo.

### F2 — Motor de firma y sellado (semanas 3–4)

| Entregable | Fuente |
|------------|--------|
| `ValidateMasterSignature` + `GetMasterSignature` (validación local + normalización) | ADR-009, doc 04 |
| `SendTransaction`, `SubmitSignature`, `RejectTransaction`, `CancelTransaction`, `RetrySealing` — con lock de fila, precedencia de guards e idempotencia | docs 04 §5, 06 |
| Worker de sellado completo (9 pasos, orden durable-primero, guards del disparador) + `VerifyDocument` | doc 04 §7, ADR-011 |
| Jobs: `ExpireTransactions` (+ saneamiento T14), `ProcessReminders`, `ResealPending` | docs 04, 06 |
| Tests de la matriz completa de doc 06 (toda transición permitida y prohibida) + inventario obligatorio de doc 11 | doc 11 |

**Salida de F2:** E2E **por API, sin UI**: crear → enviar → firmar (N firmantes, ambos enrutamientos) → sellar → verificar Verde → alterar un byte → Rojo. Con TSA real desde el sandbox.

### F3 — Frontend (semanas 4–6, solapada con el final de F2)

| Entregable | Fuente |
|------------|--------|
| **Primero el visor PDF bajo la CSP real del ambiente** (gate 5 adelantado a Dev: worker, PDF escaneado, CJK) — es el riesgo frontend más grande; se valida antes de construir pantallas encima | doc 05 §6.1/§11 |
| Las 6 pantallas (dashboard 3 pestañas, crear con wizard + editor de zonas, firmar con overlay, detalle, verificar, onboarding) | doc 05 §4 |
| i18n es/en estructural; deep links + verificación de query params tras redirect (gate 6 adelantado) | doc 05 §3/§7 |
| E2E Playwright contra Dev + primera pasada de la matriz móvil | doc 11, doc 05 §8 |

**Salida de F3:** el flujo móvil de primera clase (deep link → login → visor → firmar) medido en dispositivos reales.

### F4 — Notificaciones y jobs en producción de mensajes (semana 6)

| Entregable | Fuente |
|------------|--------|
| Los 3 flows con plantillas es/en, matriz de destinatarios, heartbeat | doc 08 |
| Verificación de la matriz de notificaciones completa por checklist (cada evento → cada destinatario → deep link, ambos idiomas) | doc 08 §4, doc 11 §3 |
| **Infraestructura de pipelines + aprovisionamiento de Test (Runbook A) + PRIMER run Dev→Test con la solución parcial** — el NO VERIFICADO del mix de componentes (doc 09 §11) se resuelve acá, una semana antes del endurecimiento, no durante | doc 09 §5/§7 — coherente con la regla "los riesgos van primero" |

**Salida de F4:** heartbeat diario recibido + una transacción completa notificando de punta a punta + **primer despliegue a Test en verde (gates 1–4)**.

### F5 — Endurecimiento y despliegue (semanas 7–8)

| Entregable | Fuente |
|------------|--------|
| Los **10 gates completos** en Test (el aprovisionamiento y el primer run ya ocurrieron en F4) | doc 09 |
| UAT con datos semilla + matriz de dispositivos completa | docs 09 §7.9, 05 §8 |
| Revisión de seguridad contra el modelo de amenazas (checklist A1–A17: cada control implementado y probado) | doc 07 |
| Prod: Runbook A + pipeline + gates + transacción SMOKE | doc 09 §8 |

**Salida de F5 = go-live:** gates 1–10 en verde en Prod, con evidencia archivada del despliegue.

## 3. Riesgos que ordenan la secuencia (por qué este orden y no otro)

1. **Sandbox real primero (F1):** todo el motor descansa sobre "el stack corre en el sandbox" — es lo único importante que la Fase 0 no pudo verificar sin ambiente. Se ataca en la semana 1, con plan B ya decidido (Azure Function — ADR-008).
2. **CSP antes que pantallas (F3):** si la política organizacional no permitiera personalizar la CSP, el plan B del visor (fake worker + factories) cambia la base del frontend — mejor saberlo el día 1 de F3.
3. **TSA desde la red del sandbox (F1):** el spike encontró DigiCert bloqueado desde una red real — el fallback multi-endpoint se prueba donde va a vivir.
4. **El solapamiento F2/F3 tiene un contrato:** F3 arranca cuando `CreateTransaction`/`GetDocumentContent`/`GetMasterSignature` están estables (los clientes tipados se generan de contratos reales — doc 05 §10).

## 4. NO VERIFICADOS restantes de la Fase 0 → dónde se resuelven

| Ítem (origen) | Se resuelve en |
|---------------|----------------|
| Stack en sandbox Dataverse real (doc 04 §10) | F1 — spike residual |
| Límite del .nupkg del plugin package (doc 04 §10) | F1 — primer push mide |
| "2 min por ejecución" async (doc 04 §1) | F2 — presupuesto medido con tracing por paso |
| pdf.js bajo CSP enforced / PDFs escaneados (doc 05 §11) | F3 — gate 5 adelantado |
| Query params tras redirect Entra (doc 05 §11) | F3 — gate 6 adelantado |
| Descargas desde el iframe / Safari iOS (doc 05 §11) | F3 — gate 7 adelantado |
| Pipelines con el mix completo de componentes (doc 09 §11) | **F4** — primer Dev→Test adelantado |
| appId de code app en el destino (doc 09 §11) | **F4** — primer Dev→Test adelantado |
| Licenciamiento FakeXrmEasy comercial vs stub (doc 11 §2) | F1 — antes del primer commit |
| Export a App Insights (Managed Environments + licencias — doc 11 §6.3) | F1 — decisión con los hechos verificados |
| Decode/encode base64 de 27 MB en móviles de gama baja (doc 05 §11) | F3 (primera pasada) / F5 (matriz completa) |

**Ninguno bloquea el diseño**: todos tienen plan B decidido o son absorbibles por configuración.

## 5. Criterio de cierre de la Fase 0 (estado al 2026-07-12)

1. Documentos 01–12 aprobados → **CUMPLIDO (2026-07-13)**: los 13 documentos (00–12) en estado Aprobado, tras la ronda final de aclaraciones del equipo (zonas obligatorias RF-28, firma maestra versionada, hash en eventos de firma, metadatos XMP, hash final visible en la constancia, PAdES delegado a versión futura).
2. Preguntas Q-01..Q-08 → **todas cerradas**.
3. NO VERIFICADOS que afecten decisiones core → **ninguno**: los restantes están mapeados arriba con dueño y fase.
4. Spikes de riesgo mayor → **ejecutados con evidencia** (`spikes/RESULTADOS.md`); queda solo el residual de sandbox (F1).

---

*Anterior: [09-alm-entornos-y-despliegue.md](09-alm-entornos-y-despliegue.md) · Siguiente: [11-testing-y-observabilidad.md](11-testing-y-observabilidad.md).*

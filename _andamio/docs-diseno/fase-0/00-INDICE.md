# Sigil — Fase 0: Índice Maestro de Documentación Técnica

**FASE 0 CERRADA (2026-07-13)** — los 13 documentos aprobados, Q-01..Q-08 cerradas, spikes ejecutados, NO VERIFICADOS restantes mapeados a fases de ejecución (doc 10 §4).

**Objetivo de la Fase 0:** aterrizar TODAS las características pedidas en las especificaciones al terreno técnico, con cada decisión y cada guía escrita, verificada y trazable. Nada se implementa hasta que su documento esté en estado **Aprobado**.

**Última actualización:** 2026-07-13

## Reglas de esta documentación

1. **Todo se escribe.** Ninguna decisión vive solo en una conversación: si se decidió, hay un ADR o una sección que lo registra con su porqué.
2. **Todo se verifica.** Ningún dato de plataforma (límites, licencias, capacidades) se escribe de memoria: se contrasta con documentación oficial y se citan fuentes. Lo no verificable se marca **NO VERIFICADO**.
3. **Cada documento se re-revisa después de escrito** (coherencia interna, completitud de la guía, contradicciones con otros documentos).
4. **Trazabilidad:** los requerimientos usan IDs (`RF-xx`, `RNF-xx`), los conflictos `C-xx`, las preguntas abiertas `Q-xx`, las decisiones `ADR-xxx`. Los documentos posteriores referencian esos IDs, no re-describen.
5. **Jerarquía de fuentes:** decisión de equipo > `Sigil_GuiaDelForo.md` (V2.0) > `Especificaciones_Sigil.md` (V1). Ver documento 01, sección 2.

## Documentos de la Fase 0

| # | Documento | Contenido | Estado |
|---|-----------|-----------|--------|
| 00 | `00-INDICE.md` | Este índice, reglas y estado global | Vivo |
| 01 | [01-vision-y-alcance.md](01-vision-y-alcance.md) | Visión, resolución de conflictos V1/V2 (C-01..C-11), requerimientos consolidados (RF-01..29, RNF-01..07), fuera de alcance, preguntas Q-01..07 (todas cerradas) | **Aprobado (2026-07-10)** |
| 02 | [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) | ADRs 001–011: Code App, motor C#, notificaciones, almacenamiento solo-Dataverse, TSA como feature flag, FLS, QR, asincronía, validación+normalización de firma, tooling, esquema canónico de sellado | **Aprobado (2026-07-10)** |
| 03 | [03-modelo-datos-dataverse.md](03-modelo-datos-dataverse.md) | 6 tablas (transacción, participante, zona de firma, ledger, firma maestra, historial), choices, relaciones/cascadas, column security, roles, sharing, auditoría, variables de entorno, capacidad, riesgos de plataforma | **Aprobado (2026-07-10)** |
| 04 | [04-backend-motor-criptografico.md](04-backend-motor-criptografico.md) | Stack verificado (PDFsharp/BouncyCastle/QRCoder/ImageSharp, plugin package), 15 Custom APIs con Execute Privileges y autorización por API, concurrencia (lock de fila), contratos JSON, especificaciones cripto (TSA CertReq, coordenadas, UTC), pipeline de sellado idempotente, semántica de fallos, spikes integrados | **Aprobado (2026-07-10)** |
| 05 | [05-frontend-code-app.md](05-frontend-code-app.md) | Stack (React+TS+Vite, Fluent UI v9, pdf.js, TanStack Query), 6 pantallas (dashboard 3 pestañas, crear, firmar, detalle, verificar, onboarding), **decisión de CSP de ambiente (worker-src + child-src 'self' blob:/connect-src 'self' — requisito para doc 09)**, política de binarios fuera de caché, editor de zonas (RF-28), deep links, i18n es/en, mobile-first, riesgos de primer deploy | **Aprobado (2026-07-10)** |
| 06 | [06-maquina-de-estados-y-flujos.md](06-maquina-de-estados-y-flujos.md) | Autoridad única de transiciones: matriz exhaustiva T1–T14 (transacción), P1–P4 (participante), sub-máquina TSA del ledger, enrutamiento, precedencia de guards, reglas R1–R7 (incl. saneamiento de Sellando zombi) | **Aprobado (2026-07-10)** |
| 07 | [07-seguridad-y-cumplimiento.md](07-seguridad-y-cumplimiento.md) | Cadena de confianza por capas con límites honestos verificados, modelo de amenazas A1–A17 (incl. sysadmin pre-sellado, compromiso del Service Principal, backup/restore, destrucción de evidencia), secretos, privacidad, niveles de evidencia, recomendaciones organizacionales | **Aprobado (2026-07-10)** |
| 08 | [08-notificaciones-y-recordatorios.md](08-notificaciones-y-recordatorios.md) | 3 flows (turno de participante, estados de transacción, job diario), matriz de notificaciones con reglas de destinatarios, realidad verificada de triggers (valores numéricos de choices, Callback Registration), idioma por destinatario, conexiones y gobernanza (SP + cuenta de servicio), monitoreo con heartbeat | **Aprobado (2026-07-12)** |
| 09 | [09-alm-entornos-y-despliegue.md](09-alm-entornos-y-despliegue.md) | Ambientes Dev/Test/Prod, solución única con criterio de segmentación, Power Platform Pipelines con delegated deployments y aprobaciones, higiene de env vars (los valores NO viajan), rollback fix-forward, runbooks A (aprovisionamiento) y B (10 gates post-import), versionado, calendario operativo | **Aprobado (2026-07-12)** |
| 10 | [10-hoja-de-ruta.md](10-hoja-de-ruta.md) | Remapeo V2→real con trazabilidad, 5 fases (F1 fundaciones+spike sandbox, F2 motor, F3 frontend, F4 notificaciones+primer pipeline, F5 endurecimiento), supuestos de capacidad y disparadores de re-plan, mapa de NO VERIFICADOS→fase | **Aprobado (2026-07-13)** |
| 11 | [11-testing-y-observabilidad.md](11-testing-y-observabilidad.md) | Strict TDD, pirámide sobre el núcleo puro, FakeXrmEasy 2.x (net462 — verificado) con decisión de licencia, seam de columnas File, inventario M1–M13 de tests obligatorios heredados, límites declarados (locks, CSP, flows), observabilidad (tracing, correlación, App Insights con hechos verificados, rutinas) | **Aprobado (2026-07-13)** |
| 12 | [12-convenciones-nomenclatura.md](12-convenciones-nomenclatura.md) | Convención canónica de nombres: publisher `sanic_` + namespace `sigil_` + marcador de tipo (tbl/choice/capi/env), display names "Sigil \| TIPO \| Nombre", solución `sigil_core_sigil`, código | **Aprobado (2026-07-10)** |

**Spikes ejecutados (2026-07-10):** PDFsharp+transparencia (PASS, pin ≥6.2.4) y BouncyCastle RFC 3161 (PASS, token medido 8.844 chars base64) — evidencia y artefactos en `spikes/RESULTADOS.md`. Pendiente: corrida en sandbox Dataverse real al provisionar ambiente.

## Estado de preguntas abiertas (bloqueantes para cerrar Fase 0)

Detalle en documento 01, sección 6. **Actualización 2026-07-10: Q-01..Q-07 cerradas; se abrió Q-08.**

- **Q-08** ¿El creador puede **cancelar** una transacción ya enviada? → **Cerrada (2026-07-10): SÍ**, con estado propio *Cancelado* (RF-30, `sanic_sigil_capi_CancelTransaction`).

- **Q-01** ¿TSA sí o no? ¿Gratuita o paga? ¿Alternativas? → **Cerrada**: TSA sí, gratuita (DigiCert + Sectigo fallback), re-sellado diferido, y **como característica encendible/apagable por ambiente** (RF-29, ADR-005).
- **Q-02** Verificación con login corporativo → **Cerrada**: se sigue la recomendación de ADR-007.
- **Q-03 / Q-04** Almacenamiento → **Cerradas**: solo Dataverse; **SharePoint prohibido** (RNF-07).
- **Q-05** Firma Maestra → **Cerrada**: solo carga de imagen, con análisis + normalización a tamaño estándar (ADR-009).
- **Q-06** Idiomas → **Cerrada**: español e inglés (RNF-06).
- **Q-07** Word→PDF → **Cerrada por disolución**: no se soporta Word; ingesta solo PDF (C-11).

**Decisión nueva registrada:** RF-28 — posición de firmas: default configurado por Sigil, con opción de que el creador defina posiciones por firmante (editor visual sobre el PDF). Impacta docs 03 (coordenadas en el modelo), 04 (incrustación) y 05 (editor).

## Criterio de salida de la Fase 0

La Fase 0 se considera cerrada cuando:
1. Los documentos 01–12 están en estado **Aprobado**.
2. Todas las preguntas Q-xx tienen respuesta registrada (en el ADR o sección correspondiente, con la pregunta marcada como cerrada).
3. Ningún documento contiene afirmaciones de plataforma sin fuente o marcadas NO VERIFICADO en puntos que afecten decisiones core.

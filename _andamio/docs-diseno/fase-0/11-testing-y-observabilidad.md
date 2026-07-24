# Sigil — Testing y Observabilidad

**Documento:** 11 — Fase 0
**Estado:** **Aprobado** (2026-07-13 — visto bueno del equipo)
**Última actualización:** 2026-07-12
**Depende de:** docs 04 (§2 separación núcleo/cáscara, §3.3 autorización, §5 concurrencia, §7 pipeline), 05 (§8 matriz), 06 (matriz de transiciones), 08 (§7 monitoreo), 09 (gates, calendario operativo)

---

## 1. Principios

1. **Strict TDD** (activo en este proyecto): red → green → refactor; ninguna línea de producción sin un test rojo que la exija. Aplica a backend y frontend por igual.
2. **Cada regla de diseño mandatada tiene su test con nombre trazable** — los docs 04/06/07 ya declararon qué tests son obligatorios; este documento los inventaría (§4). La cobertura se mide contra ese inventario, no contra un % vacío.
3. **La pirámide se apoya en el núcleo puro**: la separación núcleo/cáscara del doc 04 §2 existe PARA esto — el 90% del motor (hash, PDF, TSA, imagen, reglas) se testea como clases puras sin tocar Dataverse ni mocks pesados.
4. Ningún test unitario se conecta a un ambiente vivo; la integración real vive en scripts contra Dev y en los gates (doc 09 §8).
5. **Prueba de existencia para TODO (regla del equipo, 2026-07-13):** todo componente implementado — incluido lo creado **manualmente en Power Platform** (publisher, solución, tablas, columnas, alternate keys, choices, roles, perfil FLS, variables de entorno, flows) — tiene una o varias pruebas que garantizan su existencia y creación correcta. Se materializa en la **suite de conformidad** (`tests/conformance/`): tests xUnit que leen la metadata del ambiente vía ServiceClient y verifican cada artefacto contra lo especificado en el doc 03. Son **rojos hasta que el componente existe** y verdes cuando se creó bien — TDD de infraestructura: el Runbook A se ejecuta test-first. Cada paso manual del checklist de Fase 1 referencia el ID del test que lo prueba. La suite corre a demanda contra Dev (config por variables de entorno del runner; se auto-omite si no hay conexión configurada — jamás finge verde) y es parte de los gates post-import (doc 09 §8).
6. **El antagonista, siempre (regla del equipo, 2026-07-13):** todo artefacto producido (código, configuración, documentos) pasa por revisión adversarial de lógica y coherencia antes de darse por terminado — el patrón de Fase 0, ahora permanente.

## 2. Stack de testing

| Capa | Herramienta | Qué cubre |
|------|-------------|-----------|
| Núcleo puro backend (`Sealing/`, `Crypto/`, `Pdf/`, `Imaging/`, `Domain/`) | **xUnit** solo — sin FakeXrmEasy | La mayoría de los tests: bytes entran, resultados salen |
| Orquestación de plugins (`Apis/`) | **xUnit + FakeXrmEasy 2.x** (`FakeXrmEasy.Plugins.v9 2.x`) — **CORRECCIÓN VERIFICADA:** la rama 2.x targetea **net462** (nuestro requisito — doc 04 §1); la 3.x targetea .NET 8 con el SDK ServiceClient (assembly `Microsoft.Xrm.Sdk` DISTINTO al de CrmSdk — conflicto de identidad de tipos); la 1.x es legacy abandonada | Contexto de ejecución, guards, autorización, efectos sobre tablas (`Initialize` para seed, `ExecutePluginWith<T>`, `CreateQuery` para verificar efectos — jamás asserts sobre mocks) |
| Frontend unit | **Vitest + Testing Library** | Wrappers de `api/` (base64↔Blob, validaciones espejo), hash hex, matemática de coordenadas del editor, completitud de recursos i18n |
| Frontend E2E | **Playwright** contra Dev | Flujos completos con la app real (crear → firmar → verificar) |
| Dispositivos | Matriz manual (doc 05 §8) | Safari iOS / Chrome Android reales |
| Integración de plataforma | Scripts contra Dev + **gates del doc 09 §8** | Lo que ningún fake simula: locks SQL reales, CSP, TSA desde el sandbox, pipelines |

**DECIDIDO (2026-07-15, decisión del equipo): STUB PROPIO — FakeXrmEasy queda descartado.** Contexto verificado que motivó la decisión: FakeXrmEasy 2.x+ tiene triple licencia (RPL 1.5 / Polyform NC / comercial) y para uso comercial interno de código cerrado las gratuitas NO aplican. La arquitectura núcleo/cáscara deja tan poca lógica en `Apis/` que un **stub propio de `IOrganizationService`** sobre las ~6 operaciones usadas (Create/Retrieve/RetrieveMultiple/Update/Execute/Delete + tracking de llamadas) cubre la capa de orquestación sin costo ni contrato. El stub vive en `Sigil.Plugins.Tests` (net462), se construye TDD como todo lo demás, y sus límites declarados no cambian (§3: locks SQL y file blocks siguen fuera — script de carrera y seam de archivos).

**Seam de columnas File (límite declarado):** FakeXrmEasy NO implementa los mensajes de file blocks (`InitializeFileBlocksUpload/Download` — pasos 1 y 7 del worker). La cáscara define una **interfaz propia de transferencia de archivos** (mockeable en tests; implementación real solo en producción) — sin este seam, el primer test del worker se estrella contra un mensaje no soportado.

**Reglas de estilo (del patrón del proyecto):** una clase de test por plugin; siempre camino feliz + camino `InvalidPluginExecutionException`; datos semilla con `Initialize`; verificación de efectos con `CreateQuery`, no con mocks.

## 3. Qué se testea dónde (decisiones explícitas de límites)

- **Locks de fila (doc 04 §5):** FakeXrmEasy NO simula locks de SQL. Se separa: (a) la **lógica** post-lock (releer, decidir, idempotencia) se testea unitariamente con interleavings simulados (dos ejecuciones sobre el mismo estado seed); (b) la **serialización real** se valida una vez en Dev con un script de carrera (N `SubmitSignature` concurrentes contra una transacción paralela de N firmantes → exactamente un Sellando, cero zombis). El script queda en el repo (`tests/integration/`) y corre en F2 y ante cambios del patrón de lock.
- **Pipeline de sellado (doc 04 §7):** cada paso es una clase pura testeable; la orquestación con fallos inyectados por paso (falla el upload → reintento → consistencia) se testea con FakeXrmEasy + un stub de TSA. El **presupuesto de tiempo** (doc 04 §7) se mide con el tracing por paso en Dev — no se "asume".
- **Cliente TSA (doc 04 §6.4):** unit con **stub de `HttpMessageHandler`** inyectado en el `HttpClient` (sin servidor ni puertos: cubre timeouts y status codes) + respuestas RFC 3161 fabricadas con `TimeStampTokenGenerator` de BouncyCastle (buenas, malas, sin certificado, nonce equivocado). El rechazo de `http://` es validación de configuración — testeable sin red. Integración real contra FreeTSA solo en Dev, en sesiones marcadas (rate limits).
- **CSP, descargas desde iframe, query params tras redirect:** NO testeables localmente — viven en los gates 5–7 (doc 09), adelantados a F3 (doc 10 §3).
- **Matriz de notificaciones (doc 08 §4) — límite declarado:** los flows no tienen harness de test unitario; su verificación es **manual guiada por checklist** (cada evento → cada destinatario → deep link correcto, ambos idiomas) en F4 + el heartbeat como verificación continua (gate 10). El checklist vive en el repo junto a las plantillas.
- **Playwright vs MFA (decisión requerida en F1):** doc 07 §6 recomienda MFA+Conditional Access para todos; el login automatizado choca con eso. Decisión a registrar en el aprovisionamiento de Dev: **exclusión de CA para las cuentas de prueba SOLO en Dev** (jamás en Test/Prod — la matriz de dispositivos en Test es manual con cuentas reales).
- **Regresión de los spikes:** las aserciones de `spikes/RESULTADOS.md` se convierten en tests permanentes — alfa preservado (`/SMask` presente + blend de píxeles exacto), y coordenadas en páginas rotadas 90/270 y con CropBox≠MediaBox (los PDFs de prueba del spike entran al repo como fixtures).

## 4. Inventario de tests OBLIGATORIOS (heredado de los documentos de diseño)

| # | Suite | Qué exige | Origen |
|---|-------|-----------|--------|
| M1 | Autorización negativa | **Un test negativo por cada fila** de la tabla de autorización (participante ajeno firma, no-creador cancela, usuario común invoca jobs, participante lee borrador no enviado…) | doc 04 §3.3, doc 07 A6 |
| M2 | Matriz de transiciones | Cada T1–T14 permitida (con su evento) Y cada prohibida rechazada con error limpio; **también P1–P4 + P2' (activación del siguiente turno) y la sub-máquina TSA del ledger** (la autoridad de doc 06 cubre las tres); el orden de borrado de T3 (eventos primero, verificable con `CreateQuery`) | doc 06 §1.1/§2/§2.1 |
| M3 | Precedencia de guards | Doble-click del último firmante (idempotencia antes que guard de estado); re-submit sobre Firmado = no-op | doc 06 §2 |
| M4 | Idempotencia del worker | Re-ejecución tras fallo inyectado en CADA paso; reintento zombi (post-image vieja + ledger existente) aborta sin tocar el archivo | doc 04 §7 |
| M5 | Sellado canónico | `hash_contenido` impreso = hash del PDF aprobado; `hash_final` = bytes exactos de `finalfile`; QR con txId y sin hash; **metadatos XMP escritos antes del hash final** (2026-07-13); **overflow de la hoja de cierre con 12+ firmantes**; verificación Verde/Rojo + evento tipo 11 + **verificación cruzada del historial** (`documenthash` de cada evento de firma == `contenthash`; `modifiedon==createdon`; versión de firma del lookup == snapshot) | ADR-011, doc 04 §6.2/§7, doc 03 §4.6 |
| M6 | Cliente TSA | CertReq=true, nonce aleatorio verificado en la respuesta, doble validación, fallback en orden, rate limit por endpoint, rechazo de `http://` | doc 04 §6.4 |
| M7 | Validación de entrada | Longitud antes de decodificar (**caso que prueba el ORDEN**: string sobre el límite con base64 inválido → error de tamaño, no de decodificación), magic bytes, PDF cifrado, firmas digitales previas, duplicados, órdenes con huecos, zonas huérfanas/fuera de rango | doc 04 §3.4 |
| M8 | Normalización de firma | Umbrales de alfa/contraste RMS/varianza Laplaciana con imágenes sintéticas límite; salida PNG RGBA 8-bit no entrelazado | ADR-009, doc 04 §4 |
| M9 | Coordenadas y zonas | Fixtures: rotada 90/270, CropBox≠MediaBox, multipágina; **completitud de zonas obligatorias** (Send bloqueado si un participante no tiene zona — RF-28) | doc 04 §6.1 (contrato) + §3.4 |
| M10 | Jobs | Filtro de recordatorios por estado de transacción (jamás sobre muertas); expiración solo en estados elegibles; saneamiento T14 a 24 h; ResealPending con TSA off → Sin sello TSA **+ evento 13 "TSA abandonada"**; reseal exitoso (token + evento 9); **ancla rota → `AnchorMismatchCount` (no se sella)**; **cap de lote por corrida** (presupuesto de 2 min). **Límites declarados de conformidad (2026-07-16):** T14 no es smokeable (modifiedon no se envejece por SDK; las 24 h no se esperan en CI — cubierto por unit M10); ResealPending E2E requiere un ledger en Re-sellado pendiente (forzable apagando/prendiendo TsaEnabled — corrida marcada, no en cada run). | docs 04 §3.1, 06 §3/§4 (R7) |
| M13 | Sharing | Efectos verificables de GrantAccess: `SendTransaction` comparte con participantes (+cascada a hijos existentes); cada evento nuevo se comparte explícitamente | doc 03 §2/§6 |
| M11 | Frontend espejo | Validaciones del wizard = espejo declarado de doc 04 §3.4; labels SIEMPRE desde i18n por nombre lógico (test de completitud es/en); binarios jamás en el caché de Query | doc 05 §2/§4.2/§5.2 |
| M12 | E2E | Playwright: crear→enviar→firmar (secuencial y paralelo)→sellar→verificar Verde→byte alterado→Rojo; rechazo; cancelación | doc 10 (salidas F2/F3) |

Convención de nombres: `M<suite>_<caso>` en backend, espejo en Playwright — el inventario es auditable con un grep.

## 5. CI (gates de PR — doc 09 §4)

- Backend: build + `dotnet test` (todas las suites unitarias M1–M10/M13) — rojo bloquea merge.
- Frontend: build + `vitest run` + **ESLint con regla que prohíbe `dangerouslySetInnerHTML`** (el enforcement mecánico del mandato de doc 07 A9) — ídem.
- Playwright: nightly contra Dev + a demanda pre-release (no en cada PR — depende de ambiente vivo).
- El script de carrera (§3) corre a demanda con etiqueta en el PR que toque locks/estados.

## 6. Observabilidad

### 6.1 Tracing del motor (reglas de doc 04, operacionalizadas)
- `ITracingService` en cada paso del pipeline: **duración por paso** (el presupuesto de doc 04 §7 se vigila con datos), IDs, longitudes, primeros 8 hex de hashes para correlación. **Jamás** PII, base64, tokens ni hashes completos.
- Habilitar el setting de **plugin trace log** del ambiente (Todas/Excepciones según ambiente: Dev=Todas, Prod=Excepciones) + retención revisada en el calendario operativo.

### 6.2 Correlación de incidentes
- El error genérico de la UI muestra un **ID de correlación** (doc 05 §9) que corresponde al `CorrelationId` del contexto del plugin → búsqueda directa en el trace log. Regla: todo mensaje de error técnico del backend incluye ese ID en el trace.

### 6.3 Telemetría de plataforma
- **Integración Dataverse → Application Insights** (export a nivel ambiente) — hechos **verificados**: existe (PPAC → Data export), pero exige (a) licencias Dataverse pagas en el tenant, (b) **Managed Environments** (en un ambiente no-managed la opción ni aparece — decisión de gobernanza con implicaciones de licencia que se suma al Runbook A si se adopta), (c) recurso propio de App Insights (costo de ingesta), y (d) **SLA de entrega de hasta 24 h** — es diagnóstico histórico, NO tiempo real. Recomendada para Prod si la organización ya opera Managed Environments; si no, el plugin trace log + eventos de negocio cubren el mínimo (decisión en F1 con estos cuatro hechos sobre la mesa). Fuentes: learn.microsoft.com/power-platform/admin/set-up-export-application-insights · analyze-telemetry.
- Frontend: **sin telemetría externa** (CSP — doc 05 §1). Los errores de UI se diagnostican por el ID de correlación; si a futuro se necesita telemetría de cliente, la única vía compatible es un endpoint propio vía Custom API (decisión futura, no ahora).

### 6.4 Rutinas (consolidadas — el calendario vive en doc 09 §10)
| Señal | Mecanismo | Cadencia |
|-------|-----------|----------|
| Fallos de flows | Heartbeat diario + reenvío del buzón de servicio + revisión semanal | doc 08 §7 |
| Errores del motor | Plugin trace log (Excepciones) + App Insights si está activo | Continua; revisión semanal |
| Presupuesto del worker | Duraciones por paso en trace | En cada release (comparar contra doc 04 §7) |
| Capacidad File/Log | PPAC | Mensual (doc 09 §10) |
| Transacciones zombis / Error de Sellado acumulados | Vista de operador en el dashboard (doc 05 §4.1) + evento 8 | Semanal |

## 7. Trazabilidad

| Mandato | Sección |
|---------|---------|
| Strict TDD del proyecto | §1, §5 |
| Tests negativos por regla de autorización (docs 04/07) | M1 |
| Tests de concurrencia (doc 04 §5) | §3 + M3/M4 + script de carrera |
| Casos de prueba obligatorios de coordenadas (doc 04 §6.1) | M9 |
| Matriz doc 06 = suite M2 | M2 |
| Reglas de tracing (doc 04 §2/§6) | §6.1 |
| Monitoreo de flows (doc 08 §7) | §6.4 |
| Matriz de dispositivos (doc 05 §8) | §2 |

---

*Anterior: [10-hoja-de-ruta.md](10-hoja-de-ruta.md) · Este es el último documento de la Fase 0.*

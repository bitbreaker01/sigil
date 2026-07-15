# Sigil — Decisiones de Arquitectura (ADRs)

**Documento:** 02 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — ADRs 001–011 con visto bueno del equipo)
**Última actualización:** 2026-07-10

Cada decisión sigue el formato ADR: contexto → decisión → consecuencias. Las afirmaciones sobre la plataforma fueron **verificadas contra documentación oficial en julio 2026**; las fuentes están al pie de cada ADR. Los ADR marcados **[PENDIENTE DE CONFIRMACIÓN]** tienen una recomendación pero requieren visto bueno del equipo (referencian las preguntas Q-xx del documento 01).

---

## ADR-001 — El frontend es una Power Apps Code App (React + TypeScript + Vite)

**Estado:** Aceptada (decisión del equipo)

**Contexto.** V1 y V2 asumían Canvas App. El equipo decidió usar **Code Apps**, la modalidad code-first de Power Apps: una SPA en TypeScript hosteada por la plataforma, con acceso a conectores y Dataverse mediante el SDK `@microsoft/power-apps`. Code Apps es **GA desde el 5 de febrero de 2026**.

**Decisión.** Frontend = Code App con **React 18 + TypeScript + Vite**, partiendo del template oficial. El SDK oficial es `@microsoft/power-apps`; la configuración vive en `power.config.json`.

**Hechos verificados que condicionan el diseño:**

| Hecho | Implicancia para Sigil |
|-------|------------------------|
| Los usuarios finales necesitan licencia **Power Apps Premium** para ejecutar cualquier code app. | Sin impacto: la organización ya posee Premium per User (RNF-01). |
| El **mobile player nativo NO soporta code apps** (backlog sin ETA); tampoco Power Apps for Windows. | La experiencia móvil (RF-23) es vía **navegador móvil**. La UI debe ser responsiva y probarse en Safari/Chrome móvil. |
| **Deep links solo por query params** (`https://apps.powerapps.com/play/e/{envId}/a/{appId}?param=valor`); no hay routing por path en la app publicada. Los parámetros se leen con `getContext()` → `app.queryParams`. | El routing interno usa query params (ej. `?screen=sign&txId=<guid>`); React Router en modo memoria o hash resuelto en cliente. Las notificaciones (RF-11) construyen estos links. |
| **CSP estricta desde enero 2026**: por defecto `connect-src 'none'` — el frontend no puede llamar orígenes externos. | Toda integración externa (TSA, AI Vision) vive en el **backend C#**, jamás en el frontend. No dependemos de excepciones de CSP. |
| Identidad disponible en el cliente vía `getContext()`: `user.fullName`, `objectId`, `tenantId`, `userPrincipalName`. SSO gestionado por el host con Entra ID. | Cubre RF-18 en la capa UI. La identidad **autoritativa** para la estampa se toma igual en el backend (contexto del plugin), nunca confiando solo en el cliente. |
| Las Custom APIs (bound/unbound) se consumen con cliente tipado generado por el **npm CLI** (`power-apps add-dataverse-api`, SDK ≥ 1.1.1). `pac code add-data-source` **no** soporta operation schemas. | Ver ADR-010. |
| Sin soporte de Excel Online como conector; sin Git integration ni solution packager para el código fuente. | El código fuente del frontend vive en nuestro repo Git propio; el push a la plataforma es un artefacto de build (ver documento de ALM). |

**Consecuencias.**
- (+) UI profesional con React, testing real (Vitest/Playwright), CI/CD de verdad, sin las limitaciones de expresividad de Canvas.
- (+) Visor de PDF y drag & drop implementables con librerías estándar (ver documento de frontend).
- (−) Perdemos el control PDF nativo y el Pen Input de Canvas (irrelevante: V2 ya eliminó el Pen Input por documento).
- (−) Sin app móvil nativa: mitigación con diseño responsive + navegador.

**Fuentes:** learn.microsoft.com/power-apps/developer/code-apps/overview · /architecture · /how-to/retrieve-context · /how-to/content-security-policy · blog GA de Power Platform (05-feb-2026) · GitHub microsoft/PowerAppsCodeApps (discussion #286: sin mobile player).

---

## ADR-002 — El backend es un motor de plugins C# expuesto como Custom APIs de Dataverse

**Estado:** Aceptada (V2.0)

**Contexto.** La manipulación binaria (PDF, hash, TSA, QR) excede lo razonable para Power Automate: costos por acción, timeouts, dificultad para criptografía de bajo nivel y para llamar APIs RFC 3161.

**Decisión.** Todo el procesamiento binario y criptográfico se implementa en **plugins C# (.NET Framework 4.6.2 para plugins de Dataverse)** registrados detrás de **Custom APIs** con parámetros tipados de request/response. La Code App consume esas Custom APIs con clientes tipados (ADR-010).

Superficie inicial de Custom APIs (se detalla en el documento 04):

| Custom API | Tipo | Rol |
|------------|------|-----|
| `sanic_sigil_capi_ValidateMasterSignature` | Unbound, síncrona | Valida (cómputo local — ADR-009) y normaliza la imagen de firma (RF-01/02) |
| `sanic_sigil_capi_SubmitSignature` | Bound a la transacción, síncrona-corta | Registra la intención de firma y dispara el procesamiento asíncrono (RF-04) |
| `sanic_sigil_capi_VerifyDocument` | Unbound, síncrona | Contrasta un hash contra el ledger y devuelve veredicto (RF-20) |
| `sanic_sigil_capi_CreateTransaction` | Unbound, síncrona-corta | Crea la transacción y sus participantes (RF-26), recibe el PDF a firmar (RF-25 — única vía de ingesta, C-11), valida que sea un PDF bien formado y registra las posiciones de firma si el creador las definió (RF-28) |

**Nota (2026-07-10):** la conversión Word→PDF fue eliminada del alcance (C-11 / Q-07 cerrada): la ingesta es exclusivamente PDF. Desapareció el mayor riesgo técnico del backend; la validación de ingesta se reduce a verificar estructura PDF y límites de tamaño (a definir en doc 04).

**Consecuencias.**
- (+) Control total de la criptografía (SHA-256, RFC 3161), librería PDF de nivel industrial, testeable con FakeXrmEasy.
- (+) Las Custom APIs son el contrato formal entre frontend y backend: versionables, asegurables por privilegio, invocables también desde otros clientes (Power Automate, integraciones futuras).
- (−) Requiere disciplina de empaquetado (solución, registro de steps) y una librería PDF compatible con el sandbox de plugins (ver documento 04: la selección de librería PDF es una decisión técnica crítica con verificación de compatibilidad sandbox — ilmerge/ILRepack o dependencias self-contained).
- (−) El sandbox de plugins impone límite de **2 minutos** (síncronos y asíncronos); refuerza ADR-008.

---

## ADR-003 — Power Automate queda relegado exclusivamente a notificaciones y recordatorios

**Estado:** Aceptada con alcance acotado

**Contexto.** V2 "abandona Power Automate para operaciones binarias complejas" pero no dice con qué se notifica. Las opciones para RF-05/11/12 (Teams cards, Outlook, recordatorios) son: (a) flows de Power Automate disparados por eventos de Dataverse; (b) el plugin C# llama Microsoft Graph directamente.

**Decisión.** **Power Automate solo para la capa de notificaciones**: flows disparados por cambios de estado en Dataverse (Create/Update de la tabla de participantes) que envían correo Outlook y Adaptive Cards en Teams con los deep links; más un flow recurrente diario para recordatorios (RF-12). Nada binario, nada criptográfico pasa por flows.

**Alternativa descartada (Graph desde el plugin):** exige app registration con permisos `Mail.Send`/`ChannelMessage.Send` application-level, manejo de secretos en plugins y reimplementar Adaptive Cards a mano. Los conectores de Teams/Outlook en flows resuelven esto de fábrica con conexiones administradas y connection references para ALM. Se re-evalúa solo si los flows muestran latencia o fragilidad inaceptables.

**Consecuencias.**
- (+) Separación limpia: el motor C# no conoce canales de notificación; solo transiciona estados. Los flows reaccionan a los estados.
- (+) Adaptive Cards y Outlook con conectores estándar, deployables con connection references.
- (−) Dependencia de flows en el paquete ALM (connection references a configurar por ambiente).

---

## ADR-004 — Almacenamiento de documentos: exclusivamente Dataverse (columnas de archivo)

**Estado:** Aceptada (Q-03/Q-04 cerradas el 2026-07-10 — **SharePoint está prohibido por política organizacional**)

**Contexto.** V1 pedía biblioteca SharePoint de solo lectura para los PDF completados (RF-24), pero la organización prohíbe SharePoint (RNF-07). El plugin C# necesita leer/escribir el binario del PDF durante el procesamiento; el ledger y la seguridad viven en Dataverse.

**Decisión.**
1. **Única fuente y único medio: columna de archivo (File column) en Dataverse**, en la tabla de documentos, dentro del mismo boundary de seguridad que el ledger. El plugin lee y escribe el binario nativamente, la retención se controla con los mismos roles, y el archivo queda amarrado 1:1 al registro del ledger.
2. El **repositorio de solo lectura** de RF-24 se materializa como vistas de la Code App sobre documentos completados: el "solo lectura" lo imponen los **roles de seguridad** (usuarios sin Update/Delete sobre documentos completados; la inmutabilidad funcional del binario la garantiza además el hash del ledger).

**Consecuencias.**
- (+) El binario que respalda el hash del ledger nunca sale del control de Dataverse; un solo boundary de seguridad, cero credenciales externas.
- (+) Simplifica ALM y el modelo de amenazas (no hay segundo sistema con permisos propios).
- (−) El almacenamiento **File de Dataverse cuesta más por GB** que alternativas de archivo; al no existir alternativa permitida, el doc 03 debe: (a) definir **límite de tamaño por PDF**, (b) estimar consumo de capacidad File con los volúmenes reales cuando existan, y (c) definir política de retención/archivado dentro de Dataverse si el volumen lo exige.

---

## ADR-005 — Sellado de tiempo con TSA externa RFC 3161, como característica configurable (feature flag por ambiente)

**Estado:** **Aceptada** (Q-01 cerrada el 2026-07-10 — visto bueno del equipo, con la condición de que la TSA sea encendible/apagable)

**Contexto.** V2 exige validez temporal avalada por un tercero imparcial (RNF-02: anti-repudio comprobable "ante terceros"; ni siquiera un admin puede fabricar un timestamp). El equipo preguntó: ¿es realmente necesaria la TSA? ¿Hay gratuitas viables? ¿Qué alternativas existen?

### Qué prueba cada mecanismo (el matiz que decide todo)

| Mecanismo | Qué prueba | Qué NO prueba |
|-----------|-----------|----------------|
| **TSA RFC 3161** (tercero externo) | Que el hash existía en un instante dado, firmado por un tercero imparcial — oponible fuera de la organización | — |
| **Azure Blob immutable (WORM, locked policy)** | Que nadie de la organización (ni admins, ni soporte de Microsoft) alteró el registro después de escrito | La hora ante terceros: el ancla vive en el propio tenant |
| **Azure Confidential Ledger** | Ídem WORM + receipts criptográficos verificables offline | Ídem; costo fijo ~USD 90–110/mes por instancia |
| **Auditoría Dataverse** | Log de aplicación con actor/fecha | Nada criptográfico: un log, no evidencia tamper-evident |

**Verificado:** Dataverse NO tiene ledger tables ni timestamping criptográfico nativo (2026). Su auditing es log de aplicación, no evidencia criptográfica.

### TSAs gratuitas — estado verificado (jul-2026)

| Endpoint | Estado | Advertencia |
|----------|--------|-------------|
| `timestamp.digicert.com` | Operativo, responde a cualquier request TSP, SHA-256 OK | Sin ToS ni SLA publicados (tolerancia de facto, no contrato) |
| `timestamp.sectigo.com` | Operativo | Pide ≥15 s entre requests en uso automatizado |
| `time.certum.pl` | Operativo | Su política: gratis para personas físicas/non-profit; **empresas según pricelist** — uso corporativo gratuito no autorizado |
| `freetsa.org` | Operativo | Operador individual, sin entidad ni SLA — solo desarrollo |

Pagas con precio público para nuestro volumen: **DigiStamp ~USD 25–45/mes** (100–200 sellos). GlobalSign/DigiCert/Certum qualified: precio solo vía ventas.

### Decisión (aprobada 2026-07-10)

1. **La TSA es una característica configurable por ambiente — encendida o apagada** (decisión del equipo al cerrar Q-01). El interruptor y su configuración viven en **variables de entorno de Dataverse** (solution-aware, viajan por pipelines — RNF-05): `sanic_sigil_env_TsaEnabled` (sí/no), `sanic_sigil_env_TsaEndpoints` (lista ordenada primario→fallbacks). El plugin del pipeline de sellado (ADR-011, paso 3) consulta el flag en cada ejecución; nada del motor asume que la TSA existe.
2. **Con TSA encendida:** endpoints gratuitos — primario `timestamp.digicert.com`, fallback `timestamp.sectigo.com` (respetando su throttle). Riesgo aceptado y documentado: sin SLA contractual. Si el negocio exige SLA, DigiStamp (~USD 300–540/año) es la vía paga verificable más barata; cambiar de proveedor es editar la variable de entorno, sin tocar código.
3. **Degradación elegante (con TSA encendida):** si todos los endpoints fallan, el sellado se completa **sin token TSA**, el registro queda marcado `re-sellado pendiente` y un job reintenta hasta obtener el token (el hash final ya está fijo en el ledger; el token puede llegar más tarde sin tocar los bytes del PDF — C-10 lo permite justamente porque el token no se incrusta).
4. **Con TSA apagada:** el pipeline sella igual (hashes + ledger + FLS + auditoría); el registro queda marcado explícitamente `sin sello TSA`. La cadena anti-repudio se reduce a tamper-evidence interna (ADR-006) — el nivel de evidencia SIEMPRE queda registrado por transacción, nunca implícito.
5. **Nivel de evidencia visible:** la pantalla de verificación (ADR-007) muestra si el documento tiene sello TSA o no. Apagar la TSA **no** es retroactivo: los tokens ya obtenidos se conservan; encenderla tampoco re-sella transacciones viejas automáticamente (el job de re-sellado solo procesa los marcados `re-sellado pendiente`).
6. **Extensión opcional (a evaluar en doc 07):** anclar periódicamente los registros del ledger (hashes + tokens) en **Azure Blob immutable con locked policy** — centavos por mes — como segunda capa anti-admin. Nota: requiere excepción explícita a RNF-07 (solo aplicaría a *copias de evidencia*, jamás a documentos).

**Nota sobre RNF-02:** con la TSA apagada, la evidencia "comprobable ante terceros" de RNF-02 no se cumple en esas transacciones — es una decisión operativa consciente del negocio, visible por transacción en el nivel de evidencia. RNF-02 se reinterpreta como: *la plataforma DEBE ser capaz de producir evidencia ante terceros cuando la característica está encendida*.

**Persistencia del token (sin cambios):** el token RFC 3161 se solicita **sobre el hash final** (ADR-011) y se persiste **exclusivamente en el ledger — NO se incrusta en el PDF** (C-10), base64 en columna de texto multilínea con FLS (attachments/Notes no admiten FLS; tamaño máximo de columna a verificar en doc 03). La pantalla de verificación permite descargarlo para validación independiente.

**Puntos técnicos para el documento 04:**
- El sandbox de plugins permite HTTPS saliente — viable, pero hay que presupuestar latencia y reintentos dentro del límite de 2 min del worker (ADR-008).
- `timestamp.digicert.com` dejó de aceptar SHA-384/512 en 2025; **SHA-256 funciona** — alineado con nuestro estándar.
- FreeTSA solo para desarrollo local.

**Fuentes:** knowledge.digicert.com (RFC3161 TSA server) · FAQ Sectigo Time Stamp Server · Certum TSA Policy (CERTUM-CA-TSA-CP) · digistamp.com/subpage/price · learn.microsoft.com: immutable-storage-overview, confidential-ledger/overview, manage-dataverse-auditing · azure.com pricing (blob, ACL).

---

## ADR-006 — Protección del ledger: roles restrictivos + Field-Level Security + auditoría de Dataverse

**Estado:** Aceptada (V1 + V2 combinadas)

**Decisión.** Defensa en tres capas sobre la tabla del ledger:
1. **Roles de seguridad:** usuarios ordinarios **solo Read** — sin Create, Update ni Delete. Todos los registros del ledger los crea exclusivamente el contexto de sistema desde los plugins (dar Create a usuarios permitiría insertar registros espurios y debilitaría el valor probatorio del ledger). Esto **endurece** el modelo de V1 (que pedía Append/Write para usuarios): como toda escritura pasa por Custom APIs, los usuarios no necesitan privilegios directos de escritura.
2. **Column security / FLS (V2, con corrección verificada):** las columnas críticas (hash de contenido, hash final, token TSA, timestamp de sellado) se aseguran con perfil de column security donde solo el usuario de aplicación (Service Principal) bajo el que corren los plugins tiene Create/Update/Read. Los perfiles actuales tienen cuatro permisos: *Read, Read unmasked, Update, Create*.
   **HECHO VERIFICADO (jul-2026) que corrige a V2:** la column security **NO aplica a usuarios con rol System Administrator** — la documentación oficial es explícita: "Column-level security doesn't apply for users who have the system administrator role". La promesa de V2 ("ningún usuario incluyendo Global Admins") es **técnicamente imposible** a nivel FLS. FLS protege contra todos los usuarios y admins delegados **excepto sysadmins**; contra el sysadmin malicioso, la única defensa real es la capa 4.
3. **Auditoría nativa de Dataverse activada** sobre la tabla: toda modificación efectuada queda registrada con actor y timestamp (nota verificada: audita operaciones *realizadas*, no intentos bloqueados; consume capacidad Log).
4. **TSA (ADR-005), la capa anti-sysadmin:** el token TSA externo hace detectable cualquier hash re-escrito — el token sella el hash original ante un tercero fuera del alcance del tenant. FLS + roles + auditoría elevan el costo y la trazabilidad del ataque interno; la TSA lo vuelve evidente. Con la TSA apagada (RF-29), esta capa no existe y el nivel de evidencia lo dice explícitamente. Cadena de confianza completa a desarrollar en el documento 07.

**Fuente:** learn.microsoft.com/power-platform/admin/field-level-security (secciones "Which columns can be secured?" y comportamiento con system administrator).

---

## ADR-007 — Verificación por QR: el QR deep-linkea a la pantalla de verificación; el hash se recalcula client-side

**Estado:** Aceptada con corrección técnica sobre V2 **[recomendación sobre Q-02 incluida]**

**Contexto — dos problemas que V2.0 no resuelve:**

1. **El problema del huevo y la gallina.** Si el QR se incrusta en el PDF y el QR contuviera el hash final del documento, sería imposible: incrustar el QR cambia los bytes → cambia el hash. **Resolución:** el hash del ledger se calcula sobre el PDF **final y completo** (con estampa y QR ya incrustados), y el QR contiene únicamente el **ID de la transacción** y la URL de verificación — nunca el hash.
2. **Escanear un QR no puede verificar el archivo que tenés en la mano.** El QR lleva a una URL; eso solo prueba que *existe* un registro en el ledger. Para saber si TU copia del PDF es íntegra, hay que **recalcular el hash de esos bytes** y compararlo. V2 eliminó el portal de carga, pero la física no negocia: sin recomputar el hash no hay verificación de integridad.

**Decisión.**
- El QR contiene: `https://apps.powerapps.com/play/e/{envId}/a/{appId}?screen=verify&txId={guid}`.
- Al escanearlo, la Code App abre la **pantalla de verificación** precargada con la transacción: muestra los metadatos del ledger (documento, firmantes, fecha de sellado, estado) — esto ya es útil como constancia.
- Para la verificación de integridad byte a byte, la misma pantalla ofrece **drag & drop del PDF**: el hash SHA-256 se calcula **en el navegador con Web Crypto API** (`crypto.subtle.digest`) — el archivo **no se sube a ningún lado** — y se envía solo el hash a `sanic_sigil_capi_VerifyDocument`, que lo contrasta contra el ledger y devuelve **Verde (íntegro) / Rojo (alterado)**.
- Esto **fusiona el portal de carga de V1 con el QR de V2** (resolución C-04) pero sin transferencia de archivos: un solo mecanismo cubre el objetivo de validar descargas viejas.
- Nota para el doc 05: **verificar que los query params sobreviven al redirect de autenticación de Entra ID** al abrir el link desde un dispositivo sin sesión activa (móvil que escanea el QR).

**Sobre Q-02 (¿quién puede escanear?):** con esta decisión el verificador necesita licencia Premium y login corporativo (es una pantalla de la code app). Para uso interno es aceptable y es la **recomendación**. Si a futuro se exigiera verificación sin licencia (ej. auditores externos), la alternativa es un endpoint anónimo (Azure Function + Service Principal de solo lectura contra el ledger) — se documenta como extensión, no se construye ahora.

**Consecuencias.**
- (+) Privacidad: el documento nunca viaja para verificarse; solo viaja su hash.
- (+) Sin límites de tamaño de payload en conectores (el hash son 64 caracteres hex).
- (+) El mismo flujo sirve para archivos descargados hace meses (RF-21).
- (−) El cálculo client-side requiere navegador moderno (Web Crypto es estándar — riesgo bajo).

---

## ADR-008 — Modelo de ejecución asíncrona dirigida por estados

**Estado:** Aceptada (V2)

**Contexto.** El pipeline de sellado (rellenar plantilla, incrustar firma + estampa + QR, hash, TSA, persistir, notificar) puede tomar decenas de segundos. **Advertencia verificable:** el límite de 2 minutos del sandbox aplica a plugins síncronos **y asíncronos** — la asincronía libera al usuario pero no amplía el presupuesto de ejecución. El doc 04 debe citar la fuente oficial, presupuestar cada etapa del pipeline, y si no cabe con margen (TSA con reintentos + render PDF), mover el worker pesado a una **Azure Function** disparada por Service Bus / webhook, manteniendo el mismo modelo de estados.

**Decisión.** Patrón **estado → evento → worker asíncrono**:
1. `sanic_sigil_capi_SubmitSignature` (síncrona, milisegundos): valida precondiciones (turno del firmante, estado de la transacción, Firma Maestra configurada), registra la intención de firma con la identidad del contexto de ejecución y transiciona el estado del participante a *Procesando*.
2. Esa transición dispara un **plugin asíncrono** (o job del sistema) que ejecuta el pipeline pesado de sellado.
3. Al terminar, el worker transiciona el estado final (*Firmado* / y si corresponde *Completado* a nivel transacción), lo que a su vez dispara los flows de notificación (ADR-003).
4. La UI refleja *Procesando…* vía polling liviano del estado (el dashboard ya consulta Dataverse) — sin websockets ni complejidad extra en esta fase.

**Manejo de fallas:** los plugins asíncronos de Dataverse tienen reintentos del sistema; los fallos definitivos dejan al participante en estado *Error de Procesamiento* (visible en dashboard, re-disparable). El detalle va en el documento 04/06.

---

## ADR-009 — Validación y normalización de la Firma Maestra: cómputo 100% local en el plugin C# (Azure AI Vision descartado con evidencia)

**Estado:** Aceptada (V2 + Q-05 cerrada el 2026-07-10; herramienta resuelta el 2026-07-10 con verificación)

**Resolución de herramienta (verificada jul-2026):** Azure AI Vision **no ofrece métricas de calidad de imagen** — ni blur/nitidez, ni contraste, ni exposición existen en Image Analysis v4.0 ni v3.2 (confirmado en la documentación oficial de features y en Q&A de Microsoft). Además, **Image Analysis 4.0 está deprecado con retiro el 25-sep-2028**. Conclusión: los tres parámetros de V2 se computan **localmente en el plugin** sobre los píxeles del PNG (decodificados con ImageSharp): canal alfa por inspección directa, contraste RMS por histograma, nitidez por varianza de Laplaciano. Esto elimina el servicio cognitivo, el secreto/managed identity y una llamada HTTP del pipeline — V2 pedía "Validación de IA"; lo que el requerimiento realmente exige (RF-02) es validación *automática* de esos tres parámetros, y eso se cumple de forma determinística, más barata y sin dependencias. Umbrales de aceptación: configurables en `sanic_sigil_env_SignatureImageSpec`, calibrados en la implementación (doc 04).

**Decisión.** La Custom API `sanic_sigil_capi_ValidateMasterSignature` recibe la imagen (base64) y el **plugin C#** ejecuta dos etapas, ambas locales:

1. **Validación** de los tres parámetros de V2 (transparencia/canal alfa, contraste, nitidez) por cómputo directo sobre los píxeles (ver resolución de herramienta arriba). Si falla, se rechaza con el motivo específico (RF-02).
2. **Normalización** (Q-05, 2026-07-10): la imagen aprobada se redimensiona a las **dimensiones estándar de Sigil** (configuración, no hardcodeadas), se convierte a **PNG con fondo transparente** y se limita su peso; la versión normalizada se almacena como **nueva versión vigente** del historial de firmas del usuario (versionado — decisión 2026-07-13, modelo en doc 03) y es la que se incrusta en los PDFs.

**Notas técnicas:**
- La regla que decidió esto: **si un parámetro puede evaluarse de forma determinística en C# sin servicio externo, se evalúa en C#** (menos costo, menos latencia, menos dependencias). Verificado que aplica a los tres parámetros.
- Al no haber servicio externo, **no hay secreto que gestionar** para esta función. Queda registrado igual el hecho verificado (jul-2026) por si el futuro trae integraciones: las variables de entorno tipo *secret* (Key Vault) **NO son legibles desde plugins** (limitadas a flows, Copilot Studio y custom connectors); la vía correcta para plugins → Azure es **managed identity (GA desde ago-2025)**. Fuentes: learn.microsoft.com/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets · learn.microsoft.com/power-platform/admin/set-up-managed-identity.

**Fuentes del descarte de AI Vision:** learn.microsoft.com/azure/ai-services/computer-vision/overview-image-analysis (features v4.0/v3.2 — sin métricas de calidad) · learn.microsoft.com/azure/ai-foundry/responsible-ai/computer-vision/image-analysis-characteristics-and-limitations (deprecación, retiro 25-sep-2028).

---

## ADR-010 — Tooling: npm CLI (`power-apps`) para el frontend; `pac` para solución y ALM

**Estado:** Aceptada (condicionada por la plataforma)

**Contexto verificado.** El grupo `pac code` está en camino de deprecación a favor del npm CLI del SDK (`power-apps init/run/push`), que sigue marcado *preview* pese al GA del producto. Crítico para Sigil: **solo el npm CLI (`power-apps add-dataverse-api`, SDK ≥ 1.1.1) genera clientes tipados para Custom APIs** (bound y unbound); `pac code add-data-source` no reconoce los operation schemas y los saltea.

**Decisión.**
- **Frontend:** npm CLI `power-apps` para init/run/push y para generar los servicios tipados de tablas y Custom APIs (`find-dataverse-api` / `add-dataverse-api`). Asumimos su estado preview como riesgo aceptado y documentado (es la única vía soportada para nuestro caso de uso core).
- **Backend/ALM:** `pac` CLI para todo lo demás: soluciones, registro de plugins (`pac plugin`), pipelines, connection references (`-cr` disponible desde PAC CLI 1.51.1).
- El push de la app se asocia a la solución de Sigil (`--solutionName` / preferred solution) para que la code app viaje en el paquete ALM (RNF-05).

**Riesgo registrado:** breaking changes del npm CLI en preview → se fija versión exacta en `package.json` y se documenta la versión probada en cada release.

**Fuentes:** learn.microsoft.com/power-apps/developer/code-apps/how-to/add-dataverse-action-function · /how-to/npm-quickstart · /how-to/alm · referencia PAC CLI `code`.

---

## ADR-011 — Definición canónica del sellado: qué bytes sella cada hash y en qué orden

**Estado:** Aceptada (corrige inconsistencias entre V1, V2 y los ADRs 005/006/007)

**Contexto.** Tres requerimientos heredados chocan con una realidad física: **cualquier cosa que se agregue al PDF después de calcular un hash, invalida ese hash**. V1 pedía imprimir el hash en la estampa; V2 pedía incrustar el token TSA en el PDF; y la verificación de ADR-007 recalcula el hash del archivo completo. Las tres cosas juntas son imposibles. Este ADR define el esquema canónico que las reconcilia.

**Decisión — esquema de doble hash y pipeline de sellado en orden estricto:**

El sellado ocurre **una única vez, al completarse la última firma** de la transacción (no hay PDFs intermedios oficiales; los estados parciales viven solo como datos en Dataverse). La hoja de cierre es consolidada: **una página, con overflow a páginas adicionales** si la cantidad de firmantes no entra en una (criptográficamente inocuo: toda la hoja de cierre se genera antes de `hash_final`).

**Firmas visuales dentro del documento (RF-28, 2026-07-10):** además de la hoja de cierre, la imagen de firma normalizada de cada firmante se incrusta **dentro de las páginas de contenido**, en la posición estándar configurada por Sigil o en las posiciones (página + coordenadas) que el creador definió por firmante al armar la transacción. Esta incrustación ocurre en el paso 2 del pipeline — **después** de calcular `hash_contenido`, que sella el PDF tal como fue subido y aprobado.

```
1. PDF de contenido        = el PDF subido por el creador, tal como lo
                             revisaron y aprobaron los firmantes
                             → hash_contenido = SHA-256(bytes del PDF de contenido)

2. PDF final               = PDF de contenido
                             + imágenes de firma incrustadas en sus posiciones
                               (estándar de Sigil o definidas por el creador — RF-28)
                             + hoja de cierre consolidada:
                                 · estampa por firmante (imagen de firma,
                                   nombre, correo, timestamp UTC)
                                 · hash_contenido en texto claro   ← posible: ya existe
                                 · ID de transacción del ledger
                                 · código QR (URL verify + txId)   ← nunca contiene hash
                             → hash_final = SHA-256(bytes del PDF final completo)

3. Token TSA               = RFC3161(hash_final)                  ← sella el ancla de verificación
                             → NO se incrusta: se persiste en el ledger (C-10)

4. Registro de ledger      = { txId, hash_contenido, hash_final, token TSA,
                               metadatos de firmantes } — escrito por contexto
                             de sistema, columnas críticas bajo FLS (ADR-006)

5. Persistencia del PDF    = columna File de Dataverse — único medio (ADR-004)
                             → después de este punto, NADA vuelve a tocar los bytes
```

**Qué prueba cada pieza:**
- `hash_contenido` — que las páginas que los firmantes aprobaron no cambiaron. Es el hash impreso en la hoja de cierre (C-09): puede imprimirse porque se calcula *antes* de incrustar firmas y generar esa hoja. Nota: las imágenes de firma incrustadas (RF-28) son posteriores a `hash_contenido` por diseño — lo firmado es el contenido, la imagen es representación visual; la evidencia de QUIÉN firmó vive en el ledger y la hoja de cierre, no en la imagen.
- `hash_final` — que el archivo distribuido, byte a byte, es el original. Es el ancla contra la que verifica el drag & drop de ADR-007.
- Token TSA sobre `hash_final` — que ese archivo existía en ese instante, avalado por un tercero. Si un admin reescribiera el ledger, el token en su poder no coincidiría (cadena anti-repudio de ADR-006).

**Consecuencias.**
- (+) RF-15, RF-16, RF-19, RF-20 y ADR-005/006/007 quedan mutuamente consistentes.
- (+) Verificación client-side trivial: SHA-256 del archivo completo, sin byte-ranges ni parsing de PDF.
- (−) Renunciamos a incrustar el token TSA como firma PAdES/DocTimeStamp dentro del PDF (verificable por Adobe Reader). Tradeoff aceptado: la verificación oficial es la de Sigil (QR + ledger). **Precisión registrada (2026-07-13):** PAdES es **apilable** sobre este modelo sin romperlo (el sello se incrusta ANTES del hash final — ambas verificaciones coexistirían); quedó como **extensión futura** con prerequisitos anotados en doc 01 §5, descartada para la primera versión por decisión del equipo.
- (−) Los flujos secuenciales no producen documento sellado hasta completarse — un participante que "descarga" durante el proceso obtiene el PDF de contenido sin valor oficial (RF-21).

---

## Resumen de decisiones y dependencias

```
ADR-001 Code App (React+TS, mobile browser) ──┐
ADR-002 Plugins C# + Custom APIs (solo PDF)  ──┼── contrato: clientes tipados (ADR-010)
ADR-003 Power Automate solo notifica         ──┤
ADR-004 Archivos SOLO en Dataverse           ──┤   (SharePoint prohibido — RNF-07)
ADR-005 TSA RFC 3161 (feature flag, no incrustado) ──┼── cadena anti-repudio (ADR-006)
ADR-006 Roles solo-Read + FLS + Audit        ──┤
ADR-007 QR → verify + hash client-side       ──┤
ADR-008 Asíncrono dirigido por estados       ──┤
ADR-009 Validación + normalización de firma  ──┤
ADR-011 Sellado canónico + posiciones RF-28  ──┴── reconcilia 005/006/007 + C-09/C-10
```

*Documento anterior: [01-vision-y-alcance.md](01-vision-y-alcance.md) · Siguiente: modelo de datos Dataverse (03).*

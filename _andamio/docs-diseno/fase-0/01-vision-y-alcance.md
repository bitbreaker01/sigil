# Sigil — Visión y Alcance Consolidado

**Documento:** 01 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — todas las preguntas Q-01..Q-07 cerradas)
**Última actualización:** 2026-07-10
**Fuentes de origen:** `Especificaciones_Sigil.md` (V1), `Sigil_GuiaDelForo.md` (V2.0 — Aprobado para Ejecución), decisión del equipo de usar **Power Apps Code App** como frontend.

---

## 1. Propósito

Sigil (nombre definitivo del proyecto; V2 lo llamaba "Vanguard / CoreSign") es una plataforma **interna** de firma digital con validación criptográfica para el ecosistema Microsoft 365 / Power Platform. Permite:

1. Firmar documentos PDF con una experiencia de "firma a un clic" respaldada por la identidad corporativa (Microsoft Entra ID).
2. Garantizar **integridad e inalterabilidad** de los documentos firmados mediante hash SHA-256, sellado de tiempo de terceros (TSA RFC 3161) y un ledger inalterable en Dataverse.
3. Verificar en cualquier momento —incluso meses después de descargado el archivo— que un PDF sigue siendo la versión original, mediante un código QR incrustado en el documento.

**No es** un reemplazo de firmas electrónicas calificadas ante entes externos (tipo DocuSign/Adobe Sign para contratos con terceros): su ámbito de validez es el control interno corporativo, con evidencia criptográfica robusta ante disputas.

## 2. Jerarquía de fuentes y regla de resolución de conflictos

Existen dos documentos de especificación que **se contradicen en varios puntos**. La regla acordada es:

> **V2.0 (`Sigil_GuiaDelForo.md`) prevalece sobre V1 (`Especificaciones_Sigil.md`) en todo conflicto**, por estar "Aprobado para Ejecución". V1 aporta el detalle funcional que V2 no re-especifica.
> Sobre ambos, prevalece la decisión del equipo: **el frontend es una Power Apps Code App** (no Canvas App).

### 2.1 Tabla de resolución de conflictos

| # | Tema | V1 decía | V2.0 dice | Resolución final |
|---|------|----------|-----------|------------------|
| C-01 | Frontend | Canvas App | Canvas App (solo UI) | **Code App** (React + TypeScript). Ver ADR-001. |
| C-02 | Motor de procesos | Power Automate orquesta todo | Plugins C# + Custom APIs para binarios/criptografía | **Plugins C# + Custom APIs** para todo lo binario y criptográfico. Power Automate queda relegado a notificaciones (ver ADR-003). |
| C-03 | Captura de firma | Pen Input por cada documento | Firma Maestra: imagen cargada una vez + validación IA | **Firma Maestra** (V2). El Pen Input por documento queda eliminado. |
| C-04 | Auditoría post-descarga | Portal de carga manual (drag & drop de PDF) | Código QR en el documento + validación vía Custom API | **Se fusionan**: el QR deep-linkea a la pantalla de verificación, y esa pantalla incluye drag & drop del PDF con hash calculado en el navegador (el archivo no se sube). El escaneo solo no puede verificar integridad — ver ADR-007. |
| C-05 | Sellado de tiempo | Timestamp UTC simple en la estampa | TSA externa RFC 3161 (GlobalSign/DigiCert) | **TSA RFC 3161** (V2). El timestamp visual de la estampa se mantiene, pero la validez temporal la da el token TSA. |
| C-06 | Protección del ledger | Roles: usuarios solo Append/Write, sin editar/borrar | Field-Level Security: nadie escribe el hash salvo el Service Principal/contexto de sistema | **Ambas combinadas**: roles restrictivos a nivel tabla (V1) + FLS sobre columnas críticas (V2). Ver ADR-006. |
| C-07 | Ejecución de la firma | Flujo síncrono implícito | Asíncrono: el usuario aprueba y es liberado; notificación al terminar | **Asíncrono** (V2). Ver ADR-008. |
| C-08 | Quién puede verificar por QR | — | "Al ser escaneado con **cualquier dispositivo**" | La verificación vive en la Code App: exige login corporativo y licencia Premium. Aceptable para uso interno (recomendación en ADR-007 / Q-02); un endpoint anónimo queda como extensión futura documentada. |
| C-09 | Hash en la estampa visual | "Representación en texto claro del código Hash SHA-256" | (hereda de V1) | La estampa imprime el **hash de contenido** (calculado antes de anexar la hoja de cierre) — imprimir el hash final del archivo es físicamente imposible (imprimirlo cambia los bytes). Ver ADR-011. |
| C-10 | Token TSA | — | "El Token criptográfico devuelto por la TSA **se incrusta en el PDF**" | El token **NO se incrusta**: se persiste en el ledger y se expone en la pantalla de verificación. Incrustarlo alteraría los bytes después del hash final e imposibilitaría la verificación full-file de ADR-007. Ver ADR-011. |
| C-11 | Ingesta de documentos | "Cargar documentos base **o rellenar plantillas de Microsoft Word**" | "Procesamiento Dinámico de Plantillas" (hereda de V1) | **Solo PDF** (decisión de equipo, 2026-07-10): el creador sube un PDF listo para firmar. Sin plantillas, sin Word, sin conversión de formatos. Elimina RF-06 y cierra Q-07. |

## 3. Requerimientos funcionales consolidados

Numerados como **RF-xx**. La columna "Origen" indica de qué documento sale y si fue modificado por la resolución de conflictos.

### 3.1 Onboarding y firma

| ID | Requerimiento | Origen |
|----|---------------|--------|
| RF-01 | El usuario configura una **Firma Maestra**: carga una imagen de su firma manuscrita (sin opción de dibujarla en pantalla — Q-05 cerrada). Tras validarse, la imagen se **normaliza automáticamente** (dimensiones estándar, PNG con fondo transparente, peso máximo) y se almacena como **nueva versión** — las versiones anteriores se conservan como historial inmutable (decisión 2026-07-13): cada firma estampada queda vinculada a la versión exacta usada, y cambiar la firma jamás afecta documentos ya firmados. | V2 + decisiones de equipo (2026-07-10 y 2026-07-13) |
| RF-02 | La imagen de firma se valida **automáticamente** contra tres parámetros de calidad: transparencia (canal alfa), contraste y nitidez. Si falla, se exige nueva carga con feedback del motivo. La herramienta de evaluación (Azure AI Vision, análisis local en C#, o mixta) la define el ADR-009. | V2 (herramienta sujeta a diseño) |
| RF-03 | El usuario puede **visualizar el PDF completo** dentro de la app antes de firmar. | V1 + V2 (adaptado a Code App: visor propio, no control nativo de Canvas) |
| RF-04 | La firma se ejecuta con **un clic** ("Aprobar y Firmar"); el usuario queda liberado de inmediato y el procesamiento continúa en segundo plano. | V2 |
| RF-05 | Al completarse el procesamiento, el usuario recibe notificación (Teams/Outlook) con enlace al documento final. | V2 |
| RF-06 | ~~Soporte de plantillas Word.~~ **ELIMINADO** (C-11, 2026-07-10): no hay plantillas ni soporte Word. El ID queda reservado para trazabilidad. | — |
| RF-07 | Todo flujo opera **exclusivamente en PDF**: se sube PDF, se firma PDF, se distribuye PDF. | V1 (reforzado por C-11) |
| RF-25 | La **única vía de ingesta** es la carga de un **PDF listo para firmar** por parte del creador de la transacción. | V1 ("cargar documentos base", acotado por C-11) |
| RF-28 | **Posición de las firmas en el documento — definida SIEMPRE por el creador:** al armar la transacción, el creador **debe** indicar la posición exacta (página + coordenadas) de la firma de **cada** firmante mediante el editor visual sobre el PDF (≥ 1 zona por firmante; el envío se bloquea si falta alguna). **No existe posición por defecto de Sigil** (decisión 2026-07-13 que reemplaza el default de la versión anterior). | Decisión de equipo (2026-07-13) — ver ADR-011 |

### 3.2 Flujo de trabajo y estados

| ID | Requerimiento | Origen |
|----|---------------|--------|
| RF-08 | Matriz de estados a **nivel transacción**: *Borrador, Pendiente de Firma, Firmado Parcialmente, Completado, Rechazado, Expirado*. Los estados a nivel **participante** (ej. *Procesando*, *Error de Procesamiento*) los define el documento 06. | V1 (precisado) |
| RF-26 | **Creación y envío de solicitud:** el creador arma la transacción (sube el PDF, define lista de firmantes, tipo de enrutamiento secuencial/paralelo y **las posiciones de firma de cada firmante — RF-28, obligatorias para enviar**) y la envía, pasando de *Borrador* a *Pendiente de Firma*. | V1 (implícito en §2/§5; explicitado) |
| RF-27 | El plazo de **expiración** es configurable por transacción (con default organizacional); vencido el plazo sin completarse, la transacción pasa a *Expirado* automáticamente. | V1 (implícito en el estado *Expirado*; explicitado) |
| RF-30 | El **creador puede cancelar** una transacción enviada mientras no haya entrado al sellado (estados elegibles: *Pendiente de Firma* y *Firmado Parcialmente*). La transacción pasa a **Cancelado**, se notifica a los participantes y queda registrada en el historial con actor y motivo opcional. | Decisión de equipo (2026-07-10, cierre de Q-08) |
| RF-09 | **Enrutamiento secuencial**: los firmantes reciben el documento en orden estricto (A → B → C). | V1 |
| RF-10 | **Enrutamiento paralelo**: múltiples firmantes independientes, sin orden. | V1 |
| RF-11 | **Notificaciones** por Outlook y tarjetas interactivas de Teams, con **deep links** que llevan directo a la pantalla de firma de la transacción específica. | V1 (deep links adaptados a query params de Code Apps — ver ADR-001) |
| RF-12 | **Recordatorios automáticos** configurables si un documento sigue pendiente tras X días. | V1 |
| RF-13 | Un firmante puede **rechazar** la solicitud (alimenta el estado *Rechazado*). | V1 (implícito en la matriz de estados; explicitado aquí) |

### 3.3 Criptografía y cumplimiento

| ID | Requerimiento | Origen |
|----|---------------|--------|
| RF-14 | Cálculo de **hash SHA-256** en el backend (plugin C#) según el esquema de doble hash del ADR-011: *hash de contenido* (lo que aprobaron los firmantes) y *hash final* (el archivo completo distribuido, ancla de verificación del ledger). | V1 + V2 (precisado por ADR-011) |
| RF-15 | **Hoja de cierre** (estampa de auditoría) consolidada como última página del PDF: imagen de firma, nombre completo y correo (de Entra ID) y timestamp UTC **de cada firmante**, más el **hash de contenido** en texto claro y el ID de transacción del ledger. No imprime el hash final (imposible por ADR-011 / C-09). | V1 (corregido por C-09) |
| RF-16 | **Token de sellado de tiempo RFC 3161** de una TSA externa, solicitado sobre el hash final y **persistido en el ledger** (no incrustado en el PDF — C-10). Descargable desde la pantalla de verificación para validación independiente. | V2 (corregido por C-10 / ADR-011) |
| RF-29 | El sellado TSA es una **característica configurable por ambiente (encendida/apagada)** vía variables de entorno de Dataverse. Con TSA apagada el pipeline sella igual y el registro queda marcado `sin sello TSA`; el **nivel de evidencia** de cada transacción es siempre visible en la pantalla de verificación. | Decisión de equipo (2026-07-10, cierre de Q-01) — ver ADR-005 |
| RF-17 | **Ledger inalterable** en Dataverse: registro maestro de hashes que ningún usuario puede editar ni borrar. Escritura exclusiva del contexto de sistema/Service Principal (FLS). | V1 + V2 |
| RF-18 | Identidad del firmante respaldada por **SSO con Entra ID** (sin credenciales propias de la app). | V1 |

### 3.4 Verificación y auditoría

| ID | Requerimiento | Origen |
|----|---------------|--------|
| RF-19 | **Código QR** dinámico incrustado en la hoja de cierre del PDF final. Contiene solo la URL de verificación con el ID de transacción — nunca el hash (ADR-011). | V2 (precisado) |
| RF-20 | Verificación en dos niveles: (a) **escanear el QR** abre la pantalla de verificación con los metadatos del ledger (documento, firmantes, fechas, estado) como constancia; (b) para verificar **integridad byte a byte**, la misma pantalla acepta drag & drop del PDF, calcula su SHA-256 **en el navegador** (el archivo no se sube) y lo contrasta contra el ledger: **Verde = íntegro / Rojo = alterado**. | V2 + V1 (fusionados por C-04 / ADR-007) |
| RF-21 | El mecanismo de verificación valida documentos **descargados fuera del entorno**, sin importar cuánto tiempo pasó. Solo el **PDF final completado** es verificable; las versiones intermedias de flujos parcialmente firmados no generan documento oficial (ADR-011). | V1 (objetivo) + V2 (mecanismo QR), precisado |

### 3.5 Experiencia de usuario

| ID | Requerimiento | Origen |
|----|---------------|--------|
| RF-22 | **Dashboard** con dos vistas: "Documentos pendientes por mi firma" y "Solicitudes enviadas en espera". | V1 |
| RF-23 | **Uso móvil vía navegador como escenario de primera clase:** el diseño de la Code App es responsivo/mobile-first y se prueba formalmente en navegadores móviles (Safari iOS, Chrome Android) — firmar desde el teléfono es un caso de uso central, no un extra. Restricción de plataforma: el Power Apps mobile player **no soporta Code Apps**; el acceso móvil es siempre vía navegador. Ver ADR-001. | V1 + decisión de equipo (2026-07-10) |
| RF-24 | **Repositorio final** de documentos completados con acceso de solo lectura para usuarios ordinarios, implementado **íntegramente en Dataverse** (columna de archivo + vistas de la app; el solo-lectura lo imponen los roles de seguridad). El uso de SharePoint está **prohibido** en la organización. | V1 (medio corregido por Q-03: solo Dataverse) |

## 4. Requerimientos no funcionales

| ID | Requerimiento |
|----|---------------|
| RNF-01 | **Licenciamiento:** aprovechar licencias *Power Apps Premium per User* existentes. Las Code Apps requieren Premium para **todo** usuario final (verificado contra documentación oficial — no hay costo marginal si ya se posee Premium per User). |
| RNF-02 | **No repudio:** la evidencia (hash + TSA + identidad Entra + ledger) debe resistir alteraciones **externas e internas**, incluyendo administradores. Precisión (2026-07-10): la plataforma debe ser **capaz** de producir evidencia ante terceros cuando la característica TSA está encendida (RF-29); con TSA apagada, la garantía es tamper-evidence interna y el nivel de evidencia queda registrado por transacción. |
| RNF-03 | **Sin timeouts percibidos:** ninguna operación de UI puede quedar bloqueada por procesamiento binario; todo lo pesado corre asíncrono. Ojo: el límite de **2 minutos del sandbox de Dataverse aplica a plugins síncronos Y asíncronos** — lo asíncrono libera al usuario, no amplía el presupuesto de tiempo. El pipeline de sellado debe caber en ese presupuesto o segmentarse (ver ADR-008 y doc 04, con fuente oficial citada). |
| RNF-04 | **Trazabilidad total:** cada transición de estado queda registrada con actor y timestamp. |
| RNF-05 | **ALM:** la solución completa (tablas, plugins, Custom APIs, code app, connection references) debe viajar en soluciones Dataverse desplegables por pipelines (Dev → Test → Prod). |
| RNF-06 | **Internacionalización:** la UI soporta **español e inglés** desde el día uno (i18n estructural en la Code App, no un retrofit). Los correos y tarjetas de notificación también se emiten en el idioma del destinatario. |
| RNF-07 | **Solo Dataverse como almacenamiento:** ningún artefacto del sistema (documentos, firmas, tokens, evidencia) vive fuera de Dataverse. SharePoint está prohibido por política organizacional (Q-03/Q-04). |

## 5. Fuera de alcance (Fase actual)

- **Plantillas de documentos y soporte de Word** en cualquier forma (C-11): la ingesta es exclusivamente PDF.
- **SharePoint** en cualquier rol (prohibido por política — RNF-07).
- Firmantes **externos** a la organización (invitados B2B o anónimos).
- Firma electrónica calificada / certificados digitales personales por firmante (PKI individual). La criptografía es a nivel documento + identidad Entra, no certificado personal X.509 del firmante.
- Verificación **anónima** (sin licencia/login) del QR — documentada como extensión futura en ADR-007 (C-08).
- **Firma PAdES/byte-range (sello organizacional incrustado en el PDF, validable por Adobe)** — extensión futura registrada (2026-07-13): es **apilable** sobre el modelo actual sin romperlo (el sello PAdES se incrusta antes del hash final). Prerequisitos anotados: certificado de document signing en AATL, custodia de clave en HSM/Key Vault + managed identity, y decisión de librería (iText comercial u otra verificada). No aporta a la primera versión: el no-repudio ya está cubierto por hash + TSA + ledger; PAdES agrega UX de validación en visores de terceros.
- Soporte offline y app móvil nativa (limitación de plataforma, ver RF-23).
- Integración con repositorios documentales externos a M365.

## 6. Preguntas abiertas (a resolver antes de cerrar Fase 0)

| ID | Pregunta | Impacta | Estado |
|----|----------|---------|--------|
| Q-01 | ¿Es necesaria una TSA externa? ¿Existen TSAs gratuitas viables para producción? ¿Qué alternativas hay? | ADR-005, costos | **Cerrada (2026-07-10):** TSA sí, con endpoints gratuitos (DigiCert primario + Sectigo fallback), degradación elegante con re-sellado diferido, y **como característica encendible/apagable por ambiente** (RF-29). Detalle e investigación con fuentes en ADR-005. |
| Q-02 | ¿El destino del QR de verificación exige login corporativo o se necesita un endpoint sin licencia? | ADR-007 | **Cerrada (2026-07-10):** se sigue la recomendación de ADR-007 — verificación dentro de la Code App con login corporativo y licencia Premium. Endpoint anónimo queda como extensión futura. |
| Q-03 | ¿Dónde vive el PDF final? | ADR-004 | **Cerrada (2026-07-10): solo Dataverse.** SharePoint está **prohibido** por política organizacional. Ver RNF-07 y ADR-004. |
| Q-04 | ¿Volumen estimado y capacidad de almacenamiento? | ADR-004, costos | **Cerrada en lo decisorio (2026-07-10):** el almacenamiento es solo Dataverse sí o sí (Q-03); el dimensionamiento de capacidad File se calcula en el doc 03 cuando haya volúmenes estimados. |
| Q-05 | ¿Dibujar la Firma Maestra en pantalla además de subir imagen? | RF-01, UX | **Cerrada (2026-07-10): NO.** Solo carga de imagen. A cambio, la imagen se analiza, formatea y normaliza a un tamaño estándar antes de guardarse (RF-01, ADR-009). |
| Q-06 | ¿Qué idioma(s) debe soportar la UI? | Frontend | **Cerrada (2026-07-10): español e inglés** (RNF-06). |
| Q-07 | ¿Conversión Word→PDF viable en sandbox? | ADR-002, doc 04 | **Cerrada (2026-07-10): pregunta disuelta** — no se soporta Word en absoluto (C-11). La ingesta es solo PDF. El riesgo técnico desaparece. |
| Q-08 | ¿El creador puede **cancelar/retirar** una transacción ya enviada? | RF-08, docs 03/04/06 | **Cerrada (2026-07-10): SÍ** (decisión de negocio) — con estado propio **Cancelado** (decisión técnica: *Rechazado* = un firmante declinó con motivo; *Cancelado* = el creador la retiró; semánticas distintas para dashboards, notificaciones e historial). Ver RF-30. |

---

*Siguiente documento: [02-decisiones-arquitectura.md](02-decisiones-arquitectura.md) — las decisiones técnicas (ADRs) que materializan estos requerimientos.*

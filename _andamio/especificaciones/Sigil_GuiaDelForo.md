# Especificación Técnica Arquitectónica: Plataforma de Firma Digital Interna (V2.0)
**Estado:** Aprobado para Ejecución (Consejo de los Eternos)
**Nombre Clave del Proyecto:** Vanguard / CoreSign

## 1. Arquitectura Base y Ecosistema
El sistema abandona el procesamiento de flujos tradicionales (Power Automate) para operaciones binarias complejas, adoptando una arquitectura de backend robusta sobre Microsoft Dataverse y Azure.

* **Frontend:** Power Apps (Canvas App) dedicada exclusivamente a la Interfaz de Usuario (UI) y la recolección de intenciones de firma.
* **Backend (Motor Core):** Plugins desarrollados en C# (.NET) y expuestos como Custom APIs dentro de Dataverse. Serán responsables de la manipulación binaria, generación de PDF, y criptografía.
* **Licenciamiento:** Aprovechamiento de licencias *Power Apps Premium per User* existentes (Costo marginal cero por volumen de uso).

## 2. Experiencia de Usuario (UX) y Gestión de Firmas
Se elimina la captura manual (*Pen Input*) por cada documento en favor de una experiencia de fricción cero ("Firma a un clic").

* **Configuración de Firma Maestra (Onboarding):**
    * El usuario carga una imagen de su firma manuscrita una sola vez.
    * **Validación de IA (Azure AI Vision):** Una Custom API envía la imagen a un servicio cognitivo para evaluar tres parámetros estrictos: *Transparencia (Canal Alfa)*, *Contraste* y *Nitidez*. 
    * Si la imagen es rechazada (ej. borrosa o con fondo), el sistema exige una nueva carga inmediatamente, garantizando la calidad estética del documento final.
* **Flujo Asíncrono de Firma:** 
    * El usuario revisa el PDF nativo, presiona "Aprobar y Firmar" y es liberado de la pantalla.
    * El proceso de incrustación, sellado y cálculo de Hashes ocurre en segundo plano (Asíncrono) para evitar *timeouts*.
    * Se envía una notificación (Teams/Outlook) al usuario una vez que el PDF final está listo.

## 3. Criptografía, Seguridad y Cumplimiento Legal (Anti-Repudio)
El sistema debe estar blindado contra alteraciones externas e **internas**, con validez comprobable ante terceros.

* **Autoridad de Sellado de Tiempo (TSA):** 
    * El Plugin C# integrará una API externa de una Autoridad de Sellado de Tiempo (ej. GlobalSign, DigiCert) compatible con el estándar **RFC 3161**.
    * El Token criptográfico devuelto por la TSA se incrusta en el PDF, otorgando validez temporal avalada por un tercero imparcial.
* **Segregación de Deberes (Field-Level Security):**
    * El Hash SHA-256 generado se almacenará en Dataverse.
    * Se aplicará *Field-Level Security* sobre esta columna: **Ningún usuario** (incluyendo Global Admins) tendrá permisos de escritura/modificación sobre el registro del Hash, a excepción del *Service Principal* o contexto de sistema que ejecuta el Plugin C#.

## 4. Auditoría y Verificación Inmediata (Mecanismo Poka-Yoke)
Se elimina la necesidad de portales de carga manual para auditar documentos descargados.

* **Autenticación en el Documento:** El Plugin C# generará e incrustará un **Código QR** dinámico en la hoja de firmas (última página del PDF).
* **Validación Móvil:** El QR contendrá una URL parametrizada. Al ser escaneado con cualquier dispositivo, consultará la Custom API, contrastará el Hash del documento físico con la tabla maestra intocable de Dataverse y devolverá un indicador visual claro (Verde: Íntegro / Rojo: Alterado).

## 5. Fases de Ejecución (Hoja de Ruta)
* **Fase 1 (Semanas 1-2) - El Motor Criptográfico:** Desarrollo de Plugin C#, integración con librería PDF y API de la TSA. Configuración de tablas en Dataverse con Field-Level Security.
* **Fase 2 (Semana 3) - IA y Calidad:** Implementación de Azure AI Vision y la Custom API para validación de la "Firma Maestra" (Nitidez/Transparencia).
* **Fase 3 (Semanas 4-5) - UI y Asincronía:** Desarrollo de Canvas App, visor de documentos, onboarding de firma y gatilladores asíncronos para ejecución en segundo plano.
* **Fase 4 (Semana 6) - Verificación Poka-Yoke:** Integración del generador de Códigos QR en el documento final y despliegue del endpoint de validación.
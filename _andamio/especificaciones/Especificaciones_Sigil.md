# Especificación de Requerimientos: PowerApp de Firma Digital Interna con Validación Criptográfica

Este documento detalla las características, componentes y lógica arquitectónica necesarios para el desarrollo de una solución propia de firmas digitales de uso estrictamente interno, optimizada para el ecosistema de Microsoft 365 (Power Apps, Power Automate y Dataverse).

---

## 1. Funcionalidades Core (Interfaz y Captura)
Estas características gestionan la interacción directa del usuario con el documento y la captura de la firma dentro de la Canvas App.

* **Visualizador de Documentos Nativo:** Incorporación del control visor de PDF para que el usuario pueda revisar exhaustivamente el contrato, política o acuerdo antes de proceder a la firma.
* **Captura de Firma (Pen Input):** Control de entrada de lápiz optimizado para pantallas táctiles (móviles/tablets) o puntero de ratón, permitiendo plasmar el trazo manuscrito de forma fluida.
* **Procesamiento Dinámico de Plantillas:** Capacidad para cargar documentos base o rellenar plantillas de Microsoft Word automáticamente con metadatos de la sesión antes de la conversión final.
* **Conversión Mandatoria a PDF:** Todo flujo finaliza con la generación estricta de un archivo en formato PDF, garantizando un estándar cerrado e intercambiable.

## 2. Flujo de Trabajo y Automatización (Motor de Procesos)
Orquestación gestionada a través de Power Automate para asegurar la trazabilidad del ciclo de vida del documento.

* **Gestión de Estados Integrada:** Matriz de estados clara para cada transacción: *Borrador, Pendiente de Firma, Firmado Parcialmente, Completado, Rechazado y Expirado*.
* **Enrutamiento Avanzado:**
    * *Flujo Secuencial:* El documento se envía en un orden específico (Usuario A -> Usuario B -> Usuario C).
    * *Flujo en Paralelo:* Múltiples usuarios pueden firmar de forma independiente sin un orden preestablecido.
* **Notificaciones Inteligentes y Deep Links:** Envío automático de alertas por correo electrónico (Outlook) o tarjetas interactivas en Microsoft Teams. Incluyen enlaces profundos parametrizados para dirigir al usuario directamente a la pantalla de firma de la PowerApp de la transacción específica.
* **Recordatorios Automáticos:** Sistema de alertas periódicas configurable para notificar de manera automática si un documento sigue en estado pendiente tras "X" días.

## 3. Seguridad, Criptografía y Cumplimiento Legal
El núcleo de la validez interna de la aplicación, diseñado para asegurar el no repudio y la inalterabilidad de los archivos, cumpliendo con los estándares de control de acceso corporativo.

* **Autenticación Integrada (Single Sign-On):** Aprovechamiento de Microsoft Entra ID (Azure AD). Al ser una app interna, la identidad del firmante queda plenamente respaldada por sus credenciales corporativas activas.
* **Generación de Hash Criptográfico (SHA-256):** Una vez estructurado el PDF final, un flujo binario calcula la huella digital única (Hash SHA-256) del archivo. Cualquier cambio posterior en el PDF (incluso un solo espacio o metadato) corromperá la coincidencia del Hash.
* **Estampa Digital de Auditoría:** Inserción visual inamovible en la última página del PDF que funciona como hoja de cierre. Debe contener:
    * Imagen digitalizada del trazo (Pen Input).
    * Nombre completo y correo del firmante (extraídos de Entra ID).
    * Sello de tiempo preciso (Timestamp en formato UTC).
    * Representación en texto claro del código Hash SHA-256 generado.
* **Registro de Auditoría Centralizado (Ledger Inalterable):** Tabla dedicada en **Dataverse** que actúa como el registro maestro de hashes. 
    * *Seguridad Estricta:* Configuración de roles donde los usuarios ordinarios solo tienen permisos de creación (Append/Write), pero tienen estrictamente prohibida la edición o eliminación de registros históricos.

## 4. Módulo de Verificación y Auditoría (Anti-Alteración y Validación Post-Descarga)
Herramienta interna de control de calidad y cumplimiento para validar documentos en cualquier momento, garantizando su integridad incluso si salieron temporalmente del entorno controlado.

* **Portal de Carga de Auditoría:** Pantalla exclusiva dentro de la PowerApp donde un auditor o empleado puede arrastrar y soltar un PDF.
* **Validación de Archivos Externos/Descargados:** Capacidad para que un usuario suba un PDF que fue descargado a su equipo local (días, semanas o meses atrás) para verificar que sigue siendo la versión original y autorizada.
* **Recálculo Dinámico de Hash:** Al subir el archivo, un flujo en segundo plano lee el binario actual y extrae su Hash SHA-256 en tiempo real.
* **Validación de Integridad contra el Ledger:** El sistema contrasta el Hash recién calculado contra el registro inalterable de Dataverse:
    * *Coincidencia Exitosa (Distintivo Verde):* Indica que el documento es íntegro, auténtico y no ha sufrido ninguna modificación desde que fue firmado y descargado.
    * *Discrepancia de Hash (Alerta Roja):* Lanza una alerta crítica de alteración, indicando que el PDF fue modificado de forma no autorizada (cambios de texto, firmas, o metadatos) posterior a su descarga original.

## 5. Experiencia de Usuario y Arquitectura
* **Panel de Control Centralizado (Dashboard):** Pantalla principal dividida en dos vistas operativas: *"Documentos pendientes por mi firma"* y *"Solicitudes enviadas en espera"*.
* **Diseño Responsivo:** Interfaz adaptada al formato móvil (teléfonos inteligentes) para facilitar la firma de documentos sobre la marcha desde cualquier lugar de la red interna.
* **Repositorio Final de Solo Lectura:** Al completarse el flujo de firmas, el archivo PDF se almacena automáticamente en una biblioteca de SharePoint con permisos estrictos de "Solo Lectura", protegiendo los archivos físicos del acceso interactivo directo.

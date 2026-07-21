# Sigil — Manual del Operador

**Documento autónomo.** Todo lo necesario para **desplegar, configurar, monitorear y mantener** Sigil en cada ambiente está acá adentro. No requiere ningún otro documento.

**Para quién:** quien administra la plataforma — despliegues, gobernanza de credenciales, monitoreo del canal de notificaciones, y resolución de problemas operativos.

**La regla de oro que atraviesa todo este manual:**
> **La solución trae los componentes; la configuración de cada ambiente NO viaja en la solución.** Identidades, membresías de seguridad, conexiones, valores de variables de entorno, el interruptor de Code Apps, la política de seguridad de contenido (CSP) y las reglas de datos (DLP) se **replican en cada ambiente**. Por eso, después de cada despliegue, hay una lista de verificación (los "gates") que confirma que la configuración quedó bien.

**Fecha:** 2026-07-21.

---

## 1. Panorama: qué operás

Sigil vive en **tres ambientes**, y el operador es responsable de moverlo entre ellos de forma segura y de mantenerlo sano:

| Ambiente | Tipo | Qué contiene | Quién lo edita |
|----------|------|--------------|----------------|
| **Dev** (Desarrollo) | Sandbox | La solución en modo **editable** (*unmanaged*) | Desarrolladores. Único ambiente donde se construye. |
| **Test** (Pruebas / UAT) | Sandbox | La solución en modo **sellado** (*managed*) | **Solo el despliegue escribe.** Se usa para pruebas de aceptación. |
| **Prod** (Producción) | Producción | La solución **managed**; datos reales | **Solo el despliegue escribe.** Sin roles de edición. |

**Reglas permanentes:**
- **Nunca** se edita a mano en Test/Prod: todo cambio nace en Dev y se promueve.
- **Nunca** se instala la solución en modo editable en Test/Prod.
- El código (backend, frontend) siempre se construye desde el control de versiones (git), nunca a mano.

---

## 2. Identidades y credenciales

Sigil usa **identidades técnicas** (no personas) para operar. Entenderlas es clave, porque su gobernanza **es** un control de seguridad del sistema.

### 2.1 Las identidades
| Identidad | Qué es | Para qué |
|-----------|--------|----------|
| **Identidad de runtime del motor** | Un usuario técnico (una **cuenta de servicio** o un **service principal**) con el rol de servicio + el perfil de seguridad de evidencia | Es la identidad bajo la cual corren los flujos automáticos y el sellado. **Escribe el libro de registro (la evidencia).** El actor técnico más potente del sistema. |
| **Cuenta de servicio de notificaciones** | Una cuenta real con buzón (correo) + Teams, licenciada | Envía los correos y las tarjetas de Teams; es dueña de los flujos. |
| **Identidad de despliegue** | Un service principal usado por el pipeline (o el propio operador en un despliegue manual) | Empuja la solución de un ambiente a otro. |
| **Usuario humano** | La persona que abre la aplicación | Necesita un rol de seguridad para entrar (ver §11). |

> **Nota sobre la identidad de runtime (decisión de arquitectura):** puede ser una **cuenta de servicio** (más simple, se reutiliza la de notificaciones) o un **service principal con certificado** (más seguro, sin sesión interactiva, recomendado para Producción). Ambas funcionan **sin cambiar el código** — el sistema autoriza por privilegios de rol, no por tipo de identidad. Para Producción, el service principal con certificado es la opción deliberada, porque esta identidad escribe la evidencia y no conviene que dependa de una contraseña interactiva sujeta a expiración o bloqueo.

### 2.2 Roles y perfiles (qué asignar y a quién)
La solución trae las **definiciones** de estos roles/perfiles, pero la **asignación** (la membresía) **no viaja** — se hace en cada ambiente **después** del despliegue:

- **Rol de Usuario** → para las personas que usan la app. Solo lectura de lo suyo.
- **Rol de Servicio** → para la identidad de runtime. Permite escribir el libro de registro y correr los trabajos automáticos.
- **Perfil de seguridad de evidencia** → membresía de la identidad de runtime. **Sin esta membresía, el sellado falla** (la identidad no puede escribir las columnas de evidencia).

### 2.3 Las tres conexiones (por ambiente)
Los flujos automáticos usan tres conexiones que **deben existir ANTES del primer despliegue** en cada ambiente (si no, los flujos llegan apagados):
1. **Base de datos (Dataverse)** — con la identidad de runtime.
2. **Correo (Office 365 Outlook)** — con la cuenta de servicio de notificaciones.
3. **Teams** — con la cuenta de servicio de notificaciones.

Las conexiones de correo y Teams exigen un inicio de sesión **interactivo** de una cuenta real; por eso la cuenta de servicio **no** puede tener el sign-in bloqueado. En despliegues por pipeline, **estas conexiones las provee quien solicita el despliegue** — hay que **compartirlas** con esa persona de antemano.

### 2.4 Gobernanza de credenciales (obligatoria)
La credencial de la identidad de runtime es la más sensible del sistema. Reglas:
- **Credencial por certificado** (no una clave/secreto de larga vida), con **rotación calendarizada**.
- **Dueño único** de la conexión; **jamás compartida**.
- **Alertas** de inicios de sesión anómalos.

---

## 3. Cómo desplegar (promover Dev → Test → Prod)

El despliegue mueve la solución de un ambiente al siguiente. El mecanismo recomendado es un **pipeline** (automatización integrada de la plataforma), pero por dentro es siempre lo mismo: **exportar la solución de un ambiente e importarla en el otro**. Un despliegue manual (exportar/importar el archivo a mano) es una alternativa válida en todo momento.

### 3.1 Antes de exportar (en Dev)
1. **Congelá la edición** en Dev — que nadie toque la solución durante la exportación.
2. **Publicá** todos los cambios.
3. **Subí la versión** de la solución y **guardá una copia** (snapshot) versionada.
4. **Higiene de variables de entorno (CRÍTICO):** los **valores** presentes en la solución **viajan y aterrizan en el destino en silencio**. Antes de exportar, **remové los valores** de la solución (opción *"Remove from this solution"* sobre cada variable). Los valores viven en cada ambiente, **nunca** en el artefacto. Sin esto, por ejemplo, la configuración de pruebas de Dev llegaría a Producción sin que nadie lo note.

### 3.2 Correr el despliegue
5. En **Dev**, abrí la solución editable → **Deploy** → elegí el destino (ej. *Deploy to Test*).
6. **Preflight:** el sistema prevalida contra el destino; si marca dependencias faltantes, resolvé antes de continuar.
7. **Cuando lo pida, proveé:**
   - **Conexiones** → asociá cada una a la conexión del ambiente destino (las tres de §2.3, que ya deben existir).
   - **Valores de variables de entorno** → los del ambiente destino (§6). **Dejá vacío el enlace de la app (AppPlayUrl)**: se configura después (§3.3, paso 12).
8. **Aprobación:** si el despliegue requiere aprobación, queda pendiente hasta que el responsable la otorgue.

### 3.3 Después de importar (configuración que NO viaja)
Apenas termina el import, **antes de los gates**, hacé esta configuración por-ambiente:
9. **Habilitá Code Apps** en el ambiente (interruptor de características). Sin esto, la app no corre. *(El cambio no es instantáneo: propaga en algunos minutos — ver §11.)*
10. **Configurá la CSP** (política de seguridad de contenido) para que el visor de PDF funcione (§6).
11. **Asigná los roles y membresías** (§2.2): rol de servicio + perfil de evidencia a la identidad de runtime; rol de usuario a las personas.
12. **Reasigná el dueño de los flujos** a la cuenta de servicio de notificaciones (el despliegue los deja con la identidad de despliegue).
13. **Configurá el enlace de la app (AppPlayUrl):** leé el identificador de la app en el ambiente destino y seteá esa variable como **valor del ambiente** (ver §11 — se edita por la solución por defecto, no por la managed).

---

## 4. Los 10 gates post-despliegue

Después de **cada** despliegue (a Test y a Prod), y **antes de habilitar tráfico**, se verifican estos diez puntos en orden. Cualquiera en rojo → el ambiente **no** se declara utilizable.

| # | Gate | Qué verifica | Si falla |
|---|------|--------------|----------|
| 1 | **Claves alternas activas** | El índice de unicidad del libro de registro se crea de forma **asíncrona** al importar; hasta que está *Activo*, la idempotencia del sellado no existe | Esperar y reintentar; si queda *Failed*, reactivarlo |
| 2 | **Componentes de backend presentes** | Que el paquete de plugins y todos los pasos (incluido el del sellado) viajaron completos | El paquete no viajó — revisar la asociación a la solución |
| 3 | **Flujos encendidos** | Los 3 flujos en *On*, con sus conexiones asociadas y **dueño = cuenta de servicio** | Faltó crear/compartir una conexión antes del despliegue |
| 4 | **Variables de entorno correctas** | Que cada variable tenga el **valor del ambiente** (no un valor heredado de Dev) | Corregir el valor en el destino + higiene en Dev (§3.1) |
| 5 | **Visor de PDF (CSP)** | Que un PDF (normal, escaneado, con caracteres asiáticos) renderice sin violaciones de seguridad | Revisar la CSP (§6) |
| 6 | **Enlace profundo** | Que un enlace desde una notificación abra directo la pantalla de firma correcta | Revisar que los parámetros sobrevivan el inicio de sesión |
| 7 | **Descargas** | Que se puedan bajar el PDF final y el token de marca de tiempo, con su huella correcta | Revisar permisos de descarga del marco embebido |
| 8 | **Marca de tiempo alcanzable** | Que se obtenga un token de la autoridad de tiempo desde el ambiente | Si degrada correctamente (queda pendiente de re-sellado), no bloquea |
| 9 | **Prueba de extremo a extremo (smoke)** | Crear → firmar → sellar → verificar (Verde) → alterar un byte → verificar (Rojo), con marca de tiempo real. **El juez final.** | No habilitar tráfico; diagnosticar |
| 10 | **Latido del trabajo diario (heartbeat)** | Que el correo de resumen del trabajo diario llegue al buzón del operador | Revisar el flujo, la conexión de correo y la regla de reenvío (§8) |

El resultado de los 10 gates se **archiva** en el registro del despliegue (§12).

---

## 5. Los tres flujos de notificación

El sistema tiene tres flujos automáticos. El operador no los edita (viajan en la solución), pero debe saber qué hacen y vigilarlos:

1. **Turno de firma** — avisa a un firmante (correo + Teams) cuando es su turno.
2. **Estado de la transacción** — avisa a las partes cuando una solicitud se completa, se rechaza, se cancela, expira, o falla el sellado.
3. **Trabajo diario** — de madrugada, ejecuta tres tareas en orden: **expirar** las solicitudes vencidas, enviar **recordatorios**, y **reintentar** los sellados de marca de tiempo pendientes. Termina enviando el **latido (heartbeat)** al operador.

**Todos** usan el enlace de la app (la variable AppPlayUrl del ambiente) para los enlaces profundos, y son **propiedad de la cuenta de servicio de notificaciones**.

---

## 6. Configuración por ambiente

### 6.1 Valores de variables de entorno
Los **valores** se fijan por ambiente (nunca se copian de Dev). Los principales:

| Variable | Dev | Test | Prod |
|----------|-----|------|------|
| **Marca de tiempo activa** | Apagada en el día a día; encendida en pruebas de integración | Encendida | **Encendida** |
| **Endpoints de marca de tiempo** | Autoridad de dev | Autoridades de producción (≥2, con respaldo) | Autoridades de producción (≥2) |
| **Tamaño máximo de PDF / máximo de firmantes / specs de imagen** | Valores de prueba | = Prod | Valores de negocio |
| **Días de vencimiento / cadencia de recordatorios / idioma por defecto** | Cortos (para probar rápido) | = Prod | Valores de negocio |
| **Enlace de la app (AppPlayUrl)** | Tras el primer despliegue de la app en Dev | Tras el primer despliegue en Test | Tras el primer despliegue en Prod |
| **Correo del operador (heartbeat)** | Buzón de dev | Buzón de operador de Test | Buzón de operador de Prod |

> **Importante:** hay ≥2 autoridades de marca de tiempo configuradas con respaldo, porque en la práctica una de ellas puede estar inaccesible desde la red del ambiente (se comprobó que una autoridad conocida no respondía desde el entorno). El sellado **nunca** se bloquea por esto: degrada a "pendiente" y reintenta.

### 6.2 Política de seguridad de contenido (CSP)
La CSP del componente de la app viene **forzada por defecto en modo restrictivo**, lo que **bloquea el motor del visor de PDF** — es decir, el visor está roto en un ambiente nuevo **hasta que se configura**. Hay que ampliar dos directivas:
- **Directiva del *worker*:** debe permitir `'self'` **y `blob:`**. El `blob:` es necesario porque el visor carga su motor como un recurso embebido tipo *blob* — sin `blob:` el visor no arranca.
- **Directiva de *conexión*:** debe permitir `'self'` (recursos auxiliares para PDFs escaneados y fuentes especiales).

> **Regla práctica (la que manda):** replicá **exactamente** los valores que ya funcionan en Dev. Si el visor anda en Dev, copiá esas directivas idénticas a Test/Prod — no las adivines.

> ⚠️ **Precaución de seguridad:** las directivas *custom* **reemplazan** la restricción por defecto, así que el conjunto exacto de valores importa (ampliarlo de más debilita la seguridad del ambiente). Y si para diagnosticar cambiás la CSP a **"modo reporting"**, tené presente que eso **apaga el enforcement** (baja la seguridad del ambiente): es aceptable brevemente en Dev, **jamás en Prod**.

### 6.3 Otros ajustes por ambiente
- **Interruptor de Code Apps**: encendido (§3.3, paso 9).
- **Auditoría**: activada, con la retención según la política de la organización.
- **Reglas de datos (DLP)**: base de datos + correo + Teams deben estar juntos en el grupo permitido; si están separados, los flujos se bloquean.

---

## 7. Configuración por-ambiente que NO viaja (resumen)

Tené esta lista siempre a mano — es lo que hay que rehacer en **cada** ambiente:

- [ ] Identidad de runtime + su rol de servicio + membresía del perfil de evidencia
- [ ] Cuenta de servicio de notificaciones + las 3 conexiones, compartidas
- [ ] Roles de usuario asignados a las personas
- [ ] Dueño de los flujos = cuenta de servicio
- [ ] Valores de variables de entorno del ambiente (incluido el enlace de la app y el correo del operador)
- [ ] Interruptor de Code Apps encendido
- [ ] CSP configurada
- [ ] Auditoría y DLP
- [ ] Regla de reenvío del buzón de notificaciones al del operador (§8)

---

## 8. Monitoreo y el "canal muerto"

**El riesgo silencioso:** si la conexión de correo/Teams expira o el buzón se deshabilita, **todas** las notificaciones fallan a la vez — y el síntoma es **el silencio**. Nadie se entera de que nadie se entera.

**Controles obligatorios:**
1. **Regla de reenvío:** las alertas nativas de fallo van al buzón de la **cuenta de servicio** (que nadie lee). Configurá una **regla de reenvío** de ese buzón al **buzón del equipo operador**.
2. **Revisión semanal** de los trabajos/flujos fallidos.
3. **El latido (heartbeat):** el trabajo diario termina enviando un correo de resumen ("trabajo OK: X expiradas, Y recordatorios, Z re-sellados"). **Si un día no llega ese correo, el canal está caído.** Es el detector más barato y confiable — de ahí el gate 10.

> Nota: un envío fallido individual deja el flujo en estado *Failed* (no existe "parcialmente exitoso"), lo cual es correcto para el diagnóstico. Un destinatario caído no corta el resto de los envíos.

---

## 9. Calendario de gobernanza

Tareas recurrentes con su cadencia y responsable:

| Tarea | Cadencia | Por qué |
|-------|----------|---------|
| **Rotación del certificado de la identidad de runtime** (alerta 30 días antes) | Anual | Es la credencial más sensible del sistema |
| **Rotación del secreto del ejecutor de pruebas/desarrollo** | Cada 180 días | Un secreto vencido corta la conexión en silencio |
| **Re-consentimiento de las conexiones de correo/Teams** | Al expirar/revocar | Sin ellas, el canal de notificaciones muere |
| **Capacidad de almacenamiento** (archivos y registros) | Mensual | El sistema guarda documentos y evidencia de forma permanente |
| **Revisión de avisos de seguridad** de las dependencias | En cada versión + mensual | Mantener el stack sin vulnerabilidades conocidas |
| **Revisión de flujos fallidos** | Semanal | Detectar problemas del canal de notificaciones (§8) |
| **Verificación del punto de restauración** de Producción | Semanal | Que exista un respaldo reciente (§10) |
| **Custodia de la clave de firma del código** | Permanente (no rota; fuera de git) | Integridad del paquete de backend |

---

## 10. Rollback y recuperación

Decidido **antes** de cualquier incidente, no durante:

- **Por defecto: corregir hacia adelante (fix-forward).** Un despliegue malo se arregla con una corrección nueva, no revirtiendo. Motivo: bajar de versión una solución sellada **no está soportado**, y **restaurar el ambiente retrocede el libro de registro** — es decir, **pérdida de evidencia** de transacciones ya distribuidas.
- **Restaurar el ambiente: última instancia**, solo con aprobación del dueño del producto **y un registro escrito** de la pérdida de evidencia aceptada (qué transacciones quedan fuera de la ventana). Los tokens de marca de tiempo en poder de verificadores externos **sobreviven** al restore y delatarían la discrepancia — por eso un restore no es una operación inocua sobre la evidencia.

---

## 11. Solución de problemas comunes

Los tropiezos más frecuentes al operar un ambiente nuevo, y su causa real:

| Síntoma | Causa | Solución |
|---------|-------|----------|
| **"...does not allow this operation for this Code app..."** al abrir la app | El **interruptor de Code Apps** está apagado, o el cambio aún no propagó | Encendelo en las características del ambiente; **esperá algunos minutos** y recargá (no es instantáneo) |
| **"You do not have the correct permissions to use this connection"** | Engañoso: **no** es la conexión — **tu usuario no tiene rol de seguridad** en ese ambiente | Asignale a la persona el **rol de usuario** (la asignación no viaja con la solución) |
| **El visor de PDF no renderiza** / errores de seguridad en la consola | La **CSP** por defecto bloquea el motor del visor | Configurá las directivas (§6.2); replicá los valores exactos de Dev |
| **No puedo editar el valor de una variable de entorno** ("You cannot directly edit... managed solution") | Estás editando la **definición** (managed, bloqueada). El **valor** es un registro aparte | Editá el valor desde la **solución por defecto** del ambiente (o una solución propia no-managed). Usá siempre el **valor actual**, no el por-defecto (el por-defecto se pisa en cada actualización) |
| **Me pide un segundo factor (MFA) y no quiero que lo pida** (ej. cuenta de prueba automatizada) | La MFA puede venir de tres lugares independientes: *security defaults*, *acceso condicional*, o **MFA por-usuario heredada** | Revisá los tres. La más olvidada es la **MFA por-usuario**, que es independiente de las otras dos |
| **Una solicitud queda "Sellando" para siempre** | El sellado agotó sus reintentos automáticos | La salida automática es el saneamiento del trabajo diario (mueve a "Error de sellado" tras 24 h). Si el trabajo diario aún no está activo, corré manualmente la tarea de expiración con la identidad de runtime |
| **Los flujos llegan apagados tras un despliegue** | Las conexiones no existían antes del import, o no estaban compartidas con quien desplegó | Creá/compartí las conexiones y reasociá cada flujo (editar → aceptar el mapeo → encender) |

---

## 12. Registro de despliegues

**Cada** despliegue se documenta, porque es la evidencia de que el ambiente quedó bien y la trazabilidad de qué versión corre dónde. Guardá un registro con:

- **Ambiente** y **fecha**.
- **Versión** de la solución desplegada.
- **Quién** desplegó y **quién aprobó**.
- **Resultado de los 10 gates** (cada uno, con su evidencia).
- **Número de registro** de la transacción de prueba (smoke) del gate 9.
- **La identidad de runtime** usada (cuenta de servicio o service principal).

Con los 10 gates en verde y el registro archivado, el ambiente se declara **utilizable** y se habilita el tráfico.

---

*Documento autónomo. Para el uso funcional de la aplicación (firmar, crear, verificar), ver el Manual de Usuario. Para el valor probatorio de la evidencia, ver el Dossier de Evidencia y Cumplimiento.*

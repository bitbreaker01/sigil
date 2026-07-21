# Sigil — Dossier de Evidencia y Cumplimiento

**Documento autónomo.** Describe, de forma completa y autosuficiente, qué evidencia produce Sigil, cómo se genera criptográficamente, qué se puede demostrar con ella, y —con igual énfasis— **cuáles son sus límites honestos**. No requiere ningún otro documento para entenderse.

**Para quién:** auditoría, cumplimiento, asesoría legal y responsables de riesgo. No asume conocimiento criptográfico previo (hay un glosario al final, §11).

**Principio rector de este dossier:** una evidencia sobrevalorada es peor que inútil ante un tribunal. Acá se declara con precisión qué prueba Sigil y qué **no** — porque la credibilidad de la parte honesta de la cadena depende de no exagerar el resto.

**Fecha:** 2026-07-21.

---

## 1. Qué es (y qué no es) la firma de Sigil

Sigil es una plataforma de **firma digital con evidencia criptográfica** para uso **interno corporativo**. Permite enviar documentos PDF a firmar, firmarlos, **sellarlos** de forma que cualquier alteración **del documento distribuido** sea **detectable** byte a byte, y **verificar** después su autenticidad e integridad.

> *Precisión (se desarrolla en §5 y §6):* la alteración del **archivo** siempre es detectable por su huella. Un caso distinto es la reescritura del **registro interno** por un Administrador del Sistema del tenant: eso solo es detectable con la **marca de tiempo activa** — y, en la ventana previa al sellado, no es técnicamente detectable en absoluto. Estos límites se declaran sin maquillar más abajo.

**Naturaleza jurídica de la firma — declaración honesta:**
- Sigil **NO** emite una **firma electrónica calificada** en el sentido de las normativas que exigen certificados digitales personales X.509 por firmante emitidos por una Autoridad de Certificación acreditada.
- La validez de una firma de Sigil descansa en **control interno corporativo** respaldado por **evidencia criptográfica robusta**: la identidad del firmante proviene del inicio de sesión corporativo (SSO), y la integridad y el momento se prueban con funciones de hash y una marca de tiempo de un tercero imparcial.
- Sigil está diseñado para firmantes **internos a la organización**. No contempla firmantes externos.
- **La calificación legal de esta evidencia ante un tribunal concreto es materia de asesoría legal de la organización.** Este documento describe qué evidencia técnica existe; no dictamina su peso jurídico.

Dicho esto, la evidencia que Sigil produce es fuerte y, en su nivel más alto, **oponible incluso frente a un administrador del propio sistema** (ver §5 y §6).

---

## 2. Qué protege Sigil (activos)

1. **Integridad de los documentos firmados** — que nadie pueda alterar un PDF sellado sin que sea detectable.
2. **Atribución de las firmas** — que "la persona X firmó este documento en tal momento" sea respaldable (no repudio).
3. **Confidencialidad razonable** — que solo el creador y los firmantes vean una solicitud, y que la actividad de firma de la organización no sea extraíble por curiosos.
4. **Disponibilidad del flujo de firma** — que el sistema no pueda ser bloqueado con archivos maliciosos o abuso de sus interfaces.

---

## 3. Cómo se genera la evidencia (el mecanismo)

Esta sección explica, en términos precisos pero accesibles, **cómo** Sigil produce una evidencia difícil de falsificar.

### 3.1 La huella del documento (hash SHA-256)
Toda la evidencia de integridad se apoya en una **función de hash criptográfica** llamada **SHA-256**. Un hash es una "huella digital" de un archivo: un número de longitud fija (256 bits) que se calcula a partir de **todos los bytes** del archivo.

Propiedades relevantes:
- **Determinístico:** el mismo archivo produce siempre la misma huella.
- **Sensible:** cambiar **un solo byte** del archivo produce una huella completamente distinta.
- **Irreversible y resistente a colisiones:** en la práctica no se puede fabricar un archivo distinto que tenga la misma huella.

Por eso, comparar dos huellas equivale a comparar los archivos byte a byte: si las huellas coinciden, el archivo es idéntico; si difieren, algo cambió.

### 3.2 Congelamiento de la identidad y de la firma al momento de firmar
Cuando una persona firma, Sigil **congela** (guarda una copia inmutable de) los datos que constituyen la evidencia de **esa** firma:
- La identidad del firmante (nombre, correo, identificador único corporativo) tal como era **en ese instante**.
- La imagen de su firma **tal como era en ese instante**.
- El momento exacto, en tiempo universal (UTC).

**Consecuencia probatoria:** si el firmante después cambia de nombre, de correo, o actualiza la imagen de su firma, **la evidencia del documento ya firmado no cambia**. Refleja lo que era verdad cuando se firmó.

### 3.3 El proceso de sellado (qué ocurre cuando firman todos)
Cuando el último firmante aprueba, Sigil ejecuta un proceso de **sellado** que produce el documento final y su evidencia, en un orden estricto:

1. **Recupera** el PDF de contenido original.
2. **Verifica** que ese PDF no cambió desde que se envió a firmar: recalcula su huella SHA-256 y la compara con la huella que se registró al momento del envío. **Si no coinciden, el sellado se detiene** y se marca un error crítico — Sigil **jamás sella contenido adulterado**.
3. **Incrusta** la firma de cada participante (la imagen congelada del punto 3.2) en la posición exacta del documento que el creador definió para esa persona.
4. **Agrega una hoja de cierre** con una constancia: la marca "Firmado con Sigil", la huella del contenido, un identificador de verificación y un código QR que lleva a la pantalla de verificación. También escribe metadatos en las propiedades del PDF.
5. **Calcula la huella final** (SHA-256 del PDF ya sellado, con firmas y hoja de cierre).
6. **Obtiene una marca de tiempo confiable** de un tercero sobre esa huella final (ver §3.5), si la función está activa.
7. **Guarda** el PDF final de forma durable.
8. **Registra** en un libro de registro inalterable (§3.4) las huellas, la marca de tiempo y el resumen de firmantes.
9. **Marca** la solicitud como completada y notifica a las partes.

> **Detalle de robustez (idempotencia):** el orden "guardar el archivo (7) antes que el registro (8)" es deliberado. La serialización de un PDF no es determinística (contiene identificadores internos variables), de modo que recomponer el archivo produciría bytes distintos y, por ende, otra huella. Guardando primero el archivo durable y luego la huella que lo describe, se garantiza que **la huella del registro siempre describe exactamente los bytes que existen** — incluso si el proceso se interrumpe y reintenta. El orden inverso podría producir un registro que apunta a un archivo que ya no existe: una transacción permanentemente inverificable. Ese escenario está **prohibido por diseño**.

### 3.4 El libro de registro (ledger) inalterable
El resultado del sellado se asienta en un **libro de registro de solo-agregado** (append-only). Sus propiedades:
- **Nadie lo escribe directamente:** solo el motor del sistema, actuando bajo una identidad técnica dedicada, puede crear registros — a través de operaciones controladas, nunca por edición manual.
- **Las columnas de evidencia (huellas, marca de tiempo, fecha de sellado) están protegidas con seguridad a nivel de columna:** ni los usuarios ni los administradores **delegados** pueden leerlas o modificarlas en crudo.
- **No se puede borrar ni duplicar:** una restricción de unicidad garantiza un único registro por transacción, y las reglas impiden su eliminación por parte de los usuarios.

### 3.5 La marca de tiempo confiable (TSA — RFC 3161)
Para probar **cuándo** existió el documento sellado —y hacerlo oponible **fuera** del sistema—, Sigil solicita una **marca de tiempo** (timestamp) a una **Autoridad de Sellado de Tiempo** (TSA, *Time-Stamping Authority*) externa e imparcial, según el estándar internacional **RFC 3161**.

Cómo funciona: Sigil le envía a la TSA únicamente la **huella final** del documento (nunca el documento en sí). La TSA responde con un **token firmado criptográficamente** que certifica: "recibí esta huella en este instante". Ese token queda guardado en el libro de registro y **cualquier verificador puede descargarlo**.

Controles aplicados (no negociables):
- **El token incluye el certificado de la TSA** (`CertReq = true`), para que la validación independiente siga siendo posible **años después**, aunque el certificado ya no esté publicado.
- **Número aleatorio (nonce)** en cada solicitud, para evitar respuestas reutilizadas.
- **Doble validación** antes de guardarlo: (a) correspondencia entre lo pedido y lo respondido, y (b) validez criptográfica de la firma del token contra el certificado que él mismo incluye. Un token que no pasa ambas se descarta.
- **Canal cifrado obligatorio (HTTPS):** el sistema **rechaza** endpoints de TSA sin cifrar.
- **Redundancia:** hay al menos dos TSAs configuradas; si la primera no responde, se usa la siguiente.

**Valor probatorio de la TSA:** el token está firmado por un tercero imparcial. Por lo tanto, prueba la existencia del documento en un momento dado **fuera del alcance de cualquier administrador del sistema o del tenant corporativo**. Si alguien alterara el registro interno después del sellado, el token —en poder de cualquiera que lo haya descargado— **delataría la discrepancia**.

**Límite honesto de la TSA:** la validación interna comprueba que el token está bien formado y firmado por el certificado que él mismo trae; **no** valida la cadena del certificado hasta una raíz de confianza reconocida. Un endpoint TSA comprometido podría, en teoría, emitir un token "válido" con un certificado arbitrario. **La verificación independiente con herramientas estándar (por ejemplo `openssl ts` contra la CA real) sí lo detectaría.** Un endurecimiento futuro posible es fijar (pin) las autoridades esperadas por endpoint.

---

## 4. Cómo se verifica un documento (verificación independiente)

Hay **dos vías** de verificación, y es importante distinguirlas:

**a) Dentro de la aplicación** (para usuarios de la organización): cualquier **usuario autenticado con licencia** del tenant puede usar la pantalla de verificación. No necesita permisos especiales sobre el documento, pero **sí necesita ser un usuario válido de la organización** (la aplicación corre sobre el inicio de sesión corporativo).

**b) Fuera de la aplicación, de forma totalmente independiente** (para cualquiera, incluso un tercero externo): quien tenga el PDF sellado y el token de marca de tiempo puede comprobar la evidencia **sin cuenta, sin permisos y sin depender de Sigil**, con herramientas de código abierto (ver el recuadro al final de esta sección). Esta es la vía que hace la evidencia **oponible frente a terceros ajenos a la organización**.

Vía (a), paso a paso:

1. Abre la pantalla de **verificación** y arrastra el PDF.
2. **El documento nunca sale del navegador del verificador:** Sigil calcula su huella SHA-256 **localmente** y solo compara esa huella contra el registro. El archivo no se sube a ningún lado.
3. Resultado, en tres posibilidades:

| Resultado | Significado |
|-----------|-------------|
| 🟢 **Auténtico e íntegro** | La huella coincide **exactamente** con la registrada al sellar. Es el documento original, sin ninguna modificación. |
| 🔴 **No coincide** | La huella **NO** coincide. El archivo fue modificado, o no es el documento sellado. |
| ⚪ **Sin registro de sellado** | Ese PDF no fue sellado por Sigil (o no es el archivo sellado correcto). |

Cuando es auténtico, se muestra la **constancia**: número de registro, fecha de sellado, la huella SHA-256, la lista de firmantes con sus momentos de firma, y si el historial de firmas está íntegro. Se puede **descargar el token de marca de tiempo (.tsr)** para una comprobación totalmente independiente de Sigil.

> **Verificación por fuera de Sigil:** la huella mostrada puede recalcularse con la herramienta estándar `sha256sum archivo.pdf`; el token `.tsr` puede validarse con `openssl ts`. Es decir, **la evidencia no depende de que Sigil siga existiendo**: quien tenga el PDF sellado y el token puede probar integridad y tiempo con herramientas de código abierto.

---

## 5. Niveles de evidencia

No toda transacción tiene el mismo peso probatorio, y **la pantalla de verificación siempre declara el nivel** — nunca se asume una evidencia que no existe. Los niveles:

| Nivel | Condición | Qué se puede demostrar |
|-------|-----------|------------------------|
| **Completo** | Marca de tiempo activa y token válido en el registro | Integridad + autoría + **tiempo certificado por un tercero imparcial**. Oponible **fuera** del sistema y del tenant. |
| **Completo diferido** | El token se obtuvo en un reintento posterior al sellado | Igual que Completo, pero el tercero certifica la existencia **a la fecha del token** (ambas fechas quedan visibles). |
| **Interno transitorio** | La marca de tiempo estaba activa pero la TSA no respondió al sellar; hay un reintento en curso | Integridad + autoría demostrables **hoy dentro del sistema**; el token del tercero está en obtención. La pantalla lo declara tal cual. |
| **Interno** | La marca de tiempo está desactivada | Integridad + autoría **dentro del perímetro del tenant corporativo** (huella + registro + seguridad de columna + auditoría). No hay certificación de tiempo por un tercero. |

**Recomendación de cumplimiento:** para producción, **la marca de tiempo debe estar activa**. Sin ella, la cadena de no-repudio termina dentro del tenant, y la evidencia queda expuesta a los actores más privilegiados del sistema (ver §6, amenazas A2b y A4).

---

## 6. Modelo de amenazas y riesgos residuales

Sigil se diseñó mapeando amenazas concretas a controles concretos, y **declarando el riesgo que queda sin cubrir**. A continuación, el catálogo completo. Los identificadores (A1–A17) son propios de este dossier.

### 6.1 La cadena de confianza (capas)
La defensa está en capas; **cada capa cubre el límite de la anterior**:

| Capa | Qué garantiza | Su límite honesto |
|------|----------------|-------------------|
| **Identidad** (SSO corporativo; identidad tomada del servidor, nunca del navegador del usuario) | Quién es cada actor; la evidencia no cambia si la persona se renombra después | La robustez del login (MFA, políticas de acceso) es del tenant corporativo, no de Sigil |
| **Autorización** (cada operación exige el privilegio correspondiente; los usuarios no tienen escritura directa a los datos) | Nadie ejecuta acciones ajenas ni escribe datos por fuera del motor | Depende de que cada regla de negocio esté implementada y probada (lo está, con pruebas negativas obligatorias) |
| **Integridad** (huella del contenido anclada al enviar y verificada al sellar; huella final sobre el archivo; registro inalterable) | El contenido aprobado no cambió; el archivo distribuido es detectablemente íntegro byte a byte | La verificación exige recalcular la huella (el QR solo lleva a la constancia, no "prueba" por sí mismo) |
| **Evidencia protegida** (seguridad de columna sobre las huellas y la marca de tiempo) | Ni usuarios ni administradores **delegados** leen o alteran la evidencia cruda | **No aplica a un Administrador del Sistema (sysadmin) del tenant**, que sí puede leerla y modificarla |
| **No repudio externo** (marca de tiempo del tercero) | Un tercero imparcial certifica que la huella existía en ese instante — fuera del alcance de cualquier admin | Es una función activable; apagada, esta capa no existe, y el nivel de evidencia lo declara |
| **Auditabilidad** (todo evento de negocio queda registrado, incluida cada verificación; verificación cruzada del historial; auditoría nativa de la plataforma) | Reconstrucción de quién hizo qué, cuándo, y **sobre qué bytes exactos**, con detección de ediciones posteriores del historial | La auditoría registra operaciones **efectuadas, no intentos**, y puede ser desactivada por un sysadmin. La marca de tiempo cubre la reescritura **posterior al sellado** (amenazas A4/A13), **no** la ventana **previa** al sellado (A2b) |

### 6.2 Catálogo de amenazas

| # | Amenaza | Control | Riesgo residual |
|---|---------|---------|-----------------|
| **A1** | Alterar un PDF descargado y hacerlo pasar por original | Verificación por huella de todo el archivo | **Ninguno significativo:** la huella delata cualquier cambio |
| **A2** | Alterar el contenido **entre el envío y el sellado** (por un acceso de escritura anómalo, no sysadmin) | La huella del contenido se ancla al enviar y se verifica al sellar → si no coincide, error de sellado + evento crítico | Detectado siempre |
| **A2b** | Ídem, por un **sysadmin** que reemplaza el archivo Y reescribe coherentemente su huella en la ventana previa al sellado | La auditoría nativa registra el cambio (desactivable por el mismo actor); los firmantes que ya vieron el documento son testigos de la discrepancia | **Riesgo residual declarado y no mitigable técnicamente dentro del tenant.** La marca de tiempo NO cubre esta ventana (sella lo que se le presenta). La reducción es **organizacional**: restringir sysadmins (§7). Es el límite más honesto de la cadena. |
| **A3** | Reescribir la huella del registro para legitimar un documento adulterado (admin delegado) | Seguridad de columna + auditoría + roles sin escritura | Bloqueado para todos salvo un sysadmin (→ A4) |
| **A4** | Ídem, por un **sysadmin** del tenant | **Marca de tiempo:** el token —en el registro y en poder de cualquier verificador que lo descargó— sella la huella original ante un tercero; la reescritura queda demostrable. Auditoría nativa como segunda señal (desactivable por el mismo actor — límite declarado) | Con la marca de tiempo **apagada**: riesgo aceptado y **visible** en el nivel de evidencia |
| **A5** | Repudio ("yo no firmé eso") | SSO + congelamiento de identidad y de imagen de firma en el instante exacto + evento con actor + momento UTC + (con TSA) tiempo certificado | No se captura IP/dispositivo dentro del sistema; la atribución es a la **cuenta** (ver §7, MFA y logs de inicio de sesión como control compensatorio) |
| **A6** | Invocar operaciones ajenas (firmar por otro, cancelar lo ajeno) | Privilegios por operación + autorización de negocio + bloqueo de concurrencia | Bajo; cubierto por pruebas negativas obligatorias |
| **A7** | Minar la actividad de firma de la organización | Sin acceso directo al registro; la constancia solo se obtiene con el identificador del documento (no adivinable, impreso solo en documentos legítimos); eventos visibles solo a participantes | **Tradeoff declarado:** quien posee un documento legítimo obtiene su constancia — es el propósito del sistema. Cada verificación queda registrada. |
| **A8** | Denegación de servicio con archivos gigantes | Validación de tamaño **antes** de procesar; límites configurables de tamaño y de cantidad de firmantes | Bajo |
| **A9** | Inyección de código en la interfaz (a través de nombres o motivos) | La interfaz trata todo texto como texto plano; política de seguridad de contenido estricta | Bajo |
| **A10** | Residuos de documentos en dispositivos compartidos | Sin almacenamiento local: los documentos viven solo en memoria mientras se ven; la descarga es siempre explícita | El sistema operativo puede cachear descargas explícitas — responsabilidad de la política de dispositivos (§7) |
| **A11** | Phishing con enlaces falsos hacia una app impostora | Los enlaces legítimos apuntan siempre al dominio de la plataforma con SSO corporativo | Educación de usuarios; Sigil no puede impedir que alguien haga clic en un dominio ajeno |
| **A12** | Manipulación del proceso: doble sellado, carrera de firmas, reintentos duplicados | Bloqueo de fila + revalidación + idempotencia por participante + restricción de unicidad + orden durable-primero | Diseñado, con pruebas de concurrencia obligatorias |
| **A13** | **Compromiso de la identidad técnica del motor** (la credencial que usa el sistema para operar) | Dueño restringido y credencial **jamás compartida**; **certificado** con rotación (no una clave de larga vida); monitoreo de inicios de sesión anómalos; la marca de tiempo sigue delatando reescrituras posteriores al sellado | **Medio:** atraviesa las capas internas. La gobernanza de esta credencial ES el control (ver §7) |
| **A14** | TSA comprometida o canal interceptado | HTTPS obligatorio + doble validación del token | Bajo (dos TSAs de primera línea). **Límite:** la validación interna NO comprueba la cadena del certificado hasta una raíz confiable — solo la verificación independiente (`openssl ts` contra la CA real) lo detecta |
| **A15** | Un **restore del ambiente** retrocede o elimina el registro de transacciones ya distribuidas | Los tokens de marca de tiempo ya descargados por verificadores externos **sobreviven al restore** y delatan la discrepancia; el restore queda en los logs de administración | Declarado: análogo a A4 a nivel plataforma; sin mitigación técnica interna adicional |
| **A16** | **Destrucción del archivo final** tras el sellado (única copia sistémica) | Usuarios sin permiso de borrado; la auditoría registra la operación; la verificación de copias ya descargadas sigue funcionando | **Amenaza a la disponibilidad de la evidencia:** si nadie descargó el original, el registro describe un archivo que ya no existe. Mitigación: **backups** del ambiente (§7), en tensión con A15 |
| **A17** | Indisponibilidad temporal de las TSAs | Degradación elegante: el sellado se completa con "marca de tiempo pendiente" + reintento posterior; el sellado **nunca** se bloquea por la TSA | Bajo: pérdida temporal del nivel de evidencia Completo, visible por transacción |

### 6.3 Los tres riesgos residuales que la organización debe conocer
Resumiendo lo anterior, hay tres puntos donde la garantía técnica **cede** y la mitigación pasa a ser **organizacional**:

1. **El Administrador del Sistema (sysadmin) del tenant** es el único actor que atraviesa la seguridad de columna. Puede, en la ventana previa al sellado (A2b) o —con la marca de tiempo apagada— después del sellado (A4), alterar evidencia. **La marca de tiempo activa neutraliza A4, pero NO A2b.** Control: restringir drásticamente quién es sysadmin (§7).
2. **La identidad técnica del motor** (A13) es el actor más potente después del sysadmin. Control: gobernanza estricta de su credencial (§7).
3. **La evidencia vive en un solo lugar** (A15/A16): un restore o un borrado a nivel plataforma puede retroceder o destruir el registro. La única defensa duradera es que **los verificadores externos hayan descargado el token de marca de tiempo** — que sobrevive fuera del sistema.

---

## 7. Recomendaciones organizacionales (condicionan la garantía)

Estos controles **exceden a Sigil** pero **determinan la fuerza real** de su evidencia. Sin ellos, varias de las garantías de arriba se debilitan.

| Recomendación | Amenaza que mitiga |
|---------------|--------------------|
| **MFA + políticas de acceso condicional** para todos los usuarios | A5: la atribución de firma es a la cuenta; su fuerza ES la fuerza del login. Además, los **registros de inicio de sesión** del proveedor de identidad capturan IP/dispositivo del acceso previo a cada firma — compensa la no-captura de IP dentro de Sigil |
| **Restringir el rol de Administrador del Sistema al mínimo de personas**, con activación temporal *just-in-time* | A2b y A4: el sysadmin es el único que atraviesa la seguridad de columna; la ventana previa al sellado solo se reduce por esta vía |
| **Gobernanza de la credencial técnica del motor:** certificado (no clave de larga vida), rotación calendarizada, dueño único, jamás compartida, alertas de acceso anómalo | A13 |
| **Backups del ambiente** con política definida | A16 (pérdida de evidencia), en tensión con A15 (un restore también retrocede el registro): la política debe contemplar ambos |
| **Marca de tiempo activada en producción** | Sin ella, la cadena de no-repudio termina dentro del tenant |
| **Política de dispositivos** (gestión de móviles/endpoints) para quienes descargan documentos | A10 |
| **No otorgar roles de personalización** en el ambiente de producción | Reduce la superficie de modificación de componentes del sistema |

---

## 8. Privacidad y datos personales

- **Datos personales tratados:** nombre, correo e identificador corporativo (guardados como evidencia congelada), imagen de firma manuscrita, y el contenido de los documentos.
- **La imagen de firma es dato sensible:** solo la puede leer su dueño; el sistema la usa para incrustarla. La copia congelada por firma es parte de la evidencia de esa transacción.
- **Registros técnicos sin datos personales:** los diagnósticos internos nunca contienen nombres, correos ni contenido.
- **Confidencialidad de borradores:** un firmante no puede ver el documento de un borrador **no enviado**; el documento se le presenta recién al enviarse a firma.
- **Derecho probatorio del firmante:** jamás se revoca el acceso de un firmante a lo que firmó, incluso si la solicitud se cancela — conserva la constancia de que existió.
- **Retención y su tensión legal:** los documentos completados y su evidencia son **permanentes por diseño**. No existe un flujo de borrado posterior a la completitud. **Un pedido de eliminación de datos personales contra un documento sellado es un conflicto legal a resolver por la organización** (borrar la evidencia la destruye). Este dossier lo declara como límite explícito; la decisión excede lo técnico.

---

## 9. Qué NO es Sigil (límites de cumplimiento)

- **No** es firma electrónica calificada ni usa certificados personales X.509 por firmante.
- **No** cubre firmantes externos a la organización.
- **No** dictamina el peso jurídico de su evidencia ante un tribunal concreto — eso es asesoría legal de la organización.
- Su garantía técnica más fuerte (nivel **Completo**) es **oponible fuera del tenant** gracias a la marca de tiempo; su nivel más bajo (**Interno**, con la marca de tiempo apagada) es tamper-evidence **dentro** del perímetro corporativo y así lo declara.

---

## 10. Resumen ejecutivo (una página)

- Sigil produce, para cada documento firmado, una **evidencia criptográfica** compuesta por: la **huella SHA-256** del documento final, un **registro inalterable** de firmantes y momentos, la **firma e identidad congeladas** de cada participante, y —en su nivel más alto— una **marca de tiempo de un tercero imparcial** (estándar RFC 3161).
- La evidencia es **comprobable de forma independiente con herramientas de código abierto** (sin cuenta, sin instalación y sin depender de Sigil) — lo que la hace oponible frente a terceros externos. Dentro de la organización, además, la pantalla de verificación lo resuelve en un clic para cualquier usuario autenticado.
- El sistema **declara siempre el nivel de evidencia** y **no exagera**: distingue lo que es oponible fuera del tenant de lo que solo lo es dentro.
- Los **tres límites honestos** que la organización debe gestionar por vías no técnicas son: el poder del **sysadmin** (restringirlo), la **credencial técnica del motor** (gobernarla), y la **residencia única de la evidencia** (backups + que los verificadores externos descarguen el token).
- Para producción con máximo valor probatorio: **marca de tiempo activa, sysadmins restringidos, MFA universal, y gobernanza de la credencial del motor.**

---

## 11. Glosario

- **Hash / huella (SHA-256):** número de longitud fija calculado a partir de todos los bytes de un archivo. Cambia por completo si cambia un solo byte. Sirve para detectar cualquier alteración.
- **SHA-256:** algoritmo de hash estándar de la industria (256 bits).
- **Integridad:** propiedad de que un documento no fue alterado. Se prueba comparando huellas.
- **No repudio:** imposibilidad de que un firmante niegue creíblemente haber firmado.
- **TSA (Time-Stamping Authority):** tercero imparcial que emite marcas de tiempo firmadas.
- **RFC 3161:** estándar internacional de marcas de tiempo confiables.
- **Token de marca de tiempo (.tsr):** archivo firmado por la TSA que certifica que una huella existía en un instante dado.
- **Ledger / libro de registro:** registro de solo-agregado, inalterable, de las transacciones selladas.
- **Seguridad a nivel de columna:** control que restringe quién puede leer/escribir campos específicos (aquí, la evidencia cruda).
- **SSO (Single Sign-On):** inicio de sesión corporativo unificado; provee la identidad de los actores.
- **Sysadmin (Administrador del Sistema):** rol de máximo privilegio en el ambiente; único actor que atraviesa la seguridad de columna.
- **Idempotencia:** propiedad de que reintentar una operación no produce efectos duplicados ni inconsistentes.
- **MFA (autenticación multifactor):** segundo factor de login que refuerza la atribución a la cuenta.

---

*Documento autónomo — no requiere fuentes externas para su interpretación. La calificación legal de la evidencia aquí descrita corresponde a la asesoría jurídica de la organización.*

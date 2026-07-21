# Sigil — Manual de Usuario

**Para quién:** cualquier persona que use Sigil para **firmar** documentos, **enviarlos** a firmar, o **verificar** que un documento sellado es auténtico.
**No necesitás** saber nada técnico. Si sabés usar el correo, sabés usar Sigil.

> Los textos en **negrita** son lo que ves en pantalla. La app está en español e inglés: el botón **Español / English** (arriba a la derecha) cambia el idioma.

---

## 1. ¿Qué es Sigil y cómo funciona?

Sigil reemplaza el "imprimir, firmar a mano, escanear y mandar por correo". En su lugar:

1. Alguien **crea una solicitud** con un PDF y elige quién debe firmarlo.
2. A cada firmante le llega un aviso (correo + Teams). Cada uno **abre el documento y firma** desde su celular o computadora.
3. Cuando firman todos, Sigil **sella** el documento: le estampa las firmas, le agrega una **hoja de cierre** con una constancia, y le pone una **marca de tiempo confiable**.
4. Cualquiera puede **verificar** después que ese PDF es el original y no fue alterado — sin cuenta, sin instalar nada.

La firma que usás es tu **Firma Maestra**: una imagen de tu firma que configurás **una sola vez** y Sigil reutiliza en cada documento.

---

## 2. Primeros pasos: configurar tu Firma Maestra

**Antes de poder firmar, necesitás una Firma Maestra.** Si intentás firmar sin ella, Sigil te avisa: *"Necesitás configurar tu Firma Maestra antes de firmar"* y te lleva acá.

1. En el menú de arriba, entrá a **Mi firma**.
2. Tocá **Subir imagen de firma**. Necesitás una **imagen PNG con fondo transparente** de tu firma (una foto de tu firma sobre papel blanco, recortada, sirve).
3. Se abre el editor **Encuadrá tu firma**: podés **arrastrar** para posicionar, **recortar**, hacer **zoom**, **rotar** y **espejar** (horizontal o vertical). Abajo ves un **Preview en documentos** de cómo va a quedar.
   > 💡 *Consejo:* si tu firma salió chica o torcida en la foto, acá la agrandás y enderezás. No tengas miedo de ajustarla — todavía no se guardó nada.
4. Tocá **Continuar**. Sigil **valida** la imagen y te muestra el resultado normalizado (*"Así se verá tu firma en los documentos"*).
5. Si te gusta, tocá **Guardar nueva firma** y confirmá. Si no, volvé y ajustá.

**Importante:**
- Guardar crea una **versión nueva** y pasa a ser tu firma **vigente**. **No se puede deshacer** — no volvés a la anterior.
- Podés ver tu **Historial de firmas** (todas las versiones) más abajo en la misma pantalla.
- Los documentos que ya firmaste **conservan la firma con la que los firmaste** — cambiar tu firma no toca lo ya firmado.

---

## 3. Cómo firmar un documento

Es lo que más vas a hacer. Tenés dos formas de llegar:

- **Desde el aviso:** el correo o la tarjeta de Teams traen un botón **Revisar y firmar** que te abre directo el documento (te pide iniciar sesión primero).
- **Desde la app:** entrá a **Inicio**, pestaña **Pendientes**. Ahí están los documentos que esperan tu firma. Tocá **Revisar y firmar** en el que corresponda.

Una vez adentro (**Revisá y firmá**):

1. **Leé el documento.** Usá los controles del visor para moverte: **Anterior** / **Siguiente** página, **Acercar** / **Alejar**.
2. Vas a ver un recuadro que dice **Tu firma quedará acá** — es el lugar donde se estampará tu firma. Si hay otros firmantes, sus zonas aparecen marcadas como **Otros firmantes**.
3. Cuando estés listo, tocá **Aprobar y firmar**.
4. Listo:
   - Si faltan otros firmantes: *"Tu firma quedó registrada"*.
   - Si eras el último: *"Eras el último: estamos sellando el documento"*.

### ¿No estás de acuerdo? Rechazar
En vez de aprobar, tocá **Rechazar**. Te pide un **Motivo del rechazo** (es obligatorio). Al rechazar, se cancela el proceso de firma para todos y se avisa al creador con tu motivo.

### Notas útiles
- **"Todavía no es tu turno de firmar"**: en las solicitudes **secuenciales**, firmás cuando te toca. Vas a recibir el aviso cuando llegue tu turno.
- **Desde el celular:** funciona igual. El flujo de firma está pensado para el teléfono.
- **El correo nunca trae el documento adjunto** — siempre lo abrís y firmás dentro de Sigil, por seguridad.

---

## 4. Cómo crear una solicitud de firma

Si sos vos quien manda un documento a firmar, entrá a **Nueva solicitud**. Es un asistente de **4 pasos**:

### Paso 1 — **Documento**
- Tocá **Seleccionar PDF** y elegí el archivo. (Solo PDF.)
- Poné un **Título de la solicitud** (ej. *"Contrato de servicios 2026"*).
- Opcional: un **Mensaje para los firmantes** y los **Días para vencer** (dejalo vacío si no querés vencimiento).
- **Siguiente**.
  > 💡 *Con PDFs grandes conviene usar la computadora* — subir un archivo pesado desde el celular con mala señal puede fallar.

### Paso 2 — **Firmantes**
- Elegí el **Orden de firma**:
  - **Secuencial**: cada uno firma en orden, uno después del otro.
  - **Paralelo**: todos pueden firmar al mismo tiempo.
- En **Agregar firmante**, buscá por nombre o correo y agregalos. En secuencial podés **Subir** / **Bajar** para ordenar.
- **Siguiente**.

### Paso 3 — **Zonas de firma**
Acá decís **dónde** firma cada persona en el documento (es obligatorio: cada firmante necesita al menos una zona).

1. En **Firmante activo para dibujar**, elegí a una persona (su nombre se resalta).
2. **Dibujá un rectángulo** sobre la página, donde querés que vaya su firma. La proporción se ajusta sola.
3. Repetí para cada firmante. El panel **Zonas por firmante** te muestra quién ya tiene zona (✓) y a quién le **Falta zona**.
   > Si preferís precisión, al seleccionar una zona podés escribir sus valores exactos (**X**, **Y**, **Ancho**) en vez de dibujar.
- **Siguiente**.

### Paso 4 — **Revisión**
- Revisá el resumen (título, orden, firmantes, zonas, vencimiento).
- Tocá **Enviar a firma** para mandarla, o **Guardar borrador** para terminarla después.
- *"Solicitud enviada a firma"* confirma que salió. A partir de ahí, los firmantes reciben sus avisos.

---

## 5. Cómo verificar un documento

**Cualquiera** puede verificar un PDF sellado por Sigil — no necesitás cuenta ni permisos. Sirve para comprobar que un documento que te pasaron es el **original** y **no fue modificado**.

1. Entrá a **Verificar**.
2. **Arrastrá el PDF** o tocá **Seleccionar PDF**.
   > 🔒 *El documento nunca sale de tu navegador:* Sigil calcula su "huella" (hash) localmente y solo compara esa huella. El archivo no se sube.
3. En segundos, uno de tres resultados:

| Resultado | Qué significa |
|-----------|---------------|
| 🟢 **Auténtico e íntegro** | El documento coincide **exactamente** con el registro sellado. Es el original, sin cambios. |
| 🔴 **No coincide** | El archivo **NO** coincide con el documento sellado — fue modificado o no es el original. |
| ⚪ **No encontramos un registro de sellado** | Este PDF no fue sellado por Sigil (o no es el archivo sellado). |

Cuando es auténtico, se muestra la **Constancia**: el número de **Registro**, cuándo fue **Sellado**, la **Huella del documento (SHA-256)**, los **Firmantes**, y si el **Historial de firmas** está íntegro. También podés **Descargar la marca de tiempo (.tsr)** para una comprobación independiente.

> 💡 *Comprobalo vos mismo:* la huella que muestra Sigil la podés recalcular con `sha256sum archivo.pdf` — tienen que coincidir.

---

## 6. Consultar tus documentos

- **Inicio** tiene tres pestañas:
  - **Pendientes** — documentos esperando **tu** firma.
  - **Solicitudes** — las que **vos** creaste.
  - **Participaciones** — aquellas en las que **participás** como firmante (con un filtro **Solo completados**).
- **Documentos** es el buscador completo: filtrá por **Creador**, **Otro participante**, **Estado**, **Versión de mi firma**, y ordená por fecha o título.
- Al abrir una solicitud (**Ver documento**) ves su **Historial** completo (creada, enviada, cada firma, sellado…), los **Firmantes**, y podés **Descargar firmado** / **Descargar original** o **Verificar documento**.
  - Si sos el creador, podés **Cancelar solicitud** (con un motivo). No se puede deshacer.

---

## 7. Estados de una solicitud (glosario)

Cuando mirás una solicitud, su estado te dice en qué anda:

| Estado | Qué significa |
|--------|---------------|
| **Borrador** | La estás armando; todavía no se envió. |
| **Pendiente de firma** | Enviada; esperando que firmen. |
| **Firmado parcialmente** | Algunos firmaron, faltan otros. |
| **Sellando** | Firmaron todos; Sigil está sellando el documento. |
| **Completado** | Sellado y disponible. ✅ |
| **Rechazado** | Un firmante lo rechazó. |
| **Expirado** | Venció sin completarse. |
| **Cancelado** | El creador lo canceló. |
| **Error de sellado** | Hubo un problema al sellar; el creador puede **Reintentar sellado**. |

---

## 8. Preguntas frecuentes

**¿Necesito instalar algo?** No. Sigil corre en el navegador (computadora o celular).

**¿Qué firma se usa?** Tu **Firma Maestra**. La configurás una vez en **Mi firma**.

**¿Puedo firmar desde el celular?** Sí, es el uso principal para firmar. Crear solicitudes con PDFs grandes conviene desde la computadora.

**¿El documento viaja por correo?** No. Los avisos traen un **enlace**; el documento se abre y firma **dentro** de Sigil. Nunca hay adjuntos.

**¿Qué es la "marca de tiempo"?** Una prueba independiente de **cuándo** existió el documento sellado, emitida por una autoridad externa. Refuerza el valor probatorio.

**¿Puedo recuperar una versión anterior de mi firma?** No. Guardar una firma nueva es definitivo. Pero lo ya firmado conserva su firma original.

**Rechacé por error, ¿puedo deshacer?** No. El rechazo cierra el proceso. Habría que crear una solicitud nueva.

---

*¿Sos operador, auditor o desarrollador? Mirá las otras guías en el [índice](00-indice.md).*

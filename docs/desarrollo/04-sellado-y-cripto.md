# Sellado y criptografía

**Alcance.** El corazón probatorio de Sigil: por qué el sellado es **asíncrono**, el pipeline real del
`SealingWorkerPlugin` paso a paso, el modelo de evidencia (doble hash SHA-256 + token TSA RFC 3161 +
ledger con *alternate key*), la **hoja de cierre** que compone el PDF final, y los caminos de reintento
(`RetrySealing`) y re-sellado (`ResealPending`). El framework de plugins y el lock de fila están en
[Backend](03-backend.md); el ciclo de vida de estados y el modelo de confianza, en
[Arquitectura](01-arquitectura.md); el motor de imagen que valida el PNG que acá se incrusta, en
[Firma maestra](05-firma-maestra.md).

---

## 1. Por qué el sellado es asíncrono

Los plugins de Dataverse — síncronos y asíncronos — corren dentro de un sandbox con un **límite de
ejecución de ~2 minutos**. El sellado no cabe con holgura ahí: descarga del PDF de contenido, descarga de
los snapshots de cada firma, composición del documento (incrustación de imágenes + hoja de cierre + QR),
serialización, hash, TSA por red con posibles reintentos, y varias escrituras. La solución no es "hacerlo
más rápido" sino **separar responsabilidades**:

- **`SubmitSignature`** (síncrono, [Backend](03-backend.md)) hace lo mínimo en milisegundos: valida, lockea,
  decide si es la última firma, registra la intención y transiciona la transacción a **Sellando**. Libera
  al usuario de inmediato.
- El paso a `Sellando` **dispara un step asíncrono** que corre el `SealingWorkerPlugin`. El trabajo pesado
  ocurre después, invisible al usuario.

El disparo es por **cambio de estado**, no por una llamada directa: el step está registrado como
**post-operation asíncrono sobre `Update` de la transacción**, con *filtering attribute* `sanic_sigil_status`
y una **post-image** llamada `PostImage`.

---

## 2. El pipeline del `SealingWorkerPlugin`

`src/backend/Sigil.Plugins/Apis/SealingWorkerPlugin.cs` implementa `IPlugin` directamente (no hereda de
`SigilApiPlugin` porque no es una Custom API). Resuelve su propio contexto de sistema y sus seams
(`IFileTransfer`, `ISelladorTsa`), y ejecuta el sellado bajo los guards descritos en §3.

### 2.1 Los pasos reales, en orden

El método `Sellar` (`SealingWorkerPlugin.cs:96`) ejecuta el pipeline en este orden estricto. Los pasos están
numerados en el propio código:

| Paso | Qué hace | Fallo → |
|------|----------|---------|
| **0** | **Lock + re-lectura del estado actual**: `LockDeFila.Tomar`, luego `Retrieve` de la transacción; si el estado actual ≠ `Sellando`, aborta (reintento zombi). Si ya existe **ledger** para la transacción → salta directo a completar (idempotente). *(Reutilizar el `final file` de un intento previo es un guard aparte, más adelante — §3.)* | — |
| **1** | **Descargar el PDF de contenido** (`contentfile`) | transitorio → Retry |
| **2** | **Verificar `hash_contenido`**: SHA-256 de lo descargado debe coincidir con `sanic_sigil_contenthash` anclado al enviar. Si no coincide → contenido adulterado | **definitivo** → Error de Sellado |
| **3** | **Incrustar las firmas** (los snapshots congelados) en sus zonas | — (parte de la composición) |
| **4** | **Hoja de cierre** + metadatos del documento (§5) | — (parte de la composición) |
| **5** | **Serializar UNA vez** el PDF final y calcular **`hash_final`** (SHA-256 de esos bytes) | — |
| **6** | **Obtener el token TSA** (si `TsaEnabled`) sobre `hash_final` (§4) | TSA caída no aborta: el ledger queda en `Re-sellado pendiente` |
| **7** | **Subir el PDF final** (`finalfile`) — **antes** del ledger | transitorio → Retry |
| **8** | **Crear el registro de ledger** (la *alternate key* hace el insert idempotente) | duplicado → se ignora (carrera resuelta en BD) |
| **8.5** | **Verificar el ancla**: el `finalfile` durable debe hashear al `finalhash` del ledger | **definitivo** → Error de Sellado |
| **9** | **Transicionar a `Completado`** + `completedon` + evento `SelladoCompletado` con lectores | — |

Los pasos 3-5 los ejecuta el núcleo puro: `ComposicionDeDocumento.ComponerDocumentoFinal(...)`
(`SealingWorkerPlugin.cs:209`), que devuelve los bytes finales — el worker solo orquesta descargas y subidas.

### 2.2 El orden 7 → 8 es mandatorio (idempotencia)

La serialización de un PDF **no es determinística** (IDs de objeto, metadata, orden interno): recomponer el
mismo documento en un reintento produce bytes distintos → hash distinto. Por eso el archivo durable se sube
**antes** que el ledger que lo referencia:

- Guardando el `finalfile` primero, **el hash del ledger siempre describe bytes que ya existen**.
- El orden inverso produciría un registro inmutable apuntando a bytes que ya no existen: transacción
  permanentemente inverificable. Prohibido.

Un reintento que encuentra el `finalfile` durable de un intento previo **re-usa esos bytes exactos** (los
descarga y recalcula el hash sobre ellos), jamás recompone ni sube un segundo archivo
(`SealingWorkerPlugin.cs:134`). La ausencia del final se decide con una **sonda de metadata** (un `Retrieve`
de la columna File), no con el fallo de la descarga: un timeout de transporte no significa "no hay archivo".

---

## 3. Guards del disparador

Un guard mal puesto mata el pipeline; uno de menos lo corrompe. El worker tiene cuatro
(`SealingWorkerPlugin.cs:47`):

- **Depth guard (umbral alto).** `if (contexto.Depth > 8) return;`. El worker corre **legítimamente** con
  `Depth ≥ 2` (lo dispara el `Update` de `SubmitSignature`/`RetrySealing`); el guard correcto es un anti-loop
  de umbral alto, **jamás** `Depth > 1` (eso desactivaría el sellado entero).
- **Guard de post-image.** Si la post-image `PostImage` **falta**, es un step mal registrado → lanza
  excepción **ruidosa** (un no-op silencioso dejaría el sellado muerto para siempre). Si la post-image trae
  `status ≠ Sellando`, `return` silencioso — es el auto-retrigger del paso 9 (que escribe `Completado`), que
  no tiene nada que hacer.
- **Guard de estado ACTUAL bajo lock.** La post-image no basta: un reintento encolado conserva la post-image
  vieja aunque un saneamiento o un `RetrySealing` hayan corrido en el medio. Tras tomar el lock, el worker
  **relee el estado actual** y aborta si ≠ `Sellando` (`SealingWorkerPlugin.cs:104`).
- **Guard de ledger existente.** Antes del paso 1, si ya hay ledger para esta transacción, verifica el ancla
  y salta directo a completar (paso 9) — jamás recompone ni re-sube (`SealingWorkerPlugin.cs:116`).

### 3.1 Semántica de fallos

El worker clasifica cada excepción en **transitoria** o **definitiva** (`SealingWorkerPlugin.cs:69`):

- **Transitorio** (download/upload/BD fallidos, deadlock, timeout, fault de plataforma) →
  `InvalidPluginExecutionException` con **`OperationStatus.Retry`**. La plataforma reintenta (hasta 4) y
  cada reintento **re-entra por el flujo idempotente**. Un `FaultException<OrganizationServiceFault>` de
  plataforma se trata como transitorio por contrato.
- **Definitivo** (mismatch de `hash_contenido`, PDF corrupto, env var faltante, ancla rota) → la transacción
  pasa a **Error de Sellado** con un evento `ErrorDeSellado` accionable y **sin ledger parcial**. El detalle
  técnico va **truncado al evento** (no solo al trace): el `asyncautodelete` borra el system job exitoso y el
  trace puede no estar habilitado, así que el evento es el único rastro garantizado
  (`SealingWorkerPlugin.cs:392`).

`FalloDefinitivo` revalida el estado actual antes de escribir: jamás pisa un `Completado` ajeno ni reescribe
un `Error de Sellado` ya puesto (el status idéntico dispara los flows). La única salida de `Error de Sellado`
es `RetrySealing` (§6).

---

## 4. El modelo de evidencia

La prueba de integridad se sostiene en **dos hashes SHA-256** y una **marca de tiempo externa**.

### 4.1 Doble hash

`Crypto/HashUtil.cs` produce el formato canónico — SHA-256 en **hex mayúsculas sin separadores**:

```csharp
public static string Sha256Hex(byte[] bytes) { /* SHA256 → "X2" por byte */ }
```

- **`hash_contenido`** (`sanic_sigil_contenthash`) — se calcula al **enviar** (`SendTransaction`) sobre el
  PDF que aprobaron y leyeron los firmantes. El worker lo **re-verifica** en el paso 2: si el contenido
  descargado no coincide, aborta (jamás sella contenido adulterado). Se imprime en texto claro en la hoja de
  cierre y se ancla en el ledger.
- **`hash_final`** (`sanic_sigil_finalhash`) — se calcula en el paso 5 sobre el PDF completo (con firmas y
  hoja de cierre). Prueba que el archivo distribuido es idéntico byte a byte al sellado. Es el ancla de la
  verificación.

### 4.2 El token TSA (RFC 3161)

El cliente RFC 3161 vive en el núcleo puro (`Crypto/ClienteTsa.cs`) y se ejercita a través del seam
`ISelladorTsa` ([Backend](03-backend.md)). El método `SelloPara(sha256Digest, config)` sella el digest
contra el **primer endpoint que responda un token válido**, en el orden de la config.

Requisitos **no negociables** del token (`ClienteTsa.cs:62`):

- **`SetCertReq(true)`**: el token DEBE traer el certificado del firmante de la TSA. Sin él, la validación
  independiente años después puede volverse imposible.
- **Nonce aleatorio** de 16 bytes (`RandomNumberGenerator`), para atar el request a su respuesta.
- **Doble validación antes de aceptar el token**:
  1. `response.Validate(request)` — nonce, imprint y política coinciden con lo pedido.
  2. `token.Validate(cert)` — la firma criptográfica del token verifica contra el certificado **embebido**.
  Límite honesto declarado: **no** se valida la cadena del certificado hasta una raíz confiable.
- **HTTPS obligatorio**: `TsaConfig.Parse` (`Crypto/TsaConfig.cs`) rechaza cualquier URL que no sea `https://`
  — un canal claro habilitaría un MITM del token.
- **Fallback en orden** + **rate limit por endpoint** (`minIntervalSeconds`; el intento cuenta para el rate
  limit aunque falle, porque las TSAs cuentan requests, no éxitos).

El `GenTime` del token se normaliza a UTC explícitamente (`ClienteTsa.cs:103`): viene de un
`GeneralizedTime` ASN.1 y si el `Kind` llega `Unspecified`, se fija a `Utc` para no correrlo asumiendo hora
local.

El resultado es un `ResultadoTsa` con el token DER, el `GenTimeUtc`, el endpoint que respondió y la lista de
errores por endpoint fallido. El worker lo mapea a `TsaStatus`: `SelladoConTsa` si exitoso,
`ReSelladoPendiente` si todos los endpoints fallaron (la TSA caída **no** aborta el sellado — el ledger queda
con el estado explícito para que `ResealPending` lo reintente).

La config por defecto en Dev (`ApiSpec.cs`, `EnvValues`) tiene dos endpoints en orden: Sectigo
(`https://timestamp.sectigo.com`, `minIntervalSeconds: 15`) y DigiCert (`https://timestamp.digicert.com`).

### 4.3 El ledger — evidencia idempotente

El registro de ledger (`sanic_sigil_tbl_ledgerentry`) es la evidencia. Se crea en el paso 8
(`SealingWorkerPlugin.cs:243`) con `contenthash`, `finalhash`, `tsastatus`, `sealedon`, el `tsatoken` en
base64 (si hubo) y un `signersummary` en JSON (insumo de la pantalla de verificación).

- Tiene una **alternate key** sobre `transactionid` que hace el insert **idempotente**: un reintento del
  worker que intenta crear un segundo ledger recibe un fault de duplicado
  (`-2147088238 DuplicateRecordEntityKey` o `-2147220937 DuplicateRecord`) que el worker **captura por
  código de error** (jamás por el texto del mensaje, que cambia por idioma) y trata como "carrera perdida
  limpiamente" — continúa al paso 9.
- El `name` es **autonumber**: el plugin **jamás** lo escribe (pisaría la numeración).
- **Verificación del ancla** (`VerificarAnclaOFallar`, `SealingWorkerPlugin.cs:363`): como el worker es
  asíncrono y el lock no serializa entre ejecuciones distintas, antes de completar el worker **re-descarga el
  `finalfile` durable y verifica que su hash coincida con el `finalhash` del ledger**. Un mismatch significa
  interleaving de sellados concurrentes → Error de Sellado, jamás se completa con evidencia inconsistente.

---

## 5. La hoja de cierre (`ComposicionDeDocumento`)

`src/backend/Sigil.Plugins.Core/Pdf/ComposicionDeDocumento.cs` es el pipeline puro que va de los bytes del
contenido aprobado a los bytes del documento final, con **una sola serialización**
(`ComponerDocumentoFinal`, `ComposicionDeDocumento.cs:66`). Usa PDFsharp `6.2.4`, SixLabors.ImageSharp y
QRCoder.

**Paso 3 — incrustar las firmas.** Por cada firma, decodifica su PNG y la dibuja en cada una de sus zonas
usando `TransformacionDeCoordenadas.ParaZona(page, ...)` para obtener la matriz `cm`. El contenido original
de cada página tocada se envuelve **una vez** en `q`/`Q` (guardar/restaurar estado gráfico): un PDF real
puede terminar con la CTM alterada, y sin ese envoltorio la firma aterrizaría en cualquier lado.

**Paso 4a — hoja(s) de cierre** (`AgregarHojasDeCierre`, `ComposicionDeDocumento.cs:115`). Agrega páginas
carta (612×792 pt) con, hasta **6 firmantes por hoja** (`FirmantesPorHoja = 6`; con más, *overflow* a
páginas adicionales). Cada firmante muestra su nombre, email, fecha de firma (`Firmó: … UTC`) y una **estampa
con la imagen de su snapshot congelado**. En la **última** hoja va el **pie probatorio**:

- El `SHA-256 del documento aprobado (hash_contenido)` en texto claro.
- La URL de verificación (`{AppPlayUrl}?screen=verify&txId={txId}`), **envuelta por ancho** porque puede
  pasar los 130 caracteres y pisaría el QR.
- El `Identificador: {txId}`.
- Un **QR** (QRCoder, `ECCLevel.M`) que codifica esa misma URL de verificación.

> **Enmienda de diseño verificada.** La hoja **no** imprime un "número de ledger": el ledger (autonumber)
> nace en el paso 8, **después** del upload, así que su número no se conoce al componer. La hoja imprime
> `hash_contenido` + URL de verificación + `txId`; el número de ledger lo muestra `VerifyDocument`.

**Paso 4b — metadatos** del documento (`doc.Info`): título, `Subject` con la URL de verificación y `Keywords`
con `SHA-256:{hash_contenido}; txId:{txId}`.

### 5.1 Incrustación de PNG bajo net462 (`XObjectManual`)

> **Gotcha verificado.** El importador de imágenes de PDFsharp (`XImage.FromStream`) **falla siempre** en el
> sandbox net462 (devuelve null / "unsupported image format"), aunque el header PNG sea correcto. Por eso
> `Pdf/XObjectManual.cs` incrusta la imagen **a mano**: un dict `/Image` con stream RGB `FlateDecode` +
> `/SMask` DeviceGray para el canal alfa, y el operador `Do` en el content stream. Además, net462 no tiene
> `ZLibStream`, así que el zlib se arma artesanalmente: `0x78 0x9C` + `DeflateStream` crudo + **Adler-32**
> propio (`XObjectManual.cs:75`). Esto obliga al pin **PDFsharp `=6.2.4` exacto**.

La fuente también va **embebida** (`PdfSharp.WPFonts`, Segoe WP): el sandbox no tiene filesystem ni fuentes
del sistema, así que un `IFontResolver` sirve los bytes desde el assembly.

### 5.2 El contrato de coordenadas

`Pdf/TransformacionDeCoordenadas.cs` es el contrato compartido con el frontend: las zonas vienen en **% del
área visible** (CropBox; MediaBox si no hay), origen **arriba-izquierda**, en orientación **visual**. La
clase produce la matriz `cm` que coloca la imagen *upright* compensando `/Rotate` para las cuatro rotaciones
(0/90/180/270) — el espacio de usuario del PDF es *raw* y `/Rotate` no se compensa solo. Una zona dibujada en
la UI del frontend cae en el píxel correcto del PDF sellado.

---

## 6. Reintento y re-sellado

Son dos caminos distintos para dos fallos distintos.

### 6.1 `RetrySealing` — salir de Error de Sellado

`sanic_sigil_capi_RetrySealing` (`Apis/RetrySealingPlugin.cs`, bound) es la **única salida** de
`Error de Sellado`. Sin ella el estado sería un callejón sin salida (nadie tiene `Update` directo). El
handler:

1. Lock de fila + revalidación de estado.
2. Autoriza: solo **el creador** y solo desde `Error de Sellado`.
3. Transiciona a `Sellando` — ese `Update` **re-dispara el worker** (que es idempotente, §2.2).
4. Registra un evento `SelladoIniciado` con detalle "Reintento manual" para distinguirlo en la línea de
   tiempo.

### 6.2 `ResealPending` — reintentar la TSA caída

`sanic_sigil_capi_ResealPending` (`Apis/ResealPendingPlugin.cs`, unbound, **job diario** con privilegio de
servicio) reintenta el **sello TSA** sobre los ledgers en `ReSelladoPendiente` — los que se completaron con
la TSA caída. No re-sella el documento; solo consigue el token que faltó.

Puntos clave:

- **Cap de lote** `MaxResellosPorCorrida = 3`. Por ledger: descarga + TSA + rate limit de Sectigo (≥15 s) ≈
  20-30 s. Sin cap, el 5º-8º ledger cruzaría el límite duro de 2 minutos y el rollback transaccional
  revertiría todo (tokens ya pedidos = requests quemados). El job es diario e idempotente: drenar de a 3 es
  correcto; el resto queda para las próximas corridas (`StillPendingCount`).
- **Integridad primero**: antes de sellar, verifica que el `hash_final` del `finalfile` durable coincida con
  el `finalhash` del ledger. Un mismatch es una señal de integridad catastrófica — se cuenta **aparte**
  (`AnchorMismatchCount`) para que el operador lo distinga de una TSA simplemente caída, y jamás se sella.
- **`sealedon` no se toca** en el re-sellado: el token prueba existencia a **su** `genTime`, no antes; el
  nivel de evidencia muestra ambas fechas.
- **Con `TsaEnabled = false`**: los ledgers pendientes se mueven a `SinSelloTsa` (`MovedToNoTsaCount`) con un
  evento `TsaAbandonada` (choice `159460012`), para no dejar huérfanos eternos bajo una etiqueta que promete
  un reintento que no va a ocurrir.

Outputs: `ResealedCount`, `MovedToNoTsaCount`, `StillPendingCount`, `AnchorMismatchCount`.

---

## 7. Estados relevantes del sellado

| Estado (`TransactionStatus`) | Valor | Cuándo |
|------------------------------|-------|--------|
| **Sellando** | `159460003` | Desde la última firma hasta el fin del pipeline; disparador del worker |
| **Completado** | `159460004` | Pipeline exitoso (paso 9): `finalfile` subido, ledger creado, ancla verificada |
| **Error de Sellado** | `159460007` | Fallo definitivo o retries agotados; único camino de vuelta: `RetrySealing` |

Y el nivel de evidencia del ledger, en `TsaStatus`: `SelladoConTsa` (`159460000`), `SinSelloTsa`
(`159460001`), `ReSelladoPendiente` (`159460002`).

---

## Referencias externas

- **RFC 3161 — *Time-Stamp Protocol (TSP)*** — IETF, *"Internet X.509 Public Key Infrastructure
  Time-Stamp Protocol (TSP)"*. Define el `TimeStampReq`/`TimeStampResp`, `certReq`, el nonce y la validación
  del token.
- **BouncyCastle TSP (`TimeStampRequestGenerator`, `TimeStampResponse`, `TimeStampToken`)** — documentación
  de Bouncy Castle for .NET.
- **Plugins asíncronos de Dataverse y `OperationStatus.Retry`** — Microsoft Learn, *"Asynchronous service"* /
  *"Handle exceptions"*.
- **Alternate keys de Dataverse (inserción idempotente, `DuplicateRecordEntityKey`)** — Microsoft Learn,
  *"Define alternate keys"*.
- **PDFsharp (`PdfReader`, content streams, XObjects de imagen)** — documentación de PDFsharp 6.x.

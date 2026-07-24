# Firma maestra

**Alcance.** El motor que valida y normaliza la **Firma Maestra** de un usuario: qué es, las tres métricas
de calidad que decide y sus **umbrales reales**, los *gates* de formato (PNG, tamaño, dimensiones), la
normalización a un lienzo estándar, y la persistencia versionada e inmutable. El motor de imagen vive en el
núcleo puro; los handlers que lo exponen son tres Custom API unbound. El framework de plugins está en
[Backend](03-backend.md); la incrustación del PNG normalizado en el PDF sellado, en
[Sellado y criptografía](04-sellado-y-cripto.md).

---

## 1. Qué es la Firma Maestra

La **Firma Maestra** es la imagen PNG de la firma manuscrita de un usuario, validada una vez y reutilizada
en cada firma. Al firmar una transacción (`SubmitSignature`), el backend **congela** el PNG normalizado
vigente como `signaturesnapshot` del participante y lo referencia por versión exacta
(`mastersignatureid`). Así la firma que se incrusta en el documento sellado es siempre la que el usuario
validó, no una que suba en el momento.

La firma se guarda **versionada**: cada carga válida crea una versión nueva y desactiva la anterior, pero el
historial **jamás se pisa** (§4). Cada firma incrustada apunta a la versión que estaba vigente al momento de
firmar.

---

## 2. El motor de validación (`MotorDeFirmaMaestra`)

`src/backend/Sigil.Plugins.Core/Imaging/MotorDeFirmaMaestra.cs` es una clase **pura** del núcleo: recibe los
bytes de la imagen y un `SignatureSpec` (los umbrales, cargados de una env var), y devuelve un
`ResultadoDeFirma` con el veredicto, los motivos de rechazo, las métricas medidas en JSON y — si es válida —
el PNG normalizado listo para persistir e incrustar. Usa **SixLabors.ImageSharp `2.1.13`** para decodificar
y medir píxeles.

El método `Procesar(bytes, spec)` (`MotorDeFirmaMaestra.cs:60`) aplica, en orden: *gates* de formato →
métricas de calidad → normalización → *gate* de peso.

### 2.1 Los gates de formato

Antes de tocar píxeles, el motor valida el **header** con `Image.Identify` (que no asigna píxeles) y rechaza
en tres frentes (`MotorDeFirmaMaestra.cs:62`):

| Gate | Condición de rechazo | Motivo |
|------|----------------------|--------|
| **Decodificable** | `Identify` falla o devuelve null | "La imagen no se pudo decodificar — subí un PNG válido." |
| **Formato PNG** | `formato.Name != "PNG"` | "El archivo debe ser PNG (recibido: …)." |
| **Dimensiones máximas** | `Width` o `Height` > **`MaxDimensionPx = 4096`** | "La imagen es demasiado grande (…; máximo 4096×4096)." |

> **Por qué el límite de 4096 px** (`MotorDeFirmaMaestra.cs:58`). Es un techo anti "bomba de descompresión":
> el límite de carga acota los bytes **comprimidos**, no los píxeles — un PNG chico puede declarar dimensiones
> gigantes y pedir gigas al decodificar (el análisis usa `w·h·4` + `w·h·8` de buffers), matando el worker del
> sandbox. 4096×4096 sobra para cualquier firma real (el target es 600×200).

Recién tras pasar los tres gates se hace `Image.Load<Rgba32>(bytes)` (que sí materializa píxeles).

### 2.2 Las tres métricas y sus umbrales reales

El motor mide **tres** parámetros sobre los píxeles (`Medir`, `MotorDeFirmaMaestra.cs:133`) y rechaza si
cualquiera cae por debajo de su umbral. Los umbrales viven en el `SignatureSpec` (§3), calibrables por
ambiente. Los valores **por defecto desplegados** (JSON de `sanic_sigil_env_SignatureImageSpec` en
`tools/Sigil.Deploy/ApiSpec.cs`) son:

| Métrica | Campo del spec | Umbral (default) | Qué mide |
|---------|----------------|-------------------|----------|
| **alphaRatio** | `minAlphaRatio` | **0.15** | Fracción de píxeles **totalmente transparentes** (`alfa == 0`). Una foto de firma sobre papel es 100% opaca → `0.0` → rechazo. Exige fondo transparente |
| **rmsContrast** | `minRmsContrast` | **0.25** | RMS del **apartamiento de la tinta** respecto del fondo blanco, sobre píxeles **no** transparentes: `sqrt(mean(((255 − L)/255)²))` con `L` = luminancia BT.601. Mide "trazo suficientemente oscuro" |
| **laplacianVariance** | `minLaplacianVar` | **80** | Varianza del **Laplaciano de 4 vecinos** sobre la luminancia 0–255. Bordes nítidos → alta; imagen borrosa → baja |

Detalles de cómputo, verificados en el código:

- **alphaRatio** = `transparentes / (w·h)`, donde `transparentes` cuenta los píxeles con `p.A == 0` exactos.
- **rmsContrast** se calcula **solo sobre los píxeles con tinta** (no transparentes), acumulando el desvío al
  cuadrado `((255 − L)/255)²` y sacando `sqrt(mean)`. La luminancia `L` se compone sobre **blanco** (el fondo
  transparente cuenta como papel): `r = p.R·a + 255·(1−a)`, etc., con `L = 0.299·r + 0.587·g + 0.114·b`
  (BT.601). Esto lo hace **independiente de la cobertura**: tinta negra → ~1.0; trazos desvaídos → ~0.03, sin
  importar cuánta superficie cubran.

  > **Nota de calibración verificada.** El RMS **global** de toda la imagen castigaba a las firmas legítimas
  > de trazo fino (5% de cobertura de tinta negra da ~0.21 global, por debajo de 0.25). La intención del
  > umbral es "trazo suficientemente oscuro", no "mucha tinta" — de ahí que el RMS se calcule solo sobre los
  > píxeles de tinta.

- **laplacianVariance** recorre el **interior** de la imagen (`1 … w-2`, `1 … h-2`) y por cada píxel calcula
  `lap = 4·c − arriba − abajo − izq − der`, acumulando media y media de cuadrados para la varianza.

Las métricas medidas se serializan a JSON (`alphaRatio` y `rmsContrast` redondeados a 4 decimales,
`laplacianVariance` a 2, más `width`/`height`/`bytes`) y ese JSON se guarda en `validationdetails` de la
firma para auditoría.

### 2.3 Normalización

Si las tres métricas pasan, el motor **normaliza** (`Normalizar`, `MotorDeFirmaMaestra.cs:186`):

1. Escala la imagen **sin deformar** para encajarla dentro del lienzo estándar
   (`min(targetW/w, targetH/h)`).
2. La centra sobre un lienzo transparente de `TargetWidthPx × TargetHeightPx` (default **600×200**) con
   *padding* transparente.
3. La codifica como **PNG RGBA 8-bit no entrelazado** (`ColorType = RgbWithAlpha`, `BitDepth = Bit8`,
   `InterlaceMethod = None`) — el formato exacto que el motor de PDF espera para incrustarla ([Sellado y cripto](04-sellado-y-cripto.md)).

### 2.4 El gate de peso

Tras normalizar, un último *gate*: si el PNG normalizado supera `spec.MaxKB · 1024` (default **150 KB**), se
rechaza ("La firma normalizada pesa … KB; máximo …"). Solo entonces el resultado es válido.

### 2.5 El JSON de configuración

`Imaging/SignatureSpec.cs` mapea el JSON de `sanic_sigil_env_SignatureImageSpec`. El default desplegado:

```json
{ "targetWidthPx": 600, "targetHeightPx": 200, "maxKB": 150,
  "minAlphaRatio": 0.15, "minRmsContrast": 0.25, "minLaplacianVar": 80 }
```

`SignatureSpec.Parse` valida que `targetWidthPx`, `targetHeightPx` y `maxKB` sean positivos. Al ser una env
var, los umbrales se **recalibran por ambiente sin redeploy** del backend.

---

## 3. Los handlers de Firma Maestra

Tres Custom API **unbound** (globales) exponen el motor. Todas operan **solo sobre la firma del propio
llamante** — jamás aceptan un `userId` como parámetro (la identidad sale del contexto de ejecución,
[Backend](03-backend.md)).

### 3.1 `ValidateMasterSignature` — validar y (opcionalmente) persistir

`Apis/ValidateMasterSignaturePlugin.cs` valida y normaliza, con dos modos según `Persist`:

- **Sin `Persist`** (default): solo **valida** y devuelve el preview. El frontend muestra el PNG normalizado
  y **confirma** antes de reemplazar la firma vigente (el reemplazo es irreversible).
- **Con `Persist = true`**: crea la nueva versión vigente y desactiva la anterior **en la misma operación**.

Flujo (`ValidateMasterSignaturePlugin.cs:29`):

1. **Gate de tamaño sobre el string base64, antes de decodificar** (mismo orden mandatorio que el PDF). El
   límite de carga es **10× `maxKB`** (`FactorDeCargaSobreMaxKB = 10`): una firma escaneada legítima puede
   pesar más que su versión normalizada, así que la carga cruda admite hasta 1.5 MB (con el default de 150 KB).
2. Decodifica el base64 (un error acá es de contrato → excepción).
3. `MotorDeFirmaMaestra.Procesar(bytes, spec)`.
4. Emite `IsValid` y `MetricsJson` siempre. Si es **inválida**, `FailureReasons` (un motivo por línea) y
   retorna — es un **veredicto, no una excepción** (el frontend muestra los motivos). Si es **válida**, emite
   siempre `NormalizedImageBase64` para el preview.
5. Si `Persist`, versiona (§4).

Distinción de errores importante: una imagen que **no pasa los umbrales** es un veredicto (`IsValid=false` +
motivos); los errores de **contrato** (base64 roto, tamaño excedido) sí son excepciones.

### 3.2 `GetMasterSignature` — la vigente

`Apis/GetMasterSignaturePlugin.cs` devuelve el PNG normalizado de la firma **vigente** del llamante
(`ImageBase64`) y su `ValidatedOn`. Si el usuario no tiene firma vigente, **los outputs quedan ausentes** —
no es un error: el frontend interpreta la ausencia como "todavía no tenés Firma Maestra" y ofrece el
onboarding.

### 3.3 `GetMasterSignatureHistory` — el historial

`Apis/GetMasterSignatureHistoryPlugin.cs` devuelve el historial completo de versiones del llamante como
`HistoryJson` (una Custom API no devuelve colecciones nativas). Cada ítem trae `version`, `imageBase64`
(el PNG normalizado de esa versión), `validatedOn`, `isActive` y la lista de **documentos firmados con esa
versión** (agrupando participantes firmados por `mastersignatureid`). Sin firmas, `HistoryJson = "[]"`. Una
versión con archivo corrupto se **omite** sin voltear todo el historial.

---

## 4. Persistencia y versionado

La tabla es `sanic_sigil_tbl_mastersignature` (`SchemaNames.FirmaMaestra`), con columnas `version`,
`isactive`, `signaturefile` (File), `validatedon` y `validationdetails` (el JSON de métricas).

El versionado, al persistir (`ValidateMasterSignaturePlugin.cs:78`), es **inmutable**:

1. Calcula el número nuevo: `max(version existente) + 1`, o `1` si es la primera.
2. **Desactiva** la(s) versión(es) vigente(s): `Update` con `isactive = false`. El registro histórico
   **no se borra ni se sobrescribe** — solo deja de ser el vigente.
3. **Crea** la versión nueva con `isactive = true`, `version = N`, `validatedon = ahora`,
   `validationdetails = MetricsJson`, `ownerid = llamante`. El `name` es `"{UPN} v{N}"`, donde el sufijo
   `" v{N}"` **jamás se trunca** (se recorta el UPN si es necesario).
4. **Sube** el PNG normalizado a `signaturefile` (`image/png`).

Así el historial es un rastro completo: cada versión conserva su imagen, su fecha y sus métricas, y cada
firma incrustada en un documento apunta a la versión exacta con la que se firmó — verdad histórica que no
cambia aunque el usuario suba una firma nueva después.

---

## Referencias externas

- **SixLabors.ImageSharp (`Image.Identify`, `Image.Load<Rgba32>`, `PngEncoder`)** — documentación de
  SixLabors.ImageSharp 2.x.
- **Luminancia BT.601 (coeficientes 0.299 / 0.587 / 0.114)** — ITU-R BT.601.
- **Laplaciano y varianza como métrica de nitidez (*variance of the Laplacian*)** — literatura estándar de
  *blur detection* en visión por computadora.
- **Custom APIs de Dataverse (parámetros y propiedades de respuesta)** — Microsoft Learn, *"Create and use
  Custom APIs"*.

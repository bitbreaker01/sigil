# Variables de Entorno

**Fuente única de verdad** de las **variables de entorno** (environment variables) de Sigil.
Documenta las 10 variables definidas en la solución, su tipo, su valor en el ambiente **Dev**,
quién las consume y para qué sirven.

Los valores de esta referencia se extraen directamente de la metadata real de la solución:

| Dato | Fuente |
|------|--------|
| Schema name, display name, tipo | `solutions/unpacked/environmentvariabledefinitions/*/environmentvariabledefinition.xml` |
| Valor en Dev | `solutions/unpacked/environmentvariabledefinitions/*/environmentvariablevalues.json` |
| Valores que siembra el deploy | `tools/Sigil.Deploy/ApiSpec.cs` (`Catalogo.EnvValues`) |
| Contrato de lectura del backend | `src/backend/Sigil.Plugins/Data/EnvVars.cs` |

## Tipos de variable de entorno

Dataverse persiste el tipo como un código numérico en `<type>` dentro del
`environmentvariabledefinition.xml`. El mapeo es:

| Código `<type>` | Tipo |
|-----------------|------|
| `100000000` | String (texto) |
| `100000001` | Number (número — se persiste como decimal) |
| `100000002` | Boolean (Two options — la plataforma persiste `"yes"` / `"no"`) |
| `100000003` | JSON |

> Todas las variables tienen `secretstore = 0` (no son secretos), `isrequired = 0` y
> `iscustomizable = 1`. El prefijo de schema es `sanic_sigil_env_`.

## Cómo las lee el backend

El backend NO inventa defaults: si una variable falta o está mal formada, **falla ruidoso**
(lanza `InvalidPluginExecutionException`). El contrato de lectura vive en
`src/backend/Sigil.Plugins/Data/EnvVars.cs`:

- **Number** → `EnteroObligatorio(schema)`: parsea el decimal como entero; exige que sea un
  entero positivo (`>= 1`).
- **Boolean** → `BoolObligatorio(schema)`: interpreta como `true` los valores `yes` / `true` / `1`.
- **String / JSON** → `TextoObligatorio(schema)`: exige que no esté vacía; el JSON lo parsea
  el consumidor (`TsaConfig.Parse`, `SignatureSpec.Parse`).

La lectura usa `RetrieveEnvironmentVariableValue` con caché **por ejecución** (la plataforma no
cachea entre ejecuciones).

## Catálogo de variables

| Schema name | Display name | Tipo | Valor en Dev | Consumidor | Para qué sirve |
|-------------|--------------|------|--------------|------------|----------------|
| `sanic_sigil_env_AppPlayUrl` | Sigil \| ENV \| URL de la App | String | `https://apps.powerapps.com/play/e/dev-pendiente/a/dev-pendiente` | Backend (`SealingWorkerPlugin`) | URL base de la app de firma. El backend le agrega `?screen=verify&txId=...` para el link/QR de la hoja de cierre. **Por-ambiente.** |
| `sanic_sigil_env_DefaultLanguage` | Sigil \| ENV \| Lenguaje por defecto | String | `es` | Backend (`ProcessRemindersPlugin`) | Idioma por defecto para los recordatorios cuando el participante no tiene idioma propio. |
| `sanic_sigil_env_ExpirationDefaultDays` | Sigil \| ENV \| Dias de expiracion por defecto | Number | `7` | Backend (`SendTransactionPlugin`) | Días de vigencia por defecto de una transacción al enviarse, si no se especifica uno explícito. En Dev es corto (7) para probar expiración rápido; el valor de negocio se fija por ambiente. |
| `sanic_sigil_env_MaxParticipants` | Sigil \| ENV \| Maxima cantidad de participantes | Number | `20` | Backend (`CreateTransactionPlugin`, `UpdateDraftPlugin`) | Cantidad máxima de participantes permitidos en una transacción. La definición trae `defaultvalue = 20`. |
| `sanic_sigil_env_MaxPdfSizeKB` | Sigil \| ENV \| Tamano maximo PDFs | Number | `20480` | Backend (`CreateTransactionPlugin`, `UpdateDraftPlugin`) | Tamaño máximo permitido del PDF en KB. `20480` = 20 MB. |
| `sanic_sigil_env_ReminderCadenceDays` | Sigil \| ENV \| Cadencia de recordatorio en dias | Number | `2` | Backend (`ProcessRemindersPlugin`) | Cada cuántos días se envía un recordatorio a los participantes pendientes. En Dev es corto (2) para probar rápido; el valor de negocio se fija por ambiente. |
| `sanic_sigil_env_SignatureImageSpec` | Sigil \| ENV \| Especificaciones de firma | JSON | `{ "targetWidthPx": 600, "targetHeightPx": 200, "maxKB": 150, "minAlphaRatio": 0.15, "minRmsContrast": 0.25, "minLaplacianVar": 80 }` | Backend (`ValidateMasterSignaturePlugin`) | Umbrales de validación/normalización de la imagen de firma (dimensiones objetivo, peso máximo y umbrales de calidad). Umbrales iniciales, calibrables por ambiente. |
| `sanic_sigil_env_TsaEnabled` | Sigil \| ENV \| TSA Habilitado | Boolean | `yes` (siembra del deploy) | Backend (`SealingWorkerPlugin`, `ResealPendingPlugin`) | Habilita el sellado de tiempo (TSA). Con TSA off, los ledgers pendientes se mueven a "Sin sello". *No tiene `environmentvariablevalues.json`; el valor de Dev lo siembra el deploy.* |
| `sanic_sigil_env_TsaEndpoints` | Sigil \| ENV \| Endpoints TSA | JSON | `{ "endpoints": [ { "url": "https://timestamp.sectigo.com", "timeoutSeconds": 10, "minIntervalSeconds": 15 }, { "url": "https://timestamp.digicert.com", "timeoutSeconds": 10, "minIntervalSeconds": 0 } ] }` | Backend (`SealingWorkerPlugin`, `ResealPendingPlugin` → `TsaConfig.Parse`) | Lista ordenada de autoridades de sellado de tiempo con su timeout e intervalo mínimo entre llamadas. El orden importa (fallback). **Por-ambiente.** |
| `sanic_sigil_env_emailoperador` | Sigil \| ENV \| Email del Operador | String | *sin valor en Dev* | Cloud Flow (`SigilCloudFlowJobs-Daily`) | Casilla del operador a la que el flow diario envía el email de notificación. **Por-ambiente.** *No tiene `environmentvariablevalues.json` (sin valor sembrado en Dev).* |

## Estructura de las variables JSON

### `sanic_sigil_env_SignatureImageSpec`

Objeto plano con las especificaciones de la imagen de firma:

```json
{
  "targetWidthPx": 600,
  "targetHeightPx": 200,
  "maxKB": 150,
  "minAlphaRatio": 0.15,
  "minRmsContrast": 0.25,
  "minLaplacianVar": 80
}
```

| Campo | Significado |
|-------|-------------|
| `targetWidthPx` | Ancho objetivo de la imagen en píxeles |
| `targetHeightPx` | Alto objetivo de la imagen en píxeles |
| `maxKB` | Peso máximo permitido de la imagen en KB |
| `minAlphaRatio` | Ratio mínimo de píxeles con contenido (canal alfa) |
| `minRmsContrast` | Contraste RMS mínimo aceptado |
| `minLaplacianVar` | Varianza laplaciana mínima (nitidez / detección de imagen borrosa) |

### `sanic_sigil_env_TsaEndpoints`

Objeto con un array `endpoints`, cada uno con URL, timeout e intervalo mínimo:

```json
{
  "endpoints": [
    { "url": "https://timestamp.sectigo.com", "timeoutSeconds": 10, "minIntervalSeconds": 15 },
    { "url": "https://timestamp.digicert.com", "timeoutSeconds": 10, "minIntervalSeconds": 0 }
  ]
}
```

| Campo | Significado |
|-------|-------------|
| `url` | Endpoint de la autoridad de sellado de tiempo (TSA) |
| `timeoutSeconds` | Timeout máximo por llamada, en segundos |
| `minIntervalSeconds` | Intervalo mínimo entre llamadas al mismo endpoint, en segundos (rate limiting) |

El **orden del array importa**: el backend recorre los endpoints en orden como cadena de
fallback. En Dev, Sectigo va primero porque DigiCert está bloqueada desde la red del sandbox.

## Variables por-ambiente

Estas variables **cambian su valor según el ambiente** (Dev / Test / Prod). Su valor de Dev
**NO debe heredarse** hacia otros ambientes — cada ambiente provee el suyo:

| Schema name | Por qué es por-ambiente |
|-------------|-------------------------|
| `sanic_sigil_env_AppPlayUrl` | La URL de la app cambia por ambiente. El deploy la toma de `SIGIL_APP_PLAY_URL` (la URL que imprime el `pac code push` de ese ambiente); si no está, cae a un placeholder. Re-correr el deploy nunca pisa la URL buena con el placeholder. |
| `sanic_sigil_env_TsaEndpoints` | El orden y disponibilidad de las TSA depende de la red de cada ambiente (en el sandbox DigiCert está bloqueada; en Prod el orden se fija por negocio). |
| `sanic_sigil_env_emailoperador` | La casilla del operador es distinta en cada ambiente (no tiene valor sembrado en Dev). |

> Los valores **cortos** de Dev en `ExpirationDefaultDays` (7) y `ReminderCadenceDays` (2)
> también son específicos de Dev — pensados para probar expiración y recordatorios rápido.
> El valor de negocio real se fija por ambiente.

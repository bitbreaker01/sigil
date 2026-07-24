# Cómo extender

**Alcance.** Recetas paso a paso para extender Sigil, cada una alineada con el patrón real del código:
agregar una **Custom API**, agregar una **pantalla** en el frontend, agregar/modificar un **cloud flow**,
agregar una **env var**, y **bumpear versiones**. No repite los mecanismos: los referencia. El framework de
plugins y el patrón de un handler están en [Backend](03-backend.md); el seam de datos del frontend, en
[Frontend](06-frontend.md); el deploy y el ALM, en [Deploy y ALM](07-deploy-y-alm.md); los tests, en
[Testing y CI](08-testing-y-ci.md). El panorama para un desarrollador nuevo está en la
[Guía del Desarrollador](../guias/04-guia-desarrollador.md).

**La regla de oro que gobierna toda extensión:** *el backend decide, el frontend orquesta.* Si te encontrás
poniendo una validación de negocio o una decisión de estado en el frontend, va en el backend (el frontend solo
oculta lo que el backend igualmente rechazaría). Y **toda lógica pura va al núcleo, no a la cáscara.**

---

## 1. Agregar una Custom API

El orden es **de adentro hacia afuera**: primero la lógica pura (con su test rojo), después el handler, luego
el catálogo declarativo, el deploy, y por último el cliente del frontend.

### 1.1 Núcleo primero (TDD)

Si la API tiene una decisión o validación, escribila como **clase pura** en `Sigil.Plugins.Core/Domain` (o
`Crypto`/`Imaging`/`Pdf` si corresponde) con su test rojo→verde en `Sigil.Plugins.Core.Tests`. Ejemplos del
patrón: `ReglasDeAutorizacion` (devuelve el motivo del rechazo o `null`), `ValidacionDeEntrada` (junta
**todos** los errores antes de rechazar). Ninguna dependencia de `Microsoft.Xrm.Sdk` entra al núcleo.

### 1.2 El handler (`Sigil.Plugins/Apis/`)

Creá `MiApiPlugin.cs` heredando de **`SigilApiPlugin`** e implementá `Ejecutar(EntornoDeApi e)`. El patrón
canónico (mirá `RejectTransactionPlugin.cs` como ejemplo compacto de una API **bound**):

1. Leer inputs con los helpers del entorno (`e.Input<T>`, `e.InputInt`, `e.InputOptionalInt`, `e.Target` para
   las bound).
2. **Si decide sobre estado compartido, lockeá primero** (`LockDeFila.Tomar(e.Servicio, target.Id)`) y
   **releé** después. Si crea una fila nueva (nadie compite), no lockea.
3. Validar con reglas **puras** del núcleo; rechazar con `InvalidPluginExecutionException` (mensaje accionable)
   o `e.Rechazar(errores)`.
4. Escribir (servicio elevado) y emitir outputs con `e.Output(nombre, valor)`.

```csharp
public class MiApiPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;                         // solo para APIs bound
        LockDeFila.Tomar(e.Servicio, target.Id);       // solo si decide sobre estado compartido
        var tx = Consultas.Transaccion(e.Servicio, target.Id);
        var motivo = ReglasDeAutorizacion.MotivoParaRechazar(/* … regla PURA … */);
        if (motivo is not null) throw new InvalidPluginExecutionException(motivo);
        // efectos …
        e.Output("MiResultado", valor);
    }
}
```

### 1.3 El catálogo (`tools/Sigil.Deploy/ApiSpec.cs`)

Agregá un `CustomApiSpec` a `Catalogo.Apis` — la **fuente única de verdad** del despliegue. Definí:
`UniqueName` (`sanic_sigil_capi_MiApi`), `DisplayName`, `Description`, `BindingType` (`Binding.Entity` para
bound a `sanic_sigil_tbl_transaction`, `Binding.Global` para unbound), `PluginTypeName`
(`Sigil.Plugins.Apis.MiApiPlugin`), los `RequestParams` (con su `ParamType` y opcionalidad), los
`ResponseProps`, y el `ExecutePrivilege` (dejalo `null` para `UserPrivilege`, o `Catalogo.ServicePrivilege` si
es un job de servicio).

> **Gotcha verificado — el `uniquename` del request parameter es la clave.** El nombre del parámetro debe ser
> el **nombre desnudo** (ej. `RoutingType`) porque **es** la clave de `InputParameters` que lee el plugin. Y
> `IsPrivate` **no** es un control de seguridad: la protección es el Execute Privilege (ver [Backend](03-backend.md)).

### 1.4 Tests de cáscara y conformidad

Con el stub (ver [Testing y CI](08-testing-y-ci.md)) agregá el test del handler en `Sigil.Plugins.Tests/Apis/`:
camino feliz + el camino `InvalidPluginExecutionException`. Agregá el `CF-*` en la conformidad que verifica que
la API quedó registrada (binding, params, response props, privilegio).

### 1.5 Bump, deploy y export

Subí `<Version>` del package (§5), compilá (`dotnet build src/backend/Sigil.Plugins -c Release`) y desplegá con
la herramienta (`dotnet run --project tools/Sigil.Deploy -c Release`), que hace el upsert idempotente de la
API nueva y **publica** las customizations (sin ese publish, el Web API rechaza los parámetros nuevos). Para
promover a Test/Prod, exportá la solución por el pipeline ALM.

### 1.6 El cliente del frontend

1. Regenerá el cliente tipado: `pac code add-data-source` produce el servicio en `src/generated/`
   (**autogenerado, no se edita**). Cada servicio expone un método estático cuyos parámetros posicionales
   espejan los request parameters y devuelve un `IOperationResult`.
2. Agregá el método al contrato **`SigilApi`** (`api/SigilApi.ts`).
3. Implementalo en **`api/powerApps.ts`** (desenvolviendo con `ok(...)`) **y** en **`api/mock.ts`** (misma
   forma de respuesta, datos en memoria). Las dos implementaciones deben moverse juntas o el mock diverge.

---

## 2. Agregar una pantalla

1. **Carpeta** en `src/screens/mipantalla/` siguiendo el reparto contenedor/modelo/hook (ver
   [Frontend](06-frontend.md)): `mipantallaModel.ts` (lógica pura testeable), `useMipantalla.ts` (datos vía el
   seam), `MipantallaScreen.tsx` (presentación Fluent UI).
2. **Routing** en `src/lib/navigation.ts`: agregá el nombre al union `Screen` **y** al set `SCREENS` (para que
   `parseRoute` lo acepte como deep link). Si necesita un parámetro de arranque, sumalo a `Route`.
3. **Render** en `src/App.tsx`: importala como `lazy(() => import(...))` (para no arrastrar su código al bundle
   inicial) y agregá su `case` en `renderScreen`. Si su contenido principal es un PDF, sumala a `WIDE_SCREENS`.
4. **Datos siempre por el seam.** Nunca llames el SDK directo desde una pantalla: usá `sigilApi`. Si la
   pantalla necesita un dato nuevo del backend, agregá el método a `SigilApi` (§1.6).
5. **Textos por i18n** (es + en en `src/i18n/`), jamás hardcodeados. Los tests asertan las claves.

> El seam `SigilApi` ya trae `mock.ts`, así que la pantalla nueva se desarrolla y testea **sin ambiente**
> (`npm run dev`, Vitest). Solo el build de producción usa el backend real.

---

## 3. Agregar o modificar un cloud flow

Los cloud flows (notificaciones y jobs) **se authoran en el maker portal de Power Automate**, no en el repo —
son metadata declarativa, no código C#. El ciclo:

1. Editá el flujo en el maker portal del ambiente **Dev**, dentro de la solución `sigil_core_sigil`.
2. **Versionalo** exportando la solución por el pipeline ALM (ver [Deploy y ALM](07-deploy-y-alm.md)): el
   Nivel 1 unpackea y commitea el flujo a `solutions/unpacked/Workflows/` (pares `*.json` +
   `*.json.data.xml`). El **diff de git** de esa carpeta es el changelog del flujo.

**Restricciones de diseño que no se rompen:** los flujos **solo notifican** (reaccionan a cambios de estado
para mandar correos/recordatorios) — no tocan binarios ni criptografía. Y filtran por el `status` de la
transacción; por eso el backend **jamás** reescribe un `status` con el valor idéntico (el trigger dispararía y
duplicaría notificaciones) — el lock usa una columna técnica dedicada, no `status` (ver [Backend](03-backend.md)).
Si un flujo compara por valor de choice, ese valor viene del Apéndice A (§4 de [Testing y CI](08-testing-y-ci.md)):
mantené el número en sincronía.

---

## 4. Agregar una env var

Una env var tiene **dos partes**: la **definición** (viaja en la solución) y el **valor** (por ambiente).

1. **Definición:** creala en el maker portal dentro de la solución (schema `sanic_sigil_env_MiVar`), y
   versionala vía el export → aparece en `solutions/unpacked/environmentvariabledefinitions/`.
2. **Valor por defecto (Dev):** agregá la entrada a `Catalogo.EnvValues` en `tools/Sigil.Deploy/ApiSpec.cs`;
   el deploy tool hace el upsert del valor. En Test/Prod el valor se configura por ambiente (o viaja en la
   solución si aplica).
3. **Lectura desde el backend:** el plugin la lee con `EnvVars` (`Data/EnvVars.cs`), según el tipo:
   `EnteroObligatorio(schema)` (Decimal leída como entero — `MaxPdfSizeKB`, `MaxParticipants`),
   `BoolObligatorio(schema)` (Two options — persiste `"yes"`/`"no"`), o `TextoObligatorio(schema)` (Text/JSON,
   p.ej. `SignatureImageSpec`, `TsaEndpoints`). Referenciá siempre el schema name por su constante en
   `SchemaNames`, no por un literal.

> **Gotcha verificado — falla ruidosa, sin defaults inventados.** `EnvVars` **no** inventa un default si la
> variable falta o está mal formada: lanza `InvalidPluginExecutionException`. Una validación de tamaño con un
> default fabricado sería una validación de mentira. La caché de `EnvVars` es **por ejecución** (la plataforma
> no cachea `RetrieveEnvironmentVariableValue`). Consecuencia: una env var nueva que el backend lee **debe**
> tener valor en cada ambiente antes de que el código que la usa corra ahí.

---

## 5. Bumpear versiones

Dos versiones independientes, cada una con su disparador:

| Qué | Dónde | Cuándo subirla |
|-----|-------|----------------|
| **Plugin package** | `<Version>` en `src/backend/Sigil.Plugins/Sigil.Plugins.csproj` (hoy `1.0.18`) | **Ante cualquier cambio de código del backend**, antes de empaquetar/desplegar |
| **Solución** | El input `version` de `solution-release.yml` (tag `sol-v{version}`) | Al publicar un Release de la solución para promover a otro ambiente |

> **Regla dura del package (verificada).** Dataverse **cachea el assembly del plugin package por versión**. Si
> cambiás código y **no** subís `<Version>`, la plataforma sigue corriendo el código viejo aunque actualices
> el content — un redeploy con la misma versión **no** recarga el fix. El deploy tool lee esta versión del
> `.nupkg`; Dataverse la recalcula del `.nuspec` embebido. Por eso el bump es lo primero de cualquier cambio
> de backend, no lo último.

---

## 6. La regla de oro, en concreto

Antes de escribir una línea, ubicá dónde va:

- **¿Es una decisión, validación o transición de estado?** → núcleo puro (`Sigil.Plugins.Core`), con test.
  Nunca en la cáscara, nunca en el frontend.
- **¿Es orquestación de lecturas/escrituras de Dataverse?** → la cáscara (`Sigil.Plugins`), en el handler.
- **¿Es presentación o "ocultar lo que el backend rechazaría"?** → el frontend, por el seam `SigilApi`.
- **¿Es un literal de schema (`sanic_sigil_*`)?** → una constante en `SchemaNames` (backend) o el mapa `COL`/`T`
  de `powerApps.ts` (frontend). Nunca un string suelto.
- **¿Es un valor de choice?** → un enum en `Choices.cs`, sincronizado con el Apéndice A.

Si respetás ese reparto, la pirámide de tests, la frontera núcleo/cáscara y el modelo de confianza se sostienen
solos.

---

## Referencias externas

- **Custom APIs de Dataverse (binding, request parameters, response properties, Execute Privilege)** —
  Microsoft Learn, *"Create and use Custom APIs"*.
- **Plugins de Dataverse (`IPlugin`, `IPluginExecutionContext`)** — Microsoft Learn, *"Write a plug-in"*.
- **Environment variables de Dataverse** — Microsoft Learn, *"Environment variables overview"*.
- **`pac code add-data-source` (clientes tipados de Custom APIs)** — Microsoft Learn, *"Microsoft Power Platform
  CLI — `pac code`"*.
- **Cloud flows en soluciones (Power Automate + ALM)** — Microsoft Learn, *"Flows in solutions"*.

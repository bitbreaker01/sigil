# Backend — plugins C# de Dataverse

**Alcance.** Cómo está construido el backend de Sigil: la frontera **núcleo puro / cáscara**, la clase
base `SigilApiPlugin` que resuelve el *plumbing* de cada Custom API, el patrón de un handler, el lock
de fila que serializa la concurrencia, el modelo de dominio (`SchemaNames`/`Choices`) y los *seams* que
mantienen a Dataverse fuera del núcleo. El panorama de alto nivel está en la
[Guía del Desarrollador](../guias/04-guia-desarrollador.md); el árbol del repositorio y los *targets* de
compilación, en [Estructura y build](02-estructura-y-build.md); el ciclo de vida de una firma y el modelo
de confianza, en [Arquitectura](01-arquitectura.md). El worker de sellado y la criptografía se tratan
aparte en [Sellado y criptografía](04-sellado-y-cripto.md), y el motor de imagen en
[Firma maestra](05-firma-maestra.md).

---

## 1. La frontera núcleo / cáscara

El backend son **dos proyectos** con una frontera dura, y esa separación es la decisión arquitectónica
central:

| Proyecto | TFM | Depende de Dataverse | Contiene |
|----------|-----|----------------------|----------|
| **`Sigil.Plugins.Core`** | `netstandard2.0` | **No** | Toda la lógica pura: hashing, cliente TSA, composición de PDF, transformación de coordenadas, motor de imagen, reglas de estado, autorización, validación de entrada, choices y schema names |
| **`Sigil.Plugins`** | `net462` | Sí (`Microsoft.Xrm.Sdk`) | Los plugins registrados: los 17 handlers de Custom API, `SigilApiPlugin` (base), `LockDeFila`, `SealingWorkerPlugin`, y el acceso a datos |

**Por qué net462 en la cáscara.** Los *plugin packages* de Dataverse exigen ese *target framework*: es
el runtime del sandbox donde la plataforma carga y ejecuta el assembly. No es negociable.

**Por qué netstandard2.0 en el núcleo.** `netstandard2.0` es consumible **tanto** desde el plugin `net462`
**como** desde los tests que corren en `net8.0`. Así el 90% del motor — lo difícil y lo crítico: cripto,
PDF, reglas de estado — se escribe como **clases puras** sin ninguna referencia a `Microsoft.Xrm.Sdk`, se
testea sin Dataverse ni mocks pesados, y corre en cualquier plataforma (Linux, CI, en segundos). La cáscara
queda tan delgada que su orquestación se cubre con un stub liviano de `IOrganizationService`.

> **Regla dura:** ninguna dependencia de `Microsoft.Xrm.Sdk` entra a `Sigil.Plugins.Core`. Si una lógica
> "necesita" Dataverse, o pertenece a la cáscara, o hay que abstraer esa dependencia detrás de una
> interfaz — como se hizo con `IFileTransfer` e `ISelladorTsa` (§6).

El núcleo trae sus propias dependencias de terceros, todas *pineadas* en
`Sigil.Plugins.Core.csproj`: **PDFsharp `6.2.4`**, **SixLabors.ImageSharp `2.1.13`**, **QRCoder `1.8.0`**,
**BouncyCastle.Cryptography `2.6.2`** y **System.Text.Json `9.0.6`**. El pin exacto de PDFsharp es
deliberado (ver §5 y [Sellado y criptografía](04-sellado-y-cripto.md)).

---

## 2. La clase base `SigilApiPlugin`

Todos los handlers de Custom API heredan de **`SigilApiPlugin`**
(`src/backend/Sigil.Plugins/Apis/SigilApiPlugin.cs`). La clase resuelve **una sola vez** todo el *plumbing*
del contexto de Dataverse y deja al handler un método abstracto `Ejecutar(EntornoDeApi)` para implementar.

### 2.1 El servicio elevado

```csharp
// SigilApiPlugin.Execute (SigilApiPlugin.cs:15)
var contexto = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
var trace    = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
var factory  = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

// null = contexto de SISTEMA (servicio elevado): TODA escritura la hace el sistema.
var servicio = factory.CreateOrganizationService(null);
```

El `null` en `CreateOrganizationService(null)` es la pieza de seguridad: el servicio corre con **contexto
de sistema**, elevado. Los usuarios **no** tienen privilegio directo de Create/Update sobre las tablas de
transacción, participante o evento; toda escritura pasa por una Custom API que corre como sistema. El
usuario que llama (`Contexto.InitiatingUserId`, expuesto como `EntornoDeApi.Llamante`) se usa **solo** para
autorización y para congelar los snapshots de identidad — **nunca** como el autor de las escrituras.

### 2.2 Los seams inyectables

Dos dependencias que tocan mundo externo se resuelven del `serviceProvider` con *fallback* a la
implementación de producción:

```csharp
// SigilApiPlugin.cs:26
var archivos     = serviceProvider.GetService(typeof(IFileTransfer)) as IFileTransfer ?? CrearFileTransfer(servicio);
var selladorTsa  = serviceProvider.GetService(typeof(ISelladorTsa)) as ISelladorTsa   ?? new SelladorTsaReal();
```

En Dataverse real `GetService` devuelve `null` para esos tipos, así que se instancian las reales
(`FileTransferDataverse`, `SelladorTsaReal`). En tests, el provider trae dobles y se usan esos. `IFileTransfer`
se crea con un *factory* virtual (`CrearFileTransfer`) para que una subclase de test pueda sobrescribirlo.

### 2.3 Clasificación de errores del patrón

```csharp
// SigilApiPlugin.cs:32
try
{
    trace.Trace("{0}: inicio (mensaje={1}, depth={2})", GetType().Name, contexto.MessageName, contexto.Depth);
    Ejecutar(entorno);
    trace.Trace("{0}: fin", GetType().Name);
}
catch (InvalidPluginExecutionException)
{
    throw; // ya viene con mensaje accionable para el usuario
}
catch (Exception ex)
{
    trace.Trace("{0}: error inesperado: {1}", GetType().Name, ex); // detalle técnico al trace, JAMÁS PII
    throw new InvalidPluginExecutionException($"{GetType().Name}: error inesperado — revisar el trace del plugin.", ex);
}
```

La regla es simple y consistente en todo el backend:

- Un **`InvalidPluginExecutionException`** con mensaje accionable se re-lanza **tal cual**: llega al
  usuario. Es el mecanismo que usan los handlers para rechazar (validación fallida, autorización denegada,
  estado inválido).
- **Cualquier otra excepción** se envuelve en un `InvalidPluginExecutionException` genérico y el detalle
  técnico va al **trace** — nunca al mensaje que ve el usuario, y **jamás con PII** (ni nombres, ni correos,
  ni base64, ni hashes completos).

> El worker de sellado (`SealingWorkerPlugin`, [Sellado y cripto](04-sellado-y-cripto.md)) **no** hereda de
> `SigilApiPlugin`: implementa `IPlugin` directo porque es un *step* asíncrono, no una Custom API, y su
> manejo de errores es distinto (clasifica transitorio vs definitivo por `OperationStatus`).

### 2.4 `EntornoDeApi` — todo resuelto

Cada handler recibe un `EntornoDeApi` con el contexto ya digerido (`SigilApiPlugin.cs:59`):

```csharp
public IPluginExecutionContext Contexto { get; }
public IOrganizationService     Servicio { get; }   // elevado
public ITracingService          Trace { get; }
public IFileTransfer            Archivos { get; }
public ISelladorTsa            SelladorTsa { get; }
public Guid Llamante => Contexto.InitiatingUserId;   // SOLO autorización + snapshots

public T?  Input<T>(string nombre) where T : class;  // parámetro de referencia opcional
public int? InputInt(string nombre);                 // Integer
public int? InputOptionalInt(string nombre);         // Integer OPCIONAL — ver gotcha
public EntityReference Target { get; }               // Target de una API bound (o excepción de contrato)
public void Output(string nombre, object valor);
public void Rechazar(IReadOnlyList<string> errores); // corta con TODOS los errores juntos
```

> **Gotcha verificado — `InputOptionalInt` (`SigilApiPlugin.cs:89`).** La plataforma materializa un
> parámetro Integer **opcional ausente** como `0`, indistinguible de un `0` explícito. Por eso
> `InputOptionalInt` trata `0` como "no provisto" (`return v is 0 ? null : v`). Sin esto, todo llamante que
> omitiera, por ejemplo, `ExpirationDays`, sería rechazado por la validación de dominio. `CreateTransaction`
> lo usa exactamente para eso (`CreateTransactionPlugin.cs:23`).

---

## 3. Anatomía de un handler

El patrón de un handler es siempre el mismo: **validar todo primero (con reglas puras del núcleo),
después escribir**. Los que deciden sobre estado compartido **lockean primero y releen después** (§4).

`CreateTransactionPlugin` es el ejemplo del camino "sin lock" (crea una fila nueva; nadie compite por ella):

```csharp
// CreateTransactionPlugin.Ejecutar (CreateTransactionPlugin.cs:18)
// 1. Leer los inputs del contrato.
var name = e.Input<string>("Name");
var expirationDays = e.InputOptionalInt("ExpirationDays"); // 0 = ausente

// 2. Validación PURA — se junta TODO lo reportable antes de rechazar (un solo viaje de errores).
var errores = new List<string>(ValidacionDeEntrada.ValidarEncabezado(name, expirationDays, message));
var pdf = ValidacionDeEntrada.ValidarPdfBase64(pdfBase64 ?? "", env.EnteroObligatorio(SchemaNames.EnvVars.MaxPdfSizeKB));
errores.AddRange(pdf.Errores);
// ... participantes, zonas ...
if (errores.Count > 0 || participantes?.Valor is null) e.Rechazar(errores);

// 3. Validación que SOLO Dataverse puede responder (usuarios habilitados) — ya en la cáscara.
var usuarios = Consultas.UsuariosHabilitados(e.Servicio, ...);

// 4. Escrituras: todo validado, ownerid explícito = creador.
var tx = new Entity(SchemaNames.Tx.Entidad);
tx[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Borrador);
var txId = e.Servicio.Create(tx);
e.Archivos.Subir(txRef, SchemaNames.Tx.ContentFile, "content.pdf", pdf.Valor!.Bytes, "application/pdf");
// ... participantes, zonas, evento ...
e.Output("TransactionId", txId);
```

`SubmitSignaturePlugin` es el handler más rico (registra una firma; es el crítico en concurrencia) y el que
mejor muestra el patrón "con lock", en este orden estricto (`SubmitSignaturePlugin.cs:24`):

1. **Lock de fila PRIMERO** (`LockDeFila.Tomar`, §4).
2. **Re-leer TODO** post-lock: transacción, participantes, mi participante. Sobre estos datos serializados
   se decide.
3. **Idempotencia ANTES del guard de estado**: un re-submit sobre un participante ya `Firmado` retorna éxito
   sin efectos (protege el doble clic del último firmante, cuya transacción ya está en `Sellando` y el guard
   de estado la rechazaría). La idempotencia tiene precedencia.
4. **Autorización** — regla **pura** del núcleo:
   `ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(...)` devuelve el motivo o `null`.
5. **Efectos**: firma maestra vigente (obligatoria), snapshot del PNG congelado, `documenthash` SHA-256 del
   contenido, snapshots de identidad tomados **siempre del contexto del servidor** (nunca del cliente).
6. **Decisión "último"** — otra regla pura: `ReglasDeFirma.Decidir(routing, llamante, participantes)`.
7. **Transición**: el `status` se escribe **solo si cambia** (los triggers de los flows disparan aunque el
   valor sea idéntico — reescribir duplicaría notificaciones; ver §4).
8. **Evento** (con la lista de lectores para el reparto de acceso) + `Output("IsLastSigner", ...)`.

La lección de diseño: **la lógica de decisión vive en el núcleo puro** (`ReglasDeAutorizacion`,
`ReglasDeFirma`/`ReglasDeCicloDeVida`, `ValidacionDeEntrada`); la cáscara solo orquesta lecturas y
escrituras. Ese reparto es idéntico en los 17 handlers.

---

## 4. `LockDeFila` — serializar la concurrencia

**El problema.** Dos firmantes en paralelo llaman `SubmitSignature` a la vez. Cada ejecución lee los
participantes para decidir si es el último. Sin serialización, ambos pueden verse mutuamente pendientes
(nadie transiciona a *Sellando* → transacción zombi) o ambos creerse últimos (doble worker).

**La solución** (`src/backend/Sigil.Plugins/Apis/LockDeFila.cs`) es un `Update` de no-op sobre una columna
técnica dedicada:

```csharp
public static void Tomar(IOrganizationService servicio, Guid transactionId)
{
    var fila = new Entity(SchemaNames.Tx.Entidad, transactionId);
    fila[SchemaNames.Tx.LockToken] = Environment.TickCount & int.MaxValue; // el VALOR no importa
    servicio.Update(fila); // toma el lock de fila de SQL hasta el commit
}
```

Ese `Update` toma el **lock de fila de SQL** hasta el commit de la transacción de base de datos,
serializando las ejecuciones concurrentes. El valor escrito (`sanic_sigil_locktoken`) es irrelevante; lo que
importa es que sea un `Update` a esa fila.

Reglas derivadas del lock:

- **Prohibido lockear escribiendo `status`.** Los triggers de los flujos de notificación filtran por
  `status` y disparan **aunque el valor escrito sea idéntico** al existente. Lockear por status generaría
  notificaciones duplicadas. Por eso la columna técnica dedicada `sanic_sigil_locktoken`.
- **Siempre re-leer después del lock.** Recién con las ejecuciones serializadas se lee el estado real y se
  decide sobre datos consistentes.
- **Idempotencia por participante.** `SubmitSignature` sobre un participante ya `Firmado` retorna éxito sin
  efectos.

Usan el mismo lock, como primera operación, todos los que deciden sobre estado compartido:
`SubmitSignature`, `SendTransaction`, `RejectTransaction`, `CancelTransaction`, `UpdateDraft`,
`DeleteDraft`, `RetrySealing` y el worker de sellado. `CreateTransaction` **no** lockea: la fila es nueva y
nadie compite por ella.

---

## 5. Las 17 Custom APIs

El catálogo de las 17 APIs se autoriza de forma **declarativa** en `tools/Sigil.Deploy/ApiSpec.cs`
(`Catalogo.Apis`) — la fuente única de verdad del despliegue: nombre único, display name, binding, tipo de
plugin, parámetros de entrada, propiedades de respuesta y privilegio de ejecución. Ese catálogo es el espejo
exacto de lo que registra la herramienta de despliegue y de lo que verifica la suite de conformidad.

**Binding — 8 bound + 9 unbound.** El binding se codifica en `ApiSpec.cs` con `Binding.Entity` (=1, **bound**
a `sanic_sigil_tbl_transaction`, reciben un `Target`) o `Binding.Global` (=0, **unbound**, globales):

| # | Custom API (`sanic_sigil_capi_*`) | Binding | Plugin | In → Out |
|---|-----------------------------------|---------|--------|----------|
| 1 | `CreateTransaction` | 🌐 Global | `CreateTransactionPlugin` | Name, Message?, RoutingType, ExpirationDays?, PdfBase64, ParticipantsJson, ZonesJson? → TransactionId |
| 2 | `UpdateDraft` | 🔗 Entity | `UpdateDraftPlugin` | (todos opcionales; null = sin cambio) → — |
| 3 | `DeleteDraft` | 🔗 Entity | `DeleteDraftPlugin` | — → — |
| 4 | `GetDocumentContent` | 🔗 Entity | `GetDocumentContentPlugin` | DocumentType → PdfBase64 |
| 5 | `SendTransaction` | 🔗 Entity | `SendTransactionPlugin` | — → — |
| 6 | `SubmitSignature` | 🔗 Entity | `SubmitSignaturePlugin` | — → IsLastSigner |
| 7 | `RejectTransaction` | 🔗 Entity | `RejectTransactionPlugin` | Reason → — |
| 8 | `CancelTransaction` | 🔗 Entity | `CancelTransactionPlugin` | Reason? → — |
| 9 | `RetrySealing` | 🔗 Entity | `RetrySealingPlugin` | — → — |
| 10 | `ValidateMasterSignature` | 🌐 Global | `ValidateMasterSignaturePlugin` | ImageBase64, Persist? → IsValid, FailureReasons, MetricsJson, NormalizedImageBase64 |
| 11 | `GetMasterSignature` | 🌐 Global | `GetMasterSignaturePlugin` | — → ImageBase64, ValidatedOn |
| 12 | `GetMasterSignatureHistory` | 🌐 Global | `GetMasterSignatureHistoryPlugin` | — → HistoryJson |
| 13 | `SearchDocuments` | 🌐 Global | `SearchDocumentsPlugin` | Text?, CreatorId?, Status?, ParticipantIds?, SignatureVersion?, Sort?, PageSize?, PagingCookie? → ResultsJson, Total, NextPagingCookie |
| 14 | `VerifyDocument` | 🌐 Global | `VerifyDocumentPlugin` | TransactionId?, Sha256Hash? → Found, IsIntact, MetadataJson, TsaTokenBase64 |
| 15 | `ExpireTransactions` | 🌐 Global | `ExpireTransactionsPlugin` | — → ExpiredCount, SanitizedCount |
| 16 | `ProcessReminders` | 🌐 Global | `ProcessRemindersPlugin` | — → RemindersJson |
| 17 | `ResealPending` | 🌐 Global | `ResealPendingPlugin` | — → ResealedCount, MovedToNoTsaCount, StillPendingCount, AnchorMismatchCount |

Bound (8): #2, #3, #4, #5, #6, #7, #8, #9. Unbound (9): #1, #10, #11, #12, #13, #14, #15, #16, #17.

**Autorización — el Execute Privilege, no `IsPrivate`.** Cada Custom API lleva su propio *Execute Privilege*
(la autorización a nivel de plataforma). En `ApiSpec.cs` hay dos niveles:

- **Privilegio de usuario** (`Catalogo.UserPrivilege = "prvReadsanic_sigil_tbl_transaction"`): el default,
  que tiene el rol de usuario. La mayoría de las APIs.
- **Privilegio de servicio** (`Catalogo.ServicePrivilege = "prvWritesanic_sigil_tbl_ledgerentry"`): solo lo
  posee el rol de servicio. Lo llevan los **tres jobs** — `ExpireTransactions`, `ProcessReminders`,
  `ResealPending` — de modo que un usuario común no puede invocarlos aunque conozca su firma.

> **`IsPrivate` NO es un control de seguridad** (verificado). La protección real es el *Execute Privilege*;
> `IsPrivate` solo oculta la API del *action explorer*, no impide su ejecución.

---

## 6. Los seams — inversión de dependencias

El núcleo puro no puede tocar dos cosas que sí necesita el sellado: las **columnas File** de Dataverse
(subir/bajar binarios) y la **red** (hablar con la TSA). Ambas se abstraen detrás de una interfaz en
`src/backend/Sigil.Plugins/Data/`, y la implementación real vive en la cáscara.

**`IFileTransfer`** (`Data/IFileTransfer.cs`):

```csharp
public interface IFileTransfer
{
    void   Subir(EntityReference registro, string columna, string nombreDeArchivo, byte[] bytes, string mimeType);
    byte[] Descargar(EntityReference registro, string columna);
}
```

La implementación real es `FileTransferDataverse`. El seam existe porque **ningún fake de
`IOrganizationService` simula los mensajes de *file blocks*** (subir/bajar por bloques): al abstraerlos, los
tests inyectan un doble en memoria y la cáscara nunca depende de esa mecánica en las pruebas.

**`ISelladorTsa`** (`Data/ISelladorTsa.cs`):

```csharp
public interface ISelladorTsa
{
    ResultadoTsa Sellar(byte[] sha256Digest, TsaConfig config);
}

public sealed class SelladorTsaReal : ISelladorTsa
{
    public ResultadoTsa Sellar(byte[] sha256Digest, TsaConfig config)
        => new ClienteTsa().SelloPara(sha256Digest, config); // el cliente RFC 3161 vive en el núcleo
}
```

`SelladorTsaReal` es un adaptador delgado que delega en el `ClienteTsa` del núcleo (el cliente RFC 3161 real,
detallado en [Sellado y cripto](04-sellado-y-cripto.md)). En tests se inyecta un doble que devuelve respuestas
buenas/malas sin red. Ambos seams se resuelven vía `IServiceProvider` en `SigilApiPlugin` (§2.2).

---

## 7. El modelo de dominio (`Sigil.Plugins.Core/Domain`)

El dominio puro concentra las reglas y los literales, para que un typo sea un error de compilación y no un
bug silencioso.

| Área | Archivo | Qué contiene |
|------|---------|--------------|
| **Nombres de esquema** | `SchemaNames.cs` | Constantes con los nombres `sanic_sigil_*` de tablas, columnas, choices, Custom APIs y env vars — la única fuente de esos literales |
| **Choices** | `Choices.cs` | Los enums de estado con sus valores numéricos reales |
| **Reglas de ciclo de vida** | `ReglasDeCicloDeVida.cs` | La máquina de estados pura (quién sigue, si la firma es la última) |
| **Autorización** | `ReglasDeAutorizacion.cs` | "¿este actor puede hacer esta acción en este estado?" → motivo del rechazo o `null` |
| **Reglas de jobs** | `ReglasDeJobs.cs` | Elegibilidad de expiración / recordatorio / re-sellado |
| **Validación de entrada** | `ValidacionDeEntrada.cs` | Validación de longitud antes de decodificar, magic bytes, zonas, órdenes — devuelve **todos** los errores juntos |
| **Contratos JSON** | `ContratosJson.cs` | Los DTOs de los payloads JSON (participantes, zonas, resultados) |

### 7.1 Choices — el prefijo `15946`

Los valores numéricos de cada choice se **copian del portal** (los flujos de notificación comparan por
número), y el resto del código referencia siempre los nombres lógicos. Todos usan el prefijo `15946`
(`Choices.cs`):

```csharp
public enum TransactionStatus
{
    Borrador = 159460000, PendienteDeFirma = 159460001, FirmadoParcialmente = 159460002,
    Sellando = 159460003, Completado = 159460004, Rechazado = 159460005,
    Expirado = 159460006, ErrorDeSellado = 159460007, Cancelado = 159460008,
}
```

Los cinco choices y su cardinalidad: `TransactionStatus` (9 valores, `159460000`–`159460008`),
`ParticipantStatus` (4: Pendiente/TurnoActivo/Firmado/Rechazado), `RoutingType` (2: Secuencial/Paralelo),
`TsaStatus` (3: SelladoConTsa/SinSelloTsa/ReSelladoPendiente) y `EventType` (13:
`159460000`–`159460012`, siendo `TsaAbandonada` = `159460012` el último agregado).

### 7.2 Las 6 tablas

`SchemaNames.cs` centraliza las columnas de las seis tablas del modelo:

| Tabla (`sanic_sigil_tbl_*`) | Rol | Columnas notables |
|-----------------------------|-----|-------------------|
| `transaction` | La solicitud de firma | `locktoken` (técnica, §4); `contentfile`/`finalfile` (File); `contenthash`; `status`; `routingtype` |
| `participant` | Un firmante de una transacción | `signaturesnapshot` (File, congelado al firmar); `mastersignatureid`; `signername`/`signeremail`/`signerentraobjectid` (snapshots de identidad) |
| `signaturezone` | Dónde firma cada participante | `page`, `posx`/`posy`/`width`/`height` (coordenadas en %) |
| `mastersignature` | La Firma Maestra de un usuario | `version`, `isactive`, `signaturefile` (File), `validatedon`, `validationdetails` |
| `ledgerentry` | **El libro de registro (evidencia)** | `contenthash`, `finalhash`, `tsatoken`, `tsastatus`, `sealedon`, `signersummary`; `name` es **autonumber** (el plugin jamás lo escribe) |
| `event` | Historial de negocio | `type`, `actorname`/`actoremail`, `participantid`, `documenthash`, `occurredon`, `details` |

Las columnas de identidad de `systemuser` que Sigil consulta para los snapshots están en
`SchemaNames.Usuario`: `fullname`, `internalemailaddress` (email), `domainname` (UPN),
`azureactivedirectoryobjectid` (objectId de Entra), `isdisabled`.

---

## Referencias externas

- **Custom APIs de Dataverse (binding, request parameters, response properties, Execute Privilege)** —
  Microsoft Learn, *"Create and use Custom APIs"*.
- **Plugins de Dataverse (`IPlugin`, `IPluginExecutionContext`, `IOrganizationServiceFactory`,
  `CreateOrganizationService`)** — Microsoft Learn, *"Write a plug-in"* / *"Impersonate a user"*.
- **`InvalidPluginExecutionException` y manejo de errores en plugins** — Microsoft Learn, *"Handle
  exceptions in plug-ins"*.
- **Columnas File de Dataverse (subida/descarga por bloques)** — Microsoft Learn, *"Use file column data"*.

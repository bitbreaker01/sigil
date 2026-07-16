// Script de carrera de locks (doc 11 §3 / doc 04 §5) — LA validación que ningún stub da:
// la serialización real del lock de fila de SQL, ejercitada con requests CONCURRENTES
// contra Dev. Los tests unitarios prueban la lógica post-lock con interleavings simulados;
// esto prueba que el lock EXISTE y funciona bajo concurrencia real.
//
// Dos escenarios:
//   A. N firmantes de una transacción PARALELA firman SIMULTÁNEAMENTE (barrera) → debe
//      haber EXACTAMENTE un IsLastSigner=true, la tx queda en Sellando UNA vez, cero zombis,
//      y el worker crea UN solo ledger.
//   B. DOBLE CLICK: el mismo firmante dispara SubmitSignature dos veces en paralelo → ambas
//      tienen éxito (idempotencia), el participante queda Firmado una vez, sin efectos dobles.
//
// Identidades: cada request concurrente impersona a su firmante (ServiceClient.CallerId) —
// así InitiatingUserId del plugin es el firmante real, no el Service Principal. Requiere
// usuarios semilla (SIGIL_SEED_UPNS) con rol Sigil | SR | User.
//
// Exit 0 = la carrera pasó; ≠0 = falló (con reporte). Limpia SIEMPRE.

using System.Text.Json;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.LockRace;

// ADVERTENCIA: este script NO es seguro para ejecución concurrente CONSIGO MISMO — los
// usuarios semilla y sus firmas maestras son estado global compartido por usuario, y la
// limpieza del finally borra las firmas maestras del usuario. Correr UNA instancia a la vez
// (en CI, serializado; jamás dos runs en paralelo con los mismos SIGIL_SEED_UPNS). Antagonista C5.
const string TxTable = "sanic_sigil_tbl_transaction";
const int FirmadoParcialmente = 159460002, Sellando = 159460003, Completado = 159460004, ErrorDeSellado = 159460007;
const int Firmado = 159460002; // participantstatus (mismo offset que FirmadoParcialmente de tx)

var url = Env("SIGIL_DATAVERSE_URL");
var clientId = Env("SIGIL_CLIENT_ID");
var clientSecret = Env("SIGIL_CLIENT_SECRET");
var upns = Env("SIGIL_SEED_UPNS").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
if (upns.Length < 2)
{
    Console.Error.WriteLine("[FATAL] SIGIL_SEED_UPNS necesita ≥2 usuarios para una carrera con sentido.");
    return 1;
}

string ConnString() =>
    $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true";

Console.WriteLine($"[1] Conectando a {url} ...");
using var admin = new ServiceClient(ConnString());
if (!admin.IsReady)
{
    Console.Error.WriteLine($"[FATAL] no conectó: {admin.LastError}");
    return 1;
}

// Resolver los firmantes semilla → systemuserid.
var firmantes = new List<(string Upn, Guid Id)>();
foreach (var upn in upns)
{
    var q = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("systemuserid") };
    q.Criteria.AddCondition("domainname", ConditionOperator.Equal, upn);
    q.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
    var u = admin.RetrieveMultiple(q).Entities.FirstOrDefault();
    if (u is null)
    {
        Console.Error.WriteLine($"[FATAL] usuario semilla '{upn}' no existe o está deshabilitado.");
        return 1;
    }
    firmantes.Add((upn, u.Id));
}
Console.WriteLine($"[1] {firmantes.Count} firmantes semilla resueltos.");

var fallas = new List<string>();
var aLimpiar = new List<Guid>();
var firmasMaestras = new List<Guid>();

try
{
    // Cada firmante necesita Firma Maestra vigente — se crea impersonando (flujo real).
    Console.WriteLine("[2] Asegurando Firma Maestra vigente por firmante ...");
    var png = Convert.ToBase64String(Fixtures.PngDeFirma());
    foreach (var (upn, id) in firmantes)
    {
        using var comoFirmante = ImpersonarNuevo(id);
        var r = comoFirmante.Execute(new OrganizationRequest("sanic_sigil_capi_ValidateMasterSignature")
        { ["ImageBase64"] = png }).Results;
        if (!(bool)r["IsValid"])
            throw new InvalidOperationException(
                $"la firma sintética no validó para {upn}: {(r.Contains("FailureReasons") ? r["FailureReasons"] : "(sin motivos)")}");
    }

    // ── Escenario A: N firmantes en paralelo, carrera simultánea ──
    Console.WriteLine($"[3] ESCENARIO A — {firmantes.Count} firmas simultáneas sobre una transacción paralela ...");
    var txA = CrearYEnviar(admin, firmantes, aLimpiar);
    var resultadosA = FirmarEnBarrera(firmantes.Select(f =>
        (f.Id, new EntityReference(TxTable, txA))).ToList());

    // ── PRUEBA REAL DEL LOCK (antagonista C2): el conteo de IsLastSigner. Los dos modos de
    //    falla del doc 04 §5 se cazan ACÁ: (a) zombi → nadie es último → ultimos==0;
    //    (b) doble worker → varios se creen últimos → ultimos>1. El assert de ledger de más
    //    abajo NO prueba el lock (el alternate key de SQL lo haría pasar en verde aunque el
    //    lock falle en modo (b)) — valida el alternate key, que es una prueba distinta.
    var ultimos = resultadosA.Count(r => r.IsLastSigner == true);
    var errores = resultadosA.Where(r => r.Error is not null).ToList();
    if (errores.Count > 0)
        fallas.Add($"A: {errores.Count} firma(s) lanzaron excepción: {string.Join(" | ", errores.Select(e => e.Error))}");
    if (ultimos != 1)
        fallas.Add($"A [PRUEBA DEL LOCK]: IsLastSigner=true apareció {ultimos} veces — DEBE ser exactamente 1 (doc 04 §5: exactamente uno ve cero pendientes). {ultimos} = lock roto ({(ultimos == 0 ? "modo zombi" : "doble worker")}).");

    var estadoA = EsperarSellandoOcompletado(admin, txA);
    if (estadoA is not (Sellando or Completado))
        fallas.Add($"A: la transacción quedó en estado {estadoA}, no Sellando/Completado (¿zombi?).");

    // el worker debe converger a Completado con UN solo ledger (valida el alternate key, doc 03 §4.4)
    var estadoFinalA = EsperarEstado(admin, txA, Completado, 180);
    if (estadoFinalA != Completado)
        fallas.Add($"A: no llegó a Completado en 180 s (estado {estadoFinalA}).");
    var ledgersA = ContarLedgers(admin, txA);
    if (ledgersA != 1)
        fallas.Add($"A [alternate key]: hay {ledgersA} ledgers — el alternate key debía garantizar 1 (doc 03 §4.4).");
    var firmadosA = ContarParticipantes(admin, txA, Firmado);
    if (firmadosA != firmantes.Count)
        fallas.Add($"A: {firmadosA}/{firmantes.Count} participantes quedaron Firmado.");
    Console.WriteLine($"[3] A: IsLastSigner×{ultimos}, estado tras carrera={estadoA}, final={estadoFinalA}, ledgers={ledgersA}, firmados={firmadosA}.");

    // ── Escenario B: doble click del MISMO firmante ──
    Console.WriteLine("[4] ESCENARIO B — doble click del mismo firmante (idempotencia bajo lock real) ...");
    var txB = CrearYEnviar(admin, firmantes.Take(2).ToList(), aLimpiar);
    var firmante0 = firmantes[0].Id;
    var refB = new EntityReference(TxTable, txB);
    var dobleClick = FirmarEnBarrera(new List<(Guid, EntityReference)>
        { (firmante0, refB), (firmante0, refB) }); // el MISMO firmante, dos veces

    var erroresB = dobleClick.Where(r => r.Error is not null).ToList();
    if (erroresB.Count > 0)
        fallas.Add($"B: el doble click lanzó {erroresB.Count} excepción(es): {string.Join(" | ", erroresB.Select(e => e.Error))} — debía ser idempotente.");
    // con 2 participantes, firmar a UNO no es el último → ambas respuestas IsLastSigner=false
    if (dobleClick.Any(r => r.IsLastSigner == true))
        fallas.Add("B: una respuesta trajo IsLastSigner=true — con 2 participantes, firmar 1 NO es el último (lock roto).");
    var firmasDe0 = ContarParticipanteFirmado(admin, txB, firmante0);
    if (firmasDe0 != 1)
        fallas.Add($"B: el participante quedó Firmado {firmasDe0} veces (esperado 1 — idempotencia).");
    var eventosFirma0 = ContarEventosDeFirma(admin, txB, firmante0);
    if (eventosFirma0 != 1)
        fallas.Add($"B: hay {eventosFirma0} eventos de firma para el participante — el doble click duplicó efectos (esperado 1).");
    // EL síntoma directo de un lock roto en doble click: la tx NO debe transicionar (antagonista A4)
    var estadoB = EstadoDe(admin, txB);
    if (estadoB != FirmadoParcialmente)
        fallas.Add($"B [PRUEBA DEL LOCK]: la tx quedó en {estadoB}, esperado Firmado Parcialmente ({FirmadoParcialmente}) — el doble click transicionó (lock roto).");
    Console.WriteLine($"[4] B: excepciones={erroresB.Count}, veces Firmado={firmasDe0}, eventos de firma={eventosFirma0}, estado={estadoB}.");
}
catch (Exception ex)
{
    fallas.Add($"EXCEPCIÓN no controlada: {ex.Message}");
}
finally
{
    Console.WriteLine("[5] Limpieza ...");
    foreach (var txId in aLimpiar) LimpiarTx(admin, txId);
    foreach (var (_, id) in firmantes) LimpiarFirmasDe(admin, id);
}

Console.WriteLine();
if (fallas.Count == 0)
{
    Console.WriteLine("✅ CARRERA DE LOCKS: PASA — la serialización del lock de fila funciona bajo concurrencia real.");
    return 0;
}
Console.Error.WriteLine("❌ CARRERA DE LOCKS: FALLA");
foreach (var f in fallas) Console.Error.WriteLine($"   - {f}");
return 2;

// ───────────────────────────────────────────────────────────────────────────

ServiceClient ImpersonarNuevo(Guid userId)
{
    var c = new ServiceClient(ConnString());
    if (!c.IsReady) throw new InvalidOperationException($"clon no conectó: {c.LastError}");
    c.CallerId = userId; // impersonación: InitiatingUserId del plugin = este usuario
    return c;
}

// Crea una transacción PARALELA con los firmantes dados (una zona c/u) y la envía.
Guid CrearYEnviar(ServiceClient client, List<(string Upn, Guid Id)> parts, List<Guid> cleanup)
{
    var participantsJson = JsonSerializer.Serialize(parts.Select(p => new { userId = p.Id }));
    var zonesJson = JsonSerializer.Serialize(parts.Select(p => new
    { userId = p.Id, page = 1, x = 40.0, y = 40.0, w = 20.0, h = 8.0 }));

    var txId = (Guid)client.Execute(new OrganizationRequest("sanic_sigil_capi_CreateTransaction")
    {
        ["Name"] = "LockRace " + parts.Count + " firmantes",
        ["RoutingType"] = "parallel",
        ["PdfBase64"] = Convert.ToBase64String(Fixtures.PdfDeUnaPagina()),
        ["ParticipantsJson"] = participantsJson,
        ["ZonesJson"] = zonesJson,
    }).Results["TransactionId"];
    cleanup.Add(txId);
    client.Execute(new OrganizationRequest("sanic_sigil_capi_SendTransaction")
    { ["Target"] = new EntityReference(TxTable, txId) });
    return txId;
}

// Dispara SubmitSignature en paralelo con una BARRERA para maximizar el solapamiento real
// (todos los hilos esperan y arrancan juntos — la ventana donde el lock importa).
static List<Resultado> FirmarEnBarrera(List<(Guid Firmante, EntityReference Tx)> firmas)
{
    var url = Env("SIGIL_DATAVERSE_URL");
    var clientId = Env("SIGIL_CLIENT_ID");
    var secret = Env("SIGIL_CLIENT_SECRET");
    string cs() => $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{secret}';RequireNewInstance=true";

    using var barrera = new Barrier(firmas.Count);
    var tareas = firmas.Select(f => Task.Run(() =>
    {
        // El ServiceClient se crea ANTES de la barrera A PROPÓSITO (antagonista S1): la
        // conexión/auth (round-trips de token, jitter de cientos de ms) queda FUERA de la
        // ventana de carrera — la barrera sincroniza el Execute, no el login.
        var pasoBarrera = false;
        try
        {
            var c = new ServiceClient(cs()) { CallerId = f.Firmante };
            barrera.SignalAndWait(); // todos arrancan el Execute a la vez
            pasoBarrera = true;
            var r = c.Execute(new OrganizationRequest("sanic_sigil_capi_SubmitSignature") { ["Target"] = f.Tx });
            c.Dispose();
            return new Resultado(f.Firmante, r.Results.TryGetValue("IsLastSigner", out var v) && v is bool b ? b : null, null);
        }
        catch (Exception ex)
        {
            // RemoveParticipant SOLO si falló ANTES de la barrera (antagonista A3): descontar
            // el cupo tras la barrera ya disparada corrompería el conteo de fase.
            if (!pasoBarrera)
                barrera.RemoveParticipant();
            return new Resultado(f.Firmante, null, ex.Message);
        }
    })).ToArray();
    Task.WaitAll(tareas);
    return tareas.Select(t => t.Result).ToList();
}

int EsperarSellandoOcompletado(ServiceClient client, Guid txId)
{
    for (var i = 0; i < 12; i++)
    {
        var s = EstadoDe(client, txId);
        if (s is Sellando or Completado or ErrorDeSellado) return s;
        Thread.Sleep(2000);
    }
    return EstadoDe(client, txId);
}

int EsperarEstado(ServiceClient client, Guid txId, int objetivo, int segundos)
{
    var limite = DateTime.UtcNow.AddSeconds(segundos);
    while (DateTime.UtcNow < limite)
    {
        var s = EstadoDe(client, txId);
        if (s == objetivo || s == ErrorDeSellado) return s;
        Thread.Sleep(4000);
    }
    return EstadoDe(client, txId);
}

int EstadoDe(ServiceClient client, Guid txId)
    => client.Retrieve(TxTable, txId, new ColumnSet("sanic_sigil_status"))
        .GetAttributeValue<OptionSetValue>("sanic_sigil_status").Value;

int ContarLedgers(ServiceClient client, Guid txId)
{
    var q = new QueryExpression("sanic_sigil_tbl_ledgerentry") { ColumnSet = new ColumnSet(false) };
    q.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
    return client.RetrieveMultiple(q).Entities.Count;
}

int ContarParticipantes(ServiceClient client, Guid txId, int estado)
{
    var q = new QueryExpression("sanic_sigil_tbl_participant") { ColumnSet = new ColumnSet("sanic_sigil_status") };
    q.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
    return client.RetrieveMultiple(q).Entities.Count(p =>
        p.GetAttributeValue<OptionSetValue>("sanic_sigil_status").Value == estado);
}

int ContarParticipanteFirmado(ServiceClient client, Guid txId, Guid userId)
{
    var q = new QueryExpression("sanic_sigil_tbl_participant") { ColumnSet = new ColumnSet("sanic_sigil_status") };
    q.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
    q.Criteria.AddCondition("sanic_sigil_userid", ConditionOperator.Equal, userId);
    return client.RetrieveMultiple(q).Entities.Count(p =>
        p.GetAttributeValue<OptionSetValue>("sanic_sigil_status").Value == Firmado);
}

int ContarEventosDeFirma(ServiceClient client, Guid txId, Guid userId)
{
    // eventos tipo 3 (Firma registrada) anclados al participante de ese usuario
    var pq = new QueryExpression("sanic_sigil_tbl_participant") { ColumnSet = new ColumnSet(false) };
    pq.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
    pq.Criteria.AddCondition("sanic_sigil_userid", ConditionOperator.Equal, userId);
    var pid = client.RetrieveMultiple(pq).Entities.FirstOrDefault()?.Id;
    if (pid is null) return 0;

    var eq = new QueryExpression("sanic_sigil_tbl_event") { ColumnSet = new ColumnSet("sanic_sigil_type") };
    eq.Criteria.AddCondition("sanic_sigil_participantid", ConditionOperator.Equal, pid.Value);
    return client.RetrieveMultiple(eq).Entities.Count(ev =>
        ev.GetAttributeValue<OptionSetValue>("sanic_sigil_type").Value == 159460002);
}

void LimpiarTx(ServiceClient client, Guid txId)
{
    try
    {
        // event y ledger son Delete Restrict hacia la tx (doc 03 §2): borrar primero.
        // participant y zona son Cascade All → caen con la tx (doc 03 §2 líneas 44-45),
        // no hace falta borrarlos a mano.
        foreach (var tabla in new[] { "sanic_sigil_tbl_event", "sanic_sigil_tbl_ledgerentry" })
        {
            var q = new QueryExpression(tabla) { ColumnSet = new ColumnSet(false) };
            q.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
            foreach (var e in client.RetrieveMultiple(q).Entities) client.Delete(tabla, e.Id);
        }
        client.Delete(TxTable, txId);
    }
    catch (Exception ex) { Console.Error.WriteLine($"[cleanup] tx {txId}: {ex.Message}"); }
}

void LimpiarFirmasDe(ServiceClient client, Guid userId)
{
    try
    {
        var q = new QueryExpression("sanic_sigil_tbl_mastersignature") { ColumnSet = new ColumnSet(false) };
        q.Criteria.AddCondition("sanic_sigil_userid", ConditionOperator.Equal, userId);
        foreach (var f in client.RetrieveMultiple(q).Entities) client.Delete("sanic_sigil_tbl_mastersignature", f.Id);
    }
    catch (Exception ex) { Console.Error.WriteLine($"[cleanup] firmas de {userId}: {ex.Message}"); }
}

static string Env(string n) => Environment.GetEnvironmentVariable(n)
    ?? throw new InvalidOperationException($"{n} no configurada (source .env).");

internal readonly record struct Resultado(Guid Firmante, bool? IsLastSigner, string? Error);

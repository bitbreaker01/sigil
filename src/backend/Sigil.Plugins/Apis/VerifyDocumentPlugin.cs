// sanic_sigil_capi_VerifyDocument — Unbound.
// Dos modos: (a) por TransactionId (llega del QR / Detail) → constancia + veredicto contra ESE
// finalhash; sin Sha256Hash, solo constancia. (b) por Sha256Hash SOLO → búsqueda en el ledger por
// finalhash (soltás cualquier PDF sellado, como Adobe/DocuSign; hallado ⇒ auténtico e íntegro).
// Verificación cruzada extendida: todos los documenthash de eventos de
// firma iguales entre sí e iguales al contenthash del ledger, y columnas de sistema de
// esos eventos sin modificación posterior (modifiedon==createdon, modifiedby==createdby)
// — con su límite declarado: no detiene a quien toma la identidad del motor ni al
// sysadmin; para esos, la defensa es la evidencia externa (TSA).
// La constancia incluye hash_final EN CLARO (no es secreto: describe un archivo ya
// distribuido). Escribe el evento tipo 11 con actor (el tradeoff declarado:
// quien posee un txId obtiene metadatos, y su verificación queda registrada).
// Out: Found, IsIntact?, MetadataJson, TsaTokenBase64?.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class VerifyDocumentPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var txIdOpt = e.Contexto.InputParameters.TryGetValue("TransactionId", out var v) && v is Guid g && g != Guid.Empty
            ? g
            : (Guid?)null;
        var hashAVerificar = e.Input<string>("Sha256Hash");
        var hashNormalizado = hashAVerificar?.Trim();

        // Un hash mal formado es un error de CONTRATO (typo del verificador), no un veredicto
        // "adulterado" — se rechaza claro en vez de devolver IsIntact=false engañoso (S4).
        if (!string.IsNullOrWhiteSpace(hashNormalizado) &&
            !System.Text.RegularExpressions.Regex.IsMatch(hashNormalizado!, "^[0-9A-Fa-f]{64}$"))
            throw new InvalidPluginExecutionException(
                "Sha256Hash debe ser un SHA-256 en hexadecimal (64 caracteres 0-9/A-F).");

        if (txIdOpt is null && string.IsNullOrWhiteSpace(hashNormalizado))
            throw new InvalidPluginExecutionException("Debe indicar TransactionId o Sha256Hash para verificar.");

        var q = new QueryExpression(SchemaNames.Ledger.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Ledger.TransactionId, SchemaNames.Ledger.ContentHash,
                SchemaNames.Ledger.FinalHash, SchemaNames.Ledger.TsaStatus, SchemaNames.Ledger.TsaToken,
                SchemaNames.Ledger.SealedOn, SchemaNames.Ledger.SignerSummary, SchemaNames.Ledger.Name),
        };
        // Modo por-txId: filtra por la transacción. Modo por-hash: filtra por finalhash (igualdad
        // insensible a mayúsculas en Dataverse). Quien busca por hash posee el archivo (sus bytes):
        // mismo umbral de posesión que el txId del QR — no amplía la divulgación.
        if (txIdOpt is Guid txIdVal)
            q.Criteria.AddCondition(SchemaNames.Ledger.TransactionId, ConditionOperator.Equal, txIdVal);
        else
            q.Criteria.AddCondition(SchemaNames.Ledger.FinalHash, ConditionOperator.Equal, hashNormalizado!);
        var ledger = e.Servicio.RetrieveMultiple(q).Entities.FirstOrDefault();

        if (ledger is null)
        {
            e.Output("Found", false);
            e.Output("MetadataJson", "{\"found\":false}");

            // Si veníamos por txId y la TRANSACCIÓN existe, la verificación se registra igual (el
            // evento ancla a la transacción, no al ledger — captura toda
            // verificación, incluida una sobre una tx aún sin sellar). Un txId inexistente —o una
            // búsqueda por hash sin coincidencia— no deja rastro (no hay ancla). Antagonista A3.
            if (txIdOpt is Guid txSinLedger && TransaccionExiste(e, txSinLedger, out var creadorSinLedger))
            {
                var actorSinLedger = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
                var lectoresSinLedger = LectoresDe(e, txSinLedger, creadorSinLedger);
                Consultas.CrearEvento(e.Servicio, new EntityReference(SchemaNames.Tx.Entidad, txSinLedger),
                    EventType.VerificacionRealizada, actorSinLedger,
                    "Verificación realizada sobre una transacción sin sellar (sin constancia).",
                    creadorSinLedger, lectores: lectoresSinLedger);
            }
            e.Trace.Trace("VerifyDocument: sin ledger (txId={0}, hash={1}) — Found=false.",
                txIdOpt, string.IsNullOrWhiteSpace(hashNormalizado) ? "—" : "provisto");
            return;
        }

        // Con ledger a la vista, el ancla del evento es el txId: en el modo por-txId lo tenemos; en
        // el modo por-hash lo tomamos del propio ledger (su lookup a la transacción).
        var txId = txIdOpt ?? ledger.GetAttributeValue<EntityReference>(SchemaNames.Ledger.TransactionId)?.Id
            ?? throw new InvalidPluginExecutionException("El ledger no referencia una transacción (registro corrupto).");

        var finalHash = ledger.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash) ?? string.Empty;
        var contentHash = ledger.GetAttributeValue<string>(SchemaNames.Ledger.ContentHash) ?? string.Empty;
        var tsaStatusOpt = ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus)
            ?? throw new InvalidPluginExecutionException($"El ledger de {txId} no tiene estado TSA (registro corrupto).");
        var tsaStatus = (TsaStatus)tsaStatusOpt.Value;

        bool? esIntacto = null;
        if (!string.IsNullOrWhiteSpace(hashAVerificar))
            esIntacto = string.Equals(hashAVerificar!.Trim(), finalHash, StringComparison.OrdinalIgnoreCase);

        var historiaIntacta = VerificarHistorial(e, txId, contentHash);

        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            found = true,
            ledgerNumber = ledger.GetAttributeValue<string>(SchemaNames.Ledger.Name),
            sealedOnUtc = ledger.GetAttributeValue<DateTime?>(SchemaNames.Ledger.SealedOn)?.ToString("o"),
            finalHashHex = finalHash, // en claro — verificación manual independiente (sha256sum)
            contentHashHex = contentHash,
            tsaStatus = tsaStatus switch
            {
                TsaStatus.SelladoConTsa => "sealed",
                TsaStatus.ReSelladoPendiente => "pending",
                _ => "none",
            },
            historyIntact = historiaIntacta,
            isIntact = esIntacto,
            signerSummary = ledger.GetAttributeValue<string>(SchemaNames.Ledger.SignerSummary),
            verifiedOnUtc = DateTime.UtcNow.ToString("o"),
        });

        e.Output("Found", true);
        if (esIntacto.HasValue)
            e.Output("IsIntact", esIntacto.Value);
        e.Output("MetadataJson", metadata);
        var token = ledger.GetAttributeValue<string>(SchemaNames.Ledger.TsaToken);
        if (!string.IsNullOrEmpty(token))
            e.Output("TsaTokenBase64", token);

        // Evento 11: cada verificación queda registrada con su actor.
        var (_, creador) = Consultas.EstadoYCreador(e.Servicio, txId);
        var lectores = LectoresDe(e, txId, creador);
        var actor = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
        var veredicto = esIntacto switch
        {
            true => "veredicto: INTACTO",
            false => "veredicto: NO COINCIDE",
            null => "constancia (sin hash a verificar)",
        };
        Consultas.CrearEvento(e.Servicio, new EntityReference(SchemaNames.Tx.Entidad, txId),
            EventType.VerificacionRealizada, actor,
            $"Verificación realizada — {veredicto}; historial {(historiaIntacta ? "íntegro" : "CON ANOMALÍAS")}.",
            creador, lectores: lectores);

        e.Trace.Trace("VerifyDocument: {0} — intacto={1}, historial={2}.", txId, esIntacto, historiaIntacta);
    }

    private static bool TransaccionExiste(EntornoDeApi e, Guid txId, out Guid creador)
    {
        var q = new QueryExpression(SchemaNames.Tx.Entidad) { ColumnSet = new ColumnSet(SchemaNames.Tx.OwnerId) };
        q.Criteria.AddCondition($"{SchemaNames.Tx.Entidad}id", ConditionOperator.Equal, txId);
        var fila = e.Servicio.RetrieveMultiple(q).Entities.FirstOrDefault();
        creador = fila?.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId)?.Id ?? Guid.Empty;
        return fila is not null;
    }

    private static System.Collections.Generic.List<Guid> LectoresDe(EntornoDeApi e, Guid txId, Guid creador)
        => Consultas.ParticipantesDe(e.Servicio, txId)
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();

    /// <summary>
    /// Verificación cruzada del historial: eventos de firma con documenthash
    /// homogéneo e igual al contenthash, sin señales de edición posterior.
    /// </summary>
    private static bool VerificarHistorial(EntornoDeApi e, Guid txId, string contentHash)
    {
        var q = new QueryExpression(SchemaNames.Evento.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Evento.Type, SchemaNames.Evento.DocumentHash,
                "createdon", "modifiedon", "createdby", "modifiedby"),
        };
        q.Criteria.AddCondition(SchemaNames.Evento.TransactionId, ConditionOperator.Equal, txId);
        var eventos = e.Servicio.RetrieveMultiple(q).Entities;

        var deFirma = eventos.Where(ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type)?.Value == (int)EventType.FirmaRegistrada)
            .ToList();
        if (deFirma.Count == 0)
            return false; // una transacción sellada SIN eventos de firma es una anomalía en sí misma

        foreach (var ev in deFirma)
        {
            var hash = ev.GetAttributeValue<string>(SchemaNames.Evento.DocumentHash);
            if (!string.Equals(hash, contentHash, StringComparison.OrdinalIgnoreCase))
                return false; // el firmante vio (o dice haber visto) OTRO documento

            var creado = ev.GetAttributeValue<DateTime?>("createdon");
            var modificado = ev.GetAttributeValue<DateTime?>("modifiedon");
            if (creado != modificado)
                return false; // el evento fue editado después de crearse

            var creador = ev.GetAttributeValue<EntityReference>("createdby")?.Id;
            var modificador = ev.GetAttributeValue<EntityReference>("modifiedby")?.Id;
            if (creador != modificador)
                return false;
        }
        return true;
    }
}

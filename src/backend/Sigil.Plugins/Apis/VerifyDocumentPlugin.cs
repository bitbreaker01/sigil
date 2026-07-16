// sanic_sigil_capi_VerifyDocument (RF-20/21, ADR-007, doc 04 §3.1) — Unbound.
// Solo TransactionId → CONSTANCIA; con Sha256Hash → además VEREDICTO contra finalhash.
// Verificación cruzada extendida (doc 03 §4.6): todos los documenthash de eventos de
// firma iguales entre sí e iguales al contenthash del ledger, y columnas de sistema de
// esos eventos sin modificación posterior (modifiedon==createdon, modifiedby==createdby)
// — con su límite declarado: no detiene a quien toma la identidad del motor ni al
// sysadmin; para esos, la defensa es la evidencia externa (TSA).
// La constancia incluye hash_final EN CLARO (no es secreto: describe un archivo ya
// distribuido). Escribe el evento tipo 11 con actor (el tradeoff declarado del doc 04
// §3.3: quien posee un txId obtiene metadatos, y su verificación queda registrada).
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
        var txId = e.Contexto.InputParameters.TryGetValue("TransactionId", out var v) && v is Guid g
            ? g
            : throw new InvalidPluginExecutionException("TransactionId es obligatorio.");
        var hashAVerificar = e.Input<string>("Sha256Hash");

        var q = new QueryExpression(SchemaNames.Ledger.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Ledger.ContentHash, SchemaNames.Ledger.FinalHash,
                SchemaNames.Ledger.TsaStatus, SchemaNames.Ledger.TsaToken,
                SchemaNames.Ledger.SealedOn, SchemaNames.Ledger.SignerSummary, SchemaNames.Ledger.Name),
        };
        q.Criteria.AddCondition(SchemaNames.Ledger.TransactionId, ConditionOperator.Equal, txId);
        var ledger = e.Servicio.RetrieveMultiple(q).Entities.FirstOrDefault();

        if (ledger is null)
        {
            // Sin ledger no hay constancia NI ancla donde registrar el evento (decisión declarada).
            e.Output("Found", false);
            e.Output("MetadataJson", "{\"found\":false}");
            e.Trace.Trace("VerifyDocument: {0} sin ledger — Found=false.", txId);
            return;
        }

        var finalHash = ledger.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash) ?? string.Empty;
        var contentHash = ledger.GetAttributeValue<string>(SchemaNames.Ledger.ContentHash) ?? string.Empty;
        var tsaStatus = (TsaStatus)ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus).Value;

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

        // Evento 11 (RNF-04): cada verificación queda registrada con su actor.
        var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, txId);
        _ = estado;
        var participantes = Consultas.ParticipantesDe(e.Servicio, txId);
        var lectores = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
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

    /// <summary>
    /// Verificación cruzada del historial (doc 03 §4.6): eventos de firma con documenthash
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

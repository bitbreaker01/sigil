// sanic_sigil_capi_ExpireTransactions (RF-27, T12 + saneamiento T14 — doc 04 §3.1) — job
// diario disparado por cloud flow bajo el Service Principal (ExecutePrivilegeName de
// servicio). Dos responsabilidades:
//   1. T12: expira vencidas — SOLO Pendiente de Firma / Firmado Parcialmente.
//   2. T14 (doc 06 R7): Sellando > 24 h sin actividad del worker → Error de Sellado.
// Cada transición ocurre bajo lock + revalidación (R2): el job compite con firmas y
// cancelaciones en vuelo, y las carreras pierden limpio. Out: ExpiredCount, SanitizedCount.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class ExpireTransactionsPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var ahora = DateTime.UtcNow;
        var expiradas = 0;
        var saneadas = 0;

        // ── T12: candidatas vencidas en estados elegibles ──
        var qVencidas = new QueryExpression(SchemaNames.Tx.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Tx.Status, SchemaNames.Tx.OwnerId, SchemaNames.Tx.ExpiresOn),
        };
        qVencidas.Criteria.AddCondition(SchemaNames.Tx.Status, ConditionOperator.In,
            (int)TransactionStatus.PendienteDeFirma, (int)TransactionStatus.FirmadoParcialmente);
        qVencidas.Criteria.AddCondition(SchemaNames.Tx.ExpiresOn, ConditionOperator.LessThan, ahora);

        foreach (var candidata in e.Servicio.RetrieveMultiple(qVencidas).Entities)
        {
            LockDeFila.Tomar(e.Servicio, candidata.Id); // R2 + revalidar: pudo firmarse/cancelarse en el medio
            var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, candidata.Id);
            if (!ReglasDeJobs.EsExpirable(estado))
                continue;

            var cambio = new Entity(SchemaNames.Tx.Entidad, candidata.Id);
            cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Expirado);
            e.Servicio.Update(cambio);

            CrearEventoDeJob(e, candidata.Id, creador, EventType.Expirada,
                "Transacción expirada por vencimiento del plazo (RF-27).");
            expiradas++;
        }

        // ── T14: Sellando zombi (> 24 h sin actividad — el worker toca modifiedon en cada intento) ──
        var qZombis = new QueryExpression(SchemaNames.Tx.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Tx.Status, SchemaNames.Tx.OwnerId, "modifiedon"),
        };
        qZombis.Criteria.AddCondition(SchemaNames.Tx.Status, ConditionOperator.Equal, (int)TransactionStatus.Sellando);
        qZombis.Criteria.AddCondition("modifiedon", ConditionOperator.LessThan, ahora.AddHours(-24));

        foreach (var zombi in e.Servicio.RetrieveMultiple(qZombis).Entities)
        {
            // Nota: el lock actualiza modifiedon — si la transición de abajo fallara, el
            // reloj de T14 se difiere 24 h más (auto-sanable; la zombi vuelve a calificar).
            LockDeFila.Tomar(e.Servicio, zombi.Id);
            var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, zombi.Id);
            if (estado != TransactionStatus.Sellando)
                continue; // el worker despertó en el medio — la carrera pierde limpio

            var cambio = new Entity(SchemaNames.Tx.Entidad, zombi.Id);
            cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.ErrorDeSellado);
            e.Servicio.Update(cambio);

            CrearEventoDeJob(e, zombi.Id, creador, EventType.ErrorDeSellado,
                "saneamiento: worker sin actividad"); // wording exacto de T14 (doc 06 §1.1)
            saneadas++;
        }

        e.Output("ExpiredCount", expiradas);
        e.Output("SanitizedCount", saneadas);
        e.Trace.Trace("ExpireTransactions: {0} expiradas, {1} saneadas.", expiradas, saneadas);
    }

    private static void CrearEventoDeJob(EntornoDeApi e, Guid txId, Guid creador, EventType tipo, string detalles)
    {
        var lectores = Consultas.ParticipantesDe(e.Servicio, txId)
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
        Consultas.CrearEvento(e.Servicio, new EntityReference(SchemaNames.Tx.Entidad, txId),
            tipo, ("Sistema", string.Empty), detalles, creador, lectores: lectores);
    }
}

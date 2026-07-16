// sanic_sigil_capi_ResealPending (ADR-005, doc 04 §3.1) — job diario. Reintenta el sello
// TSA sobre ledgers en Re-sellado pendiente. Con TsaEnabled=false los transiciona a
// Sin sello TSA (evita huérfanos eternos bajo una etiqueta que promete un reintento que
// no va a ocurrir). El rate limit por endpoint lo respeta el ClienteTsa (Sectigo ≥15 s
// entre requests automatizados — crítico procesando lotes).
// sealedon NO se toca en el re-sellado: el token prueba existencia a SU genTime, no antes
// (doc 04 §4 — el nivel de evidencia muestra AMBAS fechas).
// Out: ResealedCount, MovedToNoTsaCount, StillPendingCount.
//
// GAP DE CATÁLOGO REGISTRADO (2026-07-16): el doc 04 §3.1 pide "+ evento" al mover a Sin
// sello TSA, pero sanic_sigil_choice_eventtype no tiene un valor para "TSA abandonada".
// El re-sellado EXITOSO sí tiene el tipo 9. Hasta que el negocio agregue el valor al
// choice (portal + Apéndice A), el movimiento queda en el trace y en los contadores.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class ResealPendingPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var env = new EnvVars(e.Servicio);
        var tsaHabilitada = env.BoolObligatorio(SchemaNames.EnvVars.TsaEnabled);

        var q = new QueryExpression(SchemaNames.Ledger.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Ledger.TransactionId, SchemaNames.Ledger.FinalHash),
        };
        q.Criteria.AddCondition(SchemaNames.Ledger.TsaStatus, ConditionOperator.Equal,
            (int)TsaStatus.ReSelladoPendiente);
        var pendientes = e.Servicio.RetrieveMultiple(q).Entities;

        int resellados = 0, movidos = 0, siguenPendientes = 0;

        if (!tsaHabilitada)
        {
            foreach (var ledger in pendientes)
            {
                var cambio = new Entity(SchemaNames.Ledger.Entidad, ledger.Id);
                cambio[SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)TsaStatus.SinSelloTsa);
                e.Servicio.Update(cambio);
                movidos++;
                e.Trace.Trace("ResealPending: ledger {0} → Sin sello TSA (TSA deshabilitada).", ledger.Id);
            }
        }
        else
        {
            var sellador = e.SelladorTsa;
            var config = TsaConfig.Parse(env.TextoObligatorio(SchemaNames.EnvVars.TsaEndpoints));
            foreach (var ledger in pendientes)
            {
                var txRef = ledger.GetAttributeValue<EntityReference>(SchemaNames.Ledger.TransactionId);
                byte[] finalBytes;
                try
                {
                    finalBytes = e.Archivos.Descargar(txRef, SchemaNames.Tx.FinalFile);
                }
                catch (Exception ex)
                {
                    e.Trace.Trace("ResealPending: descarga del final de {0} falló: {1}", txRef.Id, ex.Message);
                    siguenPendientes++;
                    continue; // el próximo run lo reintenta — jamás se aborta el lote entero
                }

                // Integridad primero: el token debe sellar EXACTAMENTE los bytes del ledger.
                var hashReal = HashUtil.Sha256Hex(finalBytes);
                var hashLedger = ledger.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash);
                if (!string.Equals(hashReal, hashLedger, StringComparison.OrdinalIgnoreCase))
                {
                    e.Trace.Trace("ResealPending: ANCLA ROTA en {0} — jamás se sella un mismatch.", txRef.Id);
                    siguenPendientes++;
                    continue;
                }

                using var sha = System.Security.Cryptography.SHA256.Create();
                var resultado = sellador.Sellar(sha.ComputeHash(finalBytes), config);
                if (!resultado.Exitoso)
                {
                    siguenPendientes++;
                    continue;
                }

                var cambio = new Entity(SchemaNames.Ledger.Entidad, ledger.Id);
                cambio[SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)TsaStatus.SelladoConTsa);
                cambio[SchemaNames.Ledger.TsaToken] = Convert.ToBase64String(resultado.TokenDer!);
                e.Servicio.Update(cambio); // sealedon intacto — el genTime del token cuenta su propia fecha

                var (_, creador) = Consultas.EstadoYCreador(e.Servicio, txRef.Id);
                var lectores = Consultas.ParticipantesDe(e.Servicio, txRef.Id)
                    .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
                    .Where(u => u != creador)
                    .Distinct()
                    .ToList();
                Consultas.CrearEvento(e.Servicio, txRef, EventType.ReSelladoTsaObtenido,
                    ("Sistema", string.Empty),
                    $"Sello TSA obtenido en re-intento ({resultado.Endpoint}, genTime {resultado.GenTimeUtc:o}).",
                    creador, lectores: lectores);
                resellados++;
            }
        }

        e.Output("ResealedCount", resellados);
        e.Output("MovedToNoTsaCount", movidos);
        e.Output("StillPendingCount", siguenPendientes);
        e.Trace.Trace("ResealPending: {0} resellados, {1} a Sin sello, {2} pendientes.",
            resellados, movidos, siguenPendientes);
    }

}

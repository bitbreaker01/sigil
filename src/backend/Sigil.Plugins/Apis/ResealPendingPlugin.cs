// sanic_sigil_capi_ResealPending — job diario. Reintenta el sello
// TSA sobre ledgers en Re-sellado pendiente. Con TsaEnabled=false los transiciona a
// Sin sello TSA (evita huérfanos eternos bajo una etiqueta que promete un reintento que
// no va a ocurrir). El rate limit por endpoint lo respeta el ClienteTsa (Sectigo ≥15 s
// entre requests automatizados — crítico procesando lotes).
// sealedon NO se toca en el re-sellado: el token prueba existencia a SU genTime, no antes
// (el nivel de evidencia muestra AMBAS fechas).
// Out: ResealedCount, MovedToNoTsaCount, StillPendingCount.
//
// Evento del movimiento a Sin sello TSA: tipo "TSA abandonada" (159460012 — agregado por
// el negocio el 2026-07-16 al choice + Apéndice A; resuelve el gap de catálogo registrado).

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
    // CAP DE LOTE (antagonista C1, 2026-07-16 — aritmética del presupuesto): por ledger =
    // descarga (~2-6 s) + TSA (~1-10 s) + rate limit de Sectigo (15 s) ≈ 20-30 s. Sin cap,
    // el 5º-8º ledger cruza el límite DURO de 2 minutos y el rollback transaccional revierte
    // TODO (tokens ya pedidos = requests quemados) → el job quedaría rojo para siempre con
    // backlog. El job es diario e idempotente: drenar de a 3 es correcto.
    public const int MaxResellosPorCorrida = 3;

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
        q.AddOrder(SchemaNames.Ledger.SealedOn, OrderType.Ascending); // determinístico: los más viejos primero
        var pendientes = e.Servicio.RetrieveMultiple(q).Entities;

        int resellados = 0, movidos = 0, siguenPendientes = 0, anclasRotas = 0;

        if (!tsaHabilitada)
        {
            foreach (var ledger in pendientes)
            {
                var cambio = new Entity(SchemaNames.Ledger.Entidad, ledger.Id);
                cambio[SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)TsaStatus.SinSelloTsa);
                e.Servicio.Update(cambio);
                movidos++;

                var txRef = ledger.GetAttributeValue<EntityReference>(SchemaNames.Ledger.TransactionId);
                var (_, creador) = Consultas.EstadoYCreador(e.Servicio, txRef.Id);
                var lectores = Consultas.ParticipantesDe(e.Servicio, txRef.Id)
                    .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
                    .Where(u => u != creador)
                    .Distinct()
                    .ToList();
                Consultas.CrearEvento(e.Servicio, txRef, EventType.TsaAbandonada, ("Sistema", string.Empty),
                    "El re-intento de sello TSA fue abandonado (TSA deshabilitada en el ambiente) — " +
                    "el nivel de evidencia queda en 'Sin sello TSA'.", creador, lectores: lectores);
                e.Trace.Trace("ResealPending: ledger {0} → Sin sello TSA (TSA deshabilitada) + evento.", ledger.Id);
            }
        }
        else
        {
            var sellador = e.SelladorTsa;
            var config = TsaConfig.Parse(env.TextoObligatorio(SchemaNames.EnvVars.TsaEndpoints));
            var procesados = 0;
            foreach (var ledger in pendientes)
            {
                if (procesados >= MaxResellosPorCorrida)
                {
                    siguenPendientes++; // el resto drena en las próximas corridas diarias
                    continue;
                }
                procesados++;
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
                    // Señal de integridad CATASTRÓFICA (el archivo durable no coincide con el
                    // ledger inmutable): jamás se sella, y se cuenta APARTE de "TSA caída" para
                    // que el operador lo distinga de una degradación normal (antagonista A8).
                    e.Trace.Trace("ResealPending: !!! ANCLA ROTA en {0} — archivo {1}... vs ledger {2}...; requiere intervención.",
                        txRef.Id, hashReal.Substring(0, 8), hashLedger?.Substring(0, 8) ?? "(vacío)");
                    anclasRotas++;
                    continue;
                }

                using var sha = System.Security.Cryptography.SHA256.Create();
                var resultado = sellador.Sellar(sha.ComputeHash(finalBytes), config);
                if (!resultado.Exitoso)
                {
                    // El diagnóstico por endpoint que el ClienteTsa construye va al trace (S5).
                    e.Trace.Trace("ResealPending: TSA sigue caída para {0}: {1}",
                        txRef.Id, string.Join(" | ", resultado.Errores));
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
        e.Output("AnchorMismatchCount", anclasRotas); // 0 en operación normal; >0 = integridad rota
        e.Trace.Trace("ResealPending: {0} resellados, {1} a Sin sello, {2} pendientes, {3} anclas rotas.",
            resellados, movidos, siguenPendientes, anclasRotas);
    }

}

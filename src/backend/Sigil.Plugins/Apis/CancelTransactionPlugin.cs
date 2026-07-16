// sanic_sigil_capi_CancelTransaction (T13, RF-30/Q-08): el creador retira la transacción.
// Estados elegibles: Pendiente de Firma, Firmado Parcialmente y Error de Sellado (cierra el
// ciclo de vida de fallos deterministas) — JAMÁS Sellando (el pipeline está trabajando).
// Motivo opcional. Evento 12; la notificación la emiten los flows (R3), no el motor.

using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class CancelTransactionPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        var reason = e.Input<string>("Reason");
        if (reason is { Length: > 2000 })
            throw new InvalidPluginExecutionException(
                $"El motivo supera los 2.000 caracteres permitidos (tiene {reason.Length}).");

        LockDeFila.Tomar(e.Servicio, target.Id); // R2 — usa el lock de §5 (doc 04 §3.1)

        var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, target.Id);
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarCancelacion(e.Llamante, creador, estado);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        var cambio = new Entity(SchemaNames.Tx.Entidad, target.Id);
        cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Cancelado);
        e.Servicio.Update(cambio);

        var participantes = Consultas.ParticipantesDe(e.Servicio, target.Id);
        var actor = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
        var lectores = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
        Consultas.CrearEvento(e.Servicio, target, EventType.CanceladaPorElCreador, actor,
            string.IsNullOrWhiteSpace(reason) ? "Cancelada por el creador." : $"Cancelada por el creador. Motivo: {reason}",
            creador, lectores: lectores);

        e.Trace.Trace("CancelTransaction: {0} canceló {1}.", e.Llamante, target.Id);
    }
}

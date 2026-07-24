// sanic_sigil_capi_RejectTransaction (T11 + P4): rechazo por un participante
// en Turno Activo, motivo OBLIGATORIO. Un solo rechazo mata la transacción completa en
// ambos enrutamientos (decisión explícita) — los que no firmaron quedan como estén.

using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class RejectTransactionPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        var reason = e.Input<string>("Reason");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidPluginExecutionException("El motivo del rechazo es obligatorio.");
        if (reason!.Length > 2000)
            throw new InvalidPluginExecutionException(
                $"El motivo supera los 2.000 caracteres permitidos (tiene {reason.Length}).");

        LockDeFila.Tomar(e.Servicio, target.Id); // R2

        var tx = Consultas.Transaccion(e.Servicio, target.Id);
        var estadoTx = (TransactionStatus)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
        var creador = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;
        var participantes = Consultas.ParticipantesDe(e.Servicio, target.Id);
        var mio = participantes.FirstOrDefault(p =>
            p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == e.Llamante);

        var motivo = ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(
            esParticipante: mio is not null,
            mio is null ? null : (ParticipantStatus)mio.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value,
            estadoTx);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        // P4: participante → Rechazado con su motivo.
        var rechazo = new Entity(SchemaNames.Participante.Entidad, mio!.Id);
        rechazo[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.Rechazado);
        rechazo[SchemaNames.Participante.RejectionReason] = reason;
        e.Servicio.Update(rechazo);

        // T11: transacción → Rechazado.
        var cambio = new Entity(SchemaNames.Tx.Entidad, target.Id);
        cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Rechazado);
        e.Servicio.Update(cambio);

        var actor = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
        var lectores = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
        Consultas.CrearEvento(e.Servicio, target, EventType.Rechazada, actor,
            $"Rechazada por {actor.Nombre}. Motivo: {reason}",
            creador, participantId: mio.Id, lectores: lectores);

        e.Trace.Trace("RejectTransaction: {0} rechazó {1}.", e.Llamante, target.Id);
    }
}

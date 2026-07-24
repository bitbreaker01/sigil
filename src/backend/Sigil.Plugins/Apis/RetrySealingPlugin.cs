// sanic_sigil_capi_RetrySealing (T10): Error de Sellado → Sellando.
// Sin esta API el estado de error sería un callejón sin salida (nadie tiene Update
// directo). El cambio de status re-dispara el worker (idempotente).
// Autorización: el CREADOR y estado Error de Sellado.

using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class RetrySealingPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        LockDeFila.Tomar(e.Servicio, target.Id); // R2 + revalidación de estado

        var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, target.Id);
        if (e.Llamante != creador)
            throw new InvalidPluginExecutionException("Solo el creador de la transacción puede reintentar el sellado.");
        if (estado != TransactionStatus.ErrorDeSellado)
            throw new InvalidPluginExecutionException("El reintento de sellado solo aplica a transacciones en Error de Sellado.");

        var cambio = new Entity(SchemaNames.Tx.Entidad, target.Id);
        cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Sellando);
        e.Servicio.Update(cambio); // re-dispara el worker (T10)

        var participantes = Consultas.ParticipantesDe(e.Servicio, target.Id);
        var actor = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
        var lectores = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
        // Evento 6 con details = "reintento manual" — distingue las entradas de la línea de tiempo (T10)
        Consultas.CrearEvento(e.Servicio, target, EventType.SelladoIniciado, actor,
            "Reintento manual del sellado solicitado por el creador.", creador, lectores: lectores);

        e.Trace.Trace("RetrySealing: {0} re-disparada por {1}.", target.Id, e.Llamante);
    }
}

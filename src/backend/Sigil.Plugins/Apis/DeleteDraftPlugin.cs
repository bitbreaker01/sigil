// sanic_sigil_capi_DeleteDraft — Bound. Borra un borrador.
// Lock + autorización (creador + Borrador). Orden de borrado de T3:
// los EVENTOS primero (la relación transacción→evento es Delete Restrict:
// el historial no se borra en cascada), después la transacción (participantes y zonas
// caen por cascada parental).

using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class DeleteDraftPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        LockDeFila.Tomar(e.Servicio, target.Id); // SIEMPRE primero

        var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, target.Id);
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarEdicionDeBorrador(e.Llamante, creador, estado);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        foreach (var evento in Consultas.EventosDe(e.Servicio, target.Id))
            e.Servicio.Delete(SchemaNames.Evento.Entidad, evento.Id);

        e.Servicio.Delete(SchemaNames.Tx.Entidad, target.Id);
        e.Trace.Trace("DeleteDraft: {0} eliminada (eventos primero — T3).", target.Id);
    }
}

// Lock de fila: todo plugin que decide sobre estado compartido ejecuta,
// como PRIMERA operación dentro de su transacción de BD, un Update sobre la columna
// técnica sanic_sigil_locktoken. PROHIBIDO lockear escribiendo el status: los triggers
// de los flows disparan aunque el valor escrito sea idéntico (verificado).
// Después del lock, SIEMPRE re-leer el estado: las ejecuciones concurrentes quedaron
// serializadas detrás de este update.

using System;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Apis;

public static class LockDeFila
{
    public static void Tomar(IOrganizationService servicio, Guid transactionId)
    {
        var fila = new Entity(SchemaNames.Tx.Entidad, transactionId);
        // El VALOR no importa (columna de no-op); el UPDATE toma el lock de SQL hasta el commit.
        fila[SchemaNames.Tx.LockToken] = Environment.TickCount & int.MaxValue;
        servicio.Update(fila);
    }
}

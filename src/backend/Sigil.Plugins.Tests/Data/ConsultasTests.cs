// Regresión del sellado: ZonasDe debe traer la GEOMETRÍA de la zona (PosX/PosY/Width/Height).
// El bug: el ColumnSet solo pedía Page/Name/ParticipantId, así que GetAttributeValue<decimal>
// sobre las columnas ausentes devolvía 0 → cada firma se estampaba en (0,0) con tamaño 0×0
// (invisible en el documento; solo la hoja de cierre, con caja fija, mostraba la firma).
// El stub HONRA el ColumnSet (proyección real), así que este test falla con el bug y pasa con el fix.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Data;

public class ConsultasTests
{
    [Fact]
    public void ZonasDe_TraeLaGeometriaDeLaZona()
    {
        var stub = new StubOrganizationService();
        var participantId = Guid.NewGuid();
        var zona = new Entity(SchemaNames.Zona.Entidad);
        zona[SchemaNames.Zona.ParticipantId] = new EntityReference(SchemaNames.Participante.Entidad, participantId);
        zona[SchemaNames.Zona.Page] = 2;
        zona[SchemaNames.Zona.PosX] = 12.5m;
        zona[SchemaNames.Zona.PosY] = 34.5m;
        zona[SchemaNames.Zona.Width] = 20m;
        zona[SchemaNames.Zona.Height] = 8m;
        stub.Sembrar(zona);

        var z = Assert.Single(Consultas.ZonasDe(stub, new[] { participantId }));

        // Sin la geometría en el ColumnSet estos serían 0 (default(decimal)/default(int)).
        Assert.Equal(2, z.GetAttributeValue<int>(SchemaNames.Zona.Page));
        Assert.Equal(12.5m, z.GetAttributeValue<decimal>(SchemaNames.Zona.PosX));
        Assert.Equal(34.5m, z.GetAttributeValue<decimal>(SchemaNames.Zona.PosY));
        Assert.Equal(20m, z.GetAttributeValue<decimal>(SchemaNames.Zona.Width));
        Assert.Equal(8m, z.GetAttributeValue<decimal>(SchemaNames.Zona.Height));
    }

    [Fact]
    public void ZonasDe_SinParticipantes_NoConsulta()
    {
        var stub = new StubOrganizationService();
        Assert.Empty(Consultas.ZonasDe(stub, Array.Empty<Guid>()));
    }
}

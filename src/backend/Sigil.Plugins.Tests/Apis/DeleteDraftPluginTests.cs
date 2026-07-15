// sanic_sigil_capi_DeleteDraft — orquestación (doc 04 §3.1, doc 06 T3).
// El assert central es el ORDEN de borrado: eventos primero (Delete Restrict — doc 03 §2),
// la transacción al final. M2 exige que ese orden sea verificable, no asumido.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class DeleteDraftPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;

    public DeleteDraftPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
    }

    private void Ejecutar(Guid txId, Guid llamante)
    {
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        _arnes.Ejecutar(new DeleteDraftPlugin(), SchemaNames.Apis.DeleteDraft, llamante);
    }

    [Fact] // M1 — fila "DeleteDraft: el llamante es el creador"
    public void M1_UnNoCreador_EsRechazado_YNadaSeBorra()
    {
        var intruso = _arnes.SembrarUsuario("Intruso", "x@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, intruso));

        Assert.Contains("creador", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad));
    }

    [Theory] // M1 — "y el estado es Borrador": nada enviado, firmado o terminado se borra
    [InlineData(TransactionStatus.PendienteDeFirma)]
    [InlineData(TransactionStatus.Completado)]
    public void M1_FueraDeBorrador_EsRechazado(TransactionStatus estado)
    {
        var txId = _arnes.SembrarTransaccion(_creador, estado);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));
        Assert.Contains("borrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Feliz_BorraLosEventosPrimero_YLaTransaccionAlFinal()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.SembrarEvento(txId);
        _arnes.SembrarEvento(txId);

        Ejecutar(txId, _creador);

        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad));
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));

        // Orden T3: [lock] → Delete evento ×2 → Delete transacción
        var borrados = _arnes.Servicio.Operaciones.Where(o => o.Tipo == "Delete").ToList();
        Assert.Equal(3, borrados.Count);
        Assert.Equal(SchemaNames.Evento.Entidad, borrados[0].Entidad);
        Assert.Equal(SchemaNames.Evento.Entidad, borrados[1].Entidad);
        Assert.Equal(SchemaNames.Tx.Entidad, borrados[2].Entidad);
    }

    [Fact] // doc 04 §5: también DeleteDraft toma el lock antes de decidir
    public void ElLock_EsLaPrimeraOperacion()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);

        Ejecutar(txId, _creador);

        var primera = _arnes.Servicio.Operaciones.First();
        Assert.Equal("Update", primera.Tipo);
        Assert.Equal(SchemaNames.Tx.LockToken, Assert.Single(primera.Datos!.Attributes).Key);
    }
}

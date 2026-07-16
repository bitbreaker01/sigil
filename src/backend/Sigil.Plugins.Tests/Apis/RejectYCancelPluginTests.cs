// sanic_sigil_capi_RejectTransaction (T11+P4) y sanic_sigil_capi_CancelTransaction (T13).
// M1: no-creador cancela; participante sin turno rechaza. M2: estados elegibles exactos.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class RejectYCancelPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;
    private readonly Guid _firmante;

    public RejectYCancelPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        _firmante = _arnes.SembrarUsuario("Beto Firmante", "beto@bac.test");
    }

    private void Rechazar(Guid txId, Guid llamante, string? reason)
    {
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        if (reason is not null) _arnes.Contexto.InputParameters["Reason"] = reason;
        _arnes.Ejecutar(new RejectTransactionPlugin(), SchemaNames.Apis.RejectTransaction, llamante);
    }

    private void Cancelar(Guid txId, Guid llamante, string? reason = null)
    {
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        if (reason is not null) _arnes.Contexto.InputParameters["Reason"] = reason;
        _arnes.Ejecutar(new CancelTransactionPlugin(), SchemaNames.Apis.CancelTransaction, llamante);
    }

    // ── Reject (T11 + P4) ────────────────────────────────────────────────────

    [Fact]
    public void T11_RechazoConMotivo_TransicionaTodo_YRegistraElEvento()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        var pid = _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.TurnoActivo);

        Rechazar(txId, _firmante, "No estoy de acuerdo con la cláusula 3.");

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Rechazado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);

        var p = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad).Single();
        Assert.Equal((int)ParticipantStatus.Rechazado, p.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
        Assert.Equal("No estoy de acuerdo con la cláusula 3.", p.GetAttributeValue<string>(SchemaNames.Participante.RejectionReason));

        var evento = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
        Assert.Equal((int)EventType.Rechazada, evento.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value);
        Assert.Equal(pid, evento.GetAttributeValue<EntityReference>(SchemaNames.Evento.ParticipantId).Id);
        // M13 — el evento 4 quedó compartido con el participante
        Assert.Contains((SchemaNames.Evento.Entidad, evento.Id, _firmante), _arnes.Servicio.Compartidos);
    }

    [Theory] // motivo obligatorio (T11)
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void T11_SinMotivo_EsRechazado(string? reason)
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.TurnoActivo);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Rechazar(txId, _firmante, reason));
        Assert.Contains("motivo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // M1 — participante Pendiente (secuencial) no puede rechazar: no le llegó el documento
    public void M1_ParticipantePendiente_NoPuedeRechazar()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Secuencial);
        _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.Pendiente);

        Assert.Throws<InvalidPluginExecutionException>(() => Rechazar(txId, _firmante, "motivo"));
    }

    // ── Cancel (T13) ─────────────────────────────────────────────────────────

    [Fact]
    public void T13_ElCreadorCancela_ConEvento12()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.TurnoActivo);

        Cancelar(txId, _creador, "Ya no es necesario.");

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Cancelado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        var evento = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
        Assert.Equal((int)EventType.CanceladaPorElCreador, evento.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value);
        // M13: el evento quedó compartido con el participante
        Assert.Contains((SchemaNames.Evento.Entidad, evento.Id, _firmante), _arnes.Servicio.Compartidos);
    }

    [Fact] // motivo opcional en Cancel (a diferencia de Reject)
    public void T13_SinMotivo_TambienCancela()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.FirmadoParcialmente, RoutingType.Paralelo);
        Cancelar(txId, _creador);
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Cancelado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }

    [Fact] // M1 — "no-creador cancela"
    public void M1_UnNoCreador_NoPuedeCancelar()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.TurnoActivo);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Cancelar(txId, _firmante));
        Assert.Contains("creador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // M2 — jamás Sellando ni terminales (T13)
    [InlineData(TransactionStatus.Sellando)]
    [InlineData(TransactionStatus.Completado)]
    [InlineData(TransactionStatus.Borrador)]
    public void M2_CancelarEnEstadoNoElegible_EsRechazado(TransactionStatus estado)
    {
        var txId = _arnes.SembrarTransaccion(_creador, estado, RoutingType.Paralelo);
        Assert.Throws<InvalidPluginExecutionException>(() => Cancelar(txId, _creador));
    }

    [Fact] // T13 incluye Error de Sellado (cierra el ciclo de fallos deterministas)
    public void T13_DesdeErrorDeSellado_SePuedeCancelar()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.ErrorDeSellado, RoutingType.Paralelo);
        Cancelar(txId, _creador);
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Cancelado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }
}

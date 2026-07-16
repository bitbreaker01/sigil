// sanic_sigil_capi_SendTransaction — T4 (docs 04 §3.1, 06). Los asserts centrales:
// completitud de zonas RF-28 (M9), contenthash como ancla temprana, activación de turnos
// P2 por enrutamiento, share a participantes + eventos retroactivos (M13), evento 2.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class SendTransactionPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;
    private readonly Guid _firmante1;
    private readonly Guid _firmante2;

    public SendTransactionPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        _firmante1 = _arnes.SembrarUsuario("Beto Uno", "beto@bac.test");
        _firmante2 = _arnes.SembrarUsuario("Caro Dos", "caro@bac.test");
    }

    private void Ejecutar(Guid txId, Guid llamante)
    {
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        _arnes.Ejecutar(new SendTransactionPlugin(), SchemaNames.Apis.SendTransaction, llamante);
    }

    private Guid SembrarBorradorListo(RoutingType routing, params (Guid userId, int? orden)[] firmantes)
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador, routing);
        foreach (var (userId, orden) in firmantes)
        {
            var pid = _arnes.SembrarParticipante(txId, userId);
            if (orden.HasValue)
            {
                var p = new Entity(SchemaNames.Participante.Entidad, pid);
                p[SchemaNames.Participante.Order] = orden.Value;
                _arnes.Servicio.Update(p);
            }
            _arnes.SembrarZona(pid, page: 1);
        }
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));
        return txId;
    }

    [Fact] // M1 — Send comparte la regla de edición: creador + Borrador
    public void M1_UnNoCreador_EsRechazado()
    {
        var txId = SembrarBorradorListo(RoutingType.Paralelo, (_firmante1, null));
        Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _firmante1));
    }

    [Fact] // M9/RF-28 — el guard de T4: participante sin zona bloquea, LISTÁNDOLO
    public void M9_ParticipanteSinZona_BloqueaElEnvio_Listandolo()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador, RoutingType.Paralelo);
        _arnes.SembrarParticipante(txId, _firmante1); // SIN zona
        var pid2 = _arnes.SembrarParticipante(txId, _firmante2);
        _arnes.SembrarZona(pid2, page: 1);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));

        Assert.Contains("zona", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"Participante {_firmante1}", ex.Message); // nombre sembrado del participante
        // sin transición: sigue Borrador
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Borrador, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }

    [Fact] // T4 feliz en paralelo: estado + anclas + TODOS los turnos activos
    public void T4_Paralelo_TransicionaConAnclas_YActivaATodos()
    {
        var pdf = ArnesDeApi.PdfDePrueba(1);
        var txId = SembrarBorradorListo(RoutingType.Paralelo, (_firmante1, null), (_firmante2, null));
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, pdf); // pisar con bytes conocidos

        Ejecutar(txId, _creador);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.PendienteDeFirma, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.Equal(HashUtil.Sha256Hex(pdf), tx.GetAttributeValue<string>(SchemaNames.Tx.ContentHash));
        Assert.True(tx.Contains(SchemaNames.Tx.SentOn));
        // expireson = senton + 15 (default del arnés — la transacción no fijó plazo propio)
        var senton = tx.GetAttributeValue<DateTime>(SchemaNames.Tx.SentOn);
        Assert.Equal(senton.AddDays(15), tx.GetAttributeValue<DateTime>(SchemaNames.Tx.ExpiresOn));

        Assert.All(_arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad), p =>
        {
            Assert.Equal((int)ParticipantStatus.TurnoActivo, p.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
            Assert.True(p.Contains(SchemaNames.Participante.TurnActivatedOn));
        });

        var evento = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
        Assert.Equal((int)EventType.EnviadaAFirma, evento.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value);
    }

    [Fact] // C1 del antagonista — el bug real: el plazo PROPIO de la transacción debe usarse
    public void T4_ConPlazoPropio_CalculaExpiresOnConEsePlazo_NoConElDefault()
    {
        var txId = SembrarBorradorListo(RoutingType.Paralelo, (_firmante1, null));
        var conPlazo = new Entity(SchemaNames.Tx.Entidad, txId);
        conPlazo[SchemaNames.Tx.ExpirationDays] = 3; // el creador eligió 3 días (≠ default 15)
        _arnes.Servicio.Update(conPlazo);

        Ejecutar(txId, _creador);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        var senton = tx.GetAttributeValue<DateTime>(SchemaNames.Tx.SentOn);
        Assert.Equal(senton.AddDays(3), tx.GetAttributeValue<DateTime>(SchemaNames.Tx.ExpiresOn));
    }

    [Fact] // doc 04 §5 / S4 — el lock precede a todo, también en Send
    public void ElLock_EsLaPrimeraOperacion()
    {
        var txId = SembrarBorradorListo(RoutingType.Paralelo, (_firmante1, null));
        _arnes.Servicio.Operaciones.Clear();

        Ejecutar(txId, _creador);

        var primera = _arnes.Servicio.Operaciones.First();
        Assert.Equal("Update", primera.Tipo);
        Assert.Equal(SchemaNames.Tx.LockToken, Assert.Single(primera.Datos!.Attributes).Key);
    }

    [Fact] // P2 secuencial: SOLO el orden 1 se activa
    public void T4_Secuencial_ActivaSoloAlOrden1()
    {
        var txId = SembrarBorradorListo(RoutingType.Secuencial, (_firmante1, 1), (_firmante2, 2));

        Ejecutar(txId, _creador);

        var participantes = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad);
        var p1 = participantes.Single(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == _firmante1);
        var p2 = participantes.Single(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == _firmante2);
        Assert.Equal((int)ParticipantStatus.TurnoActivo, p1.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
        Assert.Equal((int)ParticipantStatus.Pendiente, p2.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
    }

    [Fact] // M13 — share: transacción a cada participante + eventos previos (cascada None)
    public void M13_CompartaLaTransaccion_YLosEventosPrevios_ConCadaParticipante()
    {
        var txId = SembrarBorradorListo(RoutingType.Paralelo, (_firmante1, null));
        var eventoPrevioId = _arnes.SembrarEvento(txId); // el evento 1 de la creación

        Ejecutar(txId, _creador);

        Assert.Contains((SchemaNames.Tx.Entidad, txId, _firmante1), _arnes.Servicio.Compartidos);
        Assert.Contains((SchemaNames.Evento.Entidad, eventoPrevioId, _firmante1), _arnes.Servicio.Compartidos);
        // el evento 2 nuevo también quedó compartido
        var evento2 = _arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad).Single(ev => ev.Id != eventoPrevioId);
        Assert.Contains((SchemaNames.Evento.Entidad, evento2.Id, _firmante1), _arnes.Servicio.Compartidos);
    }

    [Fact]
    public void SinDocumento_EsRechazado_SinTransicionar()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador, RoutingType.Paralelo);
        var pid = _arnes.SembrarParticipante(txId, _firmante1);
        _arnes.SembrarZona(pid, page: 1); // zona sí, PDF no

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));
        Assert.Contains("PDF", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

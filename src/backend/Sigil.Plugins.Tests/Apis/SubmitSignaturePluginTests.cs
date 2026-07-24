// sanic_sigil_capi_SubmitSignature — la API crítica en concurrencia (T5/T6/T7).
// Los asserts que el diseño EXIGE (M2/M3): idempotencia ANTES del guard de estado (doble
// click del último firmante con la tx ya en Sellando), decisión "último" post-lock,
// activación del siguiente turno (P2'), status jamás reescrito con valor idéntico,
// snapshot de la firma vigente + lookup a la versión exacta.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class SubmitSignaturePluginTests
{
    private static readonly byte[] PngDeFirma = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]; // bytes marcadores
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;
    private readonly Guid _firmante1;
    private readonly Guid _firmante2;

    public SubmitSignaturePluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        _firmante1 = _arnes.SembrarUsuario("Beto Uno", "beto@bac.test");
        _firmante2 = _arnes.SembrarUsuario("Caro Dos", "caro@bac.test");
    }

    private void Ejecutar(Guid txId, Guid llamante)
    {
        _arnes.Contexto.OutputParameters.Clear();
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        _arnes.Ejecutar(new SubmitSignaturePlugin(), SchemaNames.Apis.SubmitSignature, llamante);
    }

    private (Guid txId, Guid pid1, Guid pid2) SembrarEnviadaParalelo()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        var pid1 = _arnes.SembrarParticipante(txId, _firmante1, ParticipantStatus.TurnoActivo);
        var pid2 = _arnes.SembrarParticipante(txId, _firmante2, ParticipantStatus.TurnoActivo);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));
        _arnes.SembrarFirmaMaestra(_firmante1, PngDeFirma, version: 3);
        _arnes.SembrarFirmaMaestra(_firmante2, PngDeFirma);
        return (txId, pid1, pid2);
    }

    [Fact] // M1 — "participante ajeno firma"
    public void M1_UnNoParticipante_EsRechazado()
    {
        var (txId, _, _) = SembrarEnviadaParalelo();
        var ajeno = _arnes.SembrarUsuario("Ajeno", "x@bac.test");
        Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, ajeno));
    }

    [Fact] // M1 — Pendiente en secuencial no puede firmar (no le llegó el documento)
    public void M1_ParticipantePendiente_EnSecuencial_EsRechazado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Secuencial);
        _arnes.SembrarParticipante(txId, _firmante1, ParticipantStatus.TurnoActivo);
        _arnes.SembrarParticipante(txId, _firmante2, ParticipantStatus.Pendiente);
        _arnes.SembrarFirmaMaestra(_firmante2, PngDeFirma);

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _firmante2));
        Assert.Contains("turno", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // sin Firma Maestra vigente no se firma
    public void SinFirmaMaestraVigente_EsRechazado_ConMensajeAccionable()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        _arnes.SembrarParticipante(txId, _firmante1, ParticipantStatus.TurnoActivo);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));
        _arnes.SembrarFirmaMaestra(_firmante1, PngDeFirma, vigente: false); // solo una versión INACTIVA

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _firmante1));
        Assert.Contains("Firma Maestra", ex.Message);
    }

    [Fact] // P3 completo + T5: primera firma de dos → Firmado Parcialmente, no es último
    public void P3_PrimeraFirmaDeDos_RegistraTodo_YNoEsUltimo()
    {
        var pdf = ArnesDeApi.PdfDePrueba(1);
        var (txId, pid1, _) = SembrarEnviadaParalelo();
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, pdf);

        Ejecutar(txId, _firmante1);

        Assert.Equal(false, _arnes.Contexto.OutputParameters["IsLastSigner"]);

        var p1 = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad).Single(p => p.Id == pid1);
        Assert.Equal((int)ParticipantStatus.Firmado, p1.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
        Assert.True(p1.Contains(SchemaNames.Participante.SignedOn));
        Assert.Equal("Beto Uno", p1.GetAttributeValue<string>(SchemaNames.Participante.SignerName));
        Assert.Equal("beto@bac.test", p1.GetAttributeValue<string>(SchemaNames.Participante.SignerEmail));
        // lookup a la versión EXACTA usada
        Assert.NotNull(p1.GetAttributeValue<EntityReference>(SchemaNames.Participante.MasterSignatureId));

        // snapshot de bytes congelado en el participante
        Assert.Equal(PngDeFirma, _arnes.Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Participante.Entidad, pid1), SchemaNames.Participante.SignatureSnapshot)]);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.FirmadoParcialmente, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);

        // evento 3 con documenthash del contenido servido
        var evento = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
        Assert.Equal((int)EventType.FirmaRegistrada, evento.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value);
        Assert.Equal(HashUtil.Sha256Hex(pdf), evento.GetAttributeValue<string>(SchemaNames.Evento.DocumentHash));
        Assert.Equal(pid1, evento.GetAttributeValue<EntityReference>(SchemaNames.Evento.ParticipantId).Id);

        // M13 — el evento nuevo quedó compartido con los participantes (cascada None)
        Assert.Contains((SchemaNames.Evento.Entidad, evento.Id, _firmante2), _arnes.Servicio.Compartidos);
        // el snapshot subió etiquetado como imagen, no como PDF
        Assert.Equal("image/png", _arnes.Archivos.MimeTypes[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Participante.Entidad, pid1), SchemaNames.Participante.SignatureSnapshot)]);
    }

    [Fact] // S4 — el lock precede a todo, también en Submit
    public void ElLock_EsLaPrimeraOperacion()
    {
        var (txId, _, _) = SembrarEnviadaParalelo();
        _arnes.Servicio.Operaciones.Clear();

        Ejecutar(txId, _firmante1);

        var primera = _arnes.Servicio.Operaciones.First();
        Assert.Equal("Update", primera.Tipo);
        Assert.Equal(SchemaNames.Tx.LockToken, Assert.Single(primera.Datos!.Attributes).Key);
    }

    [Fact] // T7: el último firmante → Sellando + evento 6 + IsLastSigner
    public void T7_ElUltimoFirmante_TransicionaASellando_ConEvento6()
    {
        var (txId, _, pid2) = SembrarEnviadaParalelo();
        // el primero ya firmó
        var yaFirmado = new Entity(SchemaNames.Participante.Entidad,
            _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad).Single(p => p.Id != pid2).Id);
        yaFirmado[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.Firmado);
        _arnes.Servicio.Update(yaFirmado);
        var txAFirmado = new Entity(SchemaNames.Tx.Entidad, txId);
        txAFirmado[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.FirmadoParcialmente);
        _arnes.Servicio.Update(txAFirmado);
        _arnes.Servicio.Operaciones.Clear();

        Ejecutar(txId, _firmante2);

        Assert.Equal(true, _arnes.Contexto.OutputParameters["IsLastSigner"]);
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Sellando, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        var tipos = _arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad)
            .Select(ev => ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value).ToList();
        Assert.Contains((int)EventType.FirmaRegistrada, tipos);
        Assert.Contains((int)EventType.SelladoIniciado, tipos);
    }

    [Fact] // M3 — LA precedencia: doble click del último firmante (tx ya en Sellando)
    public void M3_ReSubmitDelUltimoFirmante_ConTxEnSellando_EsExitoSinEfectos()
    {
        var (txId, _, pid2) = SembrarEnviadaParalelo();
        foreach (var p in _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad))
        {
            var upd = new Entity(SchemaNames.Participante.Entidad, p.Id);
            upd[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.Firmado);
            _arnes.Servicio.Update(upd);
        }
        var aSellando = new Entity(SchemaNames.Tx.Entidad, txId);
        aSellando[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Sellando);
        _arnes.Servicio.Update(aSellando);
        _arnes.Servicio.Operaciones.Clear();

        Ejecutar(txId, _firmante2); // NO lanza — idempotencia antes que guard de estado

        // Semántica de IsLastSigner: "la tx quedó sellando" — el retry devuelve lo MISMO
        // que el click original (decisión 2026-07-16, hallazgo S1 del antagonista).
        Assert.Equal(true, _arnes.Contexto.OutputParameters["IsLastSigner"]);
        // sin efectos: la única escritura es el lock (no-op técnico)
        var escrituras = _arnes.Servicio.Operaciones.Where(o => o.Tipo != "Read").ToList();
        var unica = Assert.Single(escrituras);
        Assert.Equal(SchemaNames.Tx.LockToken, Assert.Single(unica.Datos!.Attributes).Key);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
    }

    [Fact] // P2' — secuencial: al firmar el orden 1, el orden 2 se activa
    public void P2Prima_Secuencial_ActivaAlSiguiente()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Secuencial);
        var pid1 = _arnes.SembrarParticipante(txId, _firmante1, ParticipantStatus.TurnoActivo);
        var pid2 = _arnes.SembrarParticipante(txId, _firmante2, ParticipantStatus.Pendiente);
        foreach (var (pid, orden) in new[] { (pid1, 1), (pid2, 2) })
        {
            var upd = new Entity(SchemaNames.Participante.Entidad, pid);
            upd[SchemaNames.Participante.Order] = orden;
            _arnes.Servicio.Update(upd);
        }
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));
        _arnes.SembrarFirmaMaestra(_firmante1, PngDeFirma);

        Ejecutar(txId, _firmante1);

        var p2 = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad).Single(p => p.Id == pid2);
        Assert.Equal((int)ParticipantStatus.TurnoActivo, p2.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
        Assert.True(p2.Contains(SchemaNames.Participante.TurnActivatedOn));
    }

    [Fact] // segunda firma no-última con tx YA en FirmadoParcialmente: status NO se reescribe
    public void ConTxYaEnFirmadoParcialmente_UnaFirmaNoUltima_NoReescribeElStatus()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.FirmadoParcialmente, RoutingType.Paralelo);
        _arnes.SembrarParticipante(txId, _firmante1, ParticipantStatus.TurnoActivo);
        _arnes.SembrarParticipante(txId, _firmante2, ParticipantStatus.TurnoActivo);
        var tercero = _arnes.SembrarUsuario("Dani Tres", "dani@bac.test");
        _arnes.SembrarParticipante(txId, tercero, ParticipantStatus.Firmado);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));
        _arnes.SembrarFirmaMaestra(_firmante1, PngDeFirma);
        _arnes.Servicio.Operaciones.Clear();

        Ejecutar(txId, _firmante1);

        Assert.Equal(false, _arnes.Contexto.OutputParameters["IsLastSigner"]);
        // ninguna escritura sobre la transacción tocó el status (solo el lock técnico)
        var updatesDeTx = _arnes.Servicio.Operaciones
            .Where(o => o.Tipo == "Update" && o.Entidad == SchemaNames.Tx.Entidad).ToList();
        Assert.All(updatesDeTx, u => Assert.False(u.Datos!.Contains(SchemaNames.Tx.Status),
            "el status idéntico jamás se reescribe — dispararía los flows"));
    }
}

// sanic_sigil_capi_CreateTransaction — orquestación (doc 04 §3.1).
// El núcleo de las validaciones ya está cubierto en Core.Tests (M1/M7); acá se prueba
// lo que SOLO la cáscara hace: usuarios existentes/habilitados, escrituras con ownerid
// explícito, archivo al seam, evento de creación y el output del contrato.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class CreateTransactionPluginTests
{
    private readonly ArnesDeApi _arnes = new();

    private void Ejecutar(Guid llamante)
        => _arnes.Ejecutar(new CreateTransactionPlugin(), SchemaNames.Apis.CreateTransaction, llamante);

    [Fact]
    public void Feliz_CreaTransaccionParticipantesZonasArchivoYEvento_YDevuelveElId()
    {
        var creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        var firmante = _arnes.SembrarUsuario("Beto Firmante", "beto@bac.test");
        var pdf = ArnesDeApi.PdfDePrueba(2);

        _arnes.Contexto.InputParameters["Name"] = "Contrato 2026";
        _arnes.Contexto.InputParameters["RoutingType"] = "sequential";
        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(pdf);
        _arnes.Contexto.InputParameters["ParticipantsJson"] =
            $$"""[ {"userId":"{{creador}}","order":1}, {"userId":"{{firmante}}","order":2} ]""";
        _arnes.Contexto.InputParameters["ZonesJson"] =
            $$"""[ {"userId":"{{firmante}}","page":2,"x":60,"y":80,"w":25,"h":8} ]""";

        Ejecutar(creador);

        // Output del contrato
        var txId = Assert.IsType<Guid>(_arnes.Contexto.OutputParameters["TransactionId"]);

        // Transacción: Borrador, owner = llamante (jamás el sistema)
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal(txId, tx.Id);
        Assert.Equal((int)TransactionStatus.Borrador, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.Equal(creador, tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id);

        // Participantes: 2, Pendiente, ownerid explícito = creador (doc 03 §4)
        var participantes = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad);
        Assert.Equal(2, participantes.Count);
        Assert.All(participantes, p =>
        {
            Assert.Equal((int)ParticipantStatus.Pendiente, p.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value);
            Assert.Equal(creador, p.GetAttributeValue<EntityReference>(SchemaNames.Participante.OwnerId).Id);
        });

        // Zona anclada al participante correcto
        var zona = _arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad).Single();
        var participanteDeBeto = participantes.Single(p =>
            p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == firmante);
        Assert.Equal(participanteDeBeto.Id, zona.GetAttributeValue<EntityReference>(SchemaNames.Zona.ParticipantId).Id);

        // Archivo subido al seam con los bytes exactos
        Assert.Equal(pdf, _arnes.Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Tx.Entidad, txId), SchemaNames.Tx.ContentFile)]);

        // Evento de creación con snapshot del actor
        var evento = _arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad).Single();
        Assert.Equal((int)EventType.TransaccionCreada, evento.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value);
        Assert.Equal("Ana Creadora", evento.GetAttributeValue<string>(SchemaNames.Evento.ActorName));
    }

    [Fact] // quirk de Custom API: un Integer opcional ausente llega como 0 → se trata como "no provisto"
    public void ExpirationDays_EnCero_SeTrataComoAusente_NoComoError()
    {
        var creador = _arnes.SembrarUsuario("Ana", "ana@bac.test");
        _arnes.Contexto.InputParameters["Name"] = "Doc sin plazo";
        _arnes.Contexto.InputParameters["RoutingType"] = "parallel";
        _arnes.Contexto.InputParameters["ExpirationDays"] = 0; // como lo materializa la plataforma
        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(ArnesDeApi.PdfDePrueba(1));
        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{creador}}"} ]""";

        Ejecutar(creador); // no lanza

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.False(tx.Contains(SchemaNames.Tx.ExpirationDays)); // no se persiste un plazo espurio
    }

    [Fact]
    public void M7_ConPdfSinMagicBytes_Rechaza_SinEscribirNada()
    {
        var creador = _arnes.SembrarUsuario("Ana", "ana@bac.test");
        _arnes.Contexto.InputParameters["Name"] = "Doc";
        _arnes.Contexto.InputParameters["RoutingType"] = "parallel";
        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6 });
        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{creador}}"} ]""";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(creador));

        Assert.Contains("PDF", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad));
        Assert.Empty(_arnes.Servicio.Operaciones); // ni una escritura antes de validar todo
        Assert.Empty(_arnes.Archivos.Subidas);
    }

    [Fact] // la validación que SOLO Dataverse puede responder (doc 04 §3.4)
    public void M7_ConUsuarioInexistente_Rechaza_ListandoAlAusente()
    {
        var creador = _arnes.SembrarUsuario("Ana", "ana@bac.test");
        var fantasma = Guid.NewGuid();

        _arnes.Contexto.InputParameters["Name"] = "Doc";
        _arnes.Contexto.InputParameters["RoutingType"] = "parallel";
        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(ArnesDeApi.PdfDePrueba(1));
        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{fantasma}}"} ]""";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(creador));

        Assert.Contains(fantasma.ToString(), ex.Message);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad));
    }

    [Fact]
    public void M7_ConUsuarioDeshabilitado_Rechaza_ConSuNombre()
    {
        var creador = _arnes.SembrarUsuario("Ana", "ana@bac.test");
        var inactivo = _arnes.SembrarUsuario("Carlos Inactivo", "carlos@bac.test", deshabilitado: true);

        _arnes.Contexto.InputParameters["Name"] = "Doc";
        _arnes.Contexto.InputParameters["RoutingType"] = "parallel";
        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(ArnesDeApi.PdfDePrueba(1));
        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{inactivo}}"} ]""";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(creador));

        Assert.Contains("Carlos Inactivo", ex.Message);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad));
    }

    [Fact] // los errores se juntan y se reportan TODOS (doc 04 §8: mensajes accionables)
    public void M7_ConVariosErrores_LosReportaJuntos()
    {
        var creador = _arnes.SembrarUsuario("Ana", "ana@bac.test");
        _arnes.Contexto.InputParameters["Name"] = ""; // sin título
        _arnes.Contexto.InputParameters["RoutingType"] = "ambos"; // token inválido
        _arnes.Contexto.InputParameters["PdfBase64"] = "no-base64!!"; // decodificación
        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{creador}}"} ]""";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(creador));

        Assert.Contains("título", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RoutingType", ex.Message);
        Assert.Contains("base64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

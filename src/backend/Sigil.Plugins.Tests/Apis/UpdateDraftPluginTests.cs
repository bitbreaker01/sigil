// sanic_sigil_capi_UpdateDraft — orquestación (doc 04 §3.1/§3.3/§5).
// Acá viven los asserts de la MECÁNICA: el lock va primero, la revalidación de zonas
// al reemplazar el PDF no borra nada en silencio, y el reemplazo de participantes.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class UpdateDraftPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;

    public UpdateDraftPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
    }

    private void Ejecutar(Guid txId, Guid llamante)
    {
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        _arnes.Ejecutar(new UpdateDraftPlugin(), SchemaNames.Apis.UpdateDraft, llamante);
    }

    [Fact] // M1 — fila "UpdateDraft: el llamante es el creador"
    public void M1_UnNoCreador_EsRechazado()
    {
        var intruso = _arnes.SembrarUsuario("Intruso", "x@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.Contexto.InputParameters["Name"] = "Cambiado";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, intruso));

        Assert.Contains("creador", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Documento de prueba",
            _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single().GetAttributeValue<string>(SchemaNames.Tx.Name));
    }

    [Fact] // M1 — "y el estado es Borrador"
    public void M1_SobreUnaTransaccionYaEnviada_EsRechazado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma);
        _arnes.Contexto.InputParameters["Name"] = "Cambiado";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));
        Assert.Contains("borrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // doc 04 §5: el lock de fila es la PRIMERA operación, sobre locktoken — jamás status
    public void ElLock_EsLaPrimeraOperacion_YUsaSoloLaColumnaTecnica()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.Contexto.InputParameters["Name"] = "Título nuevo";

        Ejecutar(txId, _creador);

        // El lock precede a TODA operación — el stub registra también las LECTURAS, así que
        // First() siendo el lock descarta que alguna lectura de estado no serializado ocurra
        // antes (la carrera que el lock previene, doc 04 §5).
        Assert.Contains(_arnes.Servicio.Operaciones, o => o.Tipo == "Read"); // hubo lecturas...
        var primera = _arnes.Servicio.Operaciones.First();                    // ...pero ninguna primero
        Assert.Equal("Update", primera.Tipo);
        Assert.Equal(SchemaNames.Tx.Entidad, primera.Entidad);
        Assert.Equal(txId, primera.Id);
        var atributo = Assert.Single(primera.Datos!.Attributes);
        Assert.Equal(SchemaNames.Tx.LockToken, atributo.Key);
    }

    [Fact]
    public void Feliz_CambiaSoloElTitulo()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.Contexto.InputParameters["Name"] = "Título nuevo";

        Ejecutar(txId, _creador);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal("Título nuevo", tx.GetAttributeValue<string>(SchemaNames.Tx.Name));
        // sin evento: UpdateDraft no transiciona estado (doc 04 §8)
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
    }

    [Fact] // doc 04 §3.1: PDF nuevo con zonas que quedan fuera → error EXPLÍCITO, jamás borrado silencioso
    public void ConPdfNuevo_YZonasQueQuedanFuera_Rechaza_ListandoLasZonas_YSinTocarNada()
    {
        var firmante = _arnes.SembrarUsuario("Beto", "beto@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        var participanteId = _arnes.SembrarParticipante(txId, firmante);
        _arnes.SembrarZona(participanteId, page: 3, nombre: "Firma de Beto");

        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(ArnesDeApi.PdfDePrueba(1));

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));

        Assert.Contains("Firma de Beto", ex.Message);
        Assert.Contains("página 3", ex.Message);
        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad)); // la zona sigue viva
        Assert.Empty(_arnes.Archivos.Subidas); // el PDF nuevo NO se subió
    }

    [Fact]
    public void ConPdfNuevo_YZonasVigentes_SubeElArchivo()
    {
        var firmante = _arnes.SembrarUsuario("Beto", "beto@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        var participanteId = _arnes.SembrarParticipante(txId, firmante);
        _arnes.SembrarZona(participanteId, page: 1);

        var pdfNuevo = ArnesDeApi.PdfDePrueba(2);
        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(pdfNuevo);

        Ejecutar(txId, _creador);

        Assert.Equal(pdfNuevo, _arnes.Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Tx.Entidad, txId), SchemaNames.Tx.ContentFile)]);
    }

    [Fact]
    public void ConParticipantsNuevos_ReemplazaLaListaCompleta()
    {
        var viejo = _arnes.SembrarUsuario("Viejo", "viejo@bac.test");
        var nuevo1 = _arnes.SembrarUsuario("Nuevo Uno", "n1@bac.test");
        var nuevo2 = _arnes.SembrarUsuario("Nuevo Dos", "n2@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.SembrarParticipante(txId, viejo);

        _arnes.Contexto.InputParameters["ParticipantsJson"] =
            $$"""[ {"userId":"{{nuevo1}}"}, {"userId":"{{nuevo2}}"} ]""";

        Ejecutar(txId, _creador);

        var participantes = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad);
        Assert.Equal(2, participantes.Count);
        Assert.DoesNotContain(participantes, p =>
            p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == viejo);
    }

    [Fact] // A2 — reemplazar participantes sin reenviar zonas borraría zonas en silencio: se rechaza
    public void ConParticipantsNuevos_SinZonesJson_YConZonasPersistidas_EsRechazado()
    {
        var firmante = _arnes.SembrarUsuario("Beto", "beto@bac.test");
        var otro = _arnes.SembrarUsuario("Caro", "caro@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        var participanteId = _arnes.SembrarParticipante(txId, firmante);
        _arnes.SembrarZona(participanteId, page: 1, nombre: "Firma de Beto");

        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{otro}}"} ]""";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));

        Assert.Contains("ZonesJson", ex.Message);
        // nada se tocó: la zona y el participante originales siguen vivos
        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad));
        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad));
    }

    [Fact] // reemplazar participantes CON ZonesJson explícito (aunque sea "[]") sí procede
    public void ConParticipantsNuevos_YZonesJsonVacio_Procede_YBorraLasZonasViejas()
    {
        var firmante = _arnes.SembrarUsuario("Beto", "beto@bac.test");
        var otro = _arnes.SembrarUsuario("Caro", "caro@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        var participanteId = _arnes.SembrarParticipante(txId, firmante);
        _arnes.SembrarZona(participanteId, page: 1);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1));

        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{otro}}"} ]""";
        _arnes.Contexto.InputParameters["ZonesJson"] = "[]";

        Ejecutar(txId, _creador);

        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad));
        var participante = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad));
        Assert.Equal(otro, participante.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id);
    }

    [Fact] // A7(b) — el camino más propenso a regresión: participantes Y zonas nuevas juntas
    public void ConParticipantsYZonesNuevos_AnclaLasZonasALosParticipantesRecienCreados()
    {
        var nuevo = _arnes.SembrarUsuario("Nuevo", "nuevo@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(2));

        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{nuevo}}"} ]""";
        _arnes.Contexto.InputParameters["ZonesJson"] =
            $$"""[ {"userId":"{{nuevo}}","page":2,"x":40,"y":40,"w":20,"h":8} ]""";

        Ejecutar(txId, _creador);

        var participante = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad));
        var zona = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad));
        Assert.Equal(participante.Id, zona.GetAttributeValue<EntityReference>(SchemaNames.Zona.ParticipantId).Id);
    }

    [Fact] // A7(a) — PDF nuevo + zonas nuevas: el pageCount sale del PDF NUEVO, no del persistido
    public void ConPdfNuevoYZonesNuevos_ValidaContraElPdfNuevo()
    {
        var firmante = _arnes.SembrarUsuario("Beto", "beto@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.SembrarParticipante(txId, firmante);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(1)); // persistido: 1 página

        _arnes.Contexto.InputParameters["PdfBase64"] = Convert.ToBase64String(ArnesDeApi.PdfDePrueba(3));
        _arnes.Contexto.InputParameters["ZonesJson"] =
            $$"""[ {"userId":"{{firmante}}","page":3,"x":40,"y":40,"w":20,"h":8} ]"""; // pág 3: válida solo en el nuevo

        Ejecutar(txId, _creador);

        Assert.Equal(3, Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad))
            .GetAttributeValue<int>(SchemaNames.Zona.Page));
    }

    [Fact] // M7 en el plumbing de UpdateDraft (distinto al de Create): usuario nuevo inexistente
    public void ConParticipantsNuevos_ConUsuarioInexistente_EsRechazado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        _arnes.Contexto.InputParameters["ParticipantsJson"] = $$"""[ {"userId":"{{Guid.NewGuid()}}"} ]""";
        _arnes.Contexto.InputParameters["ZonesJson"] = "[]";

        Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));
    }

    [Fact] // cambiar el routing sin reenviar participantes es ambiguo — se rechaza (decisión doc 04 §3.4)
    public void CambiarElRouting_SinReenviarParticipantes_EsRechazado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador, RoutingType.Paralelo);
        _arnes.Contexto.InputParameters["RoutingType"] = "sequential";

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Ejecutar(txId, _creador));
        Assert.Contains("ParticipantsJson", ex.Message);
    }

    [Fact] // zonas nuevas sin tocar PDF ni participantes: valida contra el PDF persistido
    public void ConZonasNuevas_ValidaContraElPdfPersistido_YReemplazaLasAnteriores()
    {
        var firmante = _arnes.SembrarUsuario("Beto", "beto@bac.test");
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Borrador);
        var participanteId = _arnes.SembrarParticipante(txId, firmante);
        var zonaViejaId = _arnes.SembrarZona(participanteId, page: 1, nombre: "Zona vieja");
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, ArnesDeApi.PdfDePrueba(2));

        _arnes.Contexto.InputParameters["ZonesJson"] =
            $$"""[ {"userId":"{{firmante}}","page":2,"x":50,"y":50,"w":20,"h":8} ]""";

        Ejecutar(txId, _creador);

        var zonas = _arnes.Servicio.FilasDe(SchemaNames.Zona.Entidad);
        var zona = Assert.Single(zonas);
        Assert.NotEqual(zonaViejaId, zona.Id);
        Assert.Equal(2, zona.GetAttributeValue<int>(SchemaNames.Zona.Page));
    }
}

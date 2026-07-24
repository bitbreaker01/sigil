// SearchDocuments (escala): base creados ∪ participados, filtros/orden/paginación
// server-side, y enriquecimiento (nombres + versión de mi firma). Verificación por el JSON de salida.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class SearchDocumentsPluginTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _yo;
    private readonly Guid _otro;

    public SearchDocumentsPluginTests()
    {
        _yo = _arnes.SembrarUsuario("Randy Kauffman", "randy@bac.test");
        _otro = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Guid SembrarTx(Guid creador, string nombre,
        TransactionStatus estado = TransactionStatus.PendienteDeFirma, DateTime? createdOn = null, DateTime? sentOn = null)
    {
        var tx = new Entity(SchemaNames.Tx.Entidad);
        tx[SchemaNames.Tx.Name] = nombre;
        tx[SchemaNames.Tx.Status] = new OptionSetValue((int)estado);
        tx[SchemaNames.Tx.RoutingType] = new OptionSetValue((int)RoutingType.Paralelo);
        tx[SchemaNames.Tx.OwnerId] = new EntityReference(SchemaNames.Usuario.Entidad, creador);
        if (createdOn is { } c) tx["createdon"] = c;
        if (sentOn is { } s) tx[SchemaNames.Tx.SentOn] = s;
        return _arnes.Servicio.Sembrar(tx).Id;
    }

    private Guid SembrarParticipante(Guid txId, Guid userId, Guid? firmaId = null,
        ParticipantStatus estado = ParticipantStatus.Firmado)
    {
        var p = new Entity(SchemaNames.Participante.Entidad);
        p[SchemaNames.Participante.Name] = $"P {userId}";
        p[SchemaNames.Participante.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        p[SchemaNames.Participante.UserId] = new EntityReference(SchemaNames.Usuario.Entidad, userId);
        p[SchemaNames.Participante.Status] = new OptionSetValue((int)estado);
        if (firmaId is { } f)
            p[SchemaNames.Participante.MasterSignatureId] = new EntityReference(SchemaNames.FirmaMaestra.Entidad, f);
        return _arnes.Servicio.Sembrar(p).Id;
    }

    private (List<Row> Rows, int Total, string Next) Buscar(
        string? text = null, Guid? creatorId = null, int? status = null, IReadOnlyCollection<Guid>? participantIds = null,
        int? signatureVersion = null, string? sort = null, int? pageSize = null, string? cookie = null)
    {
        var ip = _arnes.Contexto.InputParameters;
        ip.Clear();
        if (text != null) ip["Text"] = text;
        if (creatorId is { } cr) ip["CreatorId"] = cr;
        if (status is { } st) ip["Status"] = st;
        if (participantIds is { Count: > 0 }) ip["ParticipantIds"] = string.Join(",", participantIds);
        if (signatureVersion is { } sv) ip["SignatureVersion"] = sv;
        if (sort != null) ip["Sort"] = sort;
        if (pageSize is { } ps) ip["PageSize"] = ps;
        if (cookie != null) ip["PagingCookie"] = cookie;

        _arnes.Ejecutar(new SearchDocumentsPlugin(), SchemaNames.Apis.SearchDocuments, _yo);

        var op = _arnes.Contexto.OutputParameters;
        var rows = JsonSerializer.Deserialize<List<Row>>((string)op["ResultsJson"], JsonOpts)!;
        return (rows, (int)op["Total"], (string)op["NextPagingCookie"]);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Base_UneCreadosYParticipados_SinDuplicar()
    {
        var creado = SembrarTx(_yo, "Creado por mí");
        var ajeno = SembrarTx(_otro, "De Ana");
        SembrarParticipante(ajeno, _yo);
        var ambos = SembrarTx(_yo, "Creado y firmo");
        SembrarParticipante(ambos, _yo);

        var (rows, total, _) = Buscar();

        Assert.Equal(3, total);
        Assert.Equal(new[] { ajeno, ambos, creado }.OrderBy(x => x),
            rows.Select(r => Guid.Parse(r.Id)).OrderBy(x => x));
    }

    [Fact]
    public void SinDocumentos_DevuelveVacio()
    {
        var (rows, total, next) = Buscar();
        Assert.Empty(rows);
        Assert.Equal(0, total);
        Assert.Equal(string.Empty, next);
    }

    [Fact]
    public void FiltraPorTexto_InsensibleAMayusculas()
    {
        SembrarTx(_yo, "Contrato ACME");
        SembrarTx(_yo, "NDA Falcon");

        var (rows, total, _) = Buscar(text: "acme");

        Assert.Equal(1, total);
        Assert.Equal("Contrato ACME", rows.Single().Name);
    }

    [Fact]
    public void FiltraPorCreador()
    {
        SembrarTx(_yo, "Mío");
        var deAna = SembrarTx(_otro, "De Ana");
        SembrarParticipante(deAna, _yo);

        var (rows, total, _) = Buscar(creatorId: _otro);

        Assert.Equal(1, total);
        Assert.Equal("De Ana", rows.Single().Name);
    }

    [Fact]
    public void FiltraPorEstado()
    {
        SembrarTx(_yo, "Completado", TransactionStatus.Completado);
        SembrarTx(_yo, "Pendiente", TransactionStatus.PendienteDeFirma);

        var (rows, total, _) = Buscar(status: (int)TransactionStatus.Completado);

        Assert.Equal(1, total);
        Assert.Equal("Completado", rows.Single().Name);
    }

    [Fact]
    public void FiltraPorParticipante()
    {
        var conAna = SembrarTx(_yo, "Con Ana");
        SembrarParticipante(conAna, _otro);
        SembrarTx(_yo, "Solo yo");

        var (rows, total, _) = Buscar(participantIds: new[] { _otro });

        Assert.Equal(1, total);
        Assert.Equal("Con Ana", rows.Single().Name);
    }

    [Fact]
    public void FiltraPorVariosParticipantes_ExigeTodos_AND()
    {
        var tercero = _arnes.SembrarUsuario("Caro Tercera", "caro@bac.test");
        var conAmbos = SembrarTx(_yo, "Con Ana y Caro");
        SembrarParticipante(conAmbos, _otro);
        SembrarParticipante(conAmbos, tercero);
        var soloAna = SembrarTx(_yo, "Solo Ana");
        SembrarParticipante(soloAna, _otro);

        var (rows, total, _) = Buscar(participantIds: new[] { _otro, tercero });

        Assert.Equal(1, total);
        Assert.Equal("Con Ana y Caro", rows.Single().Name);
    }

    [Fact]
    public void FiltraPorVersionDeFirma()
    {
        var v1 = _arnes.SembrarFirmaMaestra(_yo, new byte[] { 1 }, version: 1, vigente: false);
        var v2 = _arnes.SembrarFirmaMaestra(_yo, new byte[] { 2 }, version: 2, vigente: true);
        var conV1 = SembrarTx(_otro, "Firmado con v1");
        SembrarParticipante(conV1, _yo, firmaId: v1);
        var conV2 = SembrarTx(_otro, "Firmado con v2");
        SembrarParticipante(conV2, _yo, firmaId: v2);

        var (rows, total, _) = Buscar(signatureVersion: 1);

        Assert.Equal(1, total);
        Assert.Equal("Firmado con v1", rows.Single().Name);
        Assert.Equal(1, rows.Single().MySignatureVersion);
    }

    [Fact]
    public void Ordena_PorNombre()
    {
        SembrarTx(_yo, "Beta");
        SembrarTx(_yo, "Alfa");
        SembrarTx(_yo, "Gamma");

        var (rows, _, _) = Buscar(sort: "nameAsc");

        Assert.Equal(new[] { "Alfa", "Beta", "Gamma" }, rows.Select(r => r.Name));
    }

    [Fact]
    public void Pagina_EncadenaConElCookie()
    {
        SembrarTx(_yo, "A");
        SembrarTx(_yo, "B");
        SembrarTx(_yo, "C");

        var p1 = Buscar(sort: "nameAsc", pageSize: 2);
        Assert.Equal(2, p1.Rows.Count);
        Assert.Equal(3, p1.Total);
        Assert.Equal("2", p1.Next);
        Assert.Equal(new[] { "A", "B" }, p1.Rows.Select(r => r.Name));

        var p2 = Buscar(sort: "nameAsc", pageSize: 2, cookie: p1.Next);
        Assert.Single(p2.Rows);
        Assert.Equal(3, p2.Total);
        Assert.Equal(string.Empty, p2.Next);
        Assert.Equal("C", p2.Rows.Single().Name);
    }

    [Fact]
    public void Enriquece_NombreDeCreadorYParticipantes()
    {
        var tx = SembrarTx(_otro, "De Ana");
        SembrarParticipante(tx, _yo);
        SembrarParticipante(tx, _otro);

        var row = Buscar().Rows.Single();

        Assert.Equal("Ana Creadora", row.CreatorName);
        Assert.Contains(row.Participants, p => p.Name == "Randy Kauffman");
        Assert.Contains(row.Participants, p => p.Name == "Ana Creadora");
    }

    private sealed class Row
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int State { get; set; }
        public string CreatorName { get; set; } = string.Empty;
        public int? MySignatureVersion { get; set; }
        public List<Signer> Participants { get; set; } = new();
    }

    private sealed class Signer
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}

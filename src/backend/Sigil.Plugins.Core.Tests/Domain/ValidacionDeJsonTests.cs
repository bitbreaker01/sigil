// M7 — Validación de entrada: ParticipantsJson y ZonesJson.
// Reglas puras: duplicados, órdenes con huecos, zonas huérfanas/fuera de rango, schema.
// La parte que exige Dataverse (usuarios existentes y habilitados) vive en la cáscara.

using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ValidacionDeJsonTests
{
    private static readonly Guid U1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid U2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid U3 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private const int MaxParticipantes = 20; // default de sanic_sigil_env_MaxParticipants

    // ── ParticipantsJson ─────────────────────────────────────────────────────

    [Fact]
    public void M7_Participants_SecuencialValido_Parsea_YConservaElOrden()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}","order":1}, {"userId":"{{U2}}","order":2} ]""",
            RoutingType.Secuencial, MaxParticipantes);

        Assert.True(r.EsValido, string.Join("; ", r.Errores));
        Assert.Equal(2, r.Valor!.Count);
        Assert.Equal(U1, r.Valor[0].UserId);
        Assert.Equal(1, r.Valor[0].Order);
    }

    [Fact]
    public void M7_Participants_ParaleloValido_SinOrder_Parsea()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}"}, {"userId":"{{U2}}"} ]""",
            RoutingType.Paralelo, MaxParticipantes);

        Assert.True(r.EsValido, string.Join("; ", r.Errores));
        Assert.All(r.Valor!, p => Assert.Null(p.Order));
    }

    [Fact]
    public void M7_Participants_JsonMalformado_EsRechazado_ConErrorDeSchema()
    {
        var r = ValidacionDeEntrada.ValidarParticipants("esto no es json", RoutingType.Paralelo, MaxParticipantes);
        Assert.False(r.EsValido);
    }

    [Fact]
    public void M7_Participants_Duplicados_SonRechazados()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}","order":1}, {"userId":"{{U1}}","order":2} ]""",
            RoutingType.Secuencial, MaxParticipantes);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("duplicado", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // la fila explícita de M7: "órdenes con huecos"
    public void M7_Participants_OrdenConHuecos_EsRechazado()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}","order":1}, {"userId":"{{U2}}","order":3} ]""",
            RoutingType.Secuencial, MaxParticipantes);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("orden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M7_Participants_OrdenRepetido_EsRechazado()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}","order":1}, {"userId":"{{U2}}","order":1} ]""",
            RoutingType.Secuencial, MaxParticipantes);
        Assert.False(r.EsValido);
    }

    [Fact]
    public void M7_Participants_SecuencialSinOrder_EsRechazado()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}"} ]""", RoutingType.Secuencial, MaxParticipantes);
        Assert.False(r.EsValido);
    }

    [Fact] // schema: "order solo en secuencial" — en paralelo es un error de contrato
    public void M7_Participants_ParaleloConOrder_EsRechazado()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{U1}}","order":1} ]""", RoutingType.Paralelo, MaxParticipantes);
        Assert.False(r.EsValido);
    }

    [Fact]
    public void M7_Participants_ListaVacia_EsRechazada()
    {
        var r = ValidacionDeEntrada.ValidarParticipants("[]", RoutingType.Paralelo, MaxParticipantes);
        Assert.False(r.EsValido);
    }

    [Fact]
    public void M7_Participants_SobreElMaximo_EsRechazado_MencionandoElLimite()
    {
        var items = string.Join(",", Enumerable.Range(1, 3)
            .Select(i => $$"""{"userId":"aaaaaaaa-0000-0000-0000-00000000000{{i}}"}"""));
        var r = ValidacionDeEntrada.ValidarParticipants($"[{items}]", RoutingType.Paralelo, maxParticipantes: 2);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains("2", StringComparison.Ordinal));
    }

    [Fact]
    public void M7_Participants_GuidVacio_EsRechazado()
    {
        var r = ValidacionDeEntrada.ValidarParticipants(
            $$"""[ {"userId":"{{Guid.Empty}}"} ]""", RoutingType.Paralelo, MaxParticipantes);
        Assert.False(r.EsValido);
    }

    // ── ZonesJson ────────────────────────────────────────────────────────────

    private static readonly Guid[] Firmantes = [U1, U2];

    [Fact]
    public void M7_Zones_Validas_Parsean()
    {
        var r = ValidacionDeEntrada.ValidarZones(
            $$"""[ {"userId":"{{U1}}","page":3,"x":62.5,"y":81.0,"w":22.0,"h":8.0} ]""",
            Firmantes, pageCount: 3);

        Assert.True(r.EsValido, string.Join("; ", r.Errores));
        Assert.Equal(3, r.Valor![0].Page);
        Assert.Equal(62.5, r.Valor[0].X);
    }

    [Fact] // la fila explícita de M7: "zonas huérfanas" — userId que no es participante
    public void M7_Zones_DeUnUsuarioQueNoEsParticipante_SonRechazadas()
    {
        var r = ValidacionDeEntrada.ValidarZones(
            $$"""[ {"userId":"{{U3}}","page":1,"x":10,"y":10,"w":20,"h":8} ]""",
            Firmantes, pageCount: 3);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains(U3.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Theory] // página inexistente: error explícito, jamás borrado silencioso
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void M7_Zones_ConPaginaInexistente_SonRechazadas_ListandoLaPagina(int pagina)
    {
        var r = ValidacionDeEntrada.ValidarZones(
            $$"""[ {"userId":"{{U1}}","page":{{pagina}},"x":10,"y":10,"w":20,"h":8} ]""",
            Firmantes, pageCount: 3);

        Assert.False(r.EsValido);
        Assert.Contains(r.Errores, e => e.Contains(pagina.ToString(), StringComparison.Ordinal));
    }

    [Theory] // coordenadas 0–100; zona que desborda la página también
    [InlineData(-0.5, 10, 20, 8)]
    [InlineData(10, 101, 20, 8)]
    [InlineData(10, 10, -1, 8)]
    [InlineData(10, 10, 20, 200)]
    [InlineData(95, 10, 20, 8)]
    [InlineData(10, 95, 20, 8)]
    public void M7_Zones_ConCoordenadasFueraDeRango_SonRechazadas(double x, double y, double w, double h)
    {
        var r = ValidacionDeEntrada.ValidarZones(
            $$"""[ {"userId":"{{U1}}","page":1,"x":{{x}},"y":{{y}},"w":{{w}},"h":{{h}}} ]""",
            Firmantes, pageCount: 3);
        Assert.False(r.EsValido);
    }

    [Fact] // una zona sin área no es una zona
    public void M7_Zones_ConAnchoOAltoCero_SonRechazadas()
    {
        var r = ValidacionDeEntrada.ValidarZones(
            $$"""[ {"userId":"{{U1}}","page":1,"x":10,"y":10,"w":0,"h":8} ]""",
            Firmantes, pageCount: 3);
        Assert.False(r.EsValido);
    }

    [Fact]
    public void M7_Zones_JsonMalformado_EsRechazado()
    {
        var r = ValidacionDeEntrada.ValidarZones("{no}", Firmantes, pageCount: 3);
        Assert.False(r.EsValido);
    }

    [Fact] // ZonesJson es opcional en Create/UpdateDraft — lista vacía es válida en borrador
    public void M7_Zones_ListaVacia_EsValida_EnBorrador()
    {
        var r = ValidacionDeEntrada.ValidarZones("[]", Firmantes, pageCount: 3);
        Assert.True(r.EsValido);
        Assert.Empty(r.Valor!);
    }
}

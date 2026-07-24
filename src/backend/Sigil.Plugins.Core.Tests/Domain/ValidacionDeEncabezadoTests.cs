// M7 — validación del encabezado de Create/UpdateDraft: Name (Texto 200),
// ExpirationDays (entero positivo) y el token de RoutingType del contrato
// ("sequential" | "parallel" — mismo vocabulario que signersummary.routing).

using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ValidacionDeEncabezadoTests
{
    [Theory]
    [InlineData("sequential", RoutingType.Secuencial)]
    [InlineData("parallel", RoutingType.Paralelo)]
    [InlineData("SEQUENTIAL", RoutingType.Secuencial)] // el contrato no castiga mayúsculas
    [InlineData("Parallel", RoutingType.Paralelo)]
    public void M7_RoutingType_TokensDelContrato_Parsean(string token, RoutingType esperado)
    {
        Assert.True(ValidacionDeEntrada.TryParsearRoutingType(token, out var routing));
        Assert.Equal(esperado, routing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("secuencial")] // el contrato es en inglés — el espejo del frontend también
    [InlineData("both")]
    public void M7_RoutingType_TokenInvalido_EsRechazado(string? token)
    {
        Assert.False(ValidacionDeEntrada.TryParsearRoutingType(token, out _));
    }

    [Fact]
    public void M7_Encabezado_NombreValido_Pasa()
    {
        Assert.Empty(ValidacionDeEntrada.ValidarEncabezado("Contrato de servicios 2026", expirationDays: 15));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void M7_Encabezado_SinNombre_EsRechazado_CuandoElNombreEsObligatorio(string? nombre)
    {
        Assert.NotEmpty(ValidacionDeEntrada.ValidarEncabezado(nombre, expirationDays: null));
    }

    [Fact] // en UpdateDraft null = "sin cambio": no se rechaza
    public void M7_Encabezado_NombreNull_EsValido_CuandoElNombreEsOpcional()
    {
        Assert.Empty(ValidacionDeEntrada.ValidarEncabezado(null, expirationDays: null, message: null, nombreObligatorio: false));
    }

    [Fact] // Texto 200 — el límite del schema se valida antes del Create
    public void M7_Encabezado_NombreSobre200Chars_EsRechazado()
    {
        Assert.NotEmpty(ValidacionDeEntrada.ValidarEncabezado(new string('x', 201), expirationDays: null));
    }

    [Fact]
    public void M7_Encabezado_NombreDe200Exactos_Pasa()
    {
        Assert.Empty(ValidacionDeEntrada.ValidarEncabezado(new string('x', 200), expirationDays: null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void M7_Encabezado_ExpirationDaysNoPositivo_EsRechazado(int dias)
    {
        Assert.NotEmpty(ValidacionDeEntrada.ValidarEncabezado("Doc", expirationDays: dias));
    }

    [Fact] // null es válido: aplica sanic_sigil_env_ExpirationDefaultDays al enviar
    public void M7_Encabezado_ExpirationDaysNull_EsValido()
    {
        Assert.Empty(ValidacionDeEntrada.ValidarEncabezado("Doc", expirationDays: null));
    }

    [Fact] // A6 — Message es Texto multilínea 2.000
    public void M7_Encabezado_MessageSobre2000Chars_EsRechazado()
    {
        Assert.NotEmpty(ValidacionDeEntrada.ValidarEncabezado("Doc", expirationDays: null, message: new string('m', 2001)));
    }

    [Fact]
    public void M7_Encabezado_MessageDe2000Exactos_Pasa()
    {
        Assert.Empty(ValidacionDeEntrada.ValidarEncabezado("Doc", expirationDays: null, message: new string('m', 2000)));
    }
}

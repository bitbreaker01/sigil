namespace Sigil.Plugins.Tests;

/// <summary>
/// Canario del harness net462 (corre en runner Windows).
/// Se reemplaza por los tests de orquestación (M1/M2...) cuando se decida
/// FakeXrmEasy comercial vs stub propio (F1).
/// </summary>
public class HarnessTests
{
    [Fact]
    public void ElHarnessNet462Ejecuta()
    {
        Assert.True(true, "El runner xUnit net462 funciona en este runner.");
    }
}

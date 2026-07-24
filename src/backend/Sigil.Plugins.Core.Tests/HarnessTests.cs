namespace Sigil.Plugins.Core.Tests;

/// <summary>
/// Canario del harness: prueba que el runner de tests del núcleo ejecuta
/// en esta plataforma. Se reemplaza por los primeros tests reales de Domain/ (M-suites)
/// en cuanto arranque el primer ciclo red-green-refactor de F1.
/// </summary>
public class HarnessTests
{
    [Fact]
    public void ElHarnessDelNucleoEjecuta()
    {
        Assert.True(true, "El runner xUnit del núcleo puro funciona en esta plataforma.");
    }
}

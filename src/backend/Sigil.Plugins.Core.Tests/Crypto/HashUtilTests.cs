// Crypto/ — SHA-256 hex (contenthash de T4 y documenthash de eventos de firma).
// Vector de prueba conocido (NIST): sha256("abc").

using System.Text;
using Sigil.Plugins.Core.Crypto;

namespace Sigil.Plugins.Core.Tests.Crypto;

public class HashUtilTests
{
    [Fact]
    public void Sha256Hex_VectorConocido_Coincide()
    {
        var hex = HashUtil.Sha256Hex(Encoding.ASCII.GetBytes("abc"));
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", hex);
    }

    [Fact] // formato del schema: Texto 64, sin separadores (doc 03 §4.1 contenthash)
    public void Sha256Hex_Emite64HexMayusculas()
    {
        var hex = HashUtil.Sha256Hex([1, 2, 3]);
        Assert.Equal(64, hex.Length);
        Assert.Matches("^[0-9A-F]{64}$", hex);
    }
}

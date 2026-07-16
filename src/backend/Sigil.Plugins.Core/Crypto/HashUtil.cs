// SHA-256 en hex mayúsculas sin separadores — el formato de sanic_sigil_contenthash
// (Texto 64, doc 03 §4.1) y de sanic_sigil_documenthash de los eventos (doc 03 §4.6).

using System.Security.Cryptography;
using System.Text;

namespace Sigil.Plugins.Core.Crypto;

public static class HashUtil
{
    public static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(bytes);
        var sb = new StringBuilder(digest.Length * 2);
        foreach (var b in digest)
            sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}

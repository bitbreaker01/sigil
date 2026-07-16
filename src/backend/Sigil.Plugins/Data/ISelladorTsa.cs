// Seam del sello TSA (doc 11 §3): los tests inyectan un doble (respuestas buenas/malas
// sin red); la implementación real delega en el ClienteTsa del núcleo (validado en el
// sandbox por el spike). Se resuelve vía IServiceProvider, igual que IFileTransfer.

using Sigil.Plugins.Core.Crypto;

namespace Sigil.Plugins.Data;

public interface ISelladorTsa
{
    ResultadoTsa Sellar(byte[] sha256Digest, TsaConfig config);
}

public sealed class SelladorTsaReal : ISelladorTsa
{
    public ResultadoTsa Sellar(byte[] sha256Digest, TsaConfig config)
        => new ClienteTsa().SelloPara(sha256Digest, config);
}

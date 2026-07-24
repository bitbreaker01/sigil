// M6 — Cliente TSA: CertReq=true, nonce aleatorio verificado,
// DOBLE validación, fallback en orden, rate limit, rechazo de http://.
// Sin servidor ni puertos: HttpMessageHandler stub + respuestas RFC 3161
// FABRICADAS con TimeStampResponseGenerator y un certificado TSA self-signed.

using System.Net;
using System.Net.Http;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using Sigil.Plugins.Core.Crypto;

namespace Sigil.Plugins.Core.Tests.Crypto;

public class ClienteTsaTests : IDisposable
{
    private static readonly byte[] Digest = System.Security.Cryptography.SHA256.HashData("doc"u8.ToArray());

    public ClienteTsaTests() => ClienteTsa.ResetearRateLimit();

    public void Dispose() => ClienteTsa.ResetearRateLimit();

    // ── config (rechazo de http:// es validación de configuración — testeable sin red) ──

    [Fact]
    public void M6_Config_RechazaEndpointsHttp()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TsaConfig.Parse(
            """{ "endpoints": [ { "url": "http://timestamp.inseguro.com", "timeoutSeconds": 10 } ] }"""));
        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public void M6_Config_ParseaElJsonCanonicoDelDoc04()
    {
        var c = TsaConfig.Parse("""
            { "endpoints": [
                { "url": "https://timestamp.digicert.com", "timeoutSeconds": 10, "minIntervalSeconds": 0 },
                { "url": "https://timestamp.sectigo.com",  "timeoutSeconds": 10, "minIntervalSeconds": 15 } ] }
            """);
        Assert.Equal(2, c.Endpoints.Count);
        Assert.Equal(15, c.Endpoints[1].MinIntervalSeconds);
    }

    [Fact]
    public void M6_Config_SinEndpoints_Lanza()
    {
        Assert.Throws<InvalidOperationException>(() => TsaConfig.Parse("""{ "endpoints": [] }"""));
    }

    // ── el camino feliz: token fabricado VÁLIDO con cert embebido ────────────

    [Fact]
    public void M6_TokenValido_SeAcepta_ConGenTimeYBytes()
    {
        var tsa = new TsaFalsa();
        var handler = new HandlerStub(req => tsa.Responder(req));

        var r = new ClienteTsa(handler).SelloPara(Digest, Config("https://tsa.uno"));

        Assert.True(r.Exitoso, string.Join("; ", r.Errores));
        Assert.NotEmpty(r.TokenDer!);
        Assert.NotNull(r.GenTimeUtc);
        Assert.Equal("https://tsa.uno", r.Endpoint);
    }

    [Fact] // el REQUEST cumple el contrato: CertReq=true y nonce presente
    public void M6_ElRequest_LlevaCertReqYNonce()
    {
        TimeStampRequest? capturado = null;
        var tsa = new TsaFalsa();
        var handler = new HandlerStub(req =>
        {
            capturado = new TimeStampRequest(req);
            return tsa.Responder(req);
        });

        new ClienteTsa(handler).SelloPara(Digest, Config("https://tsa.uno"));

        Assert.NotNull(capturado);
        Assert.True(capturado!.CertReq);
        Assert.NotNull(capturado.Nonce);
    }

    // ── las respuestas MALAS se descartan y se intenta el siguiente (fallback) ──

    [Fact] // nonce equivocado: la TSA responde a OTRO request → Validate(request) la descarta
    public void M6_NonceEquivocado_SeDescarta_YCaeAlSiguienteEndpoint()
    {
        var tsa = new TsaFalsa();
        var otroRequest = new TimeStampRequestGenerator().Generate(TspAlgorithms.Sha256, Digest, BigInteger.One);
        var handler = new HandlerStub(
            req => tsa.Responder(otroRequest.GetEncoded()), // tsa.uno responde con nonce ajeno
            req => tsa.Responder(req));                     // tsa.dos responde bien

        var r = new ClienteTsa(handler).SelloPara(Digest, Config("https://tsa.uno", "https://tsa.dos"));

        Assert.True(r.Exitoso, string.Join("; ", r.Errores));
        Assert.Equal("https://tsa.dos", r.Endpoint);
        Assert.Single(r.Errores); // el descarte del primero quedó registrado
    }

    [Fact] // sin certificado embebido (CertReq no honrado) → descarte con motivo claro
    public void M6_TokenSinCertificado_SeDescarta()
    {
        var tsa = new TsaFalsa(embederCertificado: false);
        var handler = new HandlerStub(req => tsa.Responder(req));

        var r = new ClienteTsa(handler).SelloPara(Digest, Config("https://tsa.uno"));

        Assert.False(r.Exitoso);
        Assert.Contains(r.Errores, e => e.Contains("certificado", StringComparison.OrdinalIgnoreCase) ||
                                        e.Contains("CertReq", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void M6_TodosLosEndpointsFallan_DevuelveLosErroresPorEndpoint()
    {
        var handler = new HandlerStub(
            _ => throw new HttpRequestException("caido"),
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var r = new ClienteTsa(handler).SelloPara(Digest, Config("https://tsa.uno", "https://tsa.dos"));

        Assert.False(r.Exitoso);
        Assert.Equal(2, r.Errores.Count);
    }

    [Fact] // rate limit por endpoint: la segunda llamada dentro del intervalo ESPERA el restante
    public void M6_RateLimit_LaSegundaLlamadaEspera()
    {
        var tsa = new TsaFalsa();
        var handler = new HandlerStub(req => tsa.Responder(req), req => tsa.Responder(req));
        var reloj = new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
        var esperas = new List<TimeSpan>();
        var cliente = new ClienteTsa(handler, () => reloj, esperas.Add);
        var config = TsaConfig.Parse(
            """{ "endpoints": [ { "url": "https://tsa.uno", "timeoutSeconds": 10, "minIntervalSeconds": 15 } ] }""");

        Assert.True(cliente.SelloPara(Digest, config).Exitoso);
        reloj = reloj.AddSeconds(5); // solo pasaron 5 s
        Assert.True(cliente.SelloPara(Digest, config).Exitoso);

        var espera = Assert.Single(esperas);
        Assert.Equal(10, espera.TotalSeconds, 1); // esperó los 10 s restantes
    }

    private static TsaConfig Config(params string[] urls) => new()
    {
        Endpoints = urls.Select(u => new TsaEndpointConfig { Url = u, TimeoutSeconds = 5 }).ToList(),
    };

    // ── TSA FALSA: certificado self-signed con EKU timestamping + generador RFC 3161 ──

    private sealed class TsaFalsa
    {
        private readonly AsymmetricCipherKeyPair _keys;
        private readonly X509Certificate _cert;
        private readonly bool _embederCertificado;
        private int _serial;

        public TsaFalsa(bool embederCertificado = true)
        {
            _embederCertificado = embederCertificado;
            var gen = new RsaKeyPairGenerator();
            gen.Init(new Org.BouncyCastle.Crypto.KeyGenerationParameters(new SecureRandom(), 2048));
            _keys = gen.GenerateKeyPair();

            var certGen = new X509V3CertificateGenerator();
            var nombre = new X509Name("CN=TSA Falsa de Sigil Tests");
            certGen.SetSerialNumber(BigInteger.ValueOf(1));
            certGen.SetIssuerDN(nombre);
            certGen.SetSubjectDN(nombre);
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
            certGen.SetPublicKey(_keys.Public);
            // EKU id-kp-timeStamping CRÍTICO — requisito de RFC 3161 para el cert de una TSA
            certGen.AddExtension(X509Extensions.ExtendedKeyUsage, critical: true,
                new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
            _cert = certGen.Generate(new Asn1SignatureFactory("SHA256WITHRSA", _keys.Private));
        }

        public HttpResponseMessage Responder(byte[] requestDer)
        {
            var request = new TimeStampRequest(requestDer);
            var tstGen = new TimeStampTokenGenerator(_keys.Private, _cert,
                "2.16.840.1.101.3.4.2.1" /* SHA-256 */, "1.3.6.1.4.1.601.10.3.1" /* policy de la TSA falsa */);
            if (_embederCertificado)
                tstGen.SetCertificates(CollectionUtilities.CreateStore(new[] { _cert }));

            var respGen = new TimeStampResponseGenerator(tstGen, TspAlgorithms.Allowed);
            var response = respGen.Generate(request, BigInteger.ValueOf(++_serial), DateTime.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(response.GetEncoded()),
            };
        }
    }

    /// <summary>Handler stub: una función por llamada, en orden (sin red).</summary>
    private sealed class HandlerStub(params Func<byte[], HttpResponseMessage>[] respuestas) : HttpMessageHandler
    {
        private int _llamada;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(ct);
            var f = respuestas[Math.Min(_llamada++, respuestas.Length - 1)];
            return f(body);
        }
    }
}

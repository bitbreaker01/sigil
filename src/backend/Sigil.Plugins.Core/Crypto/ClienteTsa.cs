// Cliente RFC 3161 (requisitos NO negociables):
//   - CertReq = true: el token DEBE traer el certificado del firmante de la TSA (sin él,
//     la validación independiente puede volverse imposible años después).
//   - Nonce aleatorio (RandomNumberGenerator).
//   - DOBLE validación antes de persistir: Response.Validate(request) (nonce/imprint/política)
//     Y Token.Validate(cert embebido) (validez criptográfica de la firma del token).
//     Límite honesto declarado: NO se valida la cadena hasta una raíz confiable.
//   - Fallback en el orden de la config; rate limit por endpoint.
// Portado del spike que corrió DENTRO del sandbox real (Sectigo 200 OK, token validado).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Tsp;

namespace Sigil.Plugins.Core.Crypto;

public sealed class ResultadoTsa
{
    public ResultadoTsa(byte[]? tokenDer, DateTime? genTimeUtc, string? endpoint, IReadOnlyList<string> errores)
    {
        TokenDer = tokenDer;
        GenTimeUtc = genTimeUtc;
        Endpoint = endpoint;
        Errores = errores;
    }

    public bool Exitoso => TokenDer is not null;
    public byte[]? TokenDer { get; }
    public DateTime? GenTimeUtc { get; }
    public string? Endpoint { get; }

    /// <summary>Un error por endpoint fallido — va al trace y al evento si todos fallan.</summary>
    public IReadOnlyList<string> Errores { get; }
}

public sealed class ClienteTsa
{
    // Rate limit por endpoint (proceso): última llamada por URL (Sectigo ≥15 s).
    private static readonly ConcurrentDictionary<string, DateTime> UltimaLlamada = new();

    private readonly HttpMessageHandler? _handler;
    private readonly Func<DateTime> _ahora;
    private readonly Action<TimeSpan> _esperar;

    public ClienteTsa(HttpMessageHandler? handler = null, Func<DateTime>? ahora = null, Action<TimeSpan>? esperar = null)
    {
        _handler = handler; // tests: stub de HttpMessageHandler (sin servidor ni puertos)
        _ahora = ahora ?? (() => DateTime.UtcNow);
        _esperar = esperar ?? (ts => Thread.Sleep(ts));
    }

    /// <summary>Sella el digest SHA-256 contra el primer endpoint que responda un token VÁLIDO.</summary>
    public ResultadoTsa SelloPara(byte[] sha256Digest, TsaConfig config)
    {
        var errores = new List<string>();
        foreach (var endpoint in config.Endpoints)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                RespetarIntervalo(endpoint);

                var reqGen = new TimeStampRequestGenerator();
                reqGen.SetCertReq(true); // NO negociable
                var nonce = NonceAleatorio();
                var request = reqGen.Generate(TspAlgorithms.Sha256, sha256Digest, nonce);

                byte[] respBytes;
                try
                {
                    respBytes = Enviar(endpoint, request.GetEncoded());
                }
                finally
                {
                    // El intento CUENTA para el rate limit aunque falle: las TSAs cuentan
                    // requests, no éxitos (Sectigo ≥15 s entre requests automatizados).
                    UltimaLlamada[endpoint.Url] = _ahora();
                }

                var response = new TimeStampResponse(respBytes);
                response.Validate(request); // 1ª validación: nonce/imprint/política — lanza si no coincide

                var token = response.TimeStampToken
                    ?? throw new InvalidOperationException($"PKIStatus={response.Status} sin token.");

                // 2ª validación: la firma del token, con el certificado EMBEBIDO (CertReq).
                var certs = token.GetCertificates().EnumerateMatches(token.SignerID).ToList();
                if (certs.Count == 0)
                    throw new InvalidOperationException("el token no trae el certificado del firmante (CertReq no honrado).");
                token.Validate(certs[0]);

                // GenTime viene de un GeneralizedTime ASN.1 (siempre UTC): si el Kind llega
                // Unspecified, ToUniversalTime() lo correría asumiendo hora local — fijarlo.
                var genTime = token.TimeStampInfo.GenTime;
                genTime = genTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(genTime, DateTimeKind.Utc)
                    : genTime.ToUniversalTime();
                return new ResultadoTsa(token.GetEncoded(), genTime, endpoint.Url, errores);
            }
            catch (Exception ex)
            {
                var raiz = ex;
                while (raiz.InnerException is not null) raiz = raiz.InnerException;
                errores.Add($"{endpoint.Url} ({sw.ElapsedMilliseconds} ms): {raiz.GetType().Name}: {raiz.Message}");
                // token inválido o endpoint caído → se descarta y se intenta el siguiente
            }
        }
        return new ResultadoTsa(null, null, null, errores);
    }

    private void RespetarIntervalo(TsaEndpointConfig endpoint)
    {
        if (endpoint.MinIntervalSeconds <= 0)
            return;
        if (!UltimaLlamada.TryGetValue(endpoint.Url, out var ultima))
            return;
        var siguiente = ultima.AddSeconds(endpoint.MinIntervalSeconds);
        var restante = siguiente - _ahora();
        if (restante > TimeSpan.Zero)
            _esperar(restante);
    }

    private byte[] Enviar(TsaEndpointConfig endpoint, byte[] requestDer)
    {
        using var http = _handler is null
            ? new HttpClient()
            : new HttpClient(_handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(endpoint.TimeoutSeconds);

        var content = new ByteArrayContent(requestDer);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        var resp = http.PostAsync(endpoint.Url, content).GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}.");
        return resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    private static BigInteger NonceAleatorio()
    {
        var bytes = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return new BigInteger(1, bytes);
    }

    /// <summary>Solo para tests: resetea el rate limit estático entre casos.</summary>
    public static void ResetearRateLimit() => UltimaLlamada.Clear();
}

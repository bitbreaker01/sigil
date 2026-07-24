// Configuración de endpoints TSA — el JSON de sanic_sigil_env_TsaEndpoints.
// Orden = prioridad. HTTPS OBLIGATORIO: un canal claro habilita MITM del token
// — el parse rechaza http:// aunque el sandbox lo permita.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sigil.Plugins.Core.Crypto;

public sealed class TsaEndpointConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Rate limit por endpoint (Sectigo documenta ≥15 s entre requests automatizados).</summary>
    [JsonPropertyName("minIntervalSeconds")]
    public int MinIntervalSeconds { get; set; }
}

public sealed class TsaConfig
{
    [JsonPropertyName("endpoints")]
    public List<TsaEndpointConfig> Endpoints { get; set; } = new();

    public static TsaConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<TsaConfig>(json)
                     ?? throw new InvalidOperationException("TsaEndpoints vacío.");
        if (config.Endpoints.Count == 0)
            throw new InvalidOperationException("TsaEndpoints sin endpoints configurados.");
        foreach (var e in config.Endpoints)
        {
            if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException(
                    $"Endpoint TSA inválido: '{e.Url}' — solo se aceptan URLs https://.");
            if (e.TimeoutSeconds < 1)
                throw new InvalidOperationException($"Endpoint TSA '{e.Url}': timeoutSeconds debe ser positivo.");
        }
        return config;
    }
}

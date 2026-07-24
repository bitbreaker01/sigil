// Contratos JSON de las Custom APIs — el schema canónico compartido con el
// frontend. Los nombres de propiedad JSON son camelCase por contrato.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sigil.Plugins.Core.Domain;

/// <summary>Elemento de ParticipantsJson (input de Create/UpdateDraft).</summary>
public sealed class ParticipantInput
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>Orden de firma — solo en enrutamiento secuencial (1..N); null en paralelo.</summary>
    [JsonPropertyName("order")]
    public int? Order { get; set; }
}

/// <summary>Elemento de ZonesJson (input; 1..N por participante — obligatorias al enviar).</summary>
public sealed class ZoneInput
{
    [JsonPropertyName("userId")]
    public Guid UserId { get; set; }

    /// <summary>Página del PDF de contenido (1..N).</summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    // Coordenadas en % del ancho/alto visible de la página, origen arriba-izquierda.
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("w")]
    public double W { get; set; }

    [JsonPropertyName("h")]
    public double H { get; set; }
}

/// <summary>PDF ya validado por <see cref="ValidacionDeEntrada.ValidarPdfBase64"/>.</summary>
public sealed class PdfValidado
{
    public PdfValidado(byte[] bytes, int pageCount)
    {
        Bytes = bytes;
        PageCount = pageCount;
    }

    public byte[] Bytes { get; }
    public int PageCount { get; }
}

/// <summary>
/// Resultado de una validación: o el valor parseado, o la lista COMPLETA de errores
/// accionables (jamás "inválido" a secas — mensajes accionables).
/// </summary>
public sealed class ResultadoDe<T> where T : class
{
    private ResultadoDe(T? valor, IReadOnlyList<string> errores)
    {
        Valor = valor;
        Errores = errores;
    }

    public T? Valor { get; }
    public IReadOnlyList<string> Errores { get; }
    public bool EsValido => Errores.Count == 0;

    public static ResultadoDe<T> Ok(T valor) => new(valor, Array.Empty<string>());

    public static ResultadoDe<T> Fallo(IReadOnlyList<string> errores) => new(null, errores);

    public static ResultadoDe<T> Fallo(string error) => new(null, new[] { error });
}

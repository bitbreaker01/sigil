// Configuración de la validación/normalización de la Firma Maestra — el JSON de
// sanic_sigil_env_SignatureImageSpec. Umbrales iniciales,
// calibrables por ambiente sin redeploy.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sigil.Plugins.Core.Imaging;

public sealed class SignatureSpec
{
    [JsonPropertyName("targetWidthPx")]
    public int TargetWidthPx { get; set; }

    [JsonPropertyName("targetHeightPx")]
    public int TargetHeightPx { get; set; }

    /// <summary>Peso máximo del PNG NORMALIZADO (se limita su peso).</summary>
    [JsonPropertyName("maxKB")]
    public int MaxKB { get; set; }

    /// <summary>Fracción mínima de píxeles totalmente transparentes (fondo transparente).</summary>
    [JsonPropertyName("minAlphaRatio")]
    public double MinAlphaRatio { get; set; }

    /// <summary>
    /// Contraste RMS mínimo — RMS del APARTAMIENTO de la tinta respecto del fondo blanco,
    /// sobre píxeles no transparentes (enmienda del 2026-07-16: el RMS global del
    /// histograma castiga a las firmas de trazo fino; ver MotorDeFirmaMaestra).
    /// </summary>
    [JsonPropertyName("minRmsContrast")]
    public double MinRmsContrast { get; set; }

    /// <summary>Varianza mínima del Laplaciano (nitidez) sobre luminancia 0–255.</summary>
    [JsonPropertyName("minLaplacianVar")]
    public double MinLaplacianVar { get; set; }

    public static SignatureSpec Parse(string json)
    {
        var spec = JsonSerializer.Deserialize<SignatureSpec>(json)
                   ?? throw new InvalidOperationException("SignatureImageSpec vacío.");
        if (spec.TargetWidthPx < 1 || spec.TargetHeightPx < 1 || spec.MaxKB < 1)
            throw new InvalidOperationException("SignatureImageSpec incompleto: dimensiones y maxKB deben ser positivos.");
        return spec;
    }
}

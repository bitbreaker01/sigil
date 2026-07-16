// Lectura de variables de entorno con caché POR EJECUCIÓN (doc 04 §8 — verificado:
// la plataforma no cachea RetrieveEnvironmentVariableValue). Variable faltante o
// mal formada → fallo RUIDOSO: una validación de tamaño con un default inventado
// es una validación de mentira.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;

namespace Sigil.Plugins.Data;

public sealed class EnvVars(IOrganizationService servicio)
{
    private readonly Dictionary<string, string?> _cache = new();

    /// <summary>Variable Decimal del schema leída como entero (MaxPdfSizeKB, MaxParticipants…).</summary>
    public int EnteroObligatorio(string schemaName)
    {
        var crudo = Leer(schemaName);
        if (string.IsNullOrWhiteSpace(crudo) ||
            !decimal.TryParse(crudo, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
        {
            throw new InvalidPluginExecutionException(
                $"La variable de entorno {schemaName} no está configurada o no es numérica — revisar el Runbook A (CF-A09).");
        }
        if (valor < 1)
        {
            throw new InvalidPluginExecutionException(
                $"La variable de entorno {schemaName} debe ser un entero positivo (valor actual: {crudo}).");
        }
        return (int)valor;
    }

    /// <summary>Variable Text/JSON obligatoria — falla ruidoso si no está configurada.</summary>
    public string TextoObligatorio(string schemaName)
    {
        var valor = Leer(schemaName);
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new InvalidPluginExecutionException(
                $"La variable de entorno {schemaName} no está configurada — revisar el Runbook A (CF-A09).");
        }
        return valor!;
    }

    private string? Leer(string schemaName)
    {
        if (_cache.TryGetValue(schemaName, out var cacheado))
            return cacheado;

        var respuesta = servicio.Execute(new OrganizationRequest("RetrieveEnvironmentVariableValue")
        {
            ["DefinitionSchemaName"] = schemaName,
        });
        var valor = respuesta.Results.Contains("Value") ? respuesta.Results["Value"] as string : null;
        _cache[schemaName] = valor;
        return valor;
    }
}

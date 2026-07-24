// Validación de entrada de las Custom APIs — funciones puras.
// Regla de oro: la longitud del string base64 se chequea ANTES de decodificar
// (decodificar basura gigante regala memoria del sandbox). Lo que exige Dataverse
// (usuarios existentes y habilitados) NO vive acá: es responsabilidad de la cáscara.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Sigil.Plugins.Core.Domain;

public static class ValidacionDeEntrada
{
    // ── Encabezado (Name / ExpirationDays / RoutingType) ─────────────────────

    /// <summary>Token del contrato ("sequential" | "parallel") → RoutingType. Case-insensitive.</summary>
    public static bool TryParsearRoutingType(string? token, out RoutingType routing)
    {
        switch (token?.ToLowerInvariant())
        {
            case "sequential":
                routing = RoutingType.Secuencial;
                return true;
            case "parallel":
                routing = RoutingType.Paralelo;
                return true;
            default:
                routing = default;
                return false;
        }
    }

    /// <summary>
    /// Name: ≤200; obligatorio en Create, opcional (null = "sin cambio") en UpdateDraft.
    /// ExpirationDays: null o positivo. Message: ≤2.000. Cada campo se valida
    /// SOLO si vino — un null nunca se rechaza cuando el campo es opcional.
    /// </summary>
    public static IReadOnlyList<string> ValidarEncabezado(
        string? name, int? expirationDays, string? message = null, bool nombreObligatorio = true)
    {
        var errores = new List<string>();

        if (name is null)
        {
            if (nombreObligatorio)
                errores.Add("El título de la solicitud es obligatorio.");
        }
        else if (string.IsNullOrWhiteSpace(name))
        {
            errores.Add("El título de la solicitud es obligatorio.");
        }
        else if (name.Length > 200)
        {
            errores.Add($"El título supera los 200 caracteres permitidos (tiene {name.Length}).");
        }

        if (expirationDays is <= 0)
            errores.Add("El plazo de expiración debe ser un número positivo de días.");

        if (message is { Length: > 2000 })
            errores.Add($"El mensaje supera los 2.000 caracteres permitidos (tiene {message.Length}).");

        return errores;
    }

    // ── ParticipantsJson ─────────────────────────────────────────────────────

    public static ResultadoDe<IReadOnlyList<ParticipantInput>> ValidarParticipants(
        string json, RoutingType routing, int maxParticipantes)
    {
        List<ParticipantInput>? lista;
        try
        {
            lista = JsonSerializer.Deserialize<List<ParticipantInput>>(json);
        }
        catch (JsonException)
        {
            return ResultadoDe<IReadOnlyList<ParticipantInput>>.Fallo(
                "ParticipantsJson no cumple el schema esperado: [ { \"userId\": \"<guid>\", \"order\": <n>? } ].");
        }

        var errores = new List<string>();
        if (lista is null || lista.Count == 0)
            return ResultadoDe<IReadOnlyList<ParticipantInput>>.Fallo(
                "La transacción necesita al menos un participante.");

        if (lista.Count > maxParticipantes)
            errores.Add($"El máximo de participantes es {maxParticipantes} (se recibieron {lista.Count}).");

        if (lista.Any(p => p.UserId == Guid.Empty))
            errores.Add("Todo participante debe traer un userId válido (GUID no vacío).");

        var duplicados = lista.GroupBy(p => p.UserId)
            .Where(g => g.Key != Guid.Empty && g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicados.Count > 0)
            errores.Add($"Hay participantes duplicados: {string.Join(", ", duplicados)}.");

        if (routing == RoutingType.Secuencial)
        {
            if (lista.Any(p => p.Order is null))
            {
                errores.Add("En enrutamiento secuencial todo participante debe traer su orden de firma.");
            }
            else
            {
                // 1..N estricto: sin repetidos y sin huecos (fila explícita de M7)
                var ordenes = lista.Select(p => p.Order!.Value).OrderBy(o => o).ToList();
                if (ordenes.Distinct().Count() != ordenes.Count)
                    errores.Add("Hay órdenes de firma repetidos.");
                else if (ordenes.First() != 1 || ordenes.Last() != ordenes.Count)
                    errores.Add($"El orden de firma debe ser consecutivo 1..{ordenes.Count}, sin huecos.");
            }
        }
        else if (lista.Any(p => p.Order is not null))
        {
            errores.Add("El orden de firma solo aplica al enrutamiento secuencial.");
        }

        return errores.Count > 0
            ? ResultadoDe<IReadOnlyList<ParticipantInput>>.Fallo(errores)
            : ResultadoDe<IReadOnlyList<ParticipantInput>>.Ok(lista);
    }

    // ── ZonesJson ────────────────────────────────────────────────────────────

    public static ResultadoDe<IReadOnlyList<ZoneInput>> ValidarZones(
        string json, IReadOnlyCollection<Guid> userIdsDeParticipantes, int pageCount)
    {
        List<ZoneInput>? lista;
        try
        {
            lista = JsonSerializer.Deserialize<List<ZoneInput>>(json);
        }
        catch (JsonException)
        {
            return ResultadoDe<IReadOnlyList<ZoneInput>>.Fallo(
                "ZonesJson no cumple el schema esperado: [ { \"userId\", \"page\", \"x\", \"y\", \"w\", \"h\" } ].");
        }

        // Lista vacía es válida en borrador — la COMPLETITUD (todo participante con ≥1 zona)
        // la exige SendTransaction, no Create/UpdateDraft.
        lista ??= new List<ZoneInput>();

        var errores = new List<string>();
        for (var i = 0; i < lista.Count; i++)
        {
            var z = lista[i];
            var zona = $"La zona #{i + 1}";

            if (z.UserId == Guid.Empty)
                errores.Add($"{zona} no trae userId.");
            else if (!userIdsDeParticipantes.Contains(z.UserId))
                errores.Add($"{zona} refiere al usuario {z.UserId}, que no es participante de la transacción.");

            if (z.Page < 1 || z.Page > pageCount)
                errores.Add($"{zona} apunta a la página {z.Page}, pero el documento tiene {pageCount} página(s).");

            if (z.X is < 0 or > 100 || z.Y is < 0 or > 100)
                errores.Add($"{zona} tiene posición fuera de rango (x/y deben estar entre 0 y 100%).");
            if (z.W <= 0 || z.W > 100 || z.H <= 0 || z.H > 100)
                errores.Add($"{zona} tiene tamaño inválido (w/h deben ser mayores a 0 y hasta 100%).");
            else if (z.X + z.W > 100 || z.Y + z.H > 100)
                errores.Add($"{zona} se sale de la página (posición + tamaño supera el 100%).");
        }

        return errores.Count > 0
            ? ResultadoDe<IReadOnlyList<ZoneInput>>.Fallo(errores)
            : ResultadoDe<IReadOnlyList<ZoneInput>>.Ok(lista);
    }

    // ── PdfBase64 ────────────────────────────────────────────────────────────

    public static ResultadoDe<PdfValidado> ValidarPdfBase64(string base64, int maxKb)
    {
        if (string.IsNullOrEmpty(base64))
            return ResultadoDe<PdfValidado>.Fallo("El documento PDF es obligatorio.");

        // 1. TAMAÑO sobre el string, ANTES de decodificar (orden mandatorio).
        //    maxKb KB de binario = ceil(maxKb*1024/3)*4 chars de base64.
        var limiteDeChars = (long)(Math.Ceiling(maxKb * 1024 / 3.0) * 4);
        if (base64.Length > limiteDeChars)
            return ResultadoDe<PdfValidado>.Fallo($"El PDF supera el tamaño máximo permitido de {maxKb} KB.");

        // 2. Decodificación.
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return ResultadoDe<PdfValidado>.Fallo("El contenido recibido no es base64 válido.");
        }

        // 3. Magic bytes %PDF- en el offset 0.
        if (bytes.Length < 5 || bytes[0] != (byte)'%' || bytes[1] != (byte)'P' ||
            bytes[2] != (byte)'D' || bytes[3] != (byte)'F' || bytes[4] != (byte)'-')
            return ResultadoDe<PdfValidado>.Fallo("El archivo no es un PDF (falta la cabecera %PDF-).");

        // 4. Apertura real con PDFsharp: cifrado → rechazo; firmas previas → rechazo.
        try
        {
            using var ms = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            PdfPasswordProvider alDetectarClave = args => throw new PdfProtegidoMarker();
            using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import, alDetectarClave, options: null);

            var motivoFirma = DetectarFirmaDigital(doc);
            if (motivoFirma is not null)
                return ResultadoDe<PdfValidado>.Fallo(motivoFirma);

            return ResultadoDe<PdfValidado>.Ok(new PdfValidado(bytes, doc.PageCount));
        }
        catch (PdfProtegidoMarker)
        {
            return ResultadoDe<PdfValidado>.Fallo(
                "El PDF está protegido con contraseña o cifrado — Sigil no acepta documentos protegidos.");
        }
        catch (PdfReaderException ex) when (ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ResultadoDe<PdfValidado>.Fallo(
                "El PDF está protegido con contraseña o cifrado — Sigil no acepta documentos protegidos.");
        }
        catch (Exception)
        {
            return ResultadoDe<PdfValidado>.Fallo("El PDF está dañado o no se pudo abrir.");
        }
    }

    /// <summary>
    /// Conteo de páginas de un PDF ya persistido (UpdateDraft revalida zonas contra él).
    /// Lanza si los bytes no abren como PDF — un contentfile ilegible es un error grave, no un "0".
    /// </summary>
    public static int ContarPaginas(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf, 0, pdf.Length, writable: false, publiclyVisible: true);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    /// <summary>
    /// Detección de firmas digitales previas (PDF 32000-1 §12.7.4.5): SigFlags con el bit
    /// SignaturesExist, o cualquier campo AcroForm /FT /Sig con valor /V. Incrustar imágenes
    /// sobre un documento ya firmado invalidaría esas firmas — rechazo con mensaje claro.
    /// </summary>
    private static string? DetectarFirmaDigital(PdfDocument doc)
    {
        var acroForm = ComoDiccionario(doc.Internals.Catalog.Elements["/AcroForm"]);
        if (acroForm is null)
            return null;

        const string motivo =
            "El PDF ya contiene firmas digitales — Sigil no acepta documentos previamente firmados " +
            "(incrustar las firmas de Sigil las invalidaría).";

        if ((acroForm.Elements.GetInteger("/SigFlags") & 1) == 1) // bit 1 = SignaturesExist
            return motivo;

        var campos = acroForm.Elements.GetArray("/Fields");
        return TieneCampoDeFirmaConValor(campos) ? motivo : null;
    }

    private static bool TieneCampoDeFirmaConValor(PdfArray? campos)
    {
        if (campos is null)
            return false;

        foreach (var item in campos.Elements)
        {
            var campo = ComoDiccionario(item);
            if (campo is null) continue;

            if (campo.Elements.GetName("/FT") == "/Sig" && campo.Elements.ContainsKey("/V"))
                return true;

            if (TieneCampoDeFirmaConValor(campo.Elements.GetArray("/Kids")))
                return true;
        }
        return false;
    }

    private static PdfDictionary? ComoDiccionario(PdfItem? item) => item switch
    {
        PdfDictionary d => d,
        PdfReference r => r.Value as PdfDictionary,
        _ => null,
    };

    private sealed class PdfProtegidoMarker : Exception;
}

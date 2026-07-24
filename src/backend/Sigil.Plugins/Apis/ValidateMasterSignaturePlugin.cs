// sanic_sigil_capi_ValidateMasterSignature — Unbound.
// Valida (cómputo local) y normaliza la Firma Maestra. Con Persist=true crea una NUEVA versión
// vigente y desactiva la anterior EN LA MISMA operación (versionado — el historial
// jamás se pisa). SOLO opera sobre la firma del propio llamante (jamás acepta un
// userId como parámetro).
// In: ImageBase64, Persist? (default false → SOLO valida/preview; el frontend muestra el preview
//     y CONFIRMA antes de reemplazar la firma vigente — el reemplazo es irreversible). Out: IsValid,
//     FailureReasons?, MetricsJson, NormalizedImageBase64? (siempre que sea válida, para el preview).
// Una imagen que NO pasa los umbrales es un VEREDICTO (IsValid=false + motivos), no una
// excepción; los errores de contrato (base64 roto, tamaño) sí son excepciones.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Core.Imaging;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class ValidateMasterSignaturePlugin : SigilApiPlugin
{
    // Límite de CARGA (longitud antes de decodificar): la spec limita el peso
    // del NORMALIZADO (maxKB); la carga cruda admite hasta 10× eso — una firma escaneada
    // legítima puede pesar más que su versión normalizada. Decisión 2026-07-16.
    private const int FactorDeCargaSobreMaxKB = 10;

    protected override void Ejecutar(EntornoDeApi e)
    {
        var imageBase64 = e.Input<string>("ImageBase64");
        if (string.IsNullOrEmpty(imageBase64))
            throw new InvalidPluginExecutionException("ImageBase64 es obligatorio.");

        var env = new EnvVars(e.Servicio);
        var spec = SignatureSpec.Parse(env.TextoObligatorio(SchemaNames.EnvVars.SignatureImageSpec));

        // 1. TAMAÑO sobre el string, ANTES de decodificar (mismo orden mandatorio del PDF).
        var limiteDeChars = (long)(Math.Ceiling(spec.MaxKB * FactorDeCargaSobreMaxKB * 1024 / 3.0) * 4);
        if (imageBase64!.Length > limiteDeChars)
            throw new InvalidPluginExecutionException(
                $"La imagen supera el tamaño máximo de carga de {spec.MaxKB * FactorDeCargaSobreMaxKB} KB.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(imageBase64);
        }
        catch (FormatException)
        {
            throw new InvalidPluginExecutionException("El contenido recibido no es base64 válido.");
        }

        // 2. Validación + normalización (núcleo puro).
        var resultado = MotorDeFirmaMaestra.Procesar(bytes, spec);

        e.Output("IsValid", resultado.EsValida);
        e.Output("MetricsJson", resultado.MetricsJson);
        if (!resultado.EsValida)
        {
            // Un motivo por línea: el frontend los separa de forma confiable.
            e.Output("FailureReasons", string.Join("\n", resultado.Motivos));
            e.Trace.Trace("ValidateMasterSignature: rechazada ({0} motivo(s)).", resultado.Motivos.Count);
            return; // veredicto, no excepción — el frontend muestra los motivos
        }

        // Válida: SIEMPRE devolver el normalizado para el preview, persista o no.
        e.Output("NormalizedImageBase64", Convert.ToBase64String(resultado.PngNormalizado!));

        // Sin Persist → solo preview (default): el frontend confirma antes de reemplazar.
        var persist = e.Contexto.InputParameters.TryGetValue("Persist", out var pv) && pv is bool pb && pb;
        if (!persist)
        {
            e.Trace.Trace("ValidateMasterSignature: válida — preview (Persist=false), no se versiona.");
            return;
        }

        // 3. Versionado: desactivar la vigente y crear la nueva EN LA MISMA operación.
        var versiones = Consultas.VersionesDeFirmaDe(e.Servicio, e.Llamante);
        var versionNueva = versiones.Count == 0
            ? 1
            : versiones.Max(v => v.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version)) + 1;

        foreach (var vigente in versiones.Where(v => v.GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive)))
        {
            var desactivar = new Entity(SchemaNames.FirmaMaestra.Entidad, vigente.Id);
            desactivar[SchemaNames.FirmaMaestra.IsActive] = false;
            e.Servicio.Update(desactivar);
        }

        var yo = e.Servicio.Retrieve(SchemaNames.Usuario.Entidad, e.Llamante,
            new ColumnSet(SchemaNames.Usuario.Upn, SchemaNames.Usuario.FullName));
        var upn = yo.GetAttributeValue<string>(SchemaNames.Usuario.Upn)
                  ?? yo.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? e.Llamante.ToString();

        // El sufijo " v{N}" JAMÁS se trunca (es parte del formato): se recorta el UPN.
        var sufijo = $" v{versionNueva}";
        var firma = new Entity(SchemaNames.FirmaMaestra.Entidad);
        firma[SchemaNames.FirmaMaestra.Name] = Consultas.Truncar(upn, 200 - sufijo.Length) + sufijo;
        firma[SchemaNames.FirmaMaestra.UserId] = new EntityReference(SchemaNames.Usuario.Entidad, e.Llamante);
        firma[SchemaNames.FirmaMaestra.Version] = versionNueva;
        firma[SchemaNames.FirmaMaestra.IsActive] = true;
        firma[SchemaNames.FirmaMaestra.ValidatedOn] = DateTime.UtcNow;
        firma[SchemaNames.FirmaMaestra.ValidationDetails] = resultado.MetricsJson;
        firma[SchemaNames.FirmaMaestra.OwnerId] = new EntityReference(SchemaNames.Usuario.Entidad, e.Llamante);
        var firmaId = e.Servicio.Create(firma);

        e.Archivos.Subir(new EntityReference(SchemaNames.FirmaMaestra.Entidad, firmaId),
            SchemaNames.FirmaMaestra.SignatureFile, "master-signature.png",
            resultado.PngNormalizado!, "image/png");

        e.Trace.Trace("ValidateMasterSignature: v{0} creada para {1} ({2} bytes normalizados).",
            versionNueva, e.Llamante, resultado.PngNormalizado!.Length);
    }
}

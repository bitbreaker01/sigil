// sanic_sigil_capi_GetMasterSignature — Unbound. Devuelve el PNG
// normalizado PROPIO (preview en onboarding y editor de zonas). Solo la firma del
// llamante. Sin firma vigente: outputs ausentes (el frontend ofrece
// el onboarding), NO es un error.

using System;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class GetMasterSignaturePlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var vigente = Consultas.FirmaMaestraVigenteDe(e.Servicio, e.Llamante);
        if (vigente is null)
        {
            e.Trace.Trace("GetMasterSignature: {0} sin firma vigente.", e.Llamante);
            return; // outputs ausentes = "todavía no tenés Firma Maestra"
        }

        byte[] png;
        try
        {
            png = e.Archivos.Descargar(
                new EntityReference(SchemaNames.FirmaMaestra.Entidad, vigente.Id),
                SchemaNames.FirmaMaestra.SignatureFile);
        }
        catch (Exception ex)
        {
            e.Trace.Trace("GetMasterSignature: vigente sin archivo: {0}", ex.Message);
            throw new InvalidPluginExecutionException(
                "Tu Firma Maestra vigente no tiene archivo (registro corrupto) — volvé a subirla desde tu perfil.");
        }

        e.Output("ImageBase64", Convert.ToBase64String(png));
        if (vigente.Contains(SchemaNames.FirmaMaestra.ValidatedOn))
            e.Output("ValidatedOn", vigente.GetAttributeValue<DateTime>(SchemaNames.FirmaMaestra.ValidatedOn));
        e.Trace.Trace("GetMasterSignature: v{0} de {1} ({2} bytes).",
            vigente.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version), e.Llamante, png.Length);
    }
}

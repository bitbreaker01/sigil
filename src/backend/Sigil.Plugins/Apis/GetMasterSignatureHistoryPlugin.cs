// sanic_sigil_capi_GetMasterSignatureHistory (doc 04 §3.1, doc 03 §4.5) — Unbound. Devuelve el
// HISTORIAL de Firmas Maestras del llamante (versionado inmutable): cada versión con su PNG
// normalizado, número, fecha de validación y si es la vigente. Solo el historial PROPIO
// (doc 04 §3.3). Sin firmas: HistoryJson = "[]" (no es error). Salida como JSON (una Custom API
// no devuelve colecciones nativas — mismo patrón que VerifyDocument.MetadataJson).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class GetMasterSignatureHistoryPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var versiones = Consultas.HistorialDeFirmaDe(e.Servicio, e.Llamante); // más nuevo primero
        var items = new List<object>();

        foreach (var v in versiones)
        {
            byte[] png;
            try
            {
                png = e.Archivos.Descargar(
                    new EntityReference(SchemaNames.FirmaMaestra.Entidad, v.Id), SchemaNames.FirmaMaestra.SignatureFile);
            }
            catch (Exception ex)
            {
                // Una versión sin archivo (registro corrupto) no debe voltear todo el historial.
                e.Trace.Trace("GetMasterSignatureHistory: v{0} sin archivo, se omite: {1}",
                    v.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version), ex.Message);
                continue;
            }

            items.Add(new
            {
                version = v.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version),
                imageBase64 = Convert.ToBase64String(png),
                validatedOn = v.GetAttributeValue<DateTime>(SchemaNames.FirmaMaestra.ValidatedOn).ToString("o"),
                isActive = v.GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive),
            });
        }

        e.Output("HistoryJson", JsonSerializer.Serialize(items));
        e.Trace.Trace("GetMasterSignatureHistory: {0} versión(es) de {1}.", items.Count, e.Llamante);
    }
}

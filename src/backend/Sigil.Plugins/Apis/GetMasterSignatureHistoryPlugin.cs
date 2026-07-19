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

        // Documentos firmados con cada versión (doc 03 §4.5): participante FIRMADO → su transacción,
        // agrupado por la versión de firma usada. Batch: una query de firmas + una de nombres.
        var idsVersion = versiones.Select(v => v.Id).ToList();
        var firmas = Consultas.FirmasPorVersionDeFirma(e.Servicio, idsVersion);
        var txPorVersion = firmas
            .Where(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.MasterSignatureId) is not null
                     && p.GetAttributeValue<EntityReference>(SchemaNames.Participante.TransactionId) is not null)
            .GroupBy(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.MasterSignatureId).Id)
            .ToDictionary(g => g.Key,
                g => g.Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.TransactionId).Id).Distinct().ToList());
        var txs = Consultas.TransaccionesPorId(e.Servicio, txPorVersion.Values.SelectMany(x => x).Distinct().ToList());

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

            var docs = (txPorVersion.TryGetValue(v.Id, out var ids) ? ids : new List<Guid>())
                .Select(id => txs.TryGetValue(id, out var tx) ? new
                {
                    id = id.ToString(),
                    name = tx.GetAttributeValue<string>(SchemaNames.Tx.Name) ?? string.Empty,
                    status = tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status)?.Value ?? 0,
                } : null)
                .Where(d => d is not null)
                .ToList();

            items.Add(new
            {
                version = v.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version),
                imageBase64 = Convert.ToBase64String(png),
                validatedOn = v.GetAttributeValue<DateTime>(SchemaNames.FirmaMaestra.ValidatedOn).ToString("o"),
                isActive = v.GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive),
                documents = docs,
            });
        }

        e.Output("HistoryJson", JsonSerializer.Serialize(items));
        e.Trace.Trace("GetMasterSignatureHistory: {0} versión(es) de {1}.", items.Count, e.Llamante);
    }
}

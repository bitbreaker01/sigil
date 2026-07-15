// sanic_sigil_capi_GetDocumentContent (doc 04 §3.1) — Bound. La operación más frecuente
// del sistema: devuelve PdfBase64 del documento de contenido (RF-03) o del final (RF-05/24).
// In: DocumentType ("content"|"final"). Autorización doc 04 §3.3: creador O participante;
// final solo en Completado; content para participantes solo desde Pendiente de Firma.
// Solo lectura: sin lock (doc 04 §5 aplica a quien DECIDE sobre estado compartido).

using System;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class GetDocumentContentPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        var token = e.Input<string>("DocumentType");
        var tipo = token?.ToLowerInvariant() switch
        {
            "content" => DocumentType.Content,
            "final" => DocumentType.Final,
            _ => throw new InvalidPluginExecutionException("DocumentType debe ser \"content\" o \"final\"."),
        };

        var (estado, creador) = Consultas.EstadoYCreador(e.Servicio, target.Id);
        var esParticipante = e.Llamante != creador && Consultas.EsParticipante(e.Servicio, target.Id, e.Llamante);

        var motivo = ReglasDeAutorizacion.MotivoParaRechazarLecturaDeDocumento(
            tipo, e.Llamante, creador, esParticipante, estado);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        var columna = tipo == DocumentType.Final ? SchemaNames.Tx.FinalFile : SchemaNames.Tx.ContentFile;
        var bytes = e.Archivos.Descargar(target, columna);

        e.Output("PdfBase64", Convert.ToBase64String(bytes));
        e.Trace.Trace("GetDocumentContent: {0} ({1}, {2} bytes).", target.Id, token, bytes.Length);
    }
}

// Implementación real del seam de archivos: mensajes de file blocks del SDK
// (InitializeFileBlocksUpload/Download + bloques de ≤4 MB, verificado).
// Sin tests unitarios POR DISEÑO: se valida contra Dev en los gates.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xrm.Sdk;

namespace Sigil.Plugins.Data;

public sealed class FileTransferDataverse(IOrganizationService servicio) : IFileTransfer
{
    private const int TamanoDeBloque = 4 * 1024 * 1024;

    public void Subir(EntityReference registro, string columna, string nombreDeArchivo, byte[] bytes, string mimeType)
    {
        var inicio = new OrganizationRequest("InitializeFileBlocksUpload")
        {
            ["Target"] = registro,
            ["FileAttributeName"] = columna,
            ["FileName"] = nombreDeArchivo,
        };
        var token = (string)servicio.Execute(inicio).Results["FileContinuationToken"];

        var bloques = new List<string>();
        for (var offset = 0; offset < bytes.Length; offset += TamanoDeBloque)
        {
            var largo = Math.Min(TamanoDeBloque, bytes.Length - offset);
            var bloque = new byte[largo];
            Buffer.BlockCopy(bytes, offset, bloque, 0, largo);

            // Base64 del índice como STRING de dígitos (patrón del SDK de Azure): la plataforma
            // pasa el blockid a Azure Blob como query param SIN url-encodear — un '+' en el
            // base64 llega como espacio y revienta con InvalidQueryParameterValue (cazado en
            // CF-D08, 2026-07-16). El base64 de ASCII de dígitos jamás emite '+' ni '/'
            // (grupos de 6 bits ≤ 57 < 62) y sin padding con largo múltiplo de 3.
            var blockId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes((offset / TamanoDeBloque).ToString("d6")));
            bloques.Add(blockId);
            servicio.Execute(new OrganizationRequest("UploadBlock")
            {
                ["FileContinuationToken"] = token,
                ["BlockId"] = blockId,
                ["BlockData"] = bloque,
            });
        }

        servicio.Execute(new OrganizationRequest("CommitFileBlocksUpload")
        {
            ["FileContinuationToken"] = token,
            ["FileName"] = nombreDeArchivo,
            ["MimeType"] = mimeType,
            ["BlockList"] = bloques.ToArray(),
        });
    }

    public byte[] Descargar(EntityReference registro, string columna)
    {
        var inicio = new OrganizationRequest("InitializeFileBlocksDownload")
        {
            ["Target"] = registro,
            ["FileAttributeName"] = columna,
        };
        var respuesta = servicio.Execute(inicio).Results;
        var token = (string)respuesta["FileContinuationToken"];
        var total = (long)respuesta["FileSizeInBytes"];

        using var ms = new MemoryStream();
        for (long offset = 0; offset < total; offset += TamanoDeBloque)
        {
            var largo = Math.Min(TamanoDeBloque, total - offset);
            var bloque = (byte[])servicio.Execute(new OrganizationRequest("DownloadBlock")
            {
                ["FileContinuationToken"] = token,
                ["Offset"] = offset,
                ["BlockLength"] = largo,
            }).Results["Data"];
            ms.Write(bloque, 0, bloque.Length);
        }
        return ms.ToArray();
    }
}

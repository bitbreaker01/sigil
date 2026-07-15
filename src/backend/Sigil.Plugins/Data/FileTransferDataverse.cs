// Implementación real del seam de archivos: mensajes de file blocks del SDK
// (InitializeFileBlocksUpload/Download + bloques de ≤4 MB — doc 03 §4.1, verificado).
// Sin tests unitarios POR DISEÑO (doc 11 §3): se valida contra Dev en los gates.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xrm.Sdk;

namespace Sigil.Plugins.Data;

public sealed class FileTransferDataverse(IOrganizationService servicio) : IFileTransfer
{
    private const int TamanoDeBloque = 4 * 1024 * 1024;

    public void Subir(EntityReference registro, string columna, string nombreDeArchivo, byte[] bytes)
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

            var blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
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
            ["MimeType"] = "application/pdf",
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

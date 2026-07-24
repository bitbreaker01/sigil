// Seam de columnas File (límite declarado): ningún fake simula los mensajes
// de file blocks, así que la cáscara define su propia interfaz de transferencia. Los tests
// la sustituyen con un doble en memoria; la implementación real (FileTransferDataverse)
// solo se ejercita contra Dev (gates de despliegue).

using Microsoft.Xrm.Sdk;

namespace Sigil.Plugins.Data;

public interface IFileTransfer
{
    /// <summary>Sube bytes a una columna File (sobrescribe si existía). El mimeType etiqueta
    /// la evidencia correctamente ("application/pdf", "image/png"…).</summary>
    void Subir(EntityReference registro, string columna, string nombreDeArchivo, byte[] bytes, string mimeType);

    /// <summary>Descarga los bytes de una columna File. Lanza si la columna está vacía.</summary>
    byte[] Descargar(EntityReference registro, string columna);
}

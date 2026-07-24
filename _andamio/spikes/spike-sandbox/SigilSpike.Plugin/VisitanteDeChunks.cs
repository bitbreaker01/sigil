using System.IO;
using PdfSharp.Internal.Png.BigGustave;

namespace SigilSpike.Plugin
{
    /// <summary>Réplica mínima del MyVisitor del importador PNG de PDFsharp — sonda p9.</summary>
    internal sealed class VisitanteDeChunks : IChunkVisitor
    {
        internal int Chunks;
        public void Visit(Stream stream, ImageHeader header, ChunkHeader chunkHeader, byte[] data, byte[] crc)
        {
            Chunks++;
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace SigilSpike.Plugin
{
    /// <summary>
    /// Plan B del motor (hallazgo del spike): el importador PNG de PDFsharp devuelve null
    /// dentro del sandbox net462 — así que la imagen se escribe A MANO como XObject
    /// (RGB FlateDecode + SMask para el alfa), sin tocar el importador.
    /// </summary>
    internal static class XObjectManual
    {
        /// <summary>Crea el XObject de imagen y lo dibuja en la página (rect en puntos, origen abajo-izq del PDF).</summary>
        internal static void DibujarImagenRgba(PdfDocument doc, PdfPage page, string nombre,
            int width, int height, byte[] rgb, byte[] alpha, double x, double y, double w, double h)
        {
            PdfDictionary smask = null;
            if (alpha != null)
            {
                smask = new PdfDictionary(doc);
                smask.Elements.SetName("/Type", "/XObject");
                smask.Elements.SetName("/Subtype", "/Image");
                smask.Elements.SetInteger("/Width", width);
                smask.Elements.SetInteger("/Height", height);
                smask.Elements.SetName("/ColorSpace", "/DeviceGray");
                smask.Elements.SetInteger("/BitsPerComponent", 8);
                smask.Elements.SetName("/Filter", "/FlateDecode");
                doc.Internals.AddObject(smask);
                smask.CreateStream(Zlib(alpha));
            }

            var img = new PdfDictionary(doc);
            img.Elements.SetName("/Type", "/XObject");
            img.Elements.SetName("/Subtype", "/Image");
            img.Elements.SetInteger("/Width", width);
            img.Elements.SetInteger("/Height", height);
            img.Elements.SetName("/ColorSpace", "/DeviceRGB");
            img.Elements.SetInteger("/BitsPerComponent", 8);
            img.Elements.SetName("/Filter", "/FlateDecode");
            doc.Internals.AddObject(img);
            img.CreateStream(Zlib(rgb));
            if (smask != null)
                img.Elements.SetReference("/SMask", smask);

            // Recursos de la página: /Resources -> /XObject -> {nombre: ref}
            var resources = page.Elements.GetDictionary("/Resources");
            if (resources == null)
            {
                resources = new PdfDictionary(doc);
                page.Elements.SetObject("/Resources", resources);
            }
            var xobjects = resources.Elements.GetDictionary("/XObject");
            if (xobjects == null)
            {
                xobjects = new PdfDictionary(doc);
                resources.Elements.SetObject("/XObject", xobjects);
            }
            xobjects.Elements.SetReference("/" + nombre, img);

            // Contenido: q  w 0 0 h x y cm  /nombre Do  Q
            string ops = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "\nq\n{0} 0 0 {1} {2} {3} cm\n/{4} Do\nQ\n", w, h, x, y, nombre);
            var content = page.Contents.AppendContent();
            content.CreateStream(System.Text.Encoding.ASCII.GetBytes(ops));
        }

        /// <summary>FlateDecode del PDF = zlib: header 0x78 0x9C + deflate crudo + Adler-32 (net462 no tiene ZLibStream).</summary>
        private static byte[] Zlib(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                {
                    deflate.Write(data, 0, data.Length);
                }
                uint adler = Adler32(data);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            const uint MOD = 65521;
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % MOD;
                b = (b + a) % MOD;
            }
            return (b << 16) | a;
        }
    }
}

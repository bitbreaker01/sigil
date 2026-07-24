// XObject de imagen escrito A MANO (hallazgo del spike 2026-07-15):
// el importador PNG de PDFsharp (XImage.FromStream) devuelve null dentro del sandbox
// net462 — las imágenes se incrustan como dict /Image con stream RGB FlateDecode +
// /SMask DeviceGray para el alfa, y el operador Do en el content stream. VALIDADO en
// el sandbox real (spike v1.0.8: smaskFound=true, roundtrip OK). Portado del spike con
// la matriz cm generalizada (rotaciones).

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using PdfSharp.Pdf;

namespace Sigil.Plugins.Core.Pdf;

public static class XObjectManual
{
    /// <summary>Crea el XObject RGBA y lo dibuja con la matriz cm dada (espacio de usuario del PDF).</summary>
    public static void DibujarImagenRgba(PdfDocument doc, PdfPage page, string nombre,
        int width, int height, byte[] rgb, byte[]? alpha, MatrizCm m)
    {
        PdfDictionary? smask = null;
        if (alpha is not null)
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
        if (smask is not null)
            img.Elements.SetReference("/SMask", smask);

        // Recursos de la página: /Resources → /XObject → { nombre: ref }
        var resources = page.Elements.GetDictionary("/Resources");
        if (resources is null)
        {
            resources = new PdfDictionary(doc);
            page.Elements.SetObject("/Resources", resources);
        }
        var xobjects = resources.Elements.GetDictionary("/XObject");
        if (xobjects is null)
        {
            xobjects = new PdfDictionary(doc);
            resources.Elements.SetObject("/XObject", xobjects);
        }
        xobjects.Elements.SetReference("/" + nombre, img);

        var ops = string.Format(CultureInfo.InvariantCulture,
            "\nq\n{0} {1} {2} {3} {4} {5} cm\n/{6} Do\nQ\n",
            R(m.A), R(m.B), R(m.C), R(m.D), R(m.E), R(m.F), nombre);
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(ops));
    }

    private static string R(double v) => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture);

    /// <summary>FlateDecode = zlib: 0x78 0x9C + deflate crudo + Adler-32 (net462 no tiene ZLibStream).</summary>
    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        var adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint mod = 65521;
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % mod;
            b = (b + a) % mod;
        }
        return (b << 16) | a;
    }
}

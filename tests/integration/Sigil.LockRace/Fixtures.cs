// Fixtures fabricadas en memoria (sin binarios en el repo): un PDF de una página y una
// firma PNG que pasa los tres umbrales de ADR-009 (trazos negros sobre fondo transparente).

using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Sigil.LockRace;

internal static class Fixtures
{
    public static byte[] PdfDeUnaPagina()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    public static byte[] PngDeFirma()
    {
        using var img = new Image<Rgba32>(600, 200);
        var negro = new Rgba32(0, 0, 0, 255);
        for (var i = 0; i < 5; i++)
        {
            double x0 = 30 + i * 100, y0 = 170, x1 = x0 + 70, y1 = 30;
            for (var s = 0; s <= 400; s++)
            {
                var t = s / 400.0;
                int cx = (int)(x0 + (x1 - x0) * t), cy = (int)(y0 + (y1 - y0) * t);
                for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                    if (cx + dx is >= 0 and < 600 && cy + dy is >= 0 and < 200 && dx * dx + dy * dy <= 16)
                        img[cx + dx, cy + dy] = negro;
            }
        }
        using var ms = new MemoryStream();
        img.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}

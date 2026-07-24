using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Mismo PNG que el plugin: RGBA8, no entrelazado
byte[] png;
using (var img = new Image<Rgba32>(400, 150))
{
        using var ms = new MemoryStream();
    img.Save(ms, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8, InterlaceMethod = PngInterlaceMode.None });
    png = ms.ToArray();
}
Console.WriteLine($"PNG: {png.Length} bytes; header: {BitConverter.ToString(png, 0, 8)}");

try
{
    using var doc = new PdfDocument();
    var page = doc.AddPage();
    using var gfx = XGraphics.FromPdfPage(page);
    using var imgMs = new MemoryStream(png);
    using var ximg = XImage.FromStream(imgMs);
    gfx.DrawImage(ximg, 10, 10, 100, 40);
    Console.WriteLine("XImage.FromStream: OK con asset netstandard2.0");
}
catch (Exception ex)
{
    Console.WriteLine($"REPRODUCIDO: {ex.GetType().Name}: {ex.Message}");
}

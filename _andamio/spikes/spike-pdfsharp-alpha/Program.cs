// Spike 1 — PDFsharp 6.2.x PNG alpha transparency (issue empira/PDFsharp#187)
// Steps:
//  1. Generate signature.png (RGBA 8-bit, non-interlaced, 600x200) with ImageSharp.
//  2. Create base.pdf with a full-page saturated red background.
//  3. Open base.pdf in Modify mode, draw signature.png, save result.pdf.
//  4. Inspect result.pdf image XObject for /SMask (in-process check).
//  5. Rotated-page test: page.Rotate = 90, draw markers + image, save rotated.pdf.

using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

string dir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
string sigPath = Path.Combine(dir, "signature.png");
string basePath = Path.Combine(dir, "base.pdf");
string resultPath = Path.Combine(dir, "result.pdf");
string rotatedPath = Path.Combine(dir, "rotated.pdf");

var pdfSharpAsm = typeof(PdfDocument).Assembly;
var imageSharpAsm = typeof(Image).Assembly;
Console.WriteLine($"[versions] PDFsharp = {pdfSharpAsm.GetName().Version} " +
    $"({System.Diagnostics.FileVersionInfo.GetVersionInfo(pdfSharpAsm.Location).ProductVersion})");
Console.WriteLine($"[versions] ImageSharp = {imageSharpAsm.GetName().Version} " +
    $"({System.Diagnostics.FileVersionInfo.GetVersionInfo(imageSharpAsm.Location).ProductVersion})");
Console.WriteLine($"[versions] Runtime = {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 1. signature.png — RGBA 8-bit non-interlaced 600x200
// ---------------------------------------------------------------------------
using (var img = new Image<Rgba32>(600, 200)) // all pixels start (0,0,0,0) = transparent
{
    var black = new Rgba32(0, 0, 0, 255);

    // Opaque black diagonal strokes (simulated signature): 5 diagonals.
    for (int i = 0; i < 5; i++)
        DrawThickLine(img, 20 + i * 80, 180, 120 + i * 80, 20, 3.0, black);

    // Semi-transparent gray zone (alpha 128).
    var gray = new Rgba32(100, 100, 100, 128);
    for (int y = 40; y < 160; y++)
        for (int x = 450; x < 590; x++)
            img[x, y] = gray;

    var encoder = new PngEncoder
    {
        ColorType = PngColorType.RgbWithAlpha,
        BitDepth = PngBitDepth.Bit8,
        InterlaceMethod = PngInterlaceMode.None,
    };
    img.Save(sigPath, encoder);
}
Console.WriteLine($"[1] signature.png written: {new FileInfo(sigPath).Length} bytes");

// Sanity check of what we wrote.
using (var check = Image.Load<Rgba32>(sigPath))
{
    Console.WriteLine($"[1] check pixel (500,20)  = {check[500, 20]}   (expected transparent 0,0,0,0)");
    Console.WriteLine($"[1] check pixel (70,100)  = {check[70, 100]}   (expected opaque black)");
    Console.WriteLine($"[1] check pixel (520,100) = {check[520, 100]}   (expected gray a=128)");
}
Console.WriteLine();

// ---------------------------------------------------------------------------
// 2. base.pdf — full-page saturated red background (Letter 612x792 pt)
// ---------------------------------------------------------------------------
using (var doc = new PdfDocument())
{
    var page = doc.AddPage();
    page.Width = XUnit.FromPoint(612);
    page.Height = XUnit.FromPoint(792);
    using (var gfx = XGraphics.FromPdfPage(page))
    {
        var red = new XSolidBrush(XColor.FromArgb(255, 255, 0, 0));
        gfx.DrawRectangle(red, 0, 0, page.Width.Point, page.Height.Point);
    }
    doc.Save(basePath);
}
Console.WriteLine($"[2] base.pdf written: {new FileInfo(basePath).Length} bytes");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 3. result.pdf — open base.pdf in Modify mode, draw the PNG
//    Image drawn at (6,100) size 600x200 pt => 1 pt == 1 px at 72 dpi render.
// ---------------------------------------------------------------------------
using (var doc = PdfReader.Open(basePath, PdfDocumentOpenMode.Modify))
{
    var page = doc.Pages[0];
    using (var gfx = XGraphics.FromPdfPage(page))
    using (var fs = File.OpenRead(sigPath))
    using (var ximg = XImage.FromStream(fs))
    {
        Console.WriteLine($"[3] XImage: {ximg.PixelWidth}x{ximg.PixelHeight} px, loaded via FromStream OK");
        gfx.DrawImage(ximg, 6, 100, 600, 200);
    }
    doc.Save(resultPath);
}
Console.WriteLine($"[3] result.pdf written: {new FileInfo(resultPath).Length} bytes");
Console.WriteLine();

// ---------------------------------------------------------------------------
// 4. Inspect result.pdf for /SMask on the image XObject
// ---------------------------------------------------------------------------
using (var doc = PdfReader.Open(resultPath, PdfDocumentOpenMode.Import))
{
    var page = doc.Pages[0];
    var resources = page.Elements.GetDictionary("/Resources");
    var xobjects = resources?.Elements.GetDictionary("/XObject");
    if (xobjects is null)
    {
        Console.WriteLine("[4] NO /XObject dictionary found in page resources!");
    }
    else
    {
        foreach (var key in xobjects.Elements.Keys)
        {
            var item = xobjects.Elements[key];
            var dict = item is PdfReference r ? r.Value as PdfDictionary : item as PdfDictionary;
            if (dict is null) continue;
            Console.WriteLine($"[4] XObject {key}:");
            Console.WriteLine($"    /Subtype          = {dict.Elements.GetName("/Subtype")}");
            Console.WriteLine($"    /Width x /Height  = {dict.Elements.GetInteger("/Width")} x {dict.Elements.GetInteger("/Height")}");
            Console.WriteLine($"    /BitsPerComponent = {dict.Elements.GetInteger("/BitsPerComponent")}");
            Console.WriteLine($"    /ColorSpace       = {dict.Elements.GetName("/ColorSpace")}");
            Console.WriteLine($"    /Filter           = {dict.Elements["/Filter"]}");
            bool hasSMask = dict.Elements.ContainsKey("/SMask");
            Console.WriteLine($"    /SMask present    = {hasSMask}");
            if (hasSMask)
            {
                var smItem = dict.Elements["/SMask"];
                var sm = smItem is PdfReference smr ? smr.Value as PdfDictionary : smItem as PdfDictionary;
                if (sm is not null)
                {
                    Console.WriteLine($"    SMask: {sm.Elements.GetInteger("/Width")}x{sm.Elements.GetInteger("/Height")}, " +
                        $"BPC={sm.Elements.GetInteger("/BitsPerComponent")}, CS={sm.Elements.GetName("/ColorSpace")}, " +
                        $"stream={sm.Stream?.Length ?? 0} bytes");
                }
            }
        }
    }
}
Console.WriteLine();

// ---------------------------------------------------------------------------
// 5. Rotated page test — page.Rotate = 90
// ---------------------------------------------------------------------------
using (var doc = new PdfDocument())
{
    var page = doc.AddPage();
    page.Width = XUnit.FromPoint(595);   // A4 portrait media box
    page.Height = XUnit.FromPoint(842);
    page.Rotate = 90;                    // viewer displays landscape 842x595

    using (var gfx = XGraphics.FromPdfPage(page))
    {
        Console.WriteLine($"[5] page.Width={page.Width.Point}pt page.Height={page.Height.Point}pt page.Rotate={page.Rotate}");
        Console.WriteLine($"[5] gfx.PageSize = {gfx.PageSize.Width} x {gfx.PageSize.Height} " +
            "(if swapped vs media box => XGraphics works in VISUAL orientation)");

        // Green background across whatever XGraphics thinks the page is.
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 0, 160, 0)),
            0, 0, gfx.PageSize.Width, gfx.PageSize.Height);
        // Black 100x100 marker at XGraphics origin (0,0).
        gfx.DrawRectangle(XBrushes.Black, 0, 0, 100, 100);
        // Blue 100x100 marker at top-right of the XGraphics coordinate space.
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 0, 0, 255)),
            gfx.PageSize.Width - 100, 0, 100, 100);
        // The signature PNG roughly centered.
        using var fs = File.OpenRead(sigPath);
        using var ximg = XImage.FromStream(fs);
        gfx.DrawImage(ximg, 120, 200, 600, 200);
    }
    doc.Save(rotatedPath);
}
Console.WriteLine($"[5] rotated.pdf written: {new FileInfo(rotatedPath).Length} bytes");
Console.WriteLine();
Console.WriteLine("DONE spike-pdfsharp-alpha");

// ---------------------------------------------------------------------------
static void DrawThickLine(Image<Rgba32> img, double x0, double y0, double x1, double y1,
    double radius, Rgba32 color)
{
    double dx = x1 - x0, dy = y1 - y0;
    double len = Math.Sqrt(dx * dx + dy * dy);
    int steps = (int)(len * 2) + 1;
    for (int s = 0; s <= steps; s++)
    {
        double t = (double)s / steps;
        double cx = x0 + dx * t, cy = y0 + dy * t;
        int minX = Math.Max(0, (int)(cx - radius)), maxX = Math.Min(img.Width - 1, (int)(cx + radius));
        int minY = Math.Max(0, (int)(cy - radius)), maxY = Math.Min(img.Height - 1, (int)(cy + radius));
        for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                double ddx = px - cx, ddy = py - cy;
                if (ddx * ddx + ddy * ddy <= radius * radius)
                    img[px, py] = color;
            }
    }
}

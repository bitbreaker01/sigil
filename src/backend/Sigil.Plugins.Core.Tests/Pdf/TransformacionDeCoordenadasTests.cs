// M9 — Coordenadas y zonas: el contrato es % del área VISIBLE (CropBox),
// origen arriba-izquierda, orientación VISUAL. Evidencia del spike: XGraphics/PDF crudo
// trabajan en orientación RAW y NO compensan /Rotate — la transformación manual es
// obligatoria. Estos tests fijan la matriz cm EXACTA para las 4 rotaciones y CropBox.
//
// Derivación (fijada acá como contrato): página con CropBox (cx0,cy0)-(cx1,cy1), rotación R,
// dims visuales VW/VH (con swap si R∈{90,270}); zona visual (vx, vy desde arriba, vw, vh):
//   R=0:   [ vw   0    0   vh  | cx0+vx        cy1−vy−vh ]
//   R=90:  [ 0    vw  −vh  0   | cx0+vy+vh     cy0+vx    ]
//   R=180: [−vw   0    0  −vh  | cx1−vx        cy0+vy+vh ]
//   R=270: [ 0   −vw   vh  0   | cx1−vy−vh     cy1−vx    ]

using PdfSharp.Pdf;
using Sigil.Plugins.Core.Pdf;

namespace Sigil.Plugins.Core.Tests.Pdf;

public class TransformacionDeCoordenadasTests
{
    private static PdfPage Pagina(double w, double h, int rotate = 0,
        (double x0, double y0, double x1, double y1)? crop = null)
    {
        var doc = new PdfDocument();
        var page = doc.AddPage();
        page.MediaBox = new PdfSharp.Pdf.PdfRectangle(
            new PdfSharp.Drawing.XPoint(0, 0), new PdfSharp.Drawing.XPoint(w, h));
        if (rotate != 0) page.Rotate = rotate;
        if (crop is { } c)
            page.CropBox = new PdfSharp.Pdf.PdfRectangle(
                new PdfSharp.Drawing.XPoint(c.x0, c.y0), new PdfSharp.Drawing.XPoint(c.x1, c.y1));
        return page;
    }

    [Fact] // caso base: carta 612×792, sin rotación — zona al 50%/25% del tamaño visible
    public void SinRotacion_MatrizDirecta_OrigenAbajoIzquierdaCompensado()
    {
        var m = TransformacionDeCoordenadas.ParaZona(Pagina(612, 792), x: 10, y: 20, w: 50, h: 25);

        Assert.Equal(306, m.A, 3);       // vw = 50% de 612
        Assert.Equal(0, m.B, 3);
        Assert.Equal(0, m.C, 3);
        Assert.Equal(198, m.D, 3);       // vh = 25% de 792
        Assert.Equal(61.2, m.E, 3);      // cx0 + 10% de 612
        Assert.Equal(792 - 158.4 - 198, m.F, 3); // cy1 − vy − vh
    }

    [Fact] // Rotate=90 (el caso del escaneado apaisado — evidencia del spike): dims visuales con swap
    public void Rotate90_IntercambiaDimensiones_YRotaLaMatriz()
    {
        // media 612×792 rotada 90 → visual 792×612
        var m = TransformacionDeCoordenadas.ParaZona(Pagina(612, 792, rotate: 90), x: 0, y: 0, w: 25, h: 10);

        double vw = 0.25 * 792, vh = 0.10 * 612;
        Assert.Equal(0, m.A, 3);
        Assert.Equal(vw, m.B, 3);
        Assert.Equal(-vh, m.C, 3);
        Assert.Equal(0, m.D, 3);
        Assert.Equal(0 + 0 + vh, m.E, 3); // cx0 + vy + vh
        Assert.Equal(0, m.F, 3);          // cy0 + vx
    }

    [Fact]
    public void Rotate180_InvierteAmbosEjes()
    {
        var m = TransformacionDeCoordenadas.ParaZona(Pagina(612, 792, rotate: 180), x: 10, y: 20, w: 50, h: 25);

        double vw = 306, vh = 198, vx = 61.2, vy = 158.4;
        Assert.Equal(-vw, m.A, 3);
        Assert.Equal(0, m.B, 3);
        Assert.Equal(0, m.C, 3);
        Assert.Equal(-vh, m.D, 3);
        Assert.Equal(612 - vx, m.E, 3);      // cx1 − vx
        Assert.Equal(0 + vy + vh, m.F, 3);   // cy0 + vy + vh
    }

    [Fact]
    public void Rotate270_RotaAlRevesQue90()
    {
        var m = TransformacionDeCoordenadas.ParaZona(Pagina(612, 792, rotate: 270), x: 0, y: 0, w: 25, h: 10);

        double vw = 0.25 * 792, vh = 0.10 * 612;
        Assert.Equal(0, m.A, 3);
        Assert.Equal(-vw, m.B, 3);
        Assert.Equal(vh, m.C, 3);
        Assert.Equal(0, m.D, 3);
        Assert.Equal(612 - 0 - vh, m.E, 3); // cx1 − vy − vh
        Assert.Equal(792 - 0, m.F, 3);      // cy1 − vx
    }

    [Fact] // CropBox ≠ MediaBox (M9 obligatorio): los % refieren al área VISIBLE, con su offset
    public void CropBoxDistintoDeMediaBox_LosPorcentajesRefierenAlCrop()
    {
        // media 700×900; crop de (50,100) a (650,800) → visible 600×700
        var m = TransformacionDeCoordenadas.ParaZona(
            Pagina(700, 900, crop: (50, 100, 650, 800)), x: 10, y: 10, w: 20, h: 10);

        Assert.Equal(120, m.A, 3);            // 20% de 600
        Assert.Equal(70, m.D, 3);             // 10% de 700
        Assert.Equal(50 + 60, m.E, 3);        // cx0 + 10% de 600
        Assert.Equal(800 - 70 - 70, m.F, 3);  // cy1 − vy − vh
    }

    [Fact] // Rotate=450 equivale a 90; negativo -90 equivale a 270 (normalización)
    public void Rotaciones_SeNormalizanModulo360()
    {
        var m450 = TransformacionDeCoordenadas.ParaZona(Pagina(612, 792, rotate: 450), 0, 0, 25, 10);
        var m90 = TransformacionDeCoordenadas.ParaZona(Pagina(612, 792, rotate: 90), 0, 0, 25, 10);
        Assert.Equal(m90.B, m450.B, 3);
        Assert.Equal(m90.C, m450.C, 3);
    }
}

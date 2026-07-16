// Contrato de coordenadas (doc 04 §6.1): las zonas vienen en % del área VISIBLE de la
// página (CropBox; MediaBox si no hay), origen ARRIBA-IZQUIERDA, en orientación VISUAL.
// Evidencia del spike (2026-07-10): el espacio de usuario del PDF es RAW y /Rotate NO se
// compensa solo — esta clase produce la matriz cm que coloca una imagen upright en la
// orientación visual para las 4 rotaciones. La derivación completa está fijada como
// contrato en TransformacionDeCoordenadasTests.

using System;
using PdfSharp.Pdf;

namespace Sigil.Plugins.Core.Pdf;

/// <summary>Matriz cm del content stream: x' = a·u + c·v + e ; y' = b·u + d·v + f.</summary>
public readonly struct MatrizCm(double a, double b, double c, double d, double e, double f)
{
    public double A { get; } = a;
    public double B { get; } = b;
    public double C { get; } = c;
    public double D { get; } = d;
    public double E { get; } = e;
    public double F { get; } = f;
}

public static class TransformacionDeCoordenadas
{
    public static MatrizCm ParaZona(PdfPage page, double x, double y, double w, double h)
    {
        // Área visible: CropBox si está definido, MediaBox si no (doc 04 §6.1).
        var crop = page.Elements.ContainsKey("/CropBox") ? page.CropBox : page.MediaBox;
        double cx0 = Math.Min(crop.X1, crop.X2), cx1 = Math.Max(crop.X1, crop.X2);
        double cy0 = Math.Min(crop.Y1, crop.Y2), cy1 = Math.Max(crop.Y1, crop.Y2);
        double cropW = cx1 - cx0, cropH = cy1 - cy0;

        var rotate = ((page.Rotate % 360) + 360) % 360;

        // Dimensiones VISUALES (con swap en 90/270).
        double vwPagina = rotate is 90 or 270 ? cropH : cropW;
        double vhPagina = rotate is 90 or 270 ? cropW : cropH;

        double vx = x / 100.0 * vwPagina;
        double vy = y / 100.0 * vhPagina;
        double vw = w / 100.0 * vwPagina;
        double vh = h / 100.0 * vhPagina;

        return rotate switch
        {
            90 => new MatrizCm(0, vw, -vh, 0, cx0 + vy + vh, cy0 + vx),
            180 => new MatrizCm(-vw, 0, 0, -vh, cx1 - vx, cy0 + vy + vh),
            270 => new MatrizCm(0, -vw, vh, 0, cx1 - vy - vh, cy1 - vx),
            _ => new MatrizCm(vw, 0, 0, vh, cx0 + vx, cy1 - vy - vh),
        };
    }
}

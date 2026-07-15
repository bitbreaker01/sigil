// Spike sandbox (Fase 1, paso 10) — corre el stack completo DENTRO del sandbox real de Dataverse:
//   a. ImageSharp: PNG RGBA con alfa (trazos opacos + zona alfa 128) en memoria.
//   b. QRCoder: PngByteQRCode (sin System.Drawing).
//   c. PDFsharp: componer PDF con ambas imágenes, round-trip PdfReader, chequeo /SMask.
//   d. SHA-256 del PDF.
//   e. BouncyCastle RFC 3161 contra DigiCert y Sectigo (¿la TSA es alcanzable desde el sandbox?).
// TODO envuelto en try/catch por paso: los resultados parciales SIEMPRE llegan al JSON de salida.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SigilSpike.Plugin
{
    public sealed class RunSpikePlugin : IPlugin
    {
        private static readonly string[] TsaEndpoints =
        {
            "https://timestamp.digicert.com",
            "https://timestamp.sectigo.com",
        };

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            var total = Stopwatch.StartNew();
            string imagesharpJson = "{\"ok\":false,\"error\":\"not run\"}";
            string qrcoderJson = "{\"ok\":false,\"error\":\"not run\"}";
            string pdfsharpJson = "{\"ok\":false,\"error\":\"not run\"}";
            string sha256Json = "null";
            string tsaJson = "[]";
            string sandboxJson = "{}";

            byte[] signaturePng = null;
            byte[] qrPng = null;
            byte[] pdfBytes = null;
            byte[] digest = null;

            // Contexto del runtime (evidencia de que estamos en el sandbox real).
            try
            {
                sandboxJson = "{" +
                    J("isolationMode", context.IsolationMode) + "," +
                    J("depth", context.Depth) + "," +
                    J("messageName", context.MessageName) + "," +
                    J("runtimeVersion", Environment.Version.ToString()) +
                    "}";
            }
            catch (Exception ex)
            {
                sandboxJson = "{" + J("error", ex.Message) + "}";
            }

            // ---------------------------------------------------------------
            // a. IMAGESHARP — 400x150 RGBA: fondo transparente, trazos negros
            //    opacos en diagonal, rectángulo gris semi-transparente (alfa 128).
            // ---------------------------------------------------------------
            try
            {
                var sw = Stopwatch.StartNew();
                using (var img = new Image<Rgba32>(400, 150)) // todo (0,0,0,0) = transparente
                {
                    var black = new Rgba32(0, 0, 0, 255);
                    for (int i = 0; i < 4; i++)
                    {
                        DrawThickLine(img, 20 + i * 70, 130, 90 + i * 70, 20, 2.5, black);
                    }

                    var gray = new Rgba32(100, 100, 100, 128);
                    for (int y = 30; y < 120; y++)
                    {
                        for (int x = 280; x < 380; x++)
                        {
                            img[x, y] = gray;
                        }
                    }

                    var encoder = new PngEncoder
                    {
                        ColorType = PngColorType.RgbWithAlpha,
                        BitDepth = PngBitDepth.Bit8,
                        InterlaceMethod = PngInterlaceMode.None,
                    };
                    using (var ms = new MemoryStream())
                    {
                        img.Save(ms, encoder);
                        signaturePng = ms.ToArray();
                    }
                }
                sw.Stop();
                imagesharpJson = "{" + J("ok", true) + "," + J("ms", sw.ElapsedMilliseconds) + "," +
                    J("pngBytes", signaturePng.Length) + "}";
                trace.Trace("imagesharp OK: {0} ms, {1} bytes", sw.ElapsedMilliseconds, signaturePng.Length);
            }
            catch (Exception ex)
            {
                imagesharpJson = FailJson(ex);
                trace.Trace("imagesharp FAIL: {0}", ex);
            }

            // ---------------------------------------------------------------
            // b. QRCODER — PngByteQRCode (jamás el renderer QRCode/System.Drawing).
            // ---------------------------------------------------------------
            try
            {
                var sw = Stopwatch.StartNew();
                using (var gen = new QRCodeGenerator())
                using (var data = gen.CreateQrCode("https://sigil.spike/verify?tx=TEST", QRCodeGenerator.ECCLevel.Q))
                using (var png = new PngByteQRCode(data))
                {
                    qrPng = png.GetGraphic(10);
                }
                sw.Stop();
                qrcoderJson = "{" + J("ok", true) + "," + J("ms", sw.ElapsedMilliseconds) + "," +
                    J("pngBytes", qrPng.Length) + "}";
                trace.Trace("qrcoder OK: {0} ms, {1} bytes", sw.ElapsedMilliseconds, qrPng.Length);
            }
            catch (Exception ex)
            {
                qrcoderJson = FailJson(ex);
                trace.Trace("qrcoder FAIL: {0}", ex);
            }

            // ---------------------------------------------------------------
            // c. PDFSHARP — página 1 roja + PNG de firma; página 2 + QR.
            //    Round-trip (PdfReader Modify) + /SMask en bytes crudos.
            // ---------------------------------------------------------------
            try
            {
                var sw = Stopwatch.StartNew();
                using (var doc = new PdfDocument())
                {
                    var page1 = doc.AddPage();
                    page1.Width = XUnit.FromPoint(612);
                    page1.Height = XUnit.FromPoint(792);
                    using (var gfx = XGraphics.FromPdfPage(page1))
                    {
                        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 255, 0, 0)),
                            0, 0, page1.Width.Point, page1.Height.Point);
                        if (signaturePng != null)
                        {
                            using (var imgMs = new MemoryStream(signaturePng, 0, signaturePng.Length, false, true))
                            using (var ximg = XImage.FromStream(imgMs))
                            {
                                gfx.DrawImage(ximg, 50, 50, 200, 75);
                            }
                        }
                    }

                    var page2 = doc.AddPage();
                    page2.Width = XUnit.FromPoint(612);
                    page2.Height = XUnit.FromPoint(792);
                    using (var gfx2 = XGraphics.FromPdfPage(page2))
                    {
                        if (qrPng != null)
                        {
                            using (var qrMs = new MemoryStream(qrPng, 0, qrPng.Length, false, true))
                            using (var xqr = XImage.FromStream(qrMs))
                            {
                                gfx2.DrawImage(xqr, 50, 50, 150, 150);
                            }
                        }
                    }

                    using (var outMs = new MemoryStream())
                    {
                        doc.Save(outMs, false);
                        pdfBytes = outMs.ToArray();
                    }
                }

                // Round-trip: re-abrir el PDF producido desde los bytes.
                int roundtripPages;
                using (var inMs = new MemoryStream(pdfBytes))
                using (var reopened = PdfReader.Open(inMs, PdfDocumentOpenMode.Modify))
                {
                    roundtripPages = reopened.PageCount;
                }

                bool smaskFound = ContainsAscii(pdfBytes, "/SMask");
                sw.Stop();

                pdfsharpJson = "{" + J("ok", true) + "," + J("ms", sw.ElapsedMilliseconds) + "," +
                    J("bytes", pdfBytes.Length) + "," + J("smaskFound", smaskFound) + "," +
                    J("roundtripPages", roundtripPages) + "}";
                trace.Trace("pdfsharp OK: {0} ms, {1} bytes, smask={2}, pages={3}",
                    sw.ElapsedMilliseconds, pdfBytes.Length, smaskFound, roundtripPages);
            }
            catch (Exception ex)
            {
                pdfsharpJson = FailJson(ex);
                trace.Trace("pdfsharp FAIL: {0}", ex);
            }

            // ---------------------------------------------------------------
            // c2. SONDA de bisección: el importador PNG de PDFsharp traga la
            // excepción real (catch{} → null → "Unsupported image format").
            // Llamamos al decoder público BigGustave DIRECTO para capturarla.
            // ---------------------------------------------------------------
            string pngProbeJson;
            try
            {
                if (signaturePng == null) throw new InvalidOperationException("sin png de firma");
                var probe = PdfSharp.Internal.Png.BigGustave.Png.Open(signaturePng);
                pngProbeJson = "{" + J("ok", true) + "," + J("width", probe.Width) + "," + J("height", probe.Height) +
                    "," + J("hasAlpha", probe.HasAlphaChannel) + "}";
                trace.Trace("pngprobe OK: {0}x{1} alpha={2}", probe.Width, probe.Height, probe.HasAlphaChannel);
            }
            catch (Exception ex)
            {
                pngProbeJson = FailJson(ex);
                trace.Trace("pngprobe FAIL: {0}", ex);
            }

            // ---------------------------------------------------------------
            // c3. RONDA DEFINITIVA de sondas — todos los sospechosos de una vez.
            // ---------------------------------------------------------------
            string probes = "{}";
            try
            {
                var sbp = new StringBuilder("{");

                // p1: primeros 16 bytes del PNG en memoria del sandbox (¿cabecera intacta?)
                try
                {
                    var hex = new StringBuilder(32);
                    for (int i = 0; i < 16 && signaturePng != null && i < signaturePng.Length; i++)
                        hex.Append(signaturePng[i].ToString("X2"));
                    sbp.Append(J("first16", hex.ToString()));
                }
                catch (Exception ex) { sbp.Append(J("first16", "FAIL " + ex.Message)); }

                // p2: PdfReader.TestPdfFile sobre el PNG (¿lo confunde con PDF? ¿mueve la posición?)
                try
                {
                    using (var ms = new MemoryStream(signaturePng, 0, signaturePng.Length, false, true))
                    {
                        int v = PdfReader.TestPdfFile(ms);
                        sbp.Append(",").Append(J("testPdfFile", v + " pos=" + ms.Position));
                    }
                }
                catch (Exception ex) { sbp.Append(",").Append(J("testPdfFile", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }

                // p3: primer toque del logging de PDFsharp (Microsoft.Extensions.Logging en net462)
                try
                {
                    var lg = PdfSharp.Logging.PdfSharpLogHost.Logger;
                    sbp.Append(",").Append(J("logHost", lg != null ? "ok " + lg.GetType().Name : "null"));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("logHost", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }

                // p4: XImage.FromStream con el PNG del QR (otro color type — ¿varietal o sistémico?)
                try
                {
                    using (var ms = new MemoryStream(qrPng, 0, qrPng.Length, false, true))
                    using (var xi = XImage.FromStream(ms))
                        sbp.Append(",").Append(J("ximageQr", "ok " + xi.PixelWidth + "x" + xi.PixelHeight));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("ximageQr", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }

                // p5: loop completo GetPixel RGBA (la copia de píxeles del importador, replicada)
                try
                {
                    var p = PdfSharp.Internal.Png.BigGustave.Png.Open(signaturePng);
                    int translucidos = 0;
                    for (int y = 0; y < p.Height; y++)
                        for (int x = 0; x < p.Width; x++)
                            if (p.GetPixel(x, y).A != 255) translucidos++;
                    sbp.Append(",").Append(J("pixelLoop", "ok translucidos=" + translucidos));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("pixelLoop", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }

                // p6: FirstChanceException — ver la excepción REAL que el importador traga.
                try
                {
                    var capturadas = new System.Collections.Generic.List<string>();
                    EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> handler =
                        (s, e2) => { if (capturadas.Count < 3) capturadas.Add(e2.Exception.GetType().FullName + ": " + e2.Exception.Message); };
                    AppDomain.CurrentDomain.FirstChanceException += handler;
                    try
                    {
                        using (var ms = new MemoryStream(signaturePng, 0, signaturePng.Length, false, true))
                        {
                            try { XImage.FromStream(ms).Dispose(); } catch { /* la de siempre */ }
                        }
                    }
                    finally { AppDomain.CurrentDomain.FirstChanceException -= handler; }
                    sbp.Append(",").Append(J("firstChance", string.Join(" || ", capturadas.ToArray())));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("firstChance", "NO DISPONIBLE " + ex.GetType().Name + ": " + ex.Message)); }

                // p7/p8: ¿es el importador PNG o la infraestructura común? JPEG y BMP por la misma puerta.
                try
                {
                    byte[] jpg;
                    using (var img = new Image<Rgba32>(50, 20))
                    using (var ms = new MemoryStream())
                    {
                        img.Save(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
                        jpg = ms.ToArray();
                    }
                    using (var ms2 = new MemoryStream(jpg, 0, jpg.Length, false, true))
                    using (var xi = XImage.FromStream(ms2))
                        sbp.Append(",").Append(J("ximageJpeg", "ok " + xi.PixelWidth + "x" + xi.PixelHeight));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("ximageJpeg", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }
                try
                {
                    byte[] bmp;
                    using (var img = new Image<Rgba32>(50, 20))
                    using (var ms = new MemoryStream())
                    {
                        img.Save(ms, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
                        bmp = ms.ToArray();
                    }
                    using (var ms2 = new MemoryStream(bmp, 0, bmp.Length, false, true))
                    using (var xi = XImage.FromStream(ms2))
                        sbp.Append(",").Append(J("ximageBmp", "ok " + xi.PixelWidth + "x" + xi.PixelHeight));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("ximageBmp", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }

                // p9: Png.Open CON visitor de chunks — la única diferencia restante con el importador.
                try
                {
                    var visitante = new VisitanteDeChunks();
                    var p9 = PdfSharp.Internal.Png.BigGustave.Png.Open(signaturePng, visitante);
                    sbp.Append(",").Append(J("pngConVisitor", "ok chunks=" + visitante.Chunks));
                }
                catch (Exception ex) { sbp.Append(",").Append(J("pngConVisitor", "FAIL " + ex.GetType().Name + ": " + ex.Message)); }

                sbp.Append("}");
                probes = sbp.ToString();
            }
            catch (Exception ex) { probes = "{" + J("error", ex.Message) + "}"; }

            // ---------------------------------------------------------------
            // c4. PLAN B DEL MOTOR: PDF con imagen escrita A MANO (XObject
            //     FlateDecode + SMask) — sin el importador PNG roto del sandbox.
            // ---------------------------------------------------------------
            string pdfManualJson;
            try
            {
                var sw = Stopwatch.StartNew();
                // Píxeles vía BigGustave (probado en sandbox) desde el PNG de la firma.
                var png = PdfSharp.Internal.Png.BigGustave.Png.Open(signaturePng);
                int w = png.Width, h = png.Height;
                var rgb = new byte[w * h * 3];
                var alpha = new byte[w * h];
                int ri = 0, ai = 0;
                bool hayAlfa = false;
                for (int yy = 0; yy < h; yy++)
                {
                    for (int xx = 0; xx < w; xx++)
                    {
                        var px = png.GetPixel(xx, yy);
                        rgb[ri++] = px.R; rgb[ri++] = px.G; rgb[ri++] = px.B;
                        alpha[ai++] = px.A;
                        hayAlfa |= px.A != 255;
                    }
                }

                using (var doc = new PdfDocument())
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(612);
                    page.Height = XUnit.FromPoint(792);
                    using (var gfx = XGraphics.FromPdfPage(page))
                    {
                        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(255, 255, 0, 0)),
                            0, 0, page.Width.Point, page.Height.Point);
                    }
                    XObjectManual.DibujarImagenRgba(doc, page, "SigilImg0", w, h, rgb, hayAlfa ? alpha : null,
                        x: 50, y: 500, w: 200, h: 75);

                    using (var outMs = new MemoryStream())
                    {
                        doc.Save(outMs, closeStream: false);
                        var bytes = outMs.ToArray();
                        bool smask = System.Text.Encoding.ASCII.GetString(bytes).Contains("/SMask");
                        int paginas;
                        using (var rt = new MemoryStream(bytes, 0, bytes.Length, false, true))
                        using (var reDoc = PdfSharp.Pdf.IO.PdfReader.Open(rt, PdfDocumentOpenMode.Import))
                            paginas = reDoc.PageCount;
                        sw.Stop();
                        pdfManualJson = "{" + J("ok", true) + "," + J("ms", sw.ElapsedMilliseconds) + "," +
                            J("bytes", bytes.Length) + "," + J("smaskFound", smask) + "," + J("roundtripPages", paginas) + "}";
                        trace.Trace("pdfmanual OK: {0} bytes, smask={1}, pages={2}", bytes.Length, smask, paginas);
                        if (pdfBytes == null) pdfBytes = bytes; // sha256/TSA operan sobre ESTE pdf
                    }
                }
            }
            catch (Exception ex)
            {
                pdfManualJson = FailJson(ex);
                trace.Trace("pdfmanual FAIL: {0}", ex);
            }

            // ---------------------------------------------------------------
            // d. SHA-256 del PDF (BCL).
            // ---------------------------------------------------------------
            try
            {
                if (pdfBytes != null)
                {
                    using (var sha = SHA256.Create())
                    {
                        digest = sha.ComputeHash(pdfBytes);
                    }
                    sha256Json = Quote(ToHex(digest));
                    trace.Trace("sha256 OK: {0}", ToHex(digest));
                }
                else
                {
                    sha256Json = Quote("SKIPPED: no pdf bytes");
                }
            }
            catch (Exception ex)
            {
                sha256Json = Quote("FAIL: " + ex.Message);
                trace.Trace("sha256 FAIL: {0}", ex);
            }

            // ---------------------------------------------------------------
            // e. TSA RFC 3161 — BouncyCastle + HttpClient, 15 s por endpoint,
            //    cada endpoint se prueba con independencia del otro.
            // ---------------------------------------------------------------
            try
            {
                // net462: TLS 1.2 no siempre es default — forzarlo antes de tocar la red.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (Exception ex)
            {
                trace.Trace("ServicePointManager FAIL (se sigue igual): {0}", ex.Message);
            }

            if (digest == null)
            {
                // Sin PDF no hay hash real: timestampear un hash sintético para
                // igualmente medir la alcanzabilidad de la TSA desde el sandbox.
                try
                {
                    using (var sha = SHA256.Create())
                    {
                        digest = sha.ComputeHash(Encoding.UTF8.GetBytes("sigil-sandbox-spike-fallback"));
                    }
                }
                catch
                {
                    // sin digest, el paso TSA reporta el error por endpoint
                }
            }

            var tsaResults = new StringBuilder("[");
            var secureRandom = new SecureRandom();
            for (int e = 0; e < TsaEndpoints.Length; e++)
            {
                string endpoint = TsaEndpoints[e];
                if (e > 0) tsaResults.Append(",");
                var sw = Stopwatch.StartNew();
                int httpStatus = 0;
                try
                {
                    if (digest == null)
                    {
                        throw new InvalidOperationException("no digest available");
                    }

                    var reqGen = new TimeStampRequestGenerator();
                    reqGen.SetCertReq(true);
                    var nonce = new BigInteger(128, secureRandom);
                    TimeStampRequest request = reqGen.Generate(TspAlgorithms.Sha256, digest, nonce);
                    byte[] reqBytes = request.GetEncoded();

                    byte[] respBytes;
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("sigil-spike-sandbox/1.0");
                        var content = new ByteArrayContent(reqBytes);
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
                        HttpResponseMessage httpResp = http.PostAsync(endpoint, content).GetAwaiter().GetResult();
                        httpStatus = (int)httpResp.StatusCode;
                        respBytes = httpResp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                        sw.Stop();
                        if (!httpResp.IsSuccessStatusCode)
                        {
                            tsaResults.Append("{" + J("endpoint", endpoint) + "," + J("ok", false) + "," +
                                J("httpStatus", httpStatus) + "," + J("latencyMs", sw.ElapsedMilliseconds) + "," +
                                J("error", "non-success HTTP status") + "}");
                            trace.Trace("tsa {0}: HTTP {1} in {2} ms", endpoint, httpStatus, sw.ElapsedMilliseconds);
                            continue;
                        }
                    }

                    var response = new TimeStampResponse(respBytes);
                    response.Validate(request); // lanza si nonce/imprint/certReq no coinciden
                    TimeStampToken token = response.TimeStampToken;
                    if (token == null)
                    {
                        tsaResults.Append("{" + J("endpoint", endpoint) + "," + J("ok", false) + "," +
                            J("httpStatus", httpStatus) + "," + J("latencyMs", sw.ElapsedMilliseconds) + "," +
                            J("error", "PKIStatus=" + response.Status + " sin token") + "}");
                        continue;
                    }

                    string genTime = token.TimeStampInfo.GenTime.ToString("o", CultureInfo.InvariantCulture);
                    int tokenBytes = token.GetEncoded().Length;
                    tsaResults.Append("{" + J("endpoint", endpoint) + "," + J("ok", true) + "," +
                        J("httpStatus", httpStatus) + "," + J("latencyMs", sw.ElapsedMilliseconds) + "," +
                        J("validateOk", true) + "," + J("genTime", genTime) + "," +
                        J("tokenBytes", tokenBytes) + "}");
                    trace.Trace("tsa {0}: OK {1} ms, genTime={2}, token={3} bytes",
                        endpoint, sw.ElapsedMilliseconds, genTime, tokenBytes);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    // La excepción real viene envuelta (AggregateException / TaskCanceled).
                    Exception root = ex;
                    while (root.InnerException != null) root = root.InnerException;
                    string error = ex.GetType().Name + ": " + ex.Message +
                        (ReferenceEquals(root, ex) ? "" : " | root: " + root.GetType().Name + ": " + root.Message);
                    tsaResults.Append("{" + J("endpoint", endpoint) + "," + J("ok", false) + "," +
                        J("httpStatus", httpStatus) + "," + J("latencyMs", sw.ElapsedMilliseconds) + "," +
                        J("error", error) + "}");
                    trace.Trace("tsa {0} FAIL ({1} ms): {2}", endpoint, sw.ElapsedMilliseconds, error);
                }
            }
            tsaResults.Append("]");
            tsaJson = tsaResults.ToString();

            total.Stop();
            string json = "{" +
                "\"sandbox\":" + sandboxJson + "," +
                "\"imagesharp\":" + imagesharpJson + "," +
                "\"qrcoder\":" + qrcoderJson + "," +
                "\"pdfsharp\":" + pdfsharpJson + "," +
                "\"pngprobe\":" + pngProbeJson + "," +
                "\"probes\":" + probes + "," +
                "\"pdfmanual\":" + pdfManualJson + "," +
                "\"sha256\":" + sha256Json + "," +
                "\"tsa\":" + tsaJson + "," +
                J("totalMs", total.ElapsedMilliseconds) +
                "}";

            trace.Trace("RESULT ({0} ms total): {1}", total.ElapsedMilliseconds, json);
            context.OutputParameters["Result"] = json;
        }

        // --- helpers -------------------------------------------------------

        private static void DrawThickLine(Image<Rgba32> img, double x0, double y0, double x1, double y1,
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
                {
                    for (int px = minX; px <= maxX; px++)
                    {
                        double ddx = px - cx, ddy = py - cy;
                        if (ddx * ddx + ddy * ddy <= radius * radius)
                        {
                            img[px, py] = color;
                        }
                    }
                }
            }
        }

        private static bool ContainsAscii(byte[] haystack, string needle)
        {
            byte[] n = Encoding.ASCII.GetBytes(needle);
            for (int i = 0; i <= haystack.Length - n.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < n.Length; j++)
                {
                    if (haystack[i + j] != n[j]) { match = false; break; }
                }
                if (match) return true;
            }
            return false;
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private static string FailJson(Exception ex)
        {
            Exception root = ex;
            while (root.InnerException != null) root = root.InnerException;
            string error = ex.GetType().Name + ": " + ex.Message +
                (ReferenceEquals(root, ex) ? "" : " | root: " + root.GetType().Name + ": " + root.Message);
            // Diagnóstico del sandbox: stack completo (recortado) — es la única vía de lectura
            // cuando el plugintracelog no está disponible.
            string full = ex.ToString();
            if (full.Length > 1800) full = full.Substring(0, 1800) + "...(truncado)";
            return "{" + J("ok", false) + "," + J("error", error) + "," + J("stack", full) + "}";
        }

        private static string J(string name, string value) => Quote(name) + ":" + Quote(value);
        private static string J(string name, bool value) => Quote(name) + ":" + (value ? "true" : "false");
        private static string J(string name, long value) => Quote(name) + ":" + value.ToString(CultureInfo.InvariantCulture);

        private static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

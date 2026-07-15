// Spike 2 — BouncyCastle 2.6.x RFC 3161 timestamping against real TSAs.
// Computes SHA-256 of result.pdf (spike 1), builds a TimeStampRequest with
// CertReq=true + random nonce, POSTs to DigiCert and Sectigo, validates the
// response and the token signature with the embedded signer certificate.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;

string dir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
string targetFile = Path.Combine(dir, "result.pdf");

var bcAsm = typeof(TimeStampRequest).Assembly;
Console.WriteLine($"[versions] BouncyCastle.Cryptography = {bcAsm.GetName().Version} " +
    $"({System.Diagnostics.FileVersionInfo.GetVersionInfo(bcAsm.Location).ProductVersion})");
Console.WriteLine($"[versions] Runtime = {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine();

// 1. SHA-256 of result.pdf
byte[] fileBytes = File.ReadAllBytes(targetFile);
byte[] digest = SHA256.HashData(fileBytes);
Console.WriteLine($"[1] Target: {targetFile} ({fileBytes.Length} bytes)");
Console.WriteLine($"[1] SHA-256 = {Convert.ToHexString(digest)}");
Console.WriteLine();

var tsas = new (string Name, string Url)[]
{
    ("digicert", "https://timestamp.digicert.com"),
    ("sectigo", "https://timestamp.sectigo.com"),
};

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("sigil-spike-tsa/1.0");
var secureRandom = new SecureRandom();

foreach (var (name, url) in tsas)
{
    Console.WriteLine($"=== TSA: {name} ({url}) ===");
    try
    {
        // 2. Build request: CertReq = true, random nonce, SHA-256 imprint.
        var reqGen = new TimeStampRequestGenerator();
        reqGen.SetCertReq(true);
        var nonce = new BigInteger(128, secureRandom);
        TimeStampRequest request = reqGen.Generate(TspAlgorithms.Sha256, digest, nonce);
        byte[] reqBytes = request.GetEncoded();
        File.WriteAllBytes(Path.Combine(dir, $"tsa-{name}.tsq"), reqBytes);
        Console.WriteLine($"[2] Request built: {reqBytes.Length} bytes DER, nonce = {nonce.ToString(16)}");

        // 3. POST application/timestamp-query
        var content = new ByteArrayContent(reqBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        var sw = Stopwatch.StartNew();
        HttpResponseMessage httpResp = http.PostAsync(url, content).GetAwaiter().GetResult();
        byte[] respBytes = httpResp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        sw.Stop();
        Console.WriteLine($"[3] HTTP {(int)httpResp.StatusCode} {httpResp.StatusCode}, " +
            $"Content-Type = {httpResp.Content.Headers.ContentType}, " +
            $"body = {respBytes.Length} bytes, latency = {sw.ElapsedMilliseconds} ms");
        if (!httpResp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[!] Non-success HTTP status; skipping parse for {name}.");
            Console.WriteLine();
            continue;
        }

        // 4. Parse + validate response and token.
        var response = new TimeStampResponse(respBytes);
        Console.WriteLine($"[4] PKIStatus = {response.Status} " +
            $"(statusString='{response.GetStatusString()}', failInfo={(response.GetFailInfo()?.ToString() ?? "none")})");
        response.Validate(request);   // throws if nonce/imprint/certReq mismatch
        Console.WriteLine("[4] response.Validate(request) => OK (nonce + imprint + certReq match)");

        TimeStampToken? token = response.TimeStampToken;
        if (token is null)
        {
            Console.WriteLine("[!] No TimeStampToken in response (rejected?).");
            Console.WriteLine();
            continue;
        }

        var tstInfo = token.TimeStampInfo;
        Console.WriteLine($"[4] genTime = {tstInfo.GenTime:O}");
        Console.WriteLine($"[4] serialNumber = {tstInfo.SerialNumber.ToString(16)}");
        Console.WriteLine($"[4] policy OID = {tstInfo.Policy}");
        Console.WriteLine($"[4] hash algorithm = {tstInfo.HashAlgorithm.Algorithm}");
        Console.WriteLine($"[4] nonce echoed = {tstInfo.Nonce?.ToString(16) ?? "(none)"}");

        // Certificates embedded in the token (CertReq honored?)
        IStore<X509Certificate> certStore = token.GetCertificates();
        var allCerts = new List<X509Certificate>(certStore.EnumerateMatches(null));
        var signerCerts = new List<X509Certificate>(certStore.EnumerateMatches(token.SignerID));
        Console.WriteLine($"[4] certs embedded in token = {allCerts.Count} " +
            $"(CertReq honored: {(allCerts.Count > 0 ? "YES" : "NO")})");
        Console.WriteLine($"[4] certs matching SignerID = {signerCerts.Count}");

        if (signerCerts.Count > 0)
        {
            X509Certificate signerCert = signerCerts[0];
            Console.WriteLine($"[4] TSA cert subject = {signerCert.SubjectDN}");
            Console.WriteLine($"[4] TSA cert issuer  = {signerCert.IssuerDN}");
            Console.WriteLine($"[4] TSA cert validity = {signerCert.NotBefore:yyyy-MM-dd} .. {signerCert.NotAfter:yyyy-MM-dd}");
            token.Validate(signerCert);  // signature + cert validity + ESSCertID check
            Console.WriteLine("[4] token.Validate(embeddedSignerCert) => OK (signature valid)");
        }
        else
        {
            Console.WriteLine("[!] No signer cert embedded despite CertReq=true — cannot validate with embedded cert.");
        }

        // 5. Sizes for the Dataverse memo column budget.
        byte[] tokenDer = token.GetEncoded();
        string tokenB64 = Convert.ToBase64String(tokenDer);
        File.WriteAllBytes(Path.Combine(dir, $"tsa-{name}.tsr"), tokenDer);
        File.WriteAllBytes(Path.Combine(dir, $"tsa-{name}-full-response.der"), respBytes);
        Console.WriteLine($"[5] token DER size = {tokenDer.Length} bytes, base64 length = {tokenB64.Length} chars");
        Console.WriteLine($"[5] full TimeStampResp DER = {respBytes.Length} bytes");
        Console.WriteLine($"[5] saved: tsa-{name}.tsr (token) + tsa-{name}-full-response.der");
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"[FINDING] Network failure against {name}: {ex.Message}");
    }
    catch (TaskCanceledException ex)
    {
        Console.WriteLine($"[FINDING] Timeout against {name}: {ex.Message}");
    }
    catch (TspException ex)
    {
        Console.WriteLine($"[FINDING] TSP validation failure against {name}: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FINDING] Unexpected failure against {name}: {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}

Console.WriteLine("DONE spike-tsa-rfc3161");

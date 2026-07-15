# Resultados de Spikes Técnicos — Sigil

**Fecha:** 2026-07-10
**Entorno:** Linux x64, .NET 8.0.28 (SDK 10.0.202), instalado user-local en `~/.dotnet`.

> **Caveat global — net8.0 vs net462:** el target real del proyecto es .NET Framework 4.6.2, que no
> corre en Linux. Estos spikes corrieron sobre .NET 8. Las bibliotecas bajo prueba (PDFsharp,
> ImageSharp, BouncyCastle) son el MISMO código managed en ambos targets, pero queda pendiente
> re-validar en net462/Windows (en particular: PDFsharp tiene builds GDI/WPF distintos en Windows —
> acá se usó el build Core, que es el que aplica a un sandbox de Dataverse). El sandbox de Dataverse
> NO fue probado.

---

## Spike 1 — PDFsharp: transparencia PNG (issue empira/PDFsharp#187)

### Qué corrió

Proyecto: `spike-pdfsharp-alpha/` (consola, net8.0).

**Versiones exactas:**
- PDFsharp **6.2.4** (`6.2.4+362d8053`)
- SixLabors.ImageSharp **2.1.13** (`2.1.13+2c3967b2`)

Flujo:
1. Con ImageSharp se generó `signature.png`: RGBA 8-bit, **no interlaced**, 600x200 px, fondo
   transparente, 5 trazos diagonales negros opacos (firma simulada) y una zona gris
   semi-transparente rgba(100,100,100,**128**) en x∈[450,590), y∈[40,160).
2. `base.pdf`: página Letter (612x792 pt) con rectángulo rojo saturado (255,0,0) a página completa.
3. `base.pdf` abierto con `PdfReader.Open(..., PdfDocumentOpenMode.Modify)`, imagen dibujada con
   `XGraphics.FromPdfPage` + `XImage.FromStream` en (6,100) tamaño 600x200 pt (1 pt = 1 px a 72 dpi),
   guardado como `result.pdf`.
4. Verificación doble: inspección de objetos PDF + render con `pdftoppm` (poppler) a 72 dpi y
   muestreo de píxeles.
5. Test de página rotada: página A4 (595x842 pt) con `page.Rotate = 90`.

### Checks

| Check | Resultado | Evidencia |
|---|---|---|
| PNG fuente correcto (RGBA8, alpha real) | **PASS** | Relectura: (500,20)=(0,0,0,0), (70,100)=(0,0,0,255), (520,100)=(100,100,100,128) |
| `/SMask` presente en el XObject de imagen | **PASS** | XObject `/I0`: `/Subtype /Image`, 600x200, BPC=8, `/DeviceRGB`, `/FlateDecode`, **`/SMask 10 0 R`** presente; SMask 600x200, BPC=8, `/DeviceGray`, stream 1597 bytes. Confirmado también con `rg -a "/SMask" result.pdf` sobre el archivo crudo. |
| Alpha 0 (transparente) deja ver el fondo | **PASS** | Render 72 dpi: píxel (506,120) — zona transparente del PNG sobre fondo rojo — = **(255,0,0) ROJO**, no blanco. También (306,290)=(255,0,0). |
| Trazo opaco se dibuja | **PASS** | Píxel (76,200) = (0,0,0) negro. |
| Alpha 128 compone correctamente | **PASS** | Píxel (526,200) = **(177,50,50)**, exactamente el blend teórico de gris(100) α=128 sobre rojo: (100·128+255·127)/255≈177, (100·128)/255≈50. |
| Fondo intacto fuera de la imagen | **PASS** | (50,50) y (306,400) = (255,0,0). |

**Sobre el issue #187:** con PDFsharp **6.2.4** el bug de pérdida de alpha en `DrawImage` **NO se
reproduce** (el reporte era contra 6.2.0). El alpha sobrevive completo: canal soft-mask separado
(`/SMask` en `/DeviceGray`) + composición correcta tanto binaria (0/255) como parcial (128).

### Test de página rotada (contrato de coordenadas §6.1 doc 04)

Página A4 portrait (595x842 pt) con `/Rotate 90`. Marcadores: cuadrado negro 100x100 en XGraphics
(0,0); cuadrado azul en (`PageSize.Width`-100, 0); fondo verde.

Evidencia:
- `gfx.PageSize` reportó **595 x 842** — es decir, NO intercambia ancho/alto según la rotación.
- Render (`pdftoppm` aplica /Rotate → salida landscape 842x595): cuadrado **negro en la esquina
  SUPERIOR DERECHA** visual (píxel (812,30)=(0,0,0)); cuadrado **azul en la INFERIOR DERECHA**
  ((812,565)=(0,0,255)); superior-izquierda e inferior-izquierda = verde (0,160,0).

**Conclusión: PDFsharp 6.2.4 trabaja en orientación RAW-MEDIA, NO visual.** El origen de XGraphics
(0,0) es la esquina superior-izquierda del media box SIN rotar; con /Rotate 90 esa esquina termina
arriba-a-la-derecha en pantalla. `XGraphics.FromPdfPage` **no compensa** `/Rotate`.
→ Para el contrato §6.1: si las coordenadas de firma vienen en orientación visual, Sigil DEBE
aplicar la transformación de rotación manualmente (o normalizar páginas rotadas) antes de dibujar.

### Veredicto Spike 1

> **RIESGO DESCARTADO (en 6.2.4): la transparencia PNG sobrevive a DrawImage — usar PDFsharp ≥ 6.2.4, evitar 6.2.0.**
> **RIESGO CONFIRMADO (rotación): XGraphics usa orientación raw-media; páginas con /Rotate ≠ 0 requieren transformación explícita de coordenadas en nuestro código.**

Artefactos: `signature.png`, `base.pdf`, `result.pdf`, `rotated.pdf`, `result-render-1.png`,
`rotated-render-1.png`, código en `spike-pdfsharp-alpha/`.

---

## Spike 2 — BouncyCastle: RFC 3161 contra TSAs reales

### Qué corrió

Proyecto: `spike-tsa-rfc3161/` (consola, net8.0).

**Versión exacta:** BouncyCastle.Cryptography **2.6.2** (`2.6.2+b4f2f6ad76`).

Flujo: SHA-256 de `result.pdf` (spike 1) → `TimeStampRequestGenerator` con **CertReq=true** y nonce
aleatorio de 128 bits → POST `application/timestamp-query` → parse `TimeStampResponse` →
`response.Validate(request)` → `token.Validate(certFirmante)` usando el certificado **embebido en el
token** (extraído vía `token.GetCertificates().EnumerateMatches(token.SignerID)`).

Hash timestampeado: `SHA-256(result.pdf) = 3D2DAA37686B689AC0DB0EB4153FD7E61723B893209F0EDC8279B622981CBA88`

### Sectigo (https://timestamp.sectigo.com) — 1 sola llamada (respetando throttle)

| Métrica | Valor |
|---|---|
| HTTP | **200 OK**, `Content-Type: application/timestamp-reply` |
| Latencia total | **815 ms** |
| PKIStatus | 0 (**Granted**) |
| `response.Validate(request)` | **PASS** (nonce + imprint + certReq coinciden; nonce eco: `38b7d3cbda55930866ef836db0f599ca`) |
| genTime | **2026-07-10T16:43:16Z** |
| CertReq honrado | **SÍ — 3 certificados embebidos** (cadena completa), 1 matchea SignerID |
| `token.Validate(certEmbebido)` | **PASS** (firma del token válida con el cert incluido) |
| Cert TSA — Subject | `C=GB, ST=Greater London, O=Sectigo Limited, CN=Sectigo Public Time Stamping Signer R37` |
| Cert TSA — Issuer | `C=GB, O=Sectigo Limited, CN=Sectigo Public Time Stamping CA R41` (válido 2026-03-25 → 2037-06-24) |
| Policy OID | `1.3.6.1.4.1.6449.2.1.1` |
| **Token DER** | **6.633 bytes** |
| **Token en Base64** | **8.844 caracteres** |
| TimeStampResp completo | 6.642 bytes DER |

Verificación cruzada independiente con `openssl ts -reply -text`: Status **Granted**, mismo imprint
SHA-256, mismo nonce, mismo genTime. Coincide 100% con lo parseado por BouncyCastle.

**Presupuesto Dataverse:** un token con cadena completa ronda **~6,5 KB DER ≈ ~8,9 K chars base64**.
Una columna memo (default 2.000 chars, máx. configurable ~1M) alcanza de sobra si se configura
`MaxLength ≥ 10.000` por token; presupuestar **~12K chars por token** deja margen (los tokens de
otras TSAs con cadenas más largas pueden crecer).

### DigiCert (https://timestamp.digicert.com) — FINDING de red, no crash

- El programa manejó el fallo con gracia: `[FINDING] Timeout ... HttpClient.Timeout of 30 seconds`.
- Diagnóstico posterior: DNS resuelve (`216.168.244.9`) pero el **TCP connect al puerto 443 expira**
  también con `curl` (20 s), incluso para un GET simple. Sectigo desde el mismo host responde en
  ~120 ms. → **Bloqueo de red del entorno hacia esa IP/red, NO un problema de BouncyCastle ni del
  protocolo.** El request DER (75 bytes) quedó guardado en `tsa-digicert.tsq` para reproducir desde
  una red sin bloqueo.
- Pendiente: repetir contra DigiCert desde otra red (o desde el sandbox real).

### Veredicto Spike 2

> **RIESGO DESCARTADO: BouncyCastle 2.6.2 arma, envía, parsea y valida RFC 3161 end-to-end contra una TSA pública real; CertReq=true es honrado (cert embebido) y la firma del token valida con ese cert.**
> **DATO PARA DISEÑO: presupuestar ~12K chars base64 por token en la columna memo de Dataverse.**
> **PENDIENTE: DigiCert inaccesible desde esta red (bloqueo TCP a nivel entorno) — re-probar desde otra red; tener SIEMPRE ≥2 TSAs configuradas como fallback.**

Artefactos: `tsa-sectigo.tsr` (token DER), `tsa-sectigo-full-response.der`, `tsa-sectigo.tsq`,
`tsa-digicert.tsq`, código en `spike-tsa-rfc3161/`.

---

## Caveats generales

1. **net8.0, no net462:** mismo código managed de las libs, pero sin validar en .NET Framework 4.6.2
   / Windows. PDFsharp en Windows ofrece builds GDI/WPF con code paths de imagen distintos — el
   build Core (el probado) es el relevante para sandbox.
2. **Sandbox de Dataverse sin probar:** ni restricciones de sandbox (partial trust, egress HTTP,
   límite de 2 min) ni el tamaño real permitido por la columna memo fueron validados contra un
   entorno real.
3. **PDFsharp 6.2.4 ≠ 6.2.0:** el fix del alpha aplica a la versión probada; **fijar (pin) la
   versión ≥ 6.2.4** en el proyecto real.
4. **Render de verificación:** poppler (`pdftoppm`) como oráculo de rendering; Adobe puede diferir
   en edge cases, no en composición básica de SMask.

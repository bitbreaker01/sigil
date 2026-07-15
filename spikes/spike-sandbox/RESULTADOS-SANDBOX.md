# Resultados del spike en sandbox real de Dataverse

**Fecha:** 2026-07-15 · **Ambiente:** Dev (`org36e60d9d.crm.dynamics.com`) · **Vehículo:** plugin package `sanic_SigilSpike` (net462) ejecutado vía Custom API `sanic_sigil_capi_RunSpike` con isolationMode=2 (sandbox), runtime CLR 4.0.30319.

**Este documento consolida DOS corridas** (cada número se atribuye a la suya):
- **v1.0.8** — los 3 veredictos originales. Evidencia cruda: `evidencia-resultado-v1.0.8.json`.
- **v1.0.9** — la ampliación de System.Text.Json (paso `f`). Evidencia cruda: `evidencia-resultado-v1.0.9.json`.

Nota: el `sha256` y las latencias de TSA **difieren entre corridas** — el sha porque la serialización PDF no es determinística (doc 04 §7); las latencias por variación de red. Las cifras de los veredictos 1–3 son de v1.0.8; las de la ampliación STJ, de v1.0.9.

---

## Los 3 veredictos

### 1. ¿El stack corre en el sandbox? — ✅ SÍ

| Paso | Resultado | Evidencia |
|------|-----------|-----------|
| ImageSharp 2.1.x — PNG RGBA 400×150 con alfa | ✅ | 1.470 bytes en 4 ms |
| QRCoder `PngByteQRCode` (sin System.Drawing) | ✅ | 584 bytes en 1 ms |
| PDFsharp 6.2.4 — composición de PDF | ✅ **vía XObject manual** (ver veredicto 3) | 4.341 bytes en 11 ms, `/SMask` presente, round-trip `PdfReader` OK (1 página) |
| SHA-256 del PDF | ✅ | `0313D9B3...CECD88` |
| BouncyCastle 2.6.x RFC 3161 | ✅ | ver veredicto 2 |
| System.Text.Json — (de)serialización reflexiva de los contratos reales | ✅ | 2 participants + 1 zone + roundtrip + JsonDocument, 481 ms (corrida v1.0.9, ver abajo) |

Bonus (evidencia de build, no del JSON): el nupkg que corrió en el sandbox **se generó con `dotnet pack` en Linux** — el pipeline de build no necesita Windows para empaquetar.

**Ampliación 2026-07-15 (v1.0.9) — System.Text.Json en el sandbox net462:** se agregó el paso `f` que ejercita la **misma ruta reflexiva** que usará el motor: `Deserialize<List<ParticipantInputSpike>>` (Guid + `int?` con `JsonPropertyName`), `Deserialize<List<ZoneInputSpike>>` (double), `Serialize` de vuelta (writer reflexivo) y `JsonDocument.Parse`. Todo verde: `{"ok":true,"ms":481,"participants":2,"zones":1,"roundtripLen":121,"docCount":2,"assemblyVersion":"9.0.0.6"}`. El riesgo real (trust parcial del sandbox → Reflection.Emit bloqueado) **no se materializó**.

**Finding importante — lo MEDIDO vs lo INTERPRETADO:**

- **Medido (hecho):** el nupkg v1.0.9 empaquetó **System.Text.Json assembly `8.0.0.5`** (package 8.0.5 — verificado leyendo el `.dll` del nupkg con `AssemblyName.GetAssemblyName`), y su cadena de 7 dependencias viaja en el package (confirmado con `unzip -l`). Sin embargo, en runtime el sandbox reportó `assemblyVersion: 9.0.0.6`. O sea: **corrió una STJ distinta (más nueva) a la que shippeamos**.
- **Interpretación (hipótesis más plausible, no probada por el spike):** la plataforma Dataverse provee su propia System.Text.Json 9.0 y el loader del sandbox la unifica, igual que hace con `Microsoft.Xrm.Sdk`. El spike **no** instrumentó el mecanismo (binding redirect / load context), así que esto es inferencia consistente con el comportamiento conocido, no medición directa. Hipótesis alternativa no descartada del todo: que el 8.0.5 no se resolviera y el runtime tomara la 9.0.0.6 por otra vía.

**Consecuencia de diseño (independiente de cuál hipótesis sea):** dado que el runtime corre 9.0.0.6, `Sigil.Plugins.Core` y el spike se pinearon a **9.0.6** (el package cuyos assets `lib/net462` se confirmaron en nuget.org — package 9.0.6 → assembly 9.0.0.x según el esquema de versionado de STJ) para que los tests locales corran la misma línea 9.0 que el sandbox — minimiza el skew test↔producción. La verificación de parity exacta se cierra cuando el primer test del motor cargue el package pineado en el ambiente.

### 2. ¿La TSA es alcanzable desde el sandbox? — ✅ SÍ (con fallback obligatorio)

| Endpoint | Resultado | Detalle |
|----------|-----------|---------|
| `https://timestamp.sectigo.com` | ✅ HTTP 200 en **544 ms** | `token.Validate()` OK, genTime real, token **6.633 bytes DER** (≈8.844 chars base64 — presupuesto de memo ~12K confirmado, doc 03) |
| `https://timestamp.digicert.com` | ❌ timeout de cliente a los 15 s, sin respuesta HTTP (`TaskCanceledException`, httpStatus 0) | Consistente con el bloqueo TCP diagnosticado en el spike local — desde el sandbox no se puede distinguir el nivel exacto (TCP/TLS/respuesta lenta) |

**Lección:** la regla ADR-005 de **≥2 TSAs con fallback** no era paranoia — el fallback se necesitó en la PRIMERA corrida real. DigiCert queda configurado pero se re-probará desde la red corporativa.

### 3. ¿El alfa (transparencia) sobrevive? — ✅ SÍ, con ajuste de diseño

**Finding crítico:** `XImage.FromStream(png)` de PDFsharp (versión resuelta **6.2.4**, verificada en `project.assets.json`) falla con "Unsupported image format" para **todos los PNG probados** (firma RGBA e QR, dos color types distintos) bajo net462 dentro del sandbox — aunque el mismo nupkg (DLL sha-idéntica) funciona en net8 local.

Bisección exhaustiva (v1.0.1 → v1.0.7):

- Header PNG perfecto (`89504E470D0A1A0A...49484452`), stream en posición 0.
- El decoder interno de PDFsharp (`BigGustave.Png.Open`) **sí** abre el PNG dentro del sandbox (3 chunks, 400×150, alfa detectado, 57.450 píxeles translúcidos leídos).
- JPEG ✅ y BMP ✅ por el mismo `XImage.FromStream` — solo el importer PNG retorna null silenciosamente.
- `MemoryStream` publiclyVisible, LogHost/NullLogger, first-chance exceptions: todo descartado.

**Plan B implementado y validado** (`XObjectManual.cs`): XObject de imagen construido a mano —
diccionario `/XObject /Image` con stream **FlateDecode RGB** + **`/SMask` DeviceGray** para el canal alfa (wrapper zlib artesanal `0x78 0x9C` + DeflateStream + Adler32, porque net462 no trae ZLibStream), píxeles decodificados con **BigGustave** (el decoder interno de PDFsharp, probado en sandbox), y operador `q cm /img Do Q` en el content stream. Para el motor de producción el decoder será ImageSharp (validado en sandbox por separado, paso a) — esa cadena combinada queda como decisión de diseño, no como parte de lo ya corrido.

Resultado en sandbox: `{"ok":true,"ms":11,"bytes":4341,"smaskFound":true,"roundtripPages":1}` — la transparencia viaja en el `/SMask` y el PDF re-abre limpio.

**Decisión registrada en doc 04 (§1 y §10):** el motor de sellado incrusta imágenes con XObject manual, nunca con `XImage.FromStream` sobre PNG.

---

## Registros creados en Dev (limpiar al cerrar el spike)

| Artefacto | ID |
|-----------|----|
| pluginpackage `sanic_SigilSpike` | `44ece57d-5680-f111-ab0e-70a8a59a720a` |
| pluginassembly | `46ece57d-5680-f111-ab0e-70a8a59a720a` |
| plugintype `SigilSpike.Plugin.RunSpikePlugin` | `47ece57d-5680-f111-ab0e-70a8a59a720a` |
| customapi `sanic_sigil_capi_RunSpike` | `fb3fde83-5680-f111-ab0e-70a8a59a720a` |

## Lecciones operativas

- Cada iteración del nupkg **exige bump de `<Version>`**: `dotnet pack` incremental no regenera el paquete y el sandbox cachea el assembly por versión.
- El sandbox suprime el evento `AppDomain.FirstChanceException` (llega vacío) — no sirve para diagnóstico.
- Registrar plugins requiere **System Administrator** en el ambiente (System Customizer no alcanza para `prvCreatePluginAssembly`) — documentado en runbook A4b.3.

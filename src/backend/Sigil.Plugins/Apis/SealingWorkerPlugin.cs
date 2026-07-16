// WORKER DE SELLADO (ADR-011, doc 04 §7) — step ASÍNCRONO en Update de la transacción
// (filtering attribute: sanic_sigil_status, post-operation). El corazón probatorio.
//
// GUARDS (un guard mal puesto mata el pipeline; uno de menos lo corrompe — doc 04 §7):
//   - Depth > 8 → abortar con trace (anti-loop). JAMÁS `Depth > 1`: el worker corre
//     legítimamente con Depth ≥ 2 (lo dispara el Update de SubmitSignature/RetrySealing).
//   - Post-image con status == Sellando → candidato; otro valor → return (neutraliza el
//     auto-retrigger del paso 9, que escribe Completado).
//   - Estado ACTUAL bajo lock == Sellando (la post-image NO basta: un reintento encolado
//     conserva la post-image vieja aunque T14/T10 hayan corrido en el medio).
//   - Ledger existente ANTES del paso 1 → saltar directo a completar (paso 9). JAMÁS
//     recomponer ni re-subir: el hash del ledger describe bytes que YA existen.
//   - Final file existente SIN ledger → re-descargar ESOS bytes exactos, recalcular
//     hash_final de lo durable, re-pedir TSA y crear el ledger — jamás subir un segundo
//     archivo (la serialización PDF no es determinística — doc 04 §7 "Idempotencia").
//
// FALLOS: transitorio (download/upload/BD) → InvalidPluginExecutionException con
// OperationStatus.Retry (la plataforma reintenta hasta 4 — re-entra por el flujo
// idempotente). Definitivo (mismatch de contenthash, PDF corrupto) → Error de Sellado +
// evento 8 accionable + trace técnico, SIN ledger parcial.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Core.Pdf;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class SealingWorkerPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var contexto = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var servicio = factory.CreateOrganizationService(null); // contexto de sistema
        var archivos = serviceProvider.GetService(typeof(IFileTransfer)) as IFileTransfer
                       ?? new FileTransferDataverse(servicio);
        var sellador = serviceProvider.GetService(typeof(ISelladorTsa)) as ISelladorTsa
                       ?? new SelladorTsaReal();

        // ── Guard anti-loop (umbral ALTO — doc 04 §7: Depth>1 desactivaría el sellado) ──
        if (contexto.Depth > 8)
        {
            trace.Trace("SealingWorker: Depth={0} > 8 — abortando (anti-loop).", contexto.Depth);
            return;
        }

        // ── Guard de post-image ──
        // Imagen AUSENTE = step mal registrado: RUIDOSO (antagonista A1 — un no-op silencioso
        // dejaría el sellado muerto para siempre). Status ≠ Sellando = auto-retrigger legítimo.
        if (!contexto.PostEntityImages.TryGetValue("PostImage", out var postImage))
            throw new InvalidPluginExecutionException(
                "SealingWorker: el step no trae la post-image 'PostImage' — registro del step corrupto (Runbook D §CF-D09).");
        if (postImage.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status)?.Value
            != (int)TransactionStatus.Sellando)
        {
            return; // no es una transición a Sellando — nada que hacer
        }

        var txId = contexto.PrimaryEntityId;
        trace.Trace("SealingWorker: inicio para {0} (depth={1}).", txId, contexto.Depth);

        try
        {
            Sellar(servicio, archivos, sellador, trace, txId);
        }
        catch (InvalidPluginExecutionException ex) when (ex.Status == OperationStatus.Retry)
        {
            throw; // transitorio clasificado — la plataforma reintenta (hasta 4)
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
        {
            // Fallo de PLATAFORMA (deadlock de BD, timeout, servicio ocupado): TRANSITORIO por
            // contrato (doc 04 §7) — el reintento re-entra por el flujo idempotente. Un fault
            // no-transitorio real agota los 4 retries y cae al saneamiento T14 (doc 06 R7).
            trace.Trace("SealingWorker: fault de plataforma ({0}): {1}", ex.Detail?.ErrorCode, ex.Message);
            throw Transitorio($"fault de plataforma {ex.Detail?.ErrorCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Definitivo (PDF corrupto, config inválida, InvalidPluginExecutionException sin
            // Retry — p.ej. env var faltante): Error de Sellado + evento accionable.
            trace.Trace("SealingWorker: fallo definitivo: {0}", ex);
            FalloDefinitivo(servicio, trace, txId,
                "El sellado falló de forma definitiva — el creador puede reintentar (RetrySealing) o cancelar.",
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Sellar(IOrganizationService servicio, IFileTransfer archivos,
        ISelladorTsa sellador, ITracingService trace, Guid txId)
    {
        // ── Lock + re-lectura del estado ACTUAL (los guards del doc 04 §7) ──
        LockDeFila.Tomar(servicio, txId);
        var tx = servicio.Retrieve(SchemaNames.Tx.Entidad, txId, new ColumnSet(
            SchemaNames.Tx.Status, SchemaNames.Tx.ContentHash, SchemaNames.Tx.Name,
            SchemaNames.Tx.RoutingType, SchemaNames.Tx.OwnerId));
        var estadoActual = (TransactionStatus)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
        if (estadoActual != TransactionStatus.Sellando)
        {
            trace.Trace("SealingWorker: estado actual {0} != Sellando — reintento zombi, abortando.", estadoActual);
            return;
        }

        var txRef = new EntityReference(SchemaNames.Tx.Entidad, txId);
        var creador = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;
        var participantes = Consultas.ParticipantesDe(servicio, txId);

        // ── Ledger existente ANTES del paso 1 → verificar el ancla y solo completar (paso 9) ──
        var ledgerExistente = LedgerDe(servicio, txId);
        if (ledgerExistente is not null)
        {
            trace.Trace("SealingWorker: ledger ya existe ({0}) — verificando el ancla antes de completar.", ledgerExistente.Id);
            VerificarAnclaOFallar(servicio, archivos, trace, txId, txRef, ledgerExistente);
            Completar(servicio, txRef, creador, participantes);
            return;
        }

        var contentHashEsperado = tx.GetAttributeValue<string>(SchemaNames.Tx.ContentHash)
            ?? throw new InvalidOperationException("la transacción no tiene contenthash (¿se envió por fuera de SendTransaction?).");

        // ── ¿Final file durable de un intento previo? → re-usar ESOS bytes (jamás re-subir) ──
        // La AUSENCIA se decide con una sonda de metadata (Retrieve de la columna File), no
        // con el fallo de la descarga: un timeout de transporte NO significa "no hay archivo"
        // (antagonista C4 — confundirlos recompondría encima de evidencia durable).
        byte[] finalBytes;
        var finalYaSubido = false;
        var sonda = servicio.Retrieve(SchemaNames.Tx.Entidad, txId, new ColumnSet(SchemaNames.Tx.FinalFile));
        if (sonda.Contains(SchemaNames.Tx.FinalFile) && sonda[SchemaNames.Tx.FinalFile] is not null)
        {
            try
            {
                finalBytes = archivos.Descargar(txRef, SchemaNames.Tx.FinalFile);
            }
            catch (Exception ex)
            {
                throw Transitorio($"el final durable existe pero su descarga falló: {ex.Message}");
            }
            finalYaSubido = true;
            trace.Trace("SealingWorker: final durable de intento previo ({0} bytes) — no se recompone.", finalBytes.Length);
        }
        else
        {
            finalBytes = Array.Empty<byte>(); // no hay final: flujo completo desde el paso 1
        }

        var datosFirmantes = CargarDatosDeFirmantes(servicio, participantes);
        var env = new EnvVars(servicio); // UNA instancia: el caché por-ejecución no se parte (doc 04 §8)

        if (!finalYaSubido)
        {
            // ── Paso 1: descargar el contenido aprobado ──
            byte[] contenido;
            try
            {
                contenido = archivos.Descargar(txRef, SchemaNames.Tx.ContentFile);
            }
            catch (Exception ex)
            {
                throw Transitorio($"descarga del contenido falló: {ex.Message}");
            }

            // ── Paso 2: verificar hash_contenido — mismatch = contenido adulterado (DEFINITIVO) ──
            var hashReal = HashUtil.Sha256Hex(contenido);
            if (!string.Equals(hashReal, contentHashEsperado, StringComparison.OrdinalIgnoreCase))
            {
                trace.Trace("SealingWorker: MISMATCH de contenthash — esperado {0}..., real {1}...",
                    contentHashEsperado.Substring(0, 8), hashReal.Substring(0, 8));
                FalloDefinitivo(servicio, trace, txId,
                    "El documento cambió entre el envío y el sellado (hash de contenido no coincide) — JAMÁS se sella contenido adulterado. Contactá al administrador.",
                    $"contenthash esperado {contentHashEsperado} != real {hashReal}");
                return;
            }

            // ── Pasos 3-5: componer el documento final (núcleo puro) y serializar UNA vez ──
            var urlBase = env.TextoObligatorio(SchemaNames.EnvVars.AppPlayUrl).TrimEnd('/');
            var firmas = new List<FirmaAIncrustar>();
            var firmantesEnHoja = new List<FirmanteEnHoja>();
            foreach (var f in datosFirmantes)
            {
                byte[] snapshot;
                try
                {
                    snapshot = archivos.Descargar(
                        new EntityReference(SchemaNames.Participante.Entidad, f.ParticipantId),
                        SchemaNames.Participante.SignatureSnapshot);
                }
                catch (Exception ex)
                {
                    throw Transitorio($"descarga del snapshot de firma falló: {ex.Message}");
                }
                var zonas = Consultas.ZonasDe(servicio, new[] { f.ParticipantId })
                    .Select(z => (
                        z.GetAttributeValue<int>(SchemaNames.Zona.Page),
                        (double)z.GetAttributeValue<decimal>(SchemaNames.Zona.PosX),
                        (double)z.GetAttributeValue<decimal>(SchemaNames.Zona.PosY),
                        (double)z.GetAttributeValue<decimal>(SchemaNames.Zona.Width),
                        (double)z.GetAttributeValue<decimal>(SchemaNames.Zona.Height)))
                    .ToList();
                firmas.Add(new FirmaAIncrustar(snapshot, zonas));
                firmantesEnHoja.Add(new FirmanteEnHoja(f.Nombre, f.Email, f.SignedOnUtc, snapshot));
            }
            finalBytes = ComposicionDeDocumento.ComponerDocumentoFinal(
                contenido, firmas, firmantesEnHoja, contentHashEsperado, urlBase, txId);
        }

        var finalHash = HashUtil.Sha256Hex(finalBytes);

        // ── Paso 6: TSA (si está habilitada) sobre hash_final ──
        var tsaHabilitada = env.BoolObligatorio(SchemaNames.EnvVars.TsaEnabled);
        ResultadoTsa? tsa = null;
        var tsaStatus = TsaStatus.SinSelloTsa;
        if (tsaHabilitada)
        {
            var config = TsaConfig.Parse(env.TextoObligatorio(SchemaNames.EnvVars.TsaEndpoints));
            using var sha = System.Security.Cryptography.SHA256.Create();
            tsa = sellador.Sellar(sha.ComputeHash(finalBytes), config);
            tsaStatus = tsa.Exitoso ? TsaStatus.SelladoConTsa : TsaStatus.ReSelladoPendiente;
            trace.Trace("SealingWorker: TSA {0} ({1}).", tsaStatus,
                tsa.Exitoso ? tsa.Endpoint : string.Join(" | ", tsa.Errores));
        }

        // ── Paso 7: subir el final ANTES del ledger (orden mandatorio — idempotencia §7) ──
        if (!finalYaSubido)
        {
            try
            {
                archivos.Subir(txRef, SchemaNames.Tx.FinalFile, "documento-final.pdf", finalBytes, "application/pdf");
            }
            catch (Exception ex)
            {
                throw Transitorio($"subida del documento final falló: {ex.Message}");
            }
        }

        // ── Paso 8: crear el ledger (el alternate key hace el insert idempotente) ──
        var ledger = new Entity(SchemaNames.Ledger.Entidad);
        // JAMÁS pasar sanic_sigil_name: pisaría el autonumber (doc 03 §4.4)
        ledger[SchemaNames.Ledger.TransactionId] = txRef;
        ledger[SchemaNames.Ledger.ContentHash] = contentHashEsperado;
        ledger[SchemaNames.Ledger.FinalHash] = finalHash;
        ledger[SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)tsaStatus);
        ledger[SchemaNames.Ledger.SealedOn] = DateTime.UtcNow;
        if (tsa is { Exitoso: true })
            ledger[SchemaNames.Ledger.TsaToken] = Convert.ToBase64String(tsa.TokenDer!);
        ledger[SchemaNames.Ledger.SignerSummary] = SignerSummary(tx, datosFirmantes, tsaStatus, tsa);
        try
        {
            servicio.Create(ledger);
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex) when (
            ex.Detail?.ErrorCode is -2147088238 /* DuplicateRecordEntityKey (alternate key) */
                or -2147220937 /* DuplicateRecord */)
        {
            // Carrera de sellados: el alternate key la resolvió a nivel BD (doc 03 §4.4).
            // JAMÁS matchear por texto del mensaje: cambia por idioma (antagonista C1).
            trace.Trace("SealingWorker: ledger duplicado ({0}) — carrera perdida limpiamente, continúa el paso 9.",
                ex.Detail.ErrorCode);
        }

        // ── Verificación del ANCLA (antagonista C3): el worker es asíncrono y el lock no
        // sostiene la serialización entre ejecuciones — antes de completar, el archivo
        // durable DEBE hashear al ledger. Un mismatch acá es el escenario prohibido del
        // doc 04 §7: jamás se completa con evidencia inconsistente.
        var ledgerFinal = LedgerDe(servicio, txId)
            ?? throw new InvalidOperationException("el ledger desapareció tras crearse.");
        VerificarAnclaOFallar(servicio, archivos, trace, txId, txRef, ledgerFinal);

        // ── Paso 9: transiciones + eventos ──
        Completar(servicio, txRef, creador, participantes);
        trace.Trace("SealingWorker: {0} COMPLETADA — final {1} bytes, hash {2}..., tsa {3}.",
            txId, finalBytes.Length, finalHash.Substring(0, 8), tsaStatus);
    }

    private sealed class DatosDeFirmante
    {
        public Guid ParticipantId;
        public string Nombre = string.Empty;
        public string Email = string.Empty;
        public DateTime SignedOnUtc;
    }

    private static IReadOnlyList<DatosDeFirmante> CargarDatosDeFirmantes(
        IOrganizationService servicio, IReadOnlyList<Entity> participantes)
    {
        // Los snapshots de identidad viven en columnas que ParticipantesDe no trae: re-leer.
        var datos = new List<DatosDeFirmante>();
        foreach (var p in participantes)
        {
            var fila = servicio.Retrieve(SchemaNames.Participante.Entidad, p.Id, new ColumnSet(
                SchemaNames.Participante.Status, SchemaNames.Participante.SignerName,
                SchemaNames.Participante.SignerEmail, SchemaNames.Participante.SignedOn));
            if ((ParticipantStatus)fila.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value
                != ParticipantStatus.Firmado)
                continue; // solo firmantes efectivos (todos deberían estarlo — T6/T7)
            datos.Add(new DatosDeFirmante
            {
                ParticipantId = p.Id,
                Nombre = fila.GetAttributeValue<string>(SchemaNames.Participante.SignerName) ?? "(sin nombre)",
                Email = fila.GetAttributeValue<string>(SchemaNames.Participante.SignerEmail) ?? string.Empty,
                SignedOnUtc = fila.GetAttributeValue<DateTime>(SchemaNames.Participante.SignedOn),
            });
        }
        return datos;
    }

    private static string SignerSummary(Entity tx, IReadOnlyList<DatosDeFirmante> firmantes,
        TsaStatus tsaStatus, ResultadoTsa? tsa)
    {
        // Formato del doc 04 §4 (sanic_sigil_signersummary) — insumo de la pantalla de verificación.
        var routing = (RoutingType)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.RoutingType).Value;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            signers = firmantes.Select(f => new
            {
                name = f.Nombre,
                email = f.Email,
                signedOnUtc = f.SignedOnUtc.ToString("o"),
            }).ToArray(),
            routing = routing == RoutingType.Secuencial ? "sequential" : "parallel",
            completedOnUtc = DateTime.UtcNow.ToString("o"),
            tsa = new
            {
                status = tsaStatus switch
                {
                    TsaStatus.SelladoConTsa => "sealed",
                    TsaStatus.ReSelladoPendiente => "pending",
                    _ => "none",
                },
                tokenGenTimeUtc = tsa?.GenTimeUtc?.ToString("o"),
            },
        });
    }

    private static void Completar(IOrganizationService servicio, EntityReference txRef,
        Guid creador, IReadOnlyList<Entity> participantes)
    {
        var cambio = new Entity(SchemaNames.Tx.Entidad, txRef.Id);
        cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Completado);
        cambio[SchemaNames.Tx.CompletedOn] = DateTime.UtcNow;
        servicio.Update(cambio); // T8 — este Update re-dispara el worker; el guard de post-image lo neutraliza

        var lectores = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
        Consultas.CrearEvento(servicio, txRef, EventType.SelladoCompletado, ("Sistema", string.Empty),
            "Documento final sellado y disponible.", creador, lectores: lectores);
    }

    /// <summary>
    /// El ancla de ADR-011: el archivo final durable DEBE hashear al finalhash del ledger.
    /// Un mismatch significa interleaving de sellados concurrentes (el worker es asíncrono:
    /// el lock no serializa entre ejecuciones) — JAMÁS se completa así (doc 04 §7).
    /// </summary>
    private static void VerificarAnclaOFallar(IOrganizationService servicio, IFileTransfer archivos,
        ITracingService trace, Guid txId, EntityReference txRef, Entity ledger)
    {
        var hashDelLedger = ledger.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash);
        if (string.IsNullOrEmpty(hashDelLedger))
            return; // ledger sin hash (sembrado de prueba/corrupto): no hay ancla que verificar acá

        byte[] finalDurable;
        try
        {
            finalDurable = archivos.Descargar(txRef, SchemaNames.Tx.FinalFile);
        }
        catch (Exception ex)
        {
            throw Transitorio($"verificación del ancla: descarga del final falló: {ex.Message}");
        }

        var hashReal = HashUtil.Sha256Hex(finalDurable);
        if (!string.Equals(hashReal, hashDelLedger, StringComparison.OrdinalIgnoreCase))
        {
            trace.Trace("SealingWorker: ANCLA ROTA — ledger {0}... vs archivo {1}... (interleaving de sellados).",
                hashDelLedger.Substring(0, 8), hashReal.Substring(0, 8));
            FalloDefinitivo(servicio, trace, txId,
                "El archivo final no coincide con el hash del ledger (evidencia inconsistente por sellados concurrentes) — contactá al administrador ANTES de reintentar.",
                $"finalhash del ledger {hashDelLedger} != hash del archivo {hashReal}");
            throw new InvalidPluginExecutionException("SealingWorker: ancla rota — ver evento de Error de Sellado.");
        }
    }

    private static void FalloDefinitivo(IOrganizationService servicio, ITracingService trace,
        Guid txId, string detalleAccionable, string detalleTecnico)
    {
        try
        {
            // Revalidar el estado ACTUAL: jamás pisar un Completado ajeno ni reescribir un
            // Error de Sellado ya puesto (doc 08 §7 — el status idéntico dispara los flows).
            var (estado, creador) = Consultas.EstadoYCreador(servicio, txId);
            if (estado is TransactionStatus.Completado or TransactionStatus.ErrorDeSellado)
            {
                trace.Trace("SealingWorker: FalloDefinitivo omitido — estado actual {0}.", estado);
                return;
            }

            var cambio = new Entity(SchemaNames.Tx.Entidad, txId);
            cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.ErrorDeSellado);
            servicio.Update(cambio); // T9

            var participantes = Consultas.ParticipantesDe(servicio, txId);
            var lectores = participantes
                .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
                .Where(u => u != creador)
                .Distinct()
                .ToList();
            // El detalle técnico va TRUNCADO al evento: asyncautodelete borra el system job
            // exitoso y el trace puede no estar habilitado (antagonista A7) — el evento es
            // el único rastro garantizado.
            var detalle = $"{detalleAccionable}\n[técnico] {Consultas.Truncar(detalleTecnico, 500)}";
            Consultas.CrearEvento(servicio, new EntityReference(SchemaNames.Tx.Entidad, txId),
                EventType.ErrorDeSellado, ("Sistema", string.Empty), detalle, creador, lectores: lectores);
        }
        catch (Exception ex)
        {
            // Si NI el registro del fallo funciona, el job debe reintentar — jamás morir mudo.
            trace.Trace("SealingWorker: FalloDefinitivo FALLÓ: {0}", ex);
            throw Transitorio($"el registro del fallo definitivo falló: {ex.Message}");
        }
    }

    private static Entity? LedgerDe(IOrganizationService servicio, Guid txId)
    {
        var q = new QueryExpression(SchemaNames.Ledger.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Ledger.FinalHash),
        };
        q.Criteria.AddCondition(SchemaNames.Ledger.TransactionId, ConditionOperator.Equal, txId);
        return servicio.RetrieveMultiple(q).Entities.FirstOrDefault();
    }

    private static InvalidPluginExecutionException Transitorio(string detalle)
        => new(OperationStatus.Retry, $"SealingWorker (transitorio, la plataforma reintenta): {detalle}");
}

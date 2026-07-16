// M4 — Idempotencia del worker (doc 11 §4 / doc 04 §7): los guards precisos, la
// re-entrada tras fallo en CADA punto crítico, y el reintento zombi que aborta sin
// tocar el archivo. También el camino feliz completo (M5 a nivel orquestación) y la
// degradación TSA (ADR-005).

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class SealingWorkerPluginTests
{
    private static readonly byte[] PngDeFirma = CrearPngDeFirma();
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;
    private readonly Guid _firmante;

    public SealingWorkerPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        _firmante = _arnes.SembrarUsuario("Beto Firmante", "beto@bac.test");
        _arnes.SelladorTsa = new SelladorTsaStub(exitoso: true);
    }

    private void EjecutarWorker(Guid txId, TransactionStatus statusEnPostImage = TransactionStatus.Sellando, int depth = 2)
    {
        _arnes.Contexto.MessageName = "Update";
        _arnes.Contexto.PrimaryEntityId = txId;
        _arnes.Contexto.Depth = depth;
        var postImage = new Entity(SchemaNames.Tx.Entidad, txId);
        postImage[SchemaNames.Tx.Status] = new OptionSetValue((int)statusEnPostImage);
        _arnes.Contexto.PostEntityImages["PostImage"] = postImage;
        new SealingWorkerPlugin().Execute(_arnes);
    }

    /// <summary>Transacción Sellando lista para el worker: contenido + hash correcto + firmante con snapshot y zona.</summary>
    private Guid SembrarSellando()
    {
        var pdf = ArnesDeApi.PdfDePrueba(1);
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Sellando, RoutingType.Paralelo);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.ContentFile, pdf);
        var tx = new Entity(SchemaNames.Tx.Entidad, txId);
        tx[SchemaNames.Tx.ContentHash] = Sigil.Plugins.Core.Crypto.HashUtil.Sha256Hex(pdf);
        _arnes.Servicio.Update(tx);

        var pid = _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.Firmado);
        var p = new Entity(SchemaNames.Participante.Entidad, pid);
        p[SchemaNames.Participante.SignerName] = "Beto Firmante";
        p[SchemaNames.Participante.SignerEmail] = "beto@bac.test";
        p[SchemaNames.Participante.SignedOn] = DateTime.UtcNow;
        _arnes.Servicio.Update(p);
        _arnes.Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Participante.Entidad, pid),
            SchemaNames.Participante.SignatureSnapshot)] = PngDeFirma;
        _arnes.SembrarZona(pid, page: 1);
        _arnes.Servicio.Operaciones.Clear();
        return txId;
    }

    // ── camino feliz (T8): compone, sella, sube, ledger, Completado, evento 7 ──

    [Fact]
    public void Feliz_ConTsaApagada_CompletaTodo_ConLedgerSinSello()
    {
        var txId = SembrarSellando();

        EjecutarWorker(txId);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Completado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.True(tx.Contains(SchemaNames.Tx.CompletedOn));

        // el final subió ANTES de crear el ledger (orden mandatorio §7)
        var claveFinal = StubFileTransfer.Clave(new EntityReference(SchemaNames.Tx.Entidad, txId), SchemaNames.Tx.FinalFile);
        Assert.True(_arnes.Archivos.Archivos.ContainsKey(claveFinal));

        var ledger = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        Assert.False(ledger.Contains(SchemaNames.Ledger.Name)); // jamás pisar el autonumber
        Assert.Equal((int)TsaStatus.SinSelloTsa, ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus).Value);
        // hash_final == SHA-256 de los bytes EXACTOS subidos (ADR-011)
        Assert.Equal(Sigil.Plugins.Core.Crypto.HashUtil.Sha256Hex(_arnes.Archivos.Archivos[claveFinal]),
            ledger.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash));
        Assert.Contains("Beto Firmante", ledger.GetAttributeValue<string>(SchemaNames.Ledger.SignerSummary));

        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.SelladoCompletado);
    }

    [Fact] // con TSA habilitada y sello exitoso: token persistido + Sellado con TSA
    public void Feliz_ConTsaPrendida_PersisteElToken()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        var txId = SembrarSellando();

        EjecutarWorker(txId);

        var ledger = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        Assert.Equal((int)TsaStatus.SelladoConTsa, ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus).Value);
        Assert.False(string.IsNullOrEmpty(ledger.GetAttributeValue<string>(SchemaNames.Ledger.TsaToken)));
    }

    [Fact] // ADR-005: todos los endpoints fallan → Re-sellado pendiente, SIN token, pero COMPLETA
    public void ConTsaCaida_DegradaAReSelladoPendiente_YCompletaIgual()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        _arnes.SelladorTsa = new SelladorTsaStub(exitoso: false);
        var txId = SembrarSellando();

        EjecutarWorker(txId);

        var ledger = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        Assert.Equal((int)TsaStatus.ReSelladoPendiente, ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus).Value);
        Assert.False(ledger.Contains(SchemaNames.Ledger.TsaToken));
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Completado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }

    // ── los guards del doc 04 §7 (M4) ────────────────────────────────────────

    [Fact] // guard de post-image: el paso 9 escribe Completado → re-disparo → sale SIN tocar nada
    public void Guard_PostImageNoSellando_EsNoOp()
    {
        var txId = SembrarSellando();

        EjecutarWorker(txId, statusEnPostImage: TransactionStatus.Completado);

        Assert.DoesNotContain(_arnes.Servicio.Operaciones, o => o.Tipo != "Read");
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
    }

    [Fact] // guard de estado ACTUAL: reintento zombi (post-image vieja, estado ya cambió) aborta tras el lock
    public void Guard_ReintentoZombi_AbortaTrasElLock_SinTocarElArchivo()
    {
        var txId = SembrarSellando();
        var cambiada = new Entity(SchemaNames.Tx.Entidad, txId);
        cambiada[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Cancelado); // T14+cancel corrieron
        _arnes.Servicio.Update(cambiada);
        _arnes.Servicio.Operaciones.Clear();
        _arnes.Archivos.Subidas.Clear();

        EjecutarWorker(txId, statusEnPostImage: TransactionStatus.Sellando); // post-image VIEJA

        Assert.Empty(_arnes.Archivos.Subidas); // jamás sube un segundo archivo
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        var escrituras = _arnes.Servicio.Operaciones.Where(o => o.Tipo != "Read").ToList();
        var unica = Assert.Single(escrituras); // solo el lock
        Assert.Equal(SchemaNames.Tx.LockToken, Assert.Single(unica.Datos!.Attributes).Key);
    }

    [Fact] // depth alto (anti-loop) — pero JAMÁS el guard clásico >1 (mataría el sellado)
    public void Guard_DepthMayorA8_Aborta()
    {
        var txId = SembrarSellando();
        EjecutarWorker(txId, depth: 9);
        Assert.DoesNotContain(_arnes.Servicio.Operaciones, o => o.Tipo != "Read");
    }

    [Fact] // el worker corre legítimamente con depth 2 (lo dispara SubmitSignature)
    public void ConDepth2_SiCorre()
    {
        var txId = SembrarSellando();
        EjecutarWorker(txId, depth: 2);
        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
    }

    // ── idempotencia por punto de fallo (M4 — doc 04 §7 "Idempotencia") ─────

    [Fact] // ledger YA existe → salta directo al paso 9: ni recompone ni re-sube
    public void Reentrada_ConLedgerExistente_SoloCompleta_SinTocarElArchivo()
    {
        var txId = SembrarSellando();
        var ledger = new Entity(SchemaNames.Ledger.Entidad);
        ledger[SchemaNames.Ledger.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        ledger[SchemaNames.Ledger.FinalHash] = "F".PadLeft(64, 'F');
        _arnes.Servicio.Sembrar(ledger);
        _arnes.Archivos.Subidas.Clear();

        EjecutarWorker(txId);

        Assert.Empty(_arnes.Archivos.Subidas); // no re-subió
        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad)); // no duplicó
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Completado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }

    [Fact] // final durable SIN ledger (falló el paso 8) → re-usa ESOS bytes exactos, jamás re-sube
    public void Reentrada_ConFinalSinLedger_ReusaLosBytesDurables()
    {
        var txId = SembrarSellando();
        var finalPrevio = ArnesDeApi.PdfDePrueba(3); // bytes de un intento anterior (distintos a lo que se recompondría)
        SembrarFinalDurable(txId, finalPrevio);
        _arnes.Archivos.Subidas.Clear();

        EjecutarWorker(txId);

        Assert.Empty(_arnes.Archivos.Subidas); // NO subió un segundo archivo
        var ledger = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        // el hash del ledger describe los bytes QUE EXISTEN (los del intento previo)
        Assert.Equal(Sigil.Plugins.Core.Crypto.HashUtil.Sha256Hex(finalPrevio),
            ledger.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash));
    }

    /// <summary>La sonda de C4 usa la METADATA de la columna File — sembrarla junto a los bytes.</summary>
    private void SembrarFinalDurable(Guid txId, byte[] bytes)
    {
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.FinalFile, bytes);
        var tx = new Entity(SchemaNames.Tx.Entidad, txId);
        tx[SchemaNames.Tx.FinalFile] = Guid.NewGuid(); // el fileid que Dataverse expone en la columna
        _arnes.Servicio.Update(tx);
    }

    // ── clasificación de fallos (semántica del doc 04 §7 — antagonista C1/C2/C4/A9) ──

    [Fact] // fallo de descarga del contenido → TRANSITORIO (OperationStatus.Retry), jamás Error de Sellado
    public void FalloDeDescarga_EsTransitorio_ConRetry()
    {
        var txId = SembrarSellando();
        _arnes.Archivos.Archivos.Remove(StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Tx.Entidad, txId), SchemaNames.Tx.ContentFile)); // el download falla

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => EjecutarWorker(txId));

        Assert.Equal(OperationStatus.Retry, ex.Status);
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Sellando, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }

    [Fact] // fault de PLATAFORMA (deadlock de BD) en el Create del ledger → Retry, no definitivo (C2)
    public void FaultDePlataformaEnElLedger_EsTransitorio()
    {
        var txId = SembrarSellando();
        _arnes.Servicio.InterceptarCreate = e => e.LogicalName == SchemaNames.Ledger.Entidad
            ? new System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault>(
                new Microsoft.Xrm.Sdk.OrganizationServiceFault { ErrorCode = unchecked((int)0x80044151), Message = "SQL deadlock" },
                new System.ServiceModel.FaultReason("SQL deadlock"))
            : null;

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => EjecutarWorker(txId));
        Assert.Equal(OperationStatus.Retry, ex.Status);
    }

    [Fact] // carrera de sellados: el alternate key la resuelve — el perdedor la absorbe por ERRORCODE (C1)
    public void LedgerDuplicadoPorAlternateKey_SeAbsorbe_YCompleta()
    {
        var txId = SembrarSellando();
        var yaCreado = false;
        _arnes.Servicio.InterceptarCreate = e =>
        {
            if (e.LogicalName != SchemaNames.Ledger.Entidad) return null;
            if (yaCreado) return null;
            yaCreado = true;
            // simular que OTRO worker ganó la carrera: sembrar el ledger del ganador y
            // lanzar el fault EXACTO del alternate key (DuplicateRecordEntityKey)
            var delGanador = new Entity(SchemaNames.Ledger.Entidad);
            delGanador[SchemaNames.Ledger.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId);
            delGanador[SchemaNames.Ledger.FinalHash] = e.GetAttributeValue<string>(SchemaNames.Ledger.FinalHash);
            _arnes.Servicio.Sembrar(delGanador);
            return new System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault>(
                new Microsoft.Xrm.Sdk.OrganizationServiceFault { ErrorCode = -2147088238, Message = "A record that has the attribute values already exists." },
                new System.ServiceModel.FaultReason("duplicate key"));
        };

        EjecutarWorker(txId); // NO lanza: la carrera se pierde limpiamente

        Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Completado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
    }

    [Fact] // step mal registrado (sin post-image) → RUIDOSO, jamás no-op silencioso (A1)
    public void SinPostImage_LanzaRuidoso()
    {
        var txId = SembrarSellando();
        _arnes.Contexto.MessageName = "Update";
        _arnes.Contexto.PrimaryEntityId = txId;
        _arnes.Contexto.PostEntityImages.Clear();

        var ex = Assert.Throws<InvalidPluginExecutionException>(() => new SealingWorkerPlugin().Execute(_arnes));
        Assert.Contains("PostImage", ex.Message);
    }

    [Fact] // LA propiedad de ADR-011: el digest sellado por TSA == SHA-256 de los bytes subidos
    public void ElDigestSellado_EsElSha256DeLosBytesSubidos()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        var capturador = new SelladorTsaCapturador();
        _arnes.SelladorTsa = capturador;
        var txId = SembrarSellando();

        EjecutarWorker(txId);

        var finalSubido = _arnes.Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Tx.Entidad, txId), SchemaNames.Tx.FinalFile)];
        using var sha = System.Security.Cryptography.SHA256.Create();
        Assert.Equal(sha.ComputeHash(finalSubido), capturador.DigestCapturado);
    }

    [Fact] // re-entrada con final durable + TSA prendida: el sello se re-pide sobre LO DURABLE
    public void Reentrada_ConTsaPrendida_SellaElHashDeLoDurable()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        var capturador = new SelladorTsaCapturador();
        _arnes.SelladorTsa = capturador;
        var txId = SembrarSellando();
        var finalPrevio = ArnesDeApi.PdfDePrueba(2);
        SembrarFinalDurable(txId, finalPrevio);

        EjecutarWorker(txId);

        using var sha = System.Security.Cryptography.SHA256.Create();
        Assert.Equal(sha.ComputeHash(finalPrevio), capturador.DigestCapturado);
    }

    // ── fallo definitivo (T9): mismatch de contenthash ───────────────────────

    [Fact]
    public void MismatchDeContenthash_ErrorDeSellado_SinLedgerNiUpload_ConEvento8()
    {
        var txId = SembrarSellando();
        var adulterada = new Entity(SchemaNames.Tx.Entidad, txId);
        adulterada[SchemaNames.Tx.ContentHash] = "A".PadLeft(64, 'A'); // el archivo ya no coincide
        _arnes.Servicio.Update(adulterada);
        _arnes.Archivos.Subidas.Clear();

        EjecutarWorker(txId);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.ErrorDeSellado, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad)); // sin ledger parcial
        Assert.Empty(_arnes.Archivos.Subidas);
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.ErrorDeSellado);
    }

    // ── RetrySealing (T10) ───────────────────────────────────────────────────

    [Fact]
    public void T10_RetrySealing_DelCreadorEnError_VuelveASellando_ConEvento6()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.ErrorDeSellado, RoutingType.Paralelo);
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        _arnes.Ejecutar(new RetrySealingPlugin(), SchemaNames.Apis.RetrySealing, _creador);

        var tx = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad).Single();
        Assert.Equal((int)TransactionStatus.Sellando, tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        var evento = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
        Assert.Equal((int)EventType.SelladoIniciado, evento.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value);
        Assert.Contains("reintento manual", evento.GetAttributeValue<string>(SchemaNames.Evento.Details),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // M1 — no-creador no reintenta; estados fuera de Error de Sellado tampoco
    public void T10_NoCreadorOEstadoIncorrecto_EsRechazado()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.ErrorDeSellado, RoutingType.Paralelo);
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            _arnes.Ejecutar(new RetrySealingPlugin(), SchemaNames.Apis.RetrySealing, _firmante));

        var txId2 = _arnes.SembrarTransaccion(_creador, TransactionStatus.Completado, RoutingType.Paralelo);
        _arnes.Contexto.InputParameters["Target"] = new EntityReference(SchemaNames.Tx.Entidad, txId2);
        Assert.Throws<InvalidPluginExecutionException>(() =>
            _arnes.Ejecutar(new RetrySealingPlugin(), SchemaNames.Apis.RetrySealing, _creador));
    }

    // ── dobles ───────────────────────────────────────────────────────────────

    private sealed class SelladorTsaStub(bool exitoso) : Data.ISelladorTsa
    {
        public ResultadoTsa Sellar(byte[] digest, TsaConfig config)
            => exitoso
                ? new ResultadoTsa([1, 2, 3, 4], new DateTime(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc),
                    config.Endpoints[0].Url, [])
                : new ResultadoTsa(null, null, null, ["https://tsa.prueba: timeout simulado"]);
    }

    private sealed class SelladorTsaCapturador : Data.ISelladorTsa
    {
        public byte[]? DigestCapturado { get; private set; }

        public ResultadoTsa Sellar(byte[] digest, TsaConfig config)
        {
            DigestCapturado = digest;
            return new ResultadoTsa([9, 9, 9], DateTime.UtcNow, config.Endpoints[0].Url, []);
        }
    }

    private static byte[] CrearPngDeFirma() => ArnesDeApi.PngDeFirmaQueValida();
}

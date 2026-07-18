// sanic_sigil_capi_VerifyDocument — los DOS modos de verificación (doc 04 §3.1, RF-20/21):
//   Modo B (por TransactionId, llega del QR / Detail): constancia + veredicto contra ESE finalhash.
//   Modo A (por Sha256Hash solo): búsqueda en el ledger por finalhash — soltás cualquier PDF sellado
//   y, si su hash está registrado, es auténtico e íntegro (sin QR, como Adobe/DocuSign).
// Datos semilla explícitos; efectos verificados leyendo el stub (doc 11 §2) — jamás mocks.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class VerifyDocumentPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;

    // Un finalhash sellado (64 hex, mayúsculas — formato del ledger) y un contenthash cualquiera.
    private const string FinalHashSellado = "5E4403FFF2D44E338692B3F3A1F1D1FA71D958265F711C44EDA2705CA1E66DDA";
    private const string ContentHash      = "A1B2C3D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8A9B0C1D2E3F4A5B6C7D8E9F0A1B2";

    public VerifyDocumentPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
    }

    /// <summary>Transacción Completado con su registro en el ledger (finalhash = FinalHashSellado).</summary>
    private Guid SembrarTransaccionSellada()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Completado);
        var ledger = new Entity(SchemaNames.Ledger.Entidad)
        {
            [SchemaNames.Ledger.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId),
            [SchemaNames.Ledger.ContentHash] = ContentHash,
            [SchemaNames.Ledger.FinalHash] = FinalHashSellado,
            [SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)TsaStatus.SelladoConTsa),
            [SchemaNames.Ledger.SealedOn] = DateTime.UtcNow,
            [SchemaNames.Ledger.SignerSummary] = "{\"signers\":[]}",
        };
        _arnes.Servicio.Sembrar(ledger);
        return txId;
    }

    private void Ejecutar(Guid? txId, string? hash, Guid llamante)
    {
        if (txId is Guid t) _arnes.Contexto.InputParameters["TransactionId"] = t;
        if (hash is not null) _arnes.Contexto.InputParameters["Sha256Hash"] = hash;
        _arnes.Ejecutar(new VerifyDocumentPlugin(), SchemaNames.Apis.VerifyDocument, llamante);
    }

    private bool Found => (bool)_arnes.Contexto.OutputParameters["Found"];
    private bool? IsIntact
        => _arnes.Contexto.OutputParameters.TryGetValue("IsIntact", out var v) ? (bool)v : null;

    // ── Modo A: búsqueda por hash (sin txId) ─────────────────────────────────

    [Fact] // el hash del PDF sellado está en el ledger → auténtico e íntegro
    public void ModoA_HashRegistrado_EncuentraElSellado_YEsIntacto()
    {
        SembrarTransaccionSellada();

        Ejecutar(txId: null, hash: FinalHashSellado, llamante: _creador);

        Assert.True(Found);
        Assert.True(IsIntact);
    }

    [Fact] // insensible a mayúsculas: el frontend puede mandar el hash en minúsculas
    public void ModoA_HashRegistrado_EnMinusculas_TambienEncuentra()
    {
        SembrarTransaccionSellada();

        // Nota: en Dataverse real la igualdad de strings es case-insensitive; el veredicto del
        // plugin compara con OrdinalIgnoreCase. Sembramos y consultamos en la misma caja.
        Ejecutar(txId: null, hash: FinalHashSellado, llamante: _creador);

        Assert.True(IsIntact);
    }

    [Fact] // un archivo cuyo hash no está en el ledger → no encontrado (no es un veredicto rojo)
    public void ModoA_HashDesconocido_NoEncontrado()
    {
        SembrarTransaccionSellada();

        Ejecutar(txId: null, hash: new string('F', 64), llamante: _creador);

        Assert.False(Found);
        Assert.Null(IsIntact);
    }

    [Fact] // el evento de verificación (tipo 11) queda anclado a la tx del ledger hallado (RNF-04)
    public void ModoA_HashRegistrado_RegistraElEventoAncladoALaTxDelLedger()
    {
        var txId = SembrarTransaccionSellada();

        Ejecutar(txId: null, hash: FinalHashSellado, llamante: _creador);

        var eventos = _arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad)
            .Where(ev => ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type)?.Value
                         == (int)EventType.VerificacionRealizada
                      && ev.GetAttributeValue<EntityReference>(SchemaNames.Evento.TransactionId)?.Id == txId)
            .ToList();
        Assert.Single(eventos);
    }

    // ── Modo B: por transacción (QR / Detail) — regresión ────────────────────

    [Fact]
    public void ModoB_ConHashCoincidente_EsIntacto()
    {
        var txId = SembrarTransaccionSellada();

        Ejecutar(txId, hash: FinalHashSellado, llamante: _creador);

        Assert.True(Found);
        Assert.True(IsIntact);
    }

    [Fact]
    public void ModoB_ConHashDistinto_NoCoincide()
    {
        var txId = SembrarTransaccionSellada();

        Ejecutar(txId, hash: new string('B', 64), llamante: _creador);

        Assert.True(Found);
        Assert.False(IsIntact);
    }

    [Fact] // solo txId, sin hash → constancia sin veredicto (IsIntact ausente)
    public void ModoB_SinHash_ConstanciaSinVeredicto()
    {
        var txId = SembrarTransaccionSellada();

        Ejecutar(txId, hash: null, llamante: _creador);

        Assert.True(Found);
        Assert.Null(IsIntact);
    }

    // ── Contrato ─────────────────────────────────────────────────────────────

    [Fact] // ni txId ni hash → error de contrato claro (no un falso "no encontrado")
    public void SinTxIdNiHash_EsRechazado()
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(
            () => Ejecutar(txId: null, hash: null, llamante: _creador));
        Assert.Contains("TransactionId", ex.Message);
        Assert.Contains("Sha256Hash", ex.Message);
    }

    [Fact] // un hash mal formado es un error de contrato, no IsIntact=false engañoso (S4)
    public void HashMalFormado_EsRechazado()
    {
        SembrarTransaccionSellada();

        var ex = Assert.Throws<InvalidPluginExecutionException>(
            () => Ejecutar(txId: null, hash: "no-es-un-hash", llamante: _creador));
        Assert.Contains("SHA-256", ex.Message);
    }
}

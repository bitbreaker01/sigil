// M10 — Jobs: expiración solo en estados elegibles, saneamiento T14 a 24 h,
// filtro de recordatorios por estado de transacción (jamás sobre muertas), ResealPending
// con TSA off → Sin sello TSA. Más VerifyDocument (verificación cruzada del historial).

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class JobsPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _creador;
    private readonly Guid _firmante;

    public JobsPluginTests()
    {
        _creador = _arnes.SembrarUsuario("Ana Creadora", "ana@bac.test");
        _firmante = _arnes.SembrarUsuario("Beto Firmante", "beto@bac.test");
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.ReminderCadenceDays, "2");
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.DefaultLanguage, "es");
    }

    private void EjecutarJob(SigilApiPlugin plugin, string api)
    {
        _arnes.Contexto.OutputParameters.Clear();
        _arnes.Contexto.InputParameters.Clear();
        _arnes.Ejecutar(plugin, api, _creador);
    }

    private Guid SembrarConFechas(TransactionStatus estado, DateTime? expireson = null, DateTime? modifiedon = null)
    {
        var txId = _arnes.SembrarTransaccion(_creador, estado, RoutingType.Paralelo);
        var tx = new Entity(SchemaNames.Tx.Entidad, txId);
        if (expireson.HasValue) tx[SchemaNames.Tx.ExpiresOn] = expireson.Value;
        if (modifiedon.HasValue) tx["modifiedon"] = modifiedon.Value;
        _arnes.Servicio.Update(tx);
        return txId;
    }

    // ── ExpireTransactions (T12 + T14) ───────────────────────────────────────

    [Fact] // M10 — expiración SOLO en estados elegibles: la Sellando vencida NO se toca
    public void M10_Expire_VencidaElegible_Expira_YSellandoVencidaNo()
    {
        var elegible = SembrarConFechas(TransactionStatus.PendienteDeFirma, expireson: DateTime.UtcNow.AddDays(-1));
        _arnes.SembrarParticipante(elegible, _firmante, ParticipantStatus.TurnoActivo);
        var sellando = SembrarConFechas(TransactionStatus.Sellando, expireson: DateTime.UtcNow.AddDays(-1));

        EjecutarJob(new ExpireTransactionsPlugin(), SchemaNames.Apis.ExpireTransactions);

        Assert.Equal(1, _arnes.Contexto.OutputParameters["ExpiredCount"]);
        var filas = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad);
        Assert.Equal((int)TransactionStatus.Expirado,
            filas.Single(t => t.Id == elegible).GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.Equal((int)TransactionStatus.Sellando,
            filas.Single(t => t.Id == sellando).GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        // evento 10 con el wording de T12
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.Expirada);
    }

    [Fact]
    public void M10_Expire_NoVencida_NoSeToca()
    {
        SembrarConFechas(TransactionStatus.PendienteDeFirma, expireson: DateTime.UtcNow.AddDays(2));
        EjecutarJob(new ExpireTransactionsPlugin(), SchemaNames.Apis.ExpireTransactions);
        Assert.Equal(0, _arnes.Contexto.OutputParameters["ExpiredCount"]);
    }

    [Fact] // M10 — saneamiento T14: Sellando >24h sin actividad → Error de Sellado con el wording exacto
    public void M10_SaneamientoT14_SellandoZombi_AErrorDeSellado()
    {
        var zombi = SembrarConFechas(TransactionStatus.Sellando, modifiedon: DateTime.UtcNow.AddHours(-25));
        _arnes.SembrarParticipante(zombi, _firmante, ParticipantStatus.Firmado);
        var reciente = SembrarConFechas(TransactionStatus.Sellando, modifiedon: DateTime.UtcNow.AddHours(-23));

        EjecutarJob(new ExpireTransactionsPlugin(), SchemaNames.Apis.ExpireTransactions);

        Assert.Equal(1, _arnes.Contexto.OutputParameters["SanitizedCount"]);
        var filas = _arnes.Servicio.FilasDe(SchemaNames.Tx.Entidad);
        Assert.Equal((int)TransactionStatus.ErrorDeSellado,
            filas.Single(t => t.Id == zombi).GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.Equal((int)TransactionStatus.Sellando, // el worker podría estar reintentando — no se pisa
            filas.Single(t => t.Id == reciente).GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value);
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<string>(SchemaNames.Evento.Details)?.Contains("saneamiento: worker sin actividad") == true);
    }

    // ── ProcessReminders ─────────────────────────────────────────────

    private Guid SembrarTurnoActivo(TransactionStatus estadoTx, int diasDeEspera, DateTime? ultimoRecordatorio = null)
    {
        var txId = _arnes.SembrarTransaccion(_creador, estadoTx, RoutingType.Paralelo, nombre: "Doc a recordar");
        var pid = _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.TurnoActivo);
        var p = new Entity(SchemaNames.Participante.Entidad, pid);
        p[SchemaNames.Participante.TurnActivatedOn] = DateTime.UtcNow.AddDays(-diasDeEspera);
        if (ultimoRecordatorio.HasValue) p[SchemaNames.Participante.LastReminderOn] = ultimoRecordatorio.Value;
        _arnes.Servicio.Update(p);
        return txId;
    }

    [Fact] // el JSON es AUTOSUFICIENTE y lastreminderon queda marcado
    public void M10_Reminders_TurnoVencido_GeneraElJsonAutosuficiente_YMarcaElUltimo()
    {
        SembrarTurnoActivo(TransactionStatus.PendienteDeFirma, diasDeEspera: 3);

        EjecutarJob(new ProcessRemindersPlugin(), SchemaNames.Apis.ProcessReminders);

        var json = (string)_arnes.Contexto.OutputParameters["RemindersJson"];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var item = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("Doc a recordar", item.GetProperty("transactionName").GetString());
        Assert.Equal("beto@bac.test", item.GetProperty("recipientEmail").GetString());
        Assert.Equal("Beto Firmante", item.GetProperty("recipientName").GetString());
        Assert.Equal("Ana Creadora", item.GetProperty("senderName").GetString());
        Assert.Equal("es", item.GetProperty("recipientLanguage").GetString()); // sin usersettings → default
        Assert.Equal(3, item.GetProperty("daysWaiting").GetInt32());

        var participante = _arnes.Servicio.FilasDe(SchemaNames.Participante.Entidad).Single();
        Assert.True(participante.Contains(SchemaNames.Participante.LastReminderOn));
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.RecordatorioProgramado);
    }

    [Theory] // EL filtro obligatorio: Turno Activo en tx muerta JAMÁS se recuerda
    [InlineData(TransactionStatus.Rechazado)]
    [InlineData(TransactionStatus.Expirado)]
    [InlineData(TransactionStatus.Cancelado)]
    [InlineData(TransactionStatus.Sellando)]
    public void M10_Reminders_TransaccionNoFirmable_JamasSeRecuerda(TransactionStatus estado)
    {
        SembrarTurnoActivo(estado, diasDeEspera: 30);

        EjecutarJob(new ProcessRemindersPlugin(), SchemaNames.Apis.ProcessReminders);

        Assert.Equal("[]", _arnes.Contexto.OutputParameters["RemindersJson"]);
        Assert.DoesNotContain(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.RecordatorioProgramado);
    }

    [Fact] // recordatorio RECIENTE no se duplica (lastreminderon dentro de la cadencia)
    public void M10_Reminders_ConRecordatorioReciente_NoDuplica()
    {
        SembrarTurnoActivo(TransactionStatus.PendienteDeFirma, diasDeEspera: 10,
            ultimoRecordatorio: DateTime.UtcNow.AddDays(-1)); // cadencia = 2

        EjecutarJob(new ProcessRemindersPlugin(), SchemaNames.Apis.ProcessReminders);

        Assert.Equal("[]", _arnes.Contexto.OutputParameters["RemindersJson"]);
    }

    [Fact] // idioma del destinatario desde usersettings
    public void M10_Reminders_ConUsersettingsEnIngles_MandaEn()
    {
        var settings = new Entity("usersettings");
        settings["systemuserid"] = _firmante;
        settings["uilanguageid"] = 1033;
        _arnes.Servicio.Sembrar(settings);
        SembrarTurnoActivo(TransactionStatus.PendienteDeFirma, diasDeEspera: 3);

        EjecutarJob(new ProcessRemindersPlugin(), SchemaNames.Apis.ProcessReminders);

        using var doc = System.Text.Json.JsonDocument.Parse((string)_arnes.Contexto.OutputParameters["RemindersJson"]);
        Assert.Equal("en", doc.RootElement[0].GetProperty("recipientLanguage").GetString());
    }

    // ── ResealPending ──────────────────────────────────────────────

    private Guid SembrarLedgerPendiente(byte[] finalBytes)
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Completado, RoutingType.Paralelo);
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.FinalFile, finalBytes);
        var ledger = new Entity(SchemaNames.Ledger.Entidad);
        ledger[SchemaNames.Ledger.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        ledger[SchemaNames.Ledger.FinalHash] = HashUtil.Sha256Hex(finalBytes);
        ledger[SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)TsaStatus.ReSelladoPendiente);
        _arnes.Servicio.Sembrar(ledger);
        return txId;
    }

    [Fact] // M10 — TSA off: los pendientes van a Sin sello TSA + evento "TSA abandonada"
    public void M10_Reseal_ConTsaApagada_MueveASinSello_ConEventoTsaAbandonada()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "no");
        SembrarLedgerPendiente(ArnesDeApi.PdfDePrueba(1));

        EjecutarJob(new ResealPendingPlugin(), SchemaNames.Apis.ResealPending);

        Assert.Equal(1, _arnes.Contexto.OutputParameters["MovedToNoTsaCount"]);
        var ledger = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        Assert.Equal((int)TsaStatus.SinSelloTsa, ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus).Value);
        // el evento del negocio (159460012, agregado 2026-07-16) queda en la línea de tiempo
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.TsaAbandonada);
    }

    [Fact] // re-sellado exitoso: token + Sellado con TSA + evento 9; sealedon INTACTO
    public void M10_Reseal_Exitoso_PersisteTokenYEvento9()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        _arnes.SelladorTsa = new SelladorOk();
        SembrarLedgerPendiente(ArnesDeApi.PdfDePrueba(1));

        EjecutarJob(new ResealPendingPlugin(), SchemaNames.Apis.ResealPending);

        Assert.Equal(1, _arnes.Contexto.OutputParameters["ResealedCount"]);
        var ledger = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.Ledger.Entidad));
        Assert.Equal((int)TsaStatus.SelladoConTsa, ledger.GetAttributeValue<OptionSetValue>(SchemaNames.Ledger.TsaStatus).Value);
        Assert.False(string.IsNullOrEmpty(ledger.GetAttributeValue<string>(SchemaNames.Ledger.TsaToken)));
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.ReSelladoTsaObtenido);
    }

    [Fact] // ancla rota (archivo ≠ hash del ledger) → JAMÁS se sella; se cuenta APARTE (A8)
    public void M10_Reseal_ConAnclaRota_NoSella_YSeCuentaComoAnclaRota()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        _arnes.SelladorTsa = new SelladorOk();
        var txId = SembrarLedgerPendiente(ArnesDeApi.PdfDePrueba(1));
        _arnes.SembrarArchivo(txId, SchemaNames.Tx.FinalFile, ArnesDeApi.PdfDePrueba(2)); // bytes adulterados

        EjecutarJob(new ResealPendingPlugin(), SchemaNames.Apis.ResealPending);

        Assert.Equal(0, _arnes.Contexto.OutputParameters["ResealedCount"]);
        Assert.Equal(0, _arnes.Contexto.OutputParameters["StillPendingCount"]);
        Assert.Equal(1, _arnes.Contexto.OutputParameters["AnchorMismatchCount"]); // distinguible de "TSA caída"
    }

    [Fact] // S10: la TSA sigue caída → StillPendingCount (distinto de ancla rota)
    public void M10_Reseal_ConTsaCaida_QuedaPendiente()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        _arnes.SelladorTsa = new SelladorCaido();
        SembrarLedgerPendiente(ArnesDeApi.PdfDePrueba(1));

        EjecutarJob(new ResealPendingPlugin(), SchemaNames.Apis.ResealPending);

        Assert.Equal(0, _arnes.Contexto.OutputParameters["ResealedCount"]);
        Assert.Equal(1, _arnes.Contexto.OutputParameters["StillPendingCount"]);
        Assert.Equal(0, _arnes.Contexto.OutputParameters["AnchorMismatchCount"]);
    }

    [Fact] // C1: el cap por corrida acota el lote (los excedentes drenan mañana)
    public void M10_Reseal_ConMasQueElCap_ProcesaSoloElCap()
    {
        _arnes.ConfigurarEnv(SchemaNames.EnvVars.TsaEnabled, "yes");
        _arnes.SelladorTsa = new SelladorOk();
        for (var i = 0; i < ResealPendingPlugin.MaxResellosPorCorrida + 2; i++)
            SembrarLedgerPendiente(ArnesDeApi.PdfDePrueba(1));

        EjecutarJob(new ResealPendingPlugin(), SchemaNames.Apis.ResealPending);

        Assert.Equal(ResealPendingPlugin.MaxResellosPorCorrida, _arnes.Contexto.OutputParameters["ResealedCount"]);
        Assert.Equal(2, _arnes.Contexto.OutputParameters["StillPendingCount"]);
    }

    // ── VerifyDocument ────────────────────────────────────────────

    private (Guid txId, string finalHash, string contentHash) SembrarSellada()
    {
        var finalBytes = ArnesDeApi.PdfDePrueba(2);
        var contentHash = "C".PadLeft(64, 'C');
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.Completado, RoutingType.Paralelo);
        var pid = _arnes.SembrarParticipante(txId, _firmante, ParticipantStatus.Firmado);
        var ledger = new Entity(SchemaNames.Ledger.Entidad);
        ledger[SchemaNames.Ledger.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        ledger[SchemaNames.Ledger.Name] = "SIGIL-2026-000042";
        ledger[SchemaNames.Ledger.ContentHash] = contentHash;
        ledger[SchemaNames.Ledger.FinalHash] = HashUtil.Sha256Hex(finalBytes);
        ledger[SchemaNames.Ledger.TsaStatus] = new OptionSetValue((int)TsaStatus.SelladoConTsa);
        ledger[SchemaNames.Ledger.TsaToken] = "dG9rZW4=";
        ledger[SchemaNames.Ledger.SealedOn] = DateTime.UtcNow;
        _arnes.Servicio.Sembrar(ledger);

        // evento de firma con historial ÍNTEGRO (documenthash == contenthash, sin ediciones)
        var creado = DateTime.UtcNow.AddHours(-1);
        var evFirma = new Entity(SchemaNames.Evento.Entidad);
        evFirma[SchemaNames.Evento.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, txId);
        evFirma[SchemaNames.Evento.Type] = new OptionSetValue((int)EventType.FirmaRegistrada);
        evFirma[SchemaNames.Evento.DocumentHash] = contentHash;
        evFirma[SchemaNames.Evento.ParticipantId] = new EntityReference(SchemaNames.Participante.Entidad, pid);
        evFirma["createdon"] = creado;
        evFirma["modifiedon"] = creado;
        evFirma["createdby"] = new EntityReference(SchemaNames.Usuario.Entidad, _creador);
        evFirma["modifiedby"] = new EntityReference(SchemaNames.Usuario.Entidad, _creador);
        _arnes.Servicio.Sembrar(evFirma);

        return (txId, HashUtil.Sha256Hex(finalBytes), contentHash);
    }

    private void Verificar(Guid txId, string? hash)
    {
        _arnes.Contexto.OutputParameters.Clear();
        _arnes.Contexto.InputParameters.Clear();
        _arnes.Contexto.InputParameters["TransactionId"] = txId;
        if (hash is not null) _arnes.Contexto.InputParameters["Sha256Hash"] = hash;
        _arnes.Ejecutar(new VerifyDocumentPlugin(), SchemaNames.Apis.VerifyDocument, _firmante);
    }

    [Fact] // constancia: sin hash → Found + metadata con hash_final EN CLARO + evento 11
    public void Verify_SoloConTxId_DevuelveLaConstancia_YRegistraElEvento11()
    {
        var (txId, finalHash, _) = SembrarSellada();

        Verificar(txId, hash: null);

        Assert.Equal(true, _arnes.Contexto.OutputParameters["Found"]);
        Assert.False(_arnes.Contexto.OutputParameters.Contains("IsIntact")); // sin hash no hay veredicto
        var meta = (string)_arnes.Contexto.OutputParameters["MetadataJson"];
        Assert.Contains(finalHash, meta);          // hash_final en claro (verificación manual)
        Assert.Contains("SIGIL-2026-000042", meta); // el número de ledger vive ACÁ, no en la hoja
        Assert.Contains("\"historyIntact\":true", meta);
        Assert.Equal("dG9rZW4=", _arnes.Contexto.OutputParameters["TsaTokenBase64"]);
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.VerificacionRealizada);
    }

    [Fact] // veredicto VERDE: hash correcto (case-insensitive)
    public void Verify_ConHashCorrecto_EsIntacto()
    {
        var (txId, finalHash, _) = SembrarSellada();
        Verificar(txId, finalHash.ToLowerInvariant());
        Assert.Equal(true, _arnes.Contexto.OutputParameters["IsIntact"]);
    }

    [Fact] // veredicto ROJO: byte alterado
    public void Verify_ConHashAlterado_NoEsIntacto()
    {
        var (txId, _, _) = SembrarSellada();
        Verificar(txId, "0".PadLeft(64, '0'));
        Assert.Equal(false, _arnes.Contexto.OutputParameters["IsIntact"]);
    }

    [Fact] // verificación cruzada: un evento de firma con hash AJENO delata el historial
    public void Verify_ConDocumenthashAjeno_HistorialNoIntegro()
    {
        var (txId, _, _) = SembrarSellada();
        var evento = _arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad)
            .Single(ev => ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.FirmaRegistrada);
        var upd = new Entity(SchemaNames.Evento.Entidad, evento.Id);
        upd[SchemaNames.Evento.DocumentHash] = "B".PadLeft(64, 'B');
        _arnes.Servicio.Update(upd);

        Verificar(txId, hash: null);

        Assert.Contains("\"historyIntact\":false", (string)_arnes.Contexto.OutputParameters["MetadataJson"]);
    }

    [Fact] // evento de firma EDITADO después (modifiedon > createdon) delata el historial
    public void Verify_ConEventoEditado_HistorialNoIntegro()
    {
        var (txId, _, _) = SembrarSellada();
        var evento = _arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad)
            .Single(ev => ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.FirmaRegistrada);
        var upd = new Entity(SchemaNames.Evento.Entidad, evento.Id);
        upd["modifiedon"] = DateTime.UtcNow.AddMinutes(5);
        _arnes.Servicio.Update(upd);

        Verificar(txId, hash: null);

        Assert.Contains("\"historyIntact\":false", (string)_arnes.Contexto.OutputParameters["MetadataJson"]);
    }

    [Fact] // A3: la tx EXISTE sin sellar → Found=false PERO la verificación se registra (ancla la tx)
    public void Verify_TxSinLedger_FoundFalse_PeroRegistraElEvento11()
    {
        var txId = _arnes.SembrarTransaccion(_creador, TransactionStatus.PendienteDeFirma, RoutingType.Paralelo);
        Verificar(txId, hash: null);
        Assert.Equal(false, _arnes.Contexto.OutputParameters["Found"]);
        Assert.Contains(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad), ev =>
            ev.GetAttributeValue<OptionSetValue>(SchemaNames.Evento.Type).Value == (int)EventType.VerificacionRealizada);
    }

    [Fact] // txId INEXISTENTE → sin ancla, sin evento (el único caso que no deja rastro)
    public void Verify_TxInexistente_FoundFalse_SinEvento()
    {
        Verificar(Guid.NewGuid(), hash: null);
        Assert.Equal(false, _arnes.Contexto.OutputParameters["Found"]);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.Evento.Entidad));
    }

    [Fact] // S4: un hash mal formado es error de CONTRATO, no veredicto "adulterado"
    public void Verify_HashMalFormado_EsRechazado_ConMensajeDeContrato()
    {
        var (txId, _, _) = SembrarSellada();
        _arnes.Contexto.InputParameters.Clear();
        _arnes.Contexto.InputParameters["TransactionId"] = txId;
        _arnes.Contexto.InputParameters["Sha256Hash"] = "no-es-un-hash";
        var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
            _arnes.Ejecutar(new VerifyDocumentPlugin(), SchemaNames.Apis.VerifyDocument, _firmante));
        Assert.Contains("hexadecimal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SelladorOk : Data.ISelladorTsa
    {
        public ResultadoTsa Sellar(byte[] digest, TsaConfig config)
            => new([7, 7, 7], DateTime.UtcNow, config.Endpoints[0].Url, []);
    }

    private sealed class SelladorCaido : Data.ISelladorTsa
    {
        public ResultadoTsa Sellar(byte[] digest, TsaConfig config)
            => new(null, null, null, ["todos los endpoints fallaron"]);
    }
}

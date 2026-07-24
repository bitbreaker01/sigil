// Espejo en código del Apéndice A (docs/referencia/12-convenciones-nomenclatura.md) — valores REALES del portal (prefijo 15946).
// Sincronización garantizada por ChoicesTests (contra el doc) y CF-A16 (doc contra ambiente).
// El resto del código referencia SIEMPRE estos nombres lógicos, jamás números.

namespace Sigil.Plugins.Core.Domain;

/// <summary>sanic_sigil_choice_transactionstatus.</summary>
public enum TransactionStatus
{
    Borrador = 159460000,
    PendienteDeFirma = 159460001,
    FirmadoParcialmente = 159460002,
    Sellando = 159460003,
    Completado = 159460004,
    Rechazado = 159460005,
    Expirado = 159460006,
    ErrorDeSellado = 159460007,
    Cancelado = 159460008,
}

/// <summary>sanic_sigil_choice_participantstatus.</summary>
public enum ParticipantStatus
{
    Pendiente = 159460000,
    TurnoActivo = 159460001,
    Firmado = 159460002,
    Rechazado = 159460003,
}

/// <summary>sanic_sigil_choice_routingtype.</summary>
public enum RoutingType
{
    Secuencial = 159460000,
    Paralelo = 159460001,
}

/// <summary>sanic_sigil_choice_tsastatus.</summary>
public enum TsaStatus
{
    SelladoConTsa = 159460000,
    SinSelloTsa = 159460001,
    ReSelladoPendiente = 159460002,
}

/// <summary>sanic_sigil_choice_eventtype.</summary>
public enum EventType
{
    TransaccionCreada = 159460000,
    EnviadaAFirma = 159460001,
    FirmaRegistrada = 159460002,
    Rechazada = 159460003,
    RecordatorioProgramado = 159460004,
    SelladoIniciado = 159460005,
    SelladoCompletado = 159460006,
    ErrorDeSellado = 159460007,
    ReSelladoTsaObtenido = 159460008,
    Expirada = 159460009,
    VerificacionRealizada = 159460010,
    CanceladaPorElCreador = 159460011,
    TsaAbandonada = 159460012, // agregado por el negocio 2026-07-16 (evento de ResealPending con TSA off)
}

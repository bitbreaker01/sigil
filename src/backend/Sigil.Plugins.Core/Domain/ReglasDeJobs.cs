// Reglas puras de los jobs diarios (expiración y
// recordatorios derivan de DATOS, no de timers). Los filtros por estado son el corazón:
// expirar una Sellando destruiría firmas puestas; recordar una terminal sería spam eterno.

using System;

namespace Sigil.Plugins.Core.Domain;

public static class ReglasDeJobs
{
    /// <summary>T12: SOLO Pendiente de Firma y Firmado Parcialmente expiran.</summary>
    public static bool EsExpirable(TransactionStatus estado)
        => estado is TransactionStatus.PendienteDeFirma or TransactionStatus.FirmadoParcialmente;

    public static bool EstaVencida(DateTime expiresOnUtc, DateTime ahoraUtc)
        => expiresOnUtc < ahoraUtc;

    /// <summary>
    /// T14: Sellando sin actividad del worker por más de 24 h → Error de Sellado.
    /// Umbral deliberadamente holgado: el intervalo entre reintentos de OperationStatus.Retry
    /// no está documentado — un umbral corto pisaría un worker legítimamente reintentando.
    /// </summary>
    public static bool NecesitaSaneamiento(DateTime ultimaActividadUtc, DateTime ahoraUtc)
        => ultimaActividadUtc < ahoraUtc.AddHours(-24);

    /// <summary>
    /// El recordatorio vence por cadencia desde el ÚLTIMO recordatorio (o desde la
    /// activación del turno si nunca hubo) — lastreminderon evita duplicados del flow diario.
    /// </summary>
    public static bool RecordatorioVencido(DateTime turnActivadoEn, DateTime? ultimoRecordatorio,
        int cadenciaDias, DateTime ahora)
    {
        var referencia = ultimoRecordatorio ?? turnActivadoEn;
        return referencia.AddDays(cadenciaDias) <= ahora;
    }

    /// <summary>
    /// Idioma por LCID de usersettings.uilanguageid — primary language id (10 bits
    /// bajos): 0x0A = español, 0x09 = inglés; cualquier otro (o ausente) → default del ambiente.
    /// </summary>
    public static string IdiomaDeLcid(int? lcid, string idiomaDefault)
    {
        if (lcid is null)
            return idiomaDefault;
        return (lcid.Value & 0x3FF) switch
        {
            0x0A => "es",
            0x09 => "en",
            _ => idiomaDefault,
        };
    }
}

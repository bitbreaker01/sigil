// M10 — Jobs (doc 11 §4 / doc 04 §3.1 / doc 06 §3-§4): las reglas PURAS que los jobs
// consumen tras leer el ambiente. El filtro por estado de transacción es el corazón:
// sin él, los jobs expirarían transacciones selladas o recordarían muertas eternamente.

using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ReglasDeJobsTests
{
    private static readonly DateTime Ahora = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

    // ── T12: expiración — SOLO Pendiente de Firma y Firmado Parcialmente ────

    [Theory]
    [InlineData(TransactionStatus.PendienteDeFirma, true)]
    [InlineData(TransactionStatus.FirmadoParcialmente, true)]
    [InlineData(TransactionStatus.Sellando, false)]        // expiraría una tx con todas las firmas
    [InlineData(TransactionStatus.ErrorDeSellado, false)]  // ídem — fallo transitorio no borra firmas
    [InlineData(TransactionStatus.Borrador, false)]        // los borradores no expiran (R6)
    [InlineData(TransactionStatus.Completado, false)]
    [InlineData(TransactionStatus.Rechazado, false)]
    [InlineData(TransactionStatus.Cancelado, false)]
    [InlineData(TransactionStatus.Expirado, false)]
    public void M10_Expiracion_SoloEnEstadosElegibles(TransactionStatus estado, bool elegible)
    {
        Assert.Equal(elegible, ReglasDeJobs.EsExpirable(estado));
    }

    [Theory] // vencida = expireson estrictamente en el pasado
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void M10_Expiracion_PorFecha(int horasDesdeAhora, bool vencida)
    {
        Assert.Equal(vencida, ReglasDeJobs.EstaVencida(Ahora.AddHours(horasDesdeAhora), Ahora));
    }

    // ── T14: saneamiento de Sellando zombi a 24 h (doc 06 R7) ────────────────

    [Theory]
    [InlineData(-25, true)]   // 25 h sin actividad → sanear
    [InlineData(-23, false)]  // 23 h → el worker podría estar legítimamente reintentando
    public void M10_SaneamientoT14_Umbral24Horas(int horasDeUltimaActividad, bool sanear)
    {
        Assert.Equal(sanear, ReglasDeJobs.NecesitaSaneamiento(Ahora.AddHours(horasDeUltimaActividad), Ahora));
    }

    // ── RF-12: recordatorio vencido (cadencia sobre turnactivatedon / lastreminderon) ──

    [Fact] // sin recordatorio previo: cuenta desde la activación del turno
    public void M10_Recordatorio_SinPrevio_VenceDesdeLaActivacion()
    {
        Assert.True(ReglasDeJobs.RecordatorioVencido(
            turnActivadoEn: Ahora.AddDays(-3), ultimoRecordatorio: null, cadenciaDias: 2, ahora: Ahora));
        Assert.False(ReglasDeJobs.RecordatorioVencido(
            turnActivadoEn: Ahora.AddDays(-1), ultimoRecordatorio: null, cadenciaDias: 2, ahora: Ahora));
    }

    [Fact] // con recordatorio previo: cuenta desde el ÚLTIMO recordatorio (evita duplicados del flow diario)
    public void M10_Recordatorio_ConPrevio_VenceDesdeElUltimo()
    {
        Assert.False(ReglasDeJobs.RecordatorioVencido(
            turnActivadoEn: Ahora.AddDays(-10), ultimoRecordatorio: Ahora.AddDays(-1), cadenciaDias: 2, ahora: Ahora));
        Assert.True(ReglasDeJobs.RecordatorioVencido(
            turnActivadoEn: Ahora.AddDays(-10), ultimoRecordatorio: Ahora.AddDays(-3), cadenciaDias: 2, ahora: Ahora));
    }

    // ── RNF-06: idioma del destinatario por LCID, con fallback ───────────────

    [Theory]
    [InlineData(1033, "en")]  // en-US
    [InlineData(2057, "en")]  // en-GB (primary 0x09)
    [InlineData(3082, "es")]  // es-ES
    [InlineData(2058, "es")]  // es-MX (primary 0x0A)
    [InlineData(19466, "es")] // es-NI
    [InlineData(1036, "pt")]  // fr-FR → ni es ni en → default del ambiente
    public void M10_Idioma_PorLcid_ConFallback(int lcid, string esperado)
    {
        Assert.Equal(esperado, ReglasDeJobs.IdiomaDeLcid(lcid, idiomaDefault: "pt"));
    }

    [Fact]
    public void M10_Idioma_SinLcid_UsaElDefault()
    {
        Assert.Equal("es", ReglasDeJobs.IdiomaDeLcid(null, idiomaDefault: "es"));
    }
}

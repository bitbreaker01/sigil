// M1/M2/M3/M9 — reglas puras del ciclo de vida (docs 04 §3.3, 06 §1.1/§2/§3):
// completitud de zonas (RF-28), activación de turnos (P2/P2'), decisión del último
// firmante DESPUÉS del lock (T5/T6/T7), autorización de Cancel (T13) y de acciones
// de firmante (Submit/Reject). El doc 06 §3 es la autoridad del enrutamiento.

using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ReglasDeCicloDeVidaTests
{
    private static readonly Guid Creador = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid U1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid U2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid U3 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    // ── RF-28: completitud de zonas al enviar (M9) ───────────────────────────

    [Fact]
    public void M9_Completitud_TodosConZona_Pasa()
    {
        var faltantes = ReglasDeEnvio.ParticipantesSinZona(
            participantes: [U1, U2],
            userIdsConZona: [U1, U2, U2]); // U2 con dos zonas — válido (1..N)
        Assert.Empty(faltantes);
    }

    [Fact] // el error debe LISTAR a quiénes les falta (doc 04 §3.4)
    public void M9_Completitud_UnoSinZona_LoLista()
    {
        var faltantes = ReglasDeEnvio.ParticipantesSinZona(
            participantes: [U1, U2, U3],
            userIdsConZona: [U1, U3]);
        Assert.Equal([U2], faltantes);
    }

    // ── P2: activación inicial al enviar (doc 06 §3) ─────────────────────────

    [Fact]
    public void M2_ActivacionInicial_Secuencial_SoloElOrden1()
    {
        var activar = ReglasDeEnvio.ActivacionInicial(RoutingType.Secuencial,
            [(U1, (int?)2), (U2, 1), (U3, 3)]);
        Assert.Equal([U2], activar);
    }

    [Fact] // invariante roto (secuencial sin órdenes) → ruido, jamás degradar a paralelo en silencio
    public void M2_ActivacionInicial_SecuencialSinOrdenes_Lanza()
    {
        Assert.ThrowsAny<InvalidOperationException>(() =>
            ReglasDeEnvio.ActivacionInicial(RoutingType.Secuencial, [(U1, (int?)null), (U2, null)]));
    }

    [Fact]
    public void M2_ActivacionInicial_Paralelo_Todos()
    {
        var activar = ReglasDeEnvio.ActivacionInicial(RoutingType.Paralelo,
            [(U1, (int?)null), (U2, null)]);
        Assert.Equal(new[] { U1, U2 }, activar);
    }

    // ── T5/T6/T7 + P2': decisión post-lock del último firmante (doc 06 §3) ───
    // El estado que entra acá es el YA re-leído tras el lock, con el firmante
    // actual todavía en Turno Activo (la regla lo marca Firmado en su cálculo).

    [Fact]
    public void M2_Secuencial_FirmaElPrimero_ActivaAlSiguiente_YNoEsUltimo()
    {
        var d = ReglasDeFirma.Decidir(RoutingType.Secuencial, firmante: U1,
        [
            (U1, (int?)1, ParticipantStatus.TurnoActivo),
            (U2, 2, ParticipantStatus.Pendiente),
        ]);
        Assert.False(d.EsUltimo);
        Assert.Equal(U2, d.SiguienteAActivar);
    }

    [Fact]
    public void M2_Secuencial_FirmaElUltimoOrden_EsUltimo_YNoActivaNada()
    {
        var d = ReglasDeFirma.Decidir(RoutingType.Secuencial, firmante: U2,
        [
            (U1, (int?)1, ParticipantStatus.Firmado),
            (U2, 2, ParticipantStatus.TurnoActivo),
        ]);
        Assert.True(d.EsUltimo);
        Assert.Null(d.SiguienteAActivar);
    }

    [Fact]
    public void M2_Paralelo_QuedanPendientes_NoEsUltimo_YNoActivaNada()
    {
        var d = ReglasDeFirma.Decidir(RoutingType.Paralelo, firmante: U1,
        [
            (U1, (int?)null, ParticipantStatus.TurnoActivo),
            (U2, null, ParticipantStatus.TurnoActivo),
        ]);
        Assert.False(d.EsUltimo);
        Assert.Null(d.SiguienteAActivar); // en paralelo no hay "siguiente" (doc 06 §3)
    }

    [Fact] // "exactamente uno verá cero pendientes" (doc 04 §5) — acá, el que cierra
    public void M2_Paralelo_ElUltimoQueFalta_EsUltimo()
    {
        var d = ReglasDeFirma.Decidir(RoutingType.Paralelo, firmante: U2,
        [
            (U1, (int?)null, ParticipantStatus.Firmado),
            (U2, null, ParticipantStatus.TurnoActivo),
        ]);
        Assert.True(d.EsUltimo);
    }

    [Fact] // firmante único: primera firma = última (T6)
    public void M2_FirmanteUnico_EsUltimo()
    {
        var d = ReglasDeFirma.Decidir(RoutingType.Paralelo, firmante: U1,
            [(U1, (int?)null, ParticipantStatus.TurnoActivo)]);
        Assert.True(d.EsUltimo);
    }

    // ── T13: autorización de Cancel (doc 04 §3.3 / doc 06) ───────────────────

    [Theory]
    [InlineData(TransactionStatus.PendienteDeFirma)]
    [InlineData(TransactionStatus.FirmadoParcialmente)]
    [InlineData(TransactionStatus.ErrorDeSellado)] // T13 lo incluye (fallos deterministas)
    public void M1_Cancelacion_CreadorEnEstadoElegible_Autorizado(TransactionStatus estado)
    {
        Assert.Null(ReglasDeAutorizacion.MotivoParaRechazarCancelacion(Creador, Creador, estado));
    }

    [Theory] // jamás Sellando (doc 04 §3.1); Borrador se borra, no se cancela; terminales no
    [InlineData(TransactionStatus.Borrador)]
    [InlineData(TransactionStatus.Sellando)]
    [InlineData(TransactionStatus.Completado)]
    [InlineData(TransactionStatus.Rechazado)]
    [InlineData(TransactionStatus.Expirado)]
    [InlineData(TransactionStatus.Cancelado)]
    public void M1_Cancelacion_EnEstadoNoElegible_Rechazada(TransactionStatus estado)
    {
        Assert.NotNull(ReglasDeAutorizacion.MotivoParaRechazarCancelacion(Creador, Creador, estado));
    }

    [Fact] // M1: "no-creador cancela"
    public void M1_Cancelacion_NoCreador_Rechazado()
    {
        var motivo = ReglasDeAutorizacion.MotivoParaRechazarCancelacion(
            U1, Creador, TransactionStatus.PendienteDeFirma);
        Assert.NotNull(motivo);
        Assert.Contains("creador", motivo, StringComparison.OrdinalIgnoreCase);
    }

    // ── Submit/Reject: participante con Turno Activo (doc 04 §3.3) ───────────

    [Theory]
    [InlineData(TransactionStatus.PendienteDeFirma)]
    [InlineData(TransactionStatus.FirmadoParcialmente)]
    public void M1_AccionDeFirmante_TurnoActivoEnEstadoFirmable_Autorizada(TransactionStatus estado)
    {
        Assert.Null(ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(
            esParticipante: true, ParticipantStatus.TurnoActivo, estado));
    }

    [Fact] // M1: "participante ajeno firma"
    public void M1_AccionDeFirmante_NoParticipante_Rechazada()
    {
        Assert.NotNull(ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(
            esParticipante: false, null, TransactionStatus.PendienteDeFirma));
    }

    [Theory] // Pendiente en secuencial no puede accionar: aún no le llegó el documento
    [InlineData(ParticipantStatus.Pendiente)]
    [InlineData(ParticipantStatus.Rechazado)]
    public void M1_AccionDeFirmante_SinTurnoActivo_Rechazada(ParticipantStatus estadoDelParticipante)
    {
        Assert.NotNull(ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(
            esParticipante: true, estadoDelParticipante, TransactionStatus.PendienteDeFirma));
    }

    [Theory] // la transacción ya no es firmable
    [InlineData(TransactionStatus.Borrador)]
    [InlineData(TransactionStatus.Sellando)]
    [InlineData(TransactionStatus.Completado)]
    [InlineData(TransactionStatus.Cancelado)]
    public void M1_AccionDeFirmante_EnEstadoNoFirmable_Rechazada(TransactionStatus estado)
    {
        Assert.NotNull(ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(
            esParticipante: true, ParticipantStatus.TurnoActivo, estado));
    }
}

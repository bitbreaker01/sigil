// Reglas puras del ciclo de vida (docs 04 §5, 06 §1.1/§2/§3) — la cáscara re-lee el
// estado DESPUÉS del lock de fila y delega acá las decisiones. Doc 06 §3 es la
// autoridad de la semántica de enrutamiento.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Sigil.Plugins.Core.Domain;

public static class ReglasDeEnvio
{
    /// <summary>
    /// RF-28: participantes sin NINGUNA zona de firma — el envío se bloquea listándolos
    /// (doc 04 §3.4). Devuelve los userIds faltantes en el orden de la lista de participantes.
    /// </summary>
    public static IReadOnlyList<Guid> ParticipantesSinZona(
        IReadOnlyList<Guid> participantes, IReadOnlyCollection<Guid> userIdsConZona)
        => participantes.Where(p => !userIdsConZona.Contains(p)).ToList();

    /// <summary>
    /// P2 — activación inicial al enviar (doc 06 §3): secuencial → solo el orden 1;
    /// paralelo → todos (el dashboard "pendientes por mi firma" queda plano, doc 03 §3).
    /// </summary>
    public static IReadOnlyList<Guid> ActivacionInicial(
        RoutingType routing, IReadOnlyList<(Guid UserId, int? Order)> participantes)
    {
        if (routing == RoutingType.Paralelo)
            return participantes.Select(p => p.UserId).ToList();

        // Invariante de secuencial: todo participante trae orden (Create/UpdateDraft validan
        // 1..N). Si está roto, ruido — degradar a "activar todos" sería paralelo en silencio.
        if (participantes.Any(p => p.Order is null))
            throw new InvalidOperationException(
                "Invariante roto: transacción secuencial con participantes sin orden de firma.");

        return participantes
            .Where(p => p.Order == participantes.Min(x => x.Order))
            .Select(p => p.UserId)
            .ToList();
    }
}

public static class ReglasDeFirma
{
    public readonly struct Decision(bool esUltimo, Guid? siguienteAActivar)
    {
        public bool EsUltimo { get; } = esUltimo;

        /// <summary>Solo secuencial (P2'): el orden siguiente a activar. Null si no aplica.</summary>
        public Guid? SiguienteAActivar { get; } = siguienteAActivar;
    }

    /// <summary>
    /// Decisión post-lock de T5/T6/T7 (doc 04 §5): el estado recibido es el re-leído tras
    /// el lock, con el firmante actual aún en Turno Activo — la regla lo cuenta como Firmado.
    /// "Último" = nadie más queda sin firmar; en secuencial, el siguiente Pendiente por orden
    /// se activa (P2'). Exactamente una ejecución serializada verá cero pendientes.
    /// </summary>
    public static Decision Decidir(
        RoutingType routing, Guid firmante,
        IReadOnlyList<(Guid UserId, int? Order, ParticipantStatus Status)> participantes)
    {
        var pendientes = participantes
            .Where(p => p.UserId != firmante && p.Status != ParticipantStatus.Firmado)
            .ToList();

        if (pendientes.Count == 0)
            return new Decision(esUltimo: true, siguienteAActivar: null);

        if (routing == RoutingType.Secuencial)
        {
            var siguiente = pendientes
                .Where(p => p.Status == ParticipantStatus.Pendiente)
                .OrderBy(p => p.Order)
                .Cast<(Guid UserId, int? Order, ParticipantStatus Status)?>()
                .FirstOrDefault();
            return new Decision(esUltimo: false, siguienteAActivar: siguiente?.UserId);
        }

        return new Decision(esUltimo: false, siguienteAActivar: null); // paralelo: nada que activar
    }
}

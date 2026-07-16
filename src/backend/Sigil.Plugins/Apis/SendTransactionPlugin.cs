// sanic_sigil_capi_SendTransaction (T4, doc 04 §3.1 / doc 06): Borrador → Pendiente de Firma.
// Guards: creador + Borrador; todo participante con ≥1 zona (RF-28 — bloquea listando);
// PDF presente. Efectos: senton/expireson, contenthash (ancla temprana del sellado),
// share a participantes (+eventos previos: la cascada no los cubre — doc 03 §2),
// activación de turnos (P2), evento 2. La historia es permanente desde acá (R6).

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class SendTransactionPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        LockDeFila.Tomar(e.Servicio, target.Id); // R2: toda transición bajo lock

        var tx = Consultas.Transaccion(e.Servicio, target.Id);
        var estado = (TransactionStatus)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
        var creador = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;

        var motivo = ReglasDeAutorizacion.MotivoParaRechazarEdicionDeBorrador(e.Llamante, creador, estado);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        // ── Guards de envío (T4) ──
        var participantes = Consultas.ParticipantesDe(e.Servicio, target.Id);
        if (participantes.Count == 0)
            throw new InvalidPluginExecutionException("La transacción no tiene participantes.");

        var zonas = Consultas.ZonasDe(e.Servicio, participantes.Select(p => p.Id).ToList());
        var participantePorId = participantes.ToDictionary(p => p.Id);
        var userIdsConZona = zonas
            .Select(z => z.GetAttributeValue<EntityReference>(SchemaNames.Zona.ParticipantId).Id)
            .Where(participantePorId.ContainsKey)
            .Select(pid => participantePorId[pid].GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .ToList();
        var sinZona = ReglasDeEnvio.ParticipantesSinZona(
            participantes.Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id).ToList(),
            userIdsConZona);
        if (sinZona.Count > 0)
        {
            var nombres = participantes
                .Where(p => sinZona.Contains(p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id))
                .Select(p => p.GetAttributeValue<string>(SchemaNames.Participante.Name));
            throw new InvalidPluginExecutionException(
                $"Estos participantes no tienen zona de firma asignada (RF-28): {string.Join(", ", nombres)}. " +
                "Asigná al menos una zona a cada firmante antes de enviar.");
        }

        // ── contenthash: SHA-256 del PDF de contenido, calculado AL ENVIAR (doc 03 §4.1) ──
        byte[] pdf;
        try
        {
            pdf = e.Archivos.Descargar(target, SchemaNames.Tx.ContentFile);
        }
        catch (Exception ex)
        {
            e.Trace.Trace("SendTransaction: sin contentfile: {0}", ex.Message);
            throw new InvalidPluginExecutionException("El borrador no tiene documento PDF cargado.");
        }
        var contentHash = HashUtil.Sha256Hex(pdf);

        // ── Efectos (todo validado) ──
        var env = new EnvVars(e.Servicio);
        var dias = tx.Contains(SchemaNames.Tx.ExpirationDays)
            ? tx.GetAttributeValue<int>(SchemaNames.Tx.ExpirationDays)
            : env.EnteroObligatorio(SchemaNames.EnvVars.ExpirationDefaultDays);
        var ahora = DateTime.UtcNow;

        var cambios = new Entity(SchemaNames.Tx.Entidad, target.Id);
        cambios[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.PendienteDeFirma);
        cambios[SchemaNames.Tx.SentOn] = ahora;
        cambios[SchemaNames.Tx.ExpiresOn] = ahora.AddDays(dias);
        cambios[SchemaNames.Tx.ContentHash] = contentHash;
        e.Servicio.Update(cambios);

        // Share de la transacción a cada participante (la cascada cubre participantes/zonas
        // existentes) + share retroactivo de los eventos previos (evento 1 — cascada None).
        var userIds = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador) // el creador ya es owner
            .Distinct()
            .ToList();
        foreach (var u in userIds)
            Consultas.CompartirLectura(e.Servicio, target, u);
        foreach (var evento in Consultas.EventosDe(e.Servicio, target.Id))
        foreach (var u in userIds)
            Consultas.CompartirLectura(e.Servicio, new EntityReference(SchemaNames.Evento.Entidad, evento.Id), u);

        // P2: activación de turnos (secuencial: orden 1; paralelo: todos) + turnactivatedon.
        var activar = ReglasDeEnvio.ActivacionInicial(
            (RoutingType)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.RoutingType).Value,
            participantes.Select(p => (
                p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id,
                p.Contains(SchemaNames.Participante.Order) ? p.GetAttributeValue<int>(SchemaNames.Participante.Order) : (int?)null
            )).ToList());
        foreach (var p in participantes.Where(p =>
                     activar.Contains(p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)))
        {
            var turno = new Entity(SchemaNames.Participante.Entidad, p.Id);
            turno[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.TurnoActivo);
            turno[SchemaNames.Participante.TurnActivatedOn] = ahora;
            e.Servicio.Update(turno);
        }

        var actor = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
        Consultas.CrearEvento(e.Servicio, target, EventType.EnviadaAFirma, actor,
            $"Enviada a firma: {participantes.Count} participante(s), expira {ahora.AddDays(dias):yyyy-MM-dd} UTC.",
            creador, lectores: userIds);

        e.Trace.Trace("SendTransaction: {0} enviada; hash={1}..., turnos activados={2}.",
            target.Id, contentHash.Substring(0, 8), activar.Count);
    }
}

// sanic_sigil_capi_ProcessReminders (RF-12, doc 04 §3.1) — job diario. Selecciona
// participantes en Turno Activo CUYA transacción está en Pendiente de Firma o Firmado
// Parcialmente (FILTRO OBLIGATORIO — doc 06 §3: los participantes conservan Turno Activo
// como verdad histórica en estados terminales; sin el filtro, el job recordaría muertas
// eternamente), con recordatorio vencido por cadencia. Actualiza lastreminderon, crea
// eventos tipo 5, y devuelve RemindersJson AUTOSUFICIENTE (doc 04 §4 / doc 08 W3: el
// flow NO hace lookups para componer la notificación).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class ProcessRemindersPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var ahora = DateTime.UtcNow;
        var env = new EnvVars(e.Servicio);
        var cadencia = env.EnteroObligatorio(SchemaNames.EnvVars.ReminderCadenceDays);
        var idiomaDefault = env.TextoObligatorio(SchemaNames.EnvVars.DefaultLanguage);

        var qActivos = new QueryExpression(SchemaNames.Participante.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Participante.TransactionId, SchemaNames.Participante.UserId,
                SchemaNames.Participante.TurnActivatedOn, SchemaNames.Participante.LastReminderOn),
        };
        qActivos.Criteria.AddCondition(SchemaNames.Participante.Status, ConditionOperator.Equal,
            (int)ParticipantStatus.TurnoActivo);
        var activos = e.Servicio.RetrieveMultiple(qActivos).Entities;

        var recordatorios = new List<object>();
        var transacciones = new Dictionary<Guid, Entity>();                  // caché de tx del lote
        var emisores = new Dictionary<Guid, (string Nombre, string Email)>(); // caché de creador (evita N+1, A2)

        foreach (var p in activos)
        {
            var txId = p.GetAttributeValue<EntityReference>(SchemaNames.Participante.TransactionId).Id;
            if (!transacciones.TryGetValue(txId, out var tx))
            {
                tx = e.Servicio.Retrieve(SchemaNames.Tx.Entidad, txId, new ColumnSet(
                    SchemaNames.Tx.Status, SchemaNames.Tx.Name, SchemaNames.Tx.Message,
                    SchemaNames.Tx.ExpiresOn, SchemaNames.Tx.OwnerId));
                transacciones[txId] = tx;
            }

            // FILTRO OBLIGATORIO por estado de la transacción (doc 06 §3): los participantes
            // conservan Turno Activo como verdad histórica en estados terminales — sin esto
            // recordaríamos muertas eternamente. LÍMITE de escala declarado (antagonista A2):
            // el filtro es en memoria, así que el job lee todos los Turno Activo históricos;
            // aceptable para el volumen de Sigil (doc 03 §9). Si crece, empujar a SQL con un
            // LinkEntity de estado.
            var estadoTx = (TransactionStatus)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
            if (estadoTx is not (TransactionStatus.PendienteDeFirma or TransactionStatus.FirmadoParcialmente))
                continue;

            if (!p.Contains(SchemaNames.Participante.TurnActivatedOn))
                continue; // sin activación registrada no hay base de cálculo (anomalía — se omite)
            var activadoEn = p.GetAttributeValue<DateTime>(SchemaNames.Participante.TurnActivatedOn);
            var ultimo = p.GetAttributeValue<DateTime?>(SchemaNames.Participante.LastReminderOn);
            if (!ReglasDeJobs.RecordatorioVencido(activadoEn, ultimo, cadencia, ahora))
                continue;

            // Destinatario + emisor (todo en memoria: el JSON es autosuficiente).
            var userId = p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id;
            var destinatario = e.Servicio.Retrieve(SchemaNames.Usuario.Entidad, userId,
                new ColumnSet(SchemaNames.Usuario.FullName, SchemaNames.Usuario.Email, SchemaNames.Usuario.Upn));
            var creadorId = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;
            if (!emisores.TryGetValue(creadorId, out var emisor)) // sin N+1 por creador (A2)
                emisores[creadorId] = emisor = Consultas.SnapshotDeActor(e.Servicio, creadorId);

            recordatorios.Add(new
            {
                participantId = p.Id,
                userId,
                transactionId = txId,
                transactionName = tx.GetAttributeValue<string>(SchemaNames.Tx.Name),
                daysWaiting = (int)(ahora - activadoEn).TotalDays,
                recipientEmail = destinatario.GetAttributeValue<string>(SchemaNames.Usuario.Email)
                                 ?? destinatario.GetAttributeValue<string>(SchemaNames.Usuario.Upn) ?? string.Empty,
                recipientName = destinatario.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? string.Empty,
                recipientLanguage = ReglasDeJobs.IdiomaDeLcid(LcidDe(e, userId), idiomaDefault),
                senderName = emisor.Nombre,
                creatorMessage = tx.GetAttributeValue<string>(SchemaNames.Tx.Message) ?? string.Empty,
                expiresOnUtc = tx.GetAttributeValue<DateTime?>(SchemaNames.Tx.ExpiresOn)?.ToString("o") ?? string.Empty,
            });

            // lastreminderon evita duplicados del flow diario (doc 03 §4.2).
            var marca = new Entity(SchemaNames.Participante.Entidad, p.Id);
            marca[SchemaNames.Participante.LastReminderOn] = ahora;
            e.Servicio.Update(marca);

            Consultas.CrearEvento(e.Servicio, new EntityReference(SchemaNames.Tx.Entidad, txId),
                EventType.RecordatorioProgramado, ("Sistema", string.Empty),
                $"Recordatorio programado tras {(int)(ahora - activadoEn).TotalDays} día(s) de espera.",
                creadorId, participantId: p.Id, lectores: new[] { userId }.Where(u => u != creadorId).ToList());
        }

        e.Output("RemindersJson", System.Text.Json.JsonSerializer.Serialize(recordatorios));
        e.Trace.Trace("ProcessReminders: {0} recordatorio(s) programado(s) de {1} turnos activos.",
            recordatorios.Count, activos.Count);
    }

    /// <summary>uilanguageid de usersettings del firmante (RNF-06) — null si no está disponible.</summary>
    private static int? LcidDe(EntornoDeApi e, Guid userId)
    {
        var q = new QueryExpression("usersettings") { ColumnSet = new ColumnSet("uilanguageid") };
        q.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        var fila = e.Servicio.RetrieveMultiple(q).Entities.FirstOrDefault();
        return fila?.GetAttributeValue<int?>("uilanguageid");
    }
}

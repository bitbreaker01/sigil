// Consultas y escrituras compartidas por los handlers de Custom APIs.
// Todo corre con el servicio elevado; el ownerid se setea EXPLÍCITO en cada Create
// (doc 03 §4: el owner no se hereda al crear hijos — verificado).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Data;

public static class Consultas
{
    /// <summary>Estado y creador (owner) de la transacción — el par que consumen las reglas de autorización.</summary>
    public static (TransactionStatus Estado, Guid Creador) EstadoYCreador(IOrganizationService servicio, Guid transactionId)
    {
        var fila = servicio.Retrieve(SchemaNames.Tx.Entidad, transactionId,
            new ColumnSet(SchemaNames.Tx.Status, SchemaNames.Tx.OwnerId, SchemaNames.Tx.RoutingType, SchemaNames.Tx.Name));
        var estado = (TransactionStatus)fila.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
        var creador = fila.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;
        return (estado, creador);
    }

    /// <summary>La fila completa que usan los handlers de edición (estado + routing + nombre).</summary>
    public static Entity Transaccion(IOrganizationService servicio, Guid transactionId)
        => servicio.Retrieve(SchemaNames.Tx.Entidad, transactionId,
            new ColumnSet(SchemaNames.Tx.Status, SchemaNames.Tx.OwnerId, SchemaNames.Tx.RoutingType, SchemaNames.Tx.Name));

    /// <summary>
    /// Carga y valida a los firmantes: existentes Y habilitados (doc 04 §3.4 — la parte
    /// de ParticipantsJson que solo Dataverse puede responder). Falla listando a los ausentes.
    /// </summary>
    public static Dictionary<Guid, (string Nombre, string Email)> UsuariosHabilitados(
        IOrganizationService servicio, IReadOnlyCollection<Guid> userIds)
    {
        var query = new QueryExpression(SchemaNames.Usuario.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Usuario.FullName, SchemaNames.Usuario.Email, SchemaNames.Usuario.IsDisabled),
        };
        query.Criteria.AddCondition(SchemaNames.Usuario.Id, ConditionOperator.In,
            userIds.Cast<object>().ToArray());

        var filas = servicio.RetrieveMultiple(query).Entities;

        var faltantes = userIds.Where(id => filas.All(f => f.Id != id)).ToList();
        if (faltantes.Count > 0)
            throw new InvalidPluginExecutionException(
                $"Estos usuarios no existen en el ambiente: {string.Join(", ", faltantes)}.");

        var deshabilitados = filas.Where(f => f.GetAttributeValue<bool>(SchemaNames.Usuario.IsDisabled)).ToList();
        if (deshabilitados.Count > 0)
            throw new InvalidPluginExecutionException(
                $"Estos usuarios están deshabilitados y no pueden firmar: {string.Join(", ", deshabilitados.Select(f => f.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? f.Id.ToString()))}.");

        return filas.ToDictionary(
            f => f.Id,
            f => (f.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? f.Id.ToString(),
                  f.GetAttributeValue<string>(SchemaNames.Usuario.Email) ?? string.Empty));
    }

    public static bool EsParticipante(IOrganizationService servicio, Guid transactionId, Guid userId)
    {
        var query = new QueryExpression(SchemaNames.Participante.Entidad) { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition(SchemaNames.Participante.TransactionId, ConditionOperator.Equal, transactionId);
        query.Criteria.AddCondition(SchemaNames.Participante.UserId, ConditionOperator.Equal, userId);
        return servicio.RetrieveMultiple(query).Entities.Count > 0;
    }

    public static IReadOnlyList<Entity> ParticipantesDe(IOrganizationService servicio, Guid transactionId)
    {
        var query = new QueryExpression(SchemaNames.Participante.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Participante.UserId, SchemaNames.Participante.Name),
        };
        query.Criteria.AddCondition(SchemaNames.Participante.TransactionId, ConditionOperator.Equal, transactionId);
        return servicio.RetrieveMultiple(query).Entities.ToList();
    }

    public static IReadOnlyList<Entity> ZonasDe(IOrganizationService servicio, IReadOnlyCollection<Guid> participantIds)
    {
        if (participantIds.Count == 0)
            return Array.Empty<Entity>();

        var query = new QueryExpression(SchemaNames.Zona.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Zona.Page, SchemaNames.Zona.Name, SchemaNames.Zona.ParticipantId),
        };
        query.Criteria.AddCondition(SchemaNames.Zona.ParticipantId, ConditionOperator.In,
            participantIds.Cast<object>().ToArray());
        return servicio.RetrieveMultiple(query).Entities.ToList();
    }

    public static IReadOnlyList<Entity> EventosDe(IOrganizationService servicio, Guid transactionId)
    {
        var query = new QueryExpression(SchemaNames.Evento.Entidad) { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition(SchemaNames.Evento.TransactionId, ConditionOperator.Equal, transactionId);
        return servicio.RetrieveMultiple(query).Entities.ToList();
    }

    /// <summary>Snapshot del actor para eventos (doc 03 §4.6) — SIEMPRE del contexto, jamás del cliente.</summary>
    public static (string Nombre, string Email) SnapshotDeActor(IOrganizationService servicio, Guid userId)
    {
        var fila = servicio.Retrieve(SchemaNames.Usuario.Entidad, userId,
            new ColumnSet(SchemaNames.Usuario.FullName, SchemaNames.Usuario.Email));
        return (fila.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? userId.ToString(),
                fila.GetAttributeValue<string>(SchemaNames.Usuario.Email) ?? string.Empty);
    }

    public static void CrearEvento(
        IOrganizationService servicio, EntityReference transaccion, EventType tipo,
        (string Nombre, string Email) actor, string detalles, Guid owner)
    {
        var ev = new Entity(SchemaNames.Evento.Entidad);
        ev[SchemaNames.Evento.Name] = Truncar($"{tipo} — {transaccion.Id}", 300);
        ev[SchemaNames.Evento.TransactionId] = transaccion;
        ev[SchemaNames.Evento.Type] = new OptionSetValue((int)tipo);
        ev[SchemaNames.Evento.ActorName] = Truncar(actor.Nombre, 200);
        ev[SchemaNames.Evento.ActorEmail] = Truncar(actor.Email, 200);
        ev[SchemaNames.Evento.OccurredOn] = DateTime.UtcNow;
        ev[SchemaNames.Evento.Details] = Truncar(detalles, 4000);
        ev[SchemaNames.Evento.OwnerId] = new EntityReference(SchemaNames.Usuario.Entidad, owner);
        servicio.Create(ev);
    }

    public static string Truncar(string valor, int max)
        => valor.Length <= max ? valor : valor.Substring(0, max);
}

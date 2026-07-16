// STUB PROPIO de IOrganizationService (decisión 2026-07-15, doc 11 §2 — FakeXrmEasy
// descartado por licenciamiento). Cubre exactamente lo que la capa Apis/ usa:
// Create/Retrieve/RetrieveMultiple/Update/Delete/Execute + tracking de llamadas.
// Límites DECLARADOS (doc 11 §3): sin locks SQL reales (script de carrera en Dev),
// sin file blocks (seam IFileTransfer), ColumnSet ignorado (devuelve todo), sin cascadas
// de plataforma (los tests siembran y verifican explícito).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Sigil.Plugins.Tests.Stub;

public sealed class OperacionRegistrada(string tipo, string entidad, Guid id, Entity? datos)
{
    public string Tipo { get; } = tipo;
    public string Entidad { get; } = entidad;
    public Guid Id { get; } = id;
    public Entity? Datos { get; } = datos;

    public override string ToString() => $"{Tipo}:{Entidad}:{Id}";
}

public sealed class StubOrganizationService : IOrganizationService
{
    /// <summary>tabla lógica → (id → fila). Estado verificable con consultas, no con mocks.</summary>
    public Dictionary<string, Dictionary<Guid, Entity>> Tablas { get; } = new();

    /// <summary>Toda operación de escritura, EN ORDEN — para asserts de "el lock va primero", "eventos antes que la transacción".</summary>
    public List<OperacionRegistrada> Operaciones { get; } = new();

    /// <summary>Handlers de Execute por RequestName (RetrieveEnvironmentVariableValue, etc.).</summary>
    public Dictionary<string, Func<OrganizationRequest, OrganizationResponse>> Manejadores { get; } = new();

    /// <summary>GrantAccess registrados — para los asserts de M13 (sharing verificable, no mockeado).</summary>
    public List<(string Entidad, Guid Id, Guid UserId)> Compartidos { get; } = new();

    /// <summary>Inyección de fallos en Create (M4): si devuelve una excepción, se lanza en vez de crear.</summary>
    public Func<Entity, Exception?>? InterceptarCreate { get; set; }

    public Entity Sembrar(Entity fila)
    {
        if (fila.Id == Guid.Empty)
            fila.Id = Guid.NewGuid();
        // Clonar como hacen Create/Retrieve: si el test muta su variable local no debe
        // "modificar la BD" por aliasing (A9 del antagonista).
        Filas(fila.LogicalName)[fila.Id] = Clonar(fila);
        return fila;
    }

    public IReadOnlyList<Entity> FilasDe(string entidad)
        => Tablas.TryGetValue(entidad, out var filas) ? filas.Values.ToList() : new List<Entity>();

    // ── IOrganizationService ─────────────────────────────────────────────────

    public Guid Create(Entity entity)
    {
        if (InterceptarCreate?.Invoke(entity) is { } ex)
            throw ex;
        var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        var clon = Clonar(entity);
        clon.Id = id;
        Filas(entity.LogicalName)[id] = clon;
        Operaciones.Add(new OperacionRegistrada("Create", entity.LogicalName, id, Clonar(entity)));
        return id;
    }

    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
    {
        Operaciones.Add(new OperacionRegistrada("Read", entityName, id, null));
        if (!Filas(entityName).TryGetValue(id, out var fila))
            throw new InvalidOperationException($"{entityName} {id} no existe en el stub.");

        // El ColumnSet se HONRA como en Dataverse real (corrección del antagonista, 2026-07-16):
        // ignorarlo tapó un bug real de producción — un Contains() sobre una columna que el
        // ColumnSet no pidió daba true en el stub y false en Dev (caso expirationdays de Send).
        return Proyectar(Clonar(fila), columnSet);
    }

    public void Update(Entity entity)
    {
        if (!Filas(entity.LogicalName).TryGetValue(entity.Id, out var fila))
            throw new InvalidOperationException($"{entity.LogicalName} {entity.Id} no existe en el stub.");
        foreach (var kv in entity.Attributes)
            fila[kv.Key] = kv.Value;
        Operaciones.Add(new OperacionRegistrada("Update", entity.LogicalName, entity.Id, Clonar(entity)));
    }

    public void Delete(string entityName, Guid id)
    {
        if (!Filas(entityName).Remove(id))
            throw new InvalidOperationException($"{entityName} {id} no existe en el stub.");
        Operaciones.Add(new OperacionRegistrada("Delete", entityName, id, null));
    }

    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        if (query is not QueryExpression qe)
            throw new NotSupportedException($"El stub solo soporta QueryExpression (recibido: {query.GetType().Name}).");

        Operaciones.Add(new OperacionRegistrada("Read", qe.EntityName, Guid.Empty, null));
        var candidatas = FilasDe(qe.EntityName)
            .Where(f => CumpleFiltro(f, qe.EntityName, qe.Criteria))
            .Select(f => Proyectar(Clonar(f), qe.ColumnSet));
        return new EntityCollection(candidatas.ToList()) { EntityName = qe.EntityName };
    }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        // GrantAccess se registra nativamente (M13): efecto verificable en Compartidos.
        if (request is Microsoft.Crm.Sdk.Messages.GrantAccessRequest grant)
        {
            Compartidos.Add((grant.Target.LogicalName, grant.Target.Id, grant.PrincipalAccess.Principal.Id));
            return new Microsoft.Crm.Sdk.Messages.GrantAccessResponse();
        }

        if (Manejadores.TryGetValue(request.RequestName, out var manejador))
            return manejador(request);
        throw new NotSupportedException(
            $"El stub no tiene handler para '{request.RequestName}' — registrarlo en Manejadores.");
    }

    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new NotSupportedException("Associate no forma parte de la superficie usada por Apis/.");

    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => throw new NotSupportedException("Disassociate no forma parte de la superficie usada por Apis/.");

    // ── interno ──────────────────────────────────────────────────────────────

    private Dictionary<Guid, Entity> Filas(string entidad)
    {
        if (!Tablas.TryGetValue(entidad, out var filas))
            Tablas[entidad] = filas = new Dictionary<Guid, Entity>();
        return filas;
    }

    private static bool CumpleFiltro(Entity fila, string entidad, FilterExpression? filtro)
    {
        if (filtro is null)
            return true;
        // Solo AND de condiciones simples — la superficie real de Apis/ (límite declarado).
        // Un OR se trataría como AND en silencio → falso verde: fallar ruidoso (A5).
        if (filtro.FilterOperator == LogicalOperator.Or && (filtro.Conditions.Count + filtro.Filters.Count) > 1)
            throw new NotSupportedException("El stub solo soporta filtros AND (registrar el caso OR si aparece).");
        return filtro.Conditions.All(c => CumpleCondicion(fila, entidad, c)) &&
               filtro.Filters.All(f => CumpleFiltro(fila, entidad, f));
    }

    private static bool CumpleCondicion(Entity fila, string entidad, ConditionExpression condicion)
    {
        var valor = ValorDeAtributo(fila, entidad, condicion.AttributeName);
        return condicion.Operator switch
        {
            ConditionOperator.Equal => Iguales(valor, condicion.Values.FirstOrDefault()),
            ConditionOperator.In => condicion.Values.Any(v => Iguales(valor, v)),
            // Fechas de los jobs (expireson/modifiedon): null NUNCA satisface un rango; un
            // valor no comparable es un error de uso, no un false silencioso (S9 del antagonista).
            ConditionOperator.LessThan => valor switch
            {
                null => false,
                IComparable c when condicion.Values.FirstOrDefault() is { } lim => c.CompareTo(lim) < 0,
                _ => throw new NotSupportedException(
                    $"LessThan sobre un valor no comparable ({valor.GetType().Name}) no está soportado por el stub."),
            },
            _ => throw new NotSupportedException($"Operador {condicion.Operator} no soportado por el stub."),
        };
    }

    private static object? ValorDeAtributo(Entity fila, string entidad, string atributo)
    {
        // Convención de PK de Dataverse: <entidad>id refiere al Id de la fila.
        if (atributo == entidad + "id")
            return fila.Id;
        return fila.Attributes.TryGetValue(atributo, out var v) ? v : null;
    }

    private static bool Iguales(object? valorDeFila, object? valorDeCondicion)
        => Equals(Normalizar(valorDeFila), Normalizar(valorDeCondicion));

    private static object? Normalizar(object? valor) => valor switch
    {
        EntityReference r => r.Id,
        OptionSetValue o => o.Value,
        _ => valor,
    };

    private static Entity Clonar(Entity original)
    {
        var clon = new Entity(original.LogicalName) { Id = original.Id };
        foreach (var kv in original.Attributes)
            clon[kv.Key] = kv.Value;
        return clon;
    }

    // Honra el ColumnSet como Dataverse real — un Contains() sobre columna no pedida debe dar false.
    private static Entity Proyectar(Entity fila, ColumnSet? columnSet)
    {
        if (columnSet is null || columnSet.AllColumns)
            return fila;
        foreach (var attr in fila.Attributes.Keys.Where(k => !columnSet.Columns.Contains(k)).ToList())
            fila.Attributes.Remove(attr);
        return fila;
    }
}

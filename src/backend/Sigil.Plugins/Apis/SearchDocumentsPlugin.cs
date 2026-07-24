// sanic_sigil_capi_SearchDocuments — Unbound. Búsqueda PAGINADA de los documentos
// en los que el llamante está involucrado (creados ∪ participados), con filtros, orden y paginación
// del lado del servidor para que el cliente NUNCA cargue el set completo. El conjunto se carga con
// queries simples (Equal/In — estilo de la casa, sin FetchXML ni LinkEntity) y el filtrado/orden/
// paginado corre in-memory sobre el set ACOTADO del propio usuario. Solo lo PROPIO.
// Out: ResultsJson (la página), Total (total filtrado), NextPagingCookie (offset opaco, "" = última).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class SearchDocumentsPlugin : SigilApiPlugin
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private static readonly ColumnSet TxColumns = new(
        SchemaNames.Tx.Name, SchemaNames.Tx.Status, SchemaNames.Tx.RoutingType, SchemaNames.Tx.Message,
        SchemaNames.Tx.SentOn, SchemaNames.Tx.ExpiresOn, SchemaNames.Tx.CompletedOn, SchemaNames.Tx.OwnerId, "createdon");

    protected override void Ejecutar(EntornoDeApi e)
    {
        var yo = e.Llamante;

        Guid? InputGuid(string n) =>
            e.Contexto.InputParameters.TryGetValue(n, out var v) && v is Guid g && g != Guid.Empty ? g : (Guid?)null;

        var text = e.Input<string>("Text")?.Trim();
        var creatorId = InputGuid("CreatorId");
        var status = e.InputOptionalInt("Status");
        // CSV of GUIDs — the doc must include ALL of them (AND).
        var participantIds = (e.Input<string>("ParticipantIds") ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Guid.TryParse(x.Trim(), out var g) ? g.ToString() : null)
            .Where(x => x is not null).Cast<string>().Distinct().ToList();
        var signatureVersion = e.InputOptionalInt("SignatureVersion");
        var sort = e.Input<string>("Sort") ?? "createdDesc";
        var pageSize = Math.Min(Math.Max(e.InputOptionalInt("PageSize") ?? DefaultPageSize, 1), MaxPageSize);
        var offset = int.TryParse(e.Input<string>("PagingCookie"), out var o) && o > 0 ? o : 0;

        // 1) Set base: docs que creé ∪ docs en los que participo (deduplicados por id).
        var porId = new Dictionary<Guid, Entity>();
        foreach (var tx in TransaccionesCreadasPor(e.Servicio, yo))
            porId[tx.Id] = tx;
        var misTxIds = ParticipacionesDe(e.Servicio, yo);
        foreach (var tx in TransaccionesPorId(e.Servicio, misTxIds.Where(id => !porId.ContainsKey(id)).ToList()))
            porId[tx.Id] = tx;

        if (porId.Count == 0)
        {
            Emitir(e, new List<object>(), 0, string.Empty);
            return;
        }

        // 2) Enriquecimiento: firmantes por doc, nombres, y la versión de MI firma en cada doc.
        var participantesPorTx = ParticipantesPorTx(e.Servicio, porId.Keys.ToList());
        var userIds = new HashSet<Guid>();
        foreach (var tx in porId.Values)
            if (tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId)?.Id is { } oid) userIds.Add(oid);
        foreach (var ps in participantesPorTx.Values)
            foreach (var p in ps)
                if (p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId)?.Id is { } uid) userIds.Add(uid);
        var nombrePorUser = NombresDeUsuarios(e.Servicio, userIds);
        var versionPorFirma = Consultas.VersionesDeFirmaDe(e.Servicio, yo)
            .ToDictionary(f => f.Id, f => f.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version));

        // 3) Filas enriquecidas.
        var filas = porId.Values.Select(tx =>
        {
            var participantes = participantesPorTx.TryGetValue(tx.Id, out var ps) ? ps : new List<Entity>();
            var mio = participantes.FirstOrDefault(p =>
                p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId)?.Id == yo);
            int? miVersion =
                mio?.GetAttributeValue<EntityReference>(SchemaNames.Participante.MasterSignatureId)?.Id is { } fid
                && versionPorFirma.TryGetValue(fid, out var ver) ? ver : (int?)null;
            var ownerId = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId)?.Id;
            return new Fila
            {
                Id = tx.Id,
                Name = tx.GetAttributeValue<string>(SchemaNames.Tx.Name) ?? string.Empty,
                Status = tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status)?.Value ?? 0,
                Routing = tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.RoutingType)?.Value ?? 0,
                CreatorId = ownerId,
                CreatorName = ownerId is { } cid && nombrePorUser.TryGetValue(cid, out var cn) ? cn : string.Empty,
                Message = tx.GetAttributeValue<string>(SchemaNames.Tx.Message),
                SentOn = Fecha(tx, SchemaNames.Tx.SentOn),
                ExpiresOn = Fecha(tx, SchemaNames.Tx.ExpiresOn),
                CompletedOn = Fecha(tx, SchemaNames.Tx.CompletedOn),
                CreatedOn = Fecha(tx, "createdon"),
                MySignatureVersion = miVersion,
                Participants = participantes.Select(p =>
                {
                    var uid = p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId)?.Id;
                    return new Signer
                    {
                        UserId = uid?.ToString() ?? string.Empty,
                        Name = uid is { } u && nombrePorUser.TryGetValue(u, out var n) ? n
                               : p.GetAttributeValue<string>(SchemaNames.Participante.SignerName) ?? string.Empty,
                    };
                }).ToList(),
            };
        });

        // 4) Filtros (in-memory).
        if (!string.IsNullOrEmpty(text))
            filas = filas.Where(f => f.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        if (creatorId is { } cf)
            filas = filas.Where(f => f.CreatorId == cf);
        if (status is { } sf)
            filas = filas.Where(f => f.Status == sf);
        if (participantIds.Count > 0)
            filas = filas.Where(f => participantIds.All(pid => f.Participants.Any(p => p.UserId == pid)));
        if (signatureVersion is { } vf)
            filas = filas.Where(f => f.MySignatureVersion == vf);

        // 5) Orden + 6) página.
        var ordenadas = Ordenar(filas, sort).ToList();
        var total = ordenadas.Count;
        var pagina = ordenadas.Skip(offset).Take(pageSize).Select(f => f.ToJson()).ToList();
        var next = offset + pageSize < total ? (offset + pageSize).ToString() : string.Empty;

        Emitir(e, pagina, total, next);
    }

    private static void Emitir(EntornoDeApi e, List<object> filas, int total, string next)
    {
        e.Output("ResultsJson", JsonSerializer.Serialize(filas));
        e.Output("Total", total);
        e.Output("NextPagingCookie", next);
        e.Trace.Trace("SearchDocuments: {0} de {1} (next='{2}').", filas.Count, total, next);
    }

    // ── queries (Equal/In — testeables con el stub) ──────────────────────────

    private static IReadOnlyList<Entity> TransaccionesCreadasPor(IOrganizationService s, Guid userId)
    {
        var q = new QueryExpression(SchemaNames.Tx.Entidad) { ColumnSet = TxColumns };
        q.Criteria.AddCondition(SchemaNames.Tx.OwnerId, ConditionOperator.Equal, userId);
        return s.RetrieveMultiple(q).Entities.ToList();
    }

    private static List<Guid> ParticipacionesDe(IOrganizationService s, Guid userId)
    {
        var q = new QueryExpression(SchemaNames.Participante.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Participante.TransactionId),
        };
        q.Criteria.AddCondition(SchemaNames.Participante.UserId, ConditionOperator.Equal, userId);
        return s.RetrieveMultiple(q).Entities
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.TransactionId)?.Id)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
    }

    private static IReadOnlyList<Entity> TransaccionesPorId(IOrganizationService s, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return Array.Empty<Entity>();
        var q = new QueryExpression(SchemaNames.Tx.Entidad) { ColumnSet = TxColumns };
        q.Criteria.AddCondition(SchemaNames.Tx.Entidad + "id", ConditionOperator.In, ids.Cast<object>().ToArray());
        return s.RetrieveMultiple(q).Entities.ToList();
    }

    private static Dictionary<Guid, List<Entity>> ParticipantesPorTx(IOrganizationService s, IReadOnlyCollection<Guid> txIds)
    {
        if (txIds.Count == 0) return new Dictionary<Guid, List<Entity>>();
        var q = new QueryExpression(SchemaNames.Participante.Entidad)
        {
            ColumnSet = new ColumnSet(SchemaNames.Participante.TransactionId, SchemaNames.Participante.UserId,
                SchemaNames.Participante.SignerName, SchemaNames.Participante.MasterSignatureId,
                SchemaNames.Participante.Status, SchemaNames.Participante.Order),
        };
        q.Criteria.AddCondition(SchemaNames.Participante.TransactionId, ConditionOperator.In, txIds.Cast<object>().ToArray());
        return s.RetrieveMultiple(q).Entities
            .Where(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.TransactionId) != null)
            .GroupBy(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.TransactionId).Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.GetAttributeValue<int>(SchemaNames.Participante.Order)).ToList());
    }

    private static Dictionary<Guid, string> NombresDeUsuarios(IOrganizationService s, ICollection<Guid> userIds)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, string>();
        var q = new QueryExpression(SchemaNames.Usuario.Entidad) { ColumnSet = new ColumnSet(SchemaNames.Usuario.FullName) };
        q.Criteria.AddCondition(SchemaNames.Usuario.Id, ConditionOperator.In, userIds.Cast<object>().ToArray());
        return s.RetrieveMultiple(q).Entities
            .ToDictionary(u => u.Id, u => u.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? string.Empty);
    }

    // ── orden / fechas ───────────────────────────────────────────────────────

    private static IEnumerable<Fila> Ordenar(IEnumerable<Fila> filas, string sort) => sort switch
    {
        "nameAsc" => filas.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
        "nameDesc" => filas.OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
        "sentAsc" => PorFecha(filas, f => f.SentOn, asc: true),
        "sentDesc" => PorFecha(filas, f => f.SentOn, asc: false),
        "completedAsc" => PorFecha(filas, f => f.CompletedOn, asc: true),
        "completedDesc" => PorFecha(filas, f => f.CompletedOn, asc: false),
        "createdAsc" => PorFecha(filas, f => f.CreatedOn, asc: true),
        _ => PorFecha(filas, f => f.CreatedOn, asc: false), // createdDesc por defecto
    };

    // Fechas ausentes SIEMPRE al final, en cualquier dirección (mismo criterio que el front).
    private static IEnumerable<Fila> PorFecha(IEnumerable<Fila> filas, Func<Fila, DateTime?> clave, bool asc)
    {
        var lista = filas.ToList();
        var conFecha = lista.Where(f => clave(f) != null);
        var ordenadas = asc ? conFecha.OrderBy(f => clave(f)!.Value) : conFecha.OrderByDescending(f => clave(f)!.Value);
        return ordenadas.Concat(lista.Where(f => clave(f) == null));
    }

    private static DateTime? Fecha(Entity tx, string attr)
    {
        var v = tx.GetAttributeValue<DateTime>(attr);
        return v == DateTime.MinValue ? (DateTime?)null : v;
    }

    private sealed class Signer
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Fila
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Status { get; set; }
        public int Routing { get; set; }
        public Guid? CreatorId { get; set; }
        public string CreatorName { get; set; } = string.Empty;
        public string? Message { get; set; }
        public DateTime? SentOn { get; set; }
        public DateTime? ExpiresOn { get; set; }
        public DateTime? CompletedOn { get; set; }
        public DateTime? CreatedOn { get; set; }
        public int? MySignatureVersion { get; set; }
        public List<Signer> Participants { get; set; } = new();

        public object ToJson() => new
        {
            id = Id.ToString(),
            name = Name,
            state = Status,
            routing = Routing,
            creatorId = CreatorId?.ToString() ?? string.Empty,
            creatorName = CreatorName,
            message = Message,
            sentOn = SentOn?.ToString("o"),
            expiresOn = ExpiresOn?.ToString("o"),
            completedOn = CompletedOn?.ToString("o"),
            createdOn = CreatedOn?.ToString("o"),
            mySignatureVersion = MySignatureVersion,
            participants = Participants.Select(p => new { userId = p.UserId, name = p.Name }).ToList(),
        };
    }
}

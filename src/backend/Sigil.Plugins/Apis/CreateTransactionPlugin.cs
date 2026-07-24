// sanic_sigil_capi_CreateTransaction — Unbound. Crea el borrador.
// In: Name, Message?, RoutingType ("sequential"|"parallel"), ExpirationDays?, PdfBase64,
//     ParticipantsJson, ZonesJson?. Out: TransactionId.
// Orden: TODA la validación primero, después las escrituras. Sin lock: la fila
// es nueva, nadie compite por ella (aplica a estado COMPARTIDO).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class CreateTransactionPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var name = e.Input<string>("Name");
        var message = e.Input<string>("Message");
        var routingToken = e.Input<string>("RoutingType");
        var expirationDays = e.InputOptionalInt("ExpirationDays"); // 0 = ausente (quirk de Custom API)
        var pdfBase64 = e.Input<string>("PdfBase64");
        var participantsJson = e.Input<string>("ParticipantsJson");
        var zonesJson = e.Input<string>("ZonesJson");

        // ── Validación (núcleo puro) — se junta TODO lo reportable antes de rechazar ──
        var errores = new List<string>(ValidacionDeEntrada.ValidarEncabezado(name, expirationDays, message));

        var routingOk = ValidacionDeEntrada.TryParsearRoutingType(routingToken, out var routing);
        if (!routingOk)
            errores.Add("RoutingType debe ser \"sequential\" o \"parallel\".");

        var env = new EnvVars(e.Servicio);
        var pdf = ValidacionDeEntrada.ValidarPdfBase64(
            pdfBase64 ?? string.Empty, env.EnteroObligatorio(SchemaNames.EnvVars.MaxPdfSizeKB));
        errores.AddRange(pdf.Errores);

        ResultadoDe<IReadOnlyList<ParticipantInput>>? participantes = null;
        if (string.IsNullOrEmpty(participantsJson))
        {
            errores.Add("ParticipantsJson es obligatorio.");
        }
        else if (routingOk) // las reglas de orden dependen del routing — sin routing válido no hay qué validar
        {
            participantes = ValidacionDeEntrada.ValidarParticipants(
                participantsJson!, routing, env.EnteroObligatorio(SchemaNames.EnvVars.MaxParticipants));
            errores.AddRange(participantes.Errores);
        }

        if (errores.Count > 0 || participantes?.Valor is null)
        {
            if (errores.Count == 0)
                errores.Add("ParticipantsJson no pudo validarse.");
            e.Rechazar(errores);
        }

        // ── Validación que solo Dataverse puede responder (cáscara) ──
        var usuarios = Consultas.UsuariosHabilitados(
            e.Servicio, participantes!.Valor!.Select(p => p.UserId).ToList());

        IReadOnlyList<ZoneInput> zonas = Array.Empty<ZoneInput>();
        if (!string.IsNullOrEmpty(zonesJson))
        {
            var rz = ValidacionDeEntrada.ValidarZones(
                zonesJson!, participantes.Valor!.Select(p => p.UserId).ToList(), pdf.Valor!.PageCount);
            if (!rz.EsValido)
                e.Rechazar(rz.Errores);
            zonas = rz.Valor!;
        }

        // ── Escrituras (todo validado; ownerid explícito = creador) ──
        var owner = new EntityReference(SchemaNames.Usuario.Entidad, e.Llamante);

        var tx = new Entity(SchemaNames.Tx.Entidad);
        tx[SchemaNames.Tx.Name] = name;
        tx[SchemaNames.Tx.Status] = new OptionSetValue((int)TransactionStatus.Borrador);
        tx[SchemaNames.Tx.RoutingType] = new OptionSetValue((int)routing);
        if (message is not null) tx[SchemaNames.Tx.Message] = message;
        if (expirationDays.HasValue) tx[SchemaNames.Tx.ExpirationDays] = expirationDays.Value;
        tx[SchemaNames.Tx.OwnerId] = owner;
        var txId = e.Servicio.Create(tx);
        var txRef = new EntityReference(SchemaNames.Tx.Entidad, txId);

        e.Archivos.Subir(txRef, SchemaNames.Tx.ContentFile, "content.pdf", pdf.Valor!.Bytes, "application/pdf");

        var participantePorUsuario = new Dictionary<Guid, Guid>();
        foreach (var p in participantes.Valor!)
        {
            var fila = new Entity(SchemaNames.Participante.Entidad);
            fila[SchemaNames.Participante.Name] = Consultas.Truncar($"{usuarios[p.UserId].Nombre} — {name}", 300);
            fila[SchemaNames.Participante.TransactionId] = txRef;
            fila[SchemaNames.Participante.UserId] = new EntityReference(SchemaNames.Usuario.Entidad, p.UserId);
            if (p.Order.HasValue) fila[SchemaNames.Participante.Order] = p.Order.Value;
            fila[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.Pendiente);
            fila[SchemaNames.Participante.OwnerId] = owner;
            participantePorUsuario[p.UserId] = e.Servicio.Create(fila);
        }

        CrearZonas(e, zonas, participantePorUsuario, owner);

        var actor = Consultas.SnapshotDeActor(e.Servicio, e.Llamante);
        Consultas.CrearEvento(e.Servicio, txRef, EventType.TransaccionCreada, actor,
            $"Borrador creado con {participantes.Valor!.Count} participante(s), enrutamiento {routingToken!.ToLowerInvariant()}.",
            e.Llamante);

        e.Output("TransactionId", txId);
        e.Trace.Trace("CreateTransaction: {0} creada con {1} participante(s), {2} zona(s).",
            txId, participantes.Valor!.Count, zonas.Count);
    }

    internal static void CrearZonas(
        EntornoDeApi e, IReadOnlyList<ZoneInput> zonas,
        IReadOnlyDictionary<Guid, Guid> participantePorUsuario, EntityReference owner)
    {
        for (var i = 0; i < zonas.Count; i++)
        {
            var z = zonas[i];
            var fila = new Entity(SchemaNames.Zona.Entidad);
            fila[SchemaNames.Zona.Name] = $"Zona {i + 1} — página {z.Page}";
            fila[SchemaNames.Zona.ParticipantId] =
                new EntityReference(SchemaNames.Participante.Entidad, participantePorUsuario[z.UserId]);
            fila[SchemaNames.Zona.Page] = z.Page;
            fila[SchemaNames.Zona.PosX] = (decimal)z.X;
            fila[SchemaNames.Zona.PosY] = (decimal)z.Y;
            fila[SchemaNames.Zona.Width] = (decimal)z.W;
            fila[SchemaNames.Zona.Height] = (decimal)z.H;
            fila[SchemaNames.Zona.OwnerId] = owner;
            e.Servicio.Create(fila);
        }
    }
}

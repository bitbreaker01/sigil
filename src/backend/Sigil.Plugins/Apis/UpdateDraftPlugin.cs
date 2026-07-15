// sanic_sigil_capi_UpdateDraft (doc 04 §3.1) — Bound. Edita un borrador; todos los inputs
// de Create, opcionales (null = no cambiar). Reglas clave:
//   - Lock de fila PRIMERO (doc 04 §5) + re-lectura + autorización (creador + Borrador).
//   - Si reemplaza el PDF, REVALIDA las zonas persistidas contra el nuevo documento:
//     páginas inexistentes → error explícito listando las zonas, jamás borrado silencioso.
//   - Cambiar el enrutamiento exige reenviar ParticipantsJson (las reglas de orden cambian).
//   - ParticipantsJson reemplaza la lista completa; sus zonas anteriores mueren con ellos
//     (cascada parental) — el frontend envía ZonesJson junto cuando corresponde.
// TODO validado ANTES de la primera escritura de negocio.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class UpdateDraftPlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        LockDeFila.Tomar(e.Servicio, target.Id); // SIEMPRE primero (doc 04 §5)

        var tx = Consultas.Transaccion(e.Servicio, target.Id);
        var estado = (TransactionStatus)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
        var creador = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;

        var motivo = ReglasDeAutorizacion.MotivoParaRechazarEdicionDeBorrador(e.Llamante, creador, estado);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        var name = e.Input<string>("Name");
        var message = e.Input<string>("Message");
        var routingToken = e.Input<string>("RoutingType");
        var expirationDays = e.InputInt("ExpirationDays");
        var pdfBase64 = e.Input<string>("PdfBase64");
        var participantsJson = e.Input<string>("ParticipantsJson");
        var zonesJson = e.Input<string>("ZonesJson");

        var errores = new List<string>();
        var env = new EnvVars(e.Servicio);

        // Cada campo opcional (null = sin cambio); nombreObligatorio: false porque en UpdateDraft
        // un Name null significa "no lo toques", no "falta el título".
        errores.AddRange(ValidacionDeEntrada.ValidarEncabezado(name, expirationDays, message, nombreObligatorio: false));

        // Routing efectivo: el nuevo si vino, si no el persistido.
        var routingActual = (RoutingType)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.RoutingType).Value;
        var routing = routingActual;
        var routingValido = true; // sin bandera basada en texto de error (S1)
        if (routingToken is not null)
        {
            routingValido = ValidacionDeEntrada.TryParsearRoutingType(routingToken, out routing);
            if (!routingValido)
                errores.Add("RoutingType debe ser \"sequential\" o \"parallel\".");
            else if (routing != routingActual && participantsJson is null)
                errores.Add("Cambiar el enrutamiento requiere reenviar ParticipantsJson (las reglas de orden cambian).");
        }

        // A2: reemplazar participantes destruye sus zonas por cascada parental; sin ZonesJson
        // explícito eso sería una pérdida silenciosa (el doc 04 §3.1 la prohíbe). Se exige
        // ZonesJson (aunque sea "[]" para borrar) cuando hay zonas persistidas que se perderían.
        if (participantsJson is not null && zonesJson is null &&
            HayZonasPersistidas(e.Servicio, Consultas.ParticipantesDe(e.Servicio, target.Id)))
        {
            errores.Add("Reemplazar los participantes descarta sus zonas de firma: reenviá ZonesJson " +
                        "con las zonas nuevas (o \"[]\" para dejar la transacción sin zonas).");
        }

        PdfValidado? pdfNuevo = null;
        if (pdfBase64 is not null)
        {
            var rp = ValidacionDeEntrada.ValidarPdfBase64(pdfBase64, env.EnteroObligatorio(SchemaNames.EnvVars.MaxPdfSizeKB));
            errores.AddRange(rp.Errores);
            pdfNuevo = rp.Valor;
        }

        ResultadoDe<IReadOnlyList<ParticipantInput>>? participantesNuevos = null;
        if (participantsJson is not null && routingValido)
        {
            participantesNuevos = ValidacionDeEntrada.ValidarParticipants(
                participantsJson, routing, env.EnteroObligatorio(SchemaNames.EnvVars.MaxParticipants));
            errores.AddRange(participantesNuevos.Errores);
        }

        if (errores.Count > 0)
            e.Rechazar(errores);

        // ── Estado persistido que las validaciones de zonas necesitan ──
        var participantesActuales = Consultas.ParticipantesDe(e.Servicio, target.Id);

        var usuariosNuevos = participantesNuevos?.Valor is not null
            ? Consultas.UsuariosHabilitados(e.Servicio, participantesNuevos.Valor.Select(p => p.UserId).ToList())
            : null;

        // userIds efectivos para validar zonas: los nuevos si hay reemplazo, si no los persistidos.
        var userIdsEfectivos = participantesNuevos?.Valor is not null
            ? participantesNuevos.Valor.Select(p => p.UserId).ToList()
            : participantesActuales
                .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
                .ToList();

        IReadOnlyList<ZoneInput>? zonasNuevas = null;
        if (zonesJson is not null)
        {
            // pageCount efectivo: del PDF nuevo, o del persistido (hay que abrirlo — no se guarda el conteo).
            var pageCount = pdfNuevo?.PageCount
                ?? ValidacionDeEntrada.ContarPaginas(e.Archivos.Descargar(target, SchemaNames.Tx.ContentFile));
            var rz = ValidacionDeEntrada.ValidarZones(zonesJson, userIdsEfectivos, pageCount);
            if (!rz.EsValido)
                e.Rechazar(rz.Errores);
            zonasNuevas = rz.Valor;
        }
        else if (pdfNuevo is not null && participantesNuevos is null)
        {
            // PDF reemplazado sin tocar zonas: las persistidas deben seguir siendo válidas.
            var zonasPersistidas = Consultas.ZonasDe(e.Servicio, participantesActuales.Select(p => p.Id).ToList());
            var huerfanas = zonasPersistidas
                .Where(z => z.GetAttributeValue<int>(SchemaNames.Zona.Page) > pdfNuevo.PageCount)
                .Select(z => $"'{z.GetAttributeValue<string>(SchemaNames.Zona.Name)}' (página {z.GetAttributeValue<int>(SchemaNames.Zona.Page)})")
                .ToList();
            if (huerfanas.Count > 0)
                e.Rechazar(new[]
                {
                    $"El nuevo PDF tiene {pdfNuevo.PageCount} página(s) y estas zonas quedarían fuera: " +
                    $"{string.Join(", ", huerfanas)}. Reubicá las zonas o mantené el documento.",
                });
        }

        // ── Escrituras (todo validado) ──
        var owner = new EntityReference(SchemaNames.Usuario.Entidad, creador);

        var cambios = new Entity(SchemaNames.Tx.Entidad, target.Id);
        if (name is not null) cambios[SchemaNames.Tx.Name] = name;
        if (message is not null) cambios[SchemaNames.Tx.Message] = message;
        if (expirationDays.HasValue) cambios[SchemaNames.Tx.ExpirationDays] = expirationDays.Value;
        if (routingToken is not null) cambios[SchemaNames.Tx.RoutingType] = new OptionSetValue((int)routing);
        if (cambios.Attributes.Count > 0)
            e.Servicio.Update(cambios);

        if (pdfNuevo is not null)
            e.Archivos.Subir(target, SchemaNames.Tx.ContentFile, "content.pdf", pdfNuevo.Bytes);

        var participantePorUsuario = new Dictionary<Guid, Guid>();
        if (participantesNuevos?.Valor is not null)
        {
            // Borrar las zonas de los salientes EXPLÍCITAMENTE (no confiar en la cascada parental):
            // explícito es testeable y defiende ante una cascada mal configurada — igual que
            // DeleteDraft borra los eventos a mano (doc 06 T3). El guard A2 ya garantizó que el
            // llamante mandó ZonesJson cuando había zonas que perder.
            foreach (var z in Consultas.ZonasDe(e.Servicio, participantesActuales.Select(p => p.Id).ToList()))
                e.Servicio.Delete(SchemaNames.Zona.Entidad, z.Id);
            foreach (var viejo in participantesActuales)
                e.Servicio.Delete(SchemaNames.Participante.Entidad, viejo.Id);

            var nombreDeTx = name ?? tx.GetAttributeValue<string>(SchemaNames.Tx.Name);
            foreach (var p in participantesNuevos.Valor)
            {
                var fila = new Entity(SchemaNames.Participante.Entidad);
                fila[SchemaNames.Participante.Name] = Consultas.Truncar($"{usuariosNuevos![p.UserId].Nombre} — {nombreDeTx}", 300);
                fila[SchemaNames.Participante.TransactionId] = target;
                fila[SchemaNames.Participante.UserId] = new EntityReference(SchemaNames.Usuario.Entidad, p.UserId);
                if (p.Order.HasValue) fila[SchemaNames.Participante.Order] = p.Order.Value;
                fila[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.Pendiente);
                fila[SchemaNames.Participante.OwnerId] = owner;
                participantePorUsuario[p.UserId] = e.Servicio.Create(fila);
            }
        }
        else
        {
            foreach (var p in participantesActuales)
                participantePorUsuario[p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id] = p.Id;
        }

        if (zonasNuevas is not null)
        {
            if (participantesNuevos?.Valor is null)
            {
                // Reemplazo de zonas sobre participantes intactos: borrar las anteriores explícitamente.
                foreach (var z in Consultas.ZonasDe(e.Servicio, participantesActuales.Select(p => p.Id).ToList()))
                    e.Servicio.Delete(SchemaNames.Zona.Entidad, z.Id);
            }
            CreateTransactionPlugin.CrearZonas(e, zonasNuevas, participantePorUsuario, owner);
        }

        // Sin evento: UpdateDraft no transiciona estado (doc 04 §8 — evento por transición).
        e.Trace.Trace("UpdateDraft: {0} actualizada.", target.Id);
    }

    private static bool HayZonasPersistidas(IOrganizationService servicio, IReadOnlyList<Entity> participantes)
        => participantes.Count > 0 &&
           Consultas.ZonasDe(servicio, participantes.Select(p => p.Id).ToList()).Count > 0;
}

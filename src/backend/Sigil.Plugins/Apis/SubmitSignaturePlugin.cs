// sanic_sigil_capi_SubmitSignature (T5/T6/T7 + P3/P2', doc 04 §3.1/§5): registra la
// intención de firma. LA API críticа en concurrencia:
//   1. Lock de fila PRIMERO; re-leer TODO después (las carreras quedan serializadas).
//   2. IDEMPOTENCIA ANTES del guard de estado (M3): re-submit sobre Firmado = éxito sin
//      efectos — el doble click del último firmante llega con la tx ya en Sellando y
//      el guard de estado la rechazaría; la idempotencia tiene precedencia.
//   3. La decisión "último" se toma DESPUÉS del lock, contando sobre la lectura serializada.
//   4. El status solo se escribe si CAMBIA (doc 08 §7: los triggers disparan aunque el
//      valor sea idéntico — reescribir FirmadoParcialmente duplicaría notificaciones).
// Efectos P3: snapshot del PNG vigente + lookup a la versión exacta, snapshots de identidad
// (del contexto, jamás del cliente), evento 3 con documenthash. Out: IsLastSigner.

using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Core.Crypto;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public class SubmitSignaturePlugin : SigilApiPlugin
{
    protected override void Ejecutar(EntornoDeApi e)
    {
        var target = e.Target;
        LockDeFila.Tomar(e.Servicio, target.Id); // R2 — SIEMPRE primero

        // Re-lectura POST-lock: sobre estos datos serializados se decide todo.
        var tx = Consultas.Transaccion(e.Servicio, target.Id);
        var estadoTx = (TransactionStatus)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.Status).Value;
        var creador = tx.GetAttributeValue<EntityReference>(SchemaNames.Tx.OwnerId).Id;
        var participantes = Consultas.ParticipantesDe(e.Servicio, target.Id);

        var mio = participantes.FirstOrDefault(p =>
            p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == e.Llamante);

        // Idempotencia ANTES del guard de estado (M3 — precedencia de guards).
        // Semántica de IsLastSigner (decisión 2026-07-16): "la transacción quedó sellando",
        // no "este call selló" — así el retry del doble click devuelve lo mismo que el
        // primer click y la UI no se confunde.
        if (mio is not null &&
            (ParticipantStatus)mio.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value
                == ParticipantStatus.Firmado)
        {
            e.Trace.Trace("SubmitSignature: re-submit sobre Firmado — éxito sin efectos (idempotencia).");
            e.Output("IsLastSigner", estadoTx == TransactionStatus.Sellando);
            return;
        }

        var motivo = ReglasDeAutorizacion.MotivoParaRechazarAccionDeFirmante(
            esParticipante: mio is not null,
            mio is null ? null : (ParticipantStatus)mio.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value,
            estadoTx);
        if (motivo is not null)
            throw new InvalidPluginExecutionException(motivo);

        // ── Firma Maestra vigente (obligatoria para firmar — RF-01) ──
        var firmaMaestra = Consultas.FirmaMaestraVigenteDe(e.Servicio, e.Llamante)
            ?? throw new InvalidPluginExecutionException(
                "No tenés una Firma Maestra validada — cargala desde tu perfil antes de firmar.");

        var firmaRef = new EntityReference(SchemaNames.FirmaMaestra.Entidad, firmaMaestra.Id);
        var pngVigente = e.Archivos.Descargar(firmaRef, SchemaNames.FirmaMaestra.SignatureFile);

        // documenthash: SHA-256 del documento de contenido AL MOMENTO del evento (doc 03 §4.6)
        var documentHash = HashUtil.Sha256Hex(e.Archivos.Descargar(target, SchemaNames.Tx.ContentFile));

        // Snapshots de identidad — SIEMPRE del contexto de ejecución, jamás del cliente.
        var yo = e.Servicio.Retrieve(SchemaNames.Usuario.Entidad, e.Llamante,
            new Microsoft.Xrm.Sdk.Query.ColumnSet(
                SchemaNames.Usuario.FullName, SchemaNames.Usuario.Email,
                SchemaNames.Usuario.Upn, SchemaNames.Usuario.EntraObjectId));
        var nombre = yo.GetAttributeValue<string>(SchemaNames.Usuario.FullName) ?? e.Llamante.ToString();
        var email = yo.GetAttributeValue<string>(SchemaNames.Usuario.Email)
                    ?? yo.GetAttributeValue<string>(SchemaNames.Usuario.Upn) ?? string.Empty;

        // El snapshot de bytes se sube ÚLTIMO entre las lecturas y PRIMERO entre las escrituras:
        // los file blocks van a blob storage y no participan del rollback — minimizar la
        // ventana en la que un fallo posterior dejaría un snapshot huérfano (antagonista A2).
        e.Archivos.Subir(new EntityReference(SchemaNames.Participante.Entidad, mio!.Id),
            SchemaNames.Participante.SignatureSnapshot, "signature-snapshot.png", pngVigente, "image/png");

        var ahora = DateTime.UtcNow;
        var firmado = new Entity(SchemaNames.Participante.Entidad, mio.Id);
        firmado[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.Firmado);
        firmado[SchemaNames.Participante.SignedOn] = ahora;
        firmado[SchemaNames.Participante.MasterSignatureId] = firmaRef;
        firmado[SchemaNames.Participante.SignerName] = Consultas.Truncar(nombre, 200);
        firmado[SchemaNames.Participante.SignerEmail] = Consultas.Truncar(email, 200);
        var entraId = yo.GetAttributeValue<Guid?>(SchemaNames.Usuario.EntraObjectId);
        if (entraId.HasValue)
            firmado[SchemaNames.Participante.SignerEntraObjectId] = entraId.Value.ToString();
        e.Servicio.Update(firmado);

        // ── Decisión post-lock (T5/T6/T7) sobre la lectura serializada ──
        var decision = ReglasDeFirma.Decidir(
            (RoutingType)tx.GetAttributeValue<OptionSetValue>(SchemaNames.Tx.RoutingType).Value,
            e.Llamante,
            participantes.Select(p => (
                p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id,
                p.Contains(SchemaNames.Participante.Order) ? p.GetAttributeValue<int>(SchemaNames.Participante.Order) : (int?)null,
                (ParticipantStatus)p.GetAttributeValue<OptionSetValue>(SchemaNames.Participante.Status).Value
            )).ToList());

        if (decision.SiguienteAActivar.HasValue) // P2' — solo secuencial
        {
            var siguiente = participantes.First(p =>
                p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id == decision.SiguienteAActivar.Value);
            var turno = new Entity(SchemaNames.Participante.Entidad, siguiente.Id);
            turno[SchemaNames.Participante.Status] = new OptionSetValue((int)ParticipantStatus.TurnoActivo);
            turno[SchemaNames.Participante.TurnActivatedOn] = ahora;
            e.Servicio.Update(turno);
        }

        // Transición de la transacción — el status SOLO se escribe si cambia (doc 08 §7).
        var estadoNuevo = decision.EsUltimo ? TransactionStatus.Sellando : TransactionStatus.FirmadoParcialmente;
        if (estadoNuevo != estadoTx)
        {
            var cambio = new Entity(SchemaNames.Tx.Entidad, target.Id);
            cambio[SchemaNames.Tx.Status] = new OptionSetValue((int)estadoNuevo);
            e.Servicio.Update(cambio);
        }

        var lectores = participantes
            .Select(p => p.GetAttributeValue<EntityReference>(SchemaNames.Participante.UserId).Id)
            .Where(u => u != creador)
            .Distinct()
            .ToList();
        Consultas.CrearEvento(e.Servicio, target, EventType.FirmaRegistrada, (nombre, email),
            $"Firma registrada (versión de Firma Maestra {firmaMaestra.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version)}).",
            creador, participantId: mio.Id, documentHash: documentHash, lectores: lectores);

        if (decision.EsUltimo) // T6/T7: evento 6 — sellado iniciado
            Consultas.CrearEvento(e.Servicio, target, EventType.SelladoIniciado, ("Sistema", string.Empty),
                "Última firma registrada — pipeline de sellado iniciado.", creador, lectores: lectores);

        e.Output("IsLastSigner", decision.EsUltimo);
        e.Trace.Trace("SubmitSignature: {0} firmó {1}; esUltimo={2}.", e.Llamante, target.Id, decision.EsUltimo);
    }
}

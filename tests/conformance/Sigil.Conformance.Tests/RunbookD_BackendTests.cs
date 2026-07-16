// CF-D — Despliegue del backend (F2): existencia y configuración del plugin package y de
// las Custom APIs desplegadas (doc 04 §3.1/§3.2), más los smokes E2E (CF-D05..D08).
// TDD de infraestructura (doc 11 §1 regla 5):
// estos tests nacen ROJOS contra Dev — se ponen verdes cuando el paquete se registre en F2
// (pac plugin push / import de solución). El runbook D (despliegue backend) se redacta en F2;
// los IDs CF-D ya quedan reservados acá.
//
// Decisiones de contrato que estos tests fijan (registradas en el paso 13 de F1):
//   - PackageId del plugin package: sanic_Sigil (prefijo del publisher + nombre del producto).
//   - RoutingType y DocumentType viajan como String ("sequential"|"parallel", "content"|"final").
//   - TransactionId de salida es Guid.

using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using PdfSharp.Pdf;
using Xunit;

namespace Sigil.Conformance.Tests;

[Collection("dataverse")]
public class RunbookD_BackendTests(DataverseFixture fx)
{
    private const string PrivilegioDeUsuario = "prvReadsanic_sigil_tbl_transaction"; // doc 04 §3.2

    [SkippableFact] // CF-D01 — el plugin package de Sigil está registrado
    public void CF_D01_PluginPackage_SanicSigil_Registrado()
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("pluginpackage") { ColumnSet = new ColumnSet("uniquename") };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, "sanic_Sigil");

        var filas = client.RetrieveMultiple(query).Entities;
        Assert.True(filas.Count == 1,
            "No existe el plugin package 'sanic_Sigil' — se registra en F2 (dotnet pack + pac plugin push).");
    }

    public static TheoryData<string, int, string?> ApisEsperadas() => new()
    {
        // uniquename, bindingtype (0=Global, 1=Entity), boundentitylogicalname
        { "sanic_sigil_capi_CreateTransaction", 0, null },
        { "sanic_sigil_capi_UpdateDraft", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_DeleteDraft", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_GetDocumentContent", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_SendTransaction", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_SubmitSignature", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_RejectTransaction", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_CancelTransaction", 1, "sanic_sigil_tbl_transaction" },
        { "sanic_sigil_capi_ValidateMasterSignature", 0, null },
        { "sanic_sigil_capi_GetMasterSignature", 0, null },
    };

    [SkippableTheory] // CF-D02 — cada Custom API existe con binding, tipo y privilegio del doc 04
    [MemberData(nameof(ApisEsperadas))]
    public void CF_D02_CustomApi_ExisteConBindingYPrivilegio(string uniqueName, int bindingEsperado, string? entidadEsperada)
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("customapi")
        {
            ColumnSet = new ColumnSet("uniquename", "bindingtype", "boundentitylogicalname", "isfunction", "isprivate", "executeprivilegename"),
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);

        var filas = client.RetrieveMultiple(query).Entities;
        Assert.True(filas.Count == 1, $"La Custom API {uniqueName} no está registrada (F2).");
        var api = filas[0];

        Assert.Equal(bindingEsperado, api.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("bindingtype").Value);
        Assert.Equal(entidadEsperada, api.GetAttributeValue<string>("boundentitylogicalname"));
        Assert.False(api.GetAttributeValue<bool>("isfunction"),
            $"{uniqueName} debe ser POST (IsFunction=false): tiene efectos o transporta binarios.");
        Assert.True(api.GetAttributeValue<bool>("isprivate"),
            $"{uniqueName} debe tener IsPrivate=true (higiene de metadata — doc 04 §3).");
        Assert.Equal(PrivilegioDeUsuario, api.GetAttributeValue<string>("executeprivilegename"));
    }

    public static TheoryData<string, string, int, bool> ParametrosEsperados() => new()
    {
        // api, nombre de parámetro, tipo (7=Integer, 10=String), opcional
        { "sanic_sigil_capi_CreateTransaction", "Name", 10, false },
        { "sanic_sigil_capi_CreateTransaction", "Message", 10, true },
        { "sanic_sigil_capi_CreateTransaction", "RoutingType", 10, false },
        { "sanic_sigil_capi_CreateTransaction", "ExpirationDays", 7, true },
        { "sanic_sigil_capi_CreateTransaction", "PdfBase64", 10, false },
        { "sanic_sigil_capi_CreateTransaction", "ParticipantsJson", 10, false },
        { "sanic_sigil_capi_CreateTransaction", "ZonesJson", 10, true },
        { "sanic_sigil_capi_UpdateDraft", "Name", 10, true },
        { "sanic_sigil_capi_UpdateDraft", "Message", 10, true },
        { "sanic_sigil_capi_UpdateDraft", "RoutingType", 10, true },
        { "sanic_sigil_capi_UpdateDraft", "ExpirationDays", 7, true },
        { "sanic_sigil_capi_UpdateDraft", "PdfBase64", 10, true },
        { "sanic_sigil_capi_UpdateDraft", "ParticipantsJson", 10, true },
        { "sanic_sigil_capi_UpdateDraft", "ZonesJson", 10, true },
        { "sanic_sigil_capi_GetDocumentContent", "DocumentType", 10, false },
        { "sanic_sigil_capi_RejectTransaction", "Reason", 10, false },
        { "sanic_sigil_capi_CancelTransaction", "Reason", 10, true },
        { "sanic_sigil_capi_ValidateMasterSignature", "ImageBase64", 10, false },
    };

    [SkippableTheory] // CF-D03 — parámetros de request: nombre EXACTO (case-sensitive), tipo y opcionalidad
    [MemberData(nameof(ParametrosEsperados))]
    public void CF_D03_RequestParameter_ExisteConTipoYOpcionalidad(string api, string parametro, int tipo, bool opcional)
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("customapirequestparameter")
        {
            ColumnSet = new ColumnSet("uniquename", "type", "isoptional"),
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, parametro);
        var link = query.AddLink("customapi", "customapiid", "customapiid");
        link.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, api);

        var filas = client.RetrieveMultiple(query).Entities;
        Assert.True(filas.Count == 1, $"El parámetro {parametro} de {api} no existe (F2).");
        Assert.Equal(tipo, filas[0].GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("type").Value);
        Assert.Equal(opcional, filas[0].GetAttributeValue<bool>("isoptional"));
    }

    public static TheoryData<string, string, int> RespuestasEsperadas() => new()
    {
        // api, propiedad de respuesta, tipo (0=Boolean, 1=DateTime, 10=String, 12=Guid)
        { "sanic_sigil_capi_CreateTransaction", "TransactionId", 12 },
        { "sanic_sigil_capi_GetDocumentContent", "PdfBase64", 10 },
        { "sanic_sigil_capi_SubmitSignature", "IsLastSigner", 0 },
        { "sanic_sigil_capi_ValidateMasterSignature", "IsValid", 0 },
        { "sanic_sigil_capi_ValidateMasterSignature", "FailureReasons", 10 },
        { "sanic_sigil_capi_ValidateMasterSignature", "MetricsJson", 10 },
        { "sanic_sigil_capi_ValidateMasterSignature", "NormalizedImageBase64", 10 },
        { "sanic_sigil_capi_GetMasterSignature", "ImageBase64", 10 },
        { "sanic_sigil_capi_GetMasterSignature", "ValidatedOn", 1 },
    };

    [SkippableTheory] // CF-D04 — propiedades de respuesta del contrato
    [MemberData(nameof(RespuestasEsperadas))]
    public void CF_D04_ResponseProperty_ExisteConTipo(string api, string propiedad, int tipo)
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("customapiresponseproperty")
        {
            ColumnSet = new ColumnSet("uniquename", "type"),
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, propiedad);
        var link = query.AddLink("customapi", "customapiid", "customapiid");
        link.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, api);

        var filas = client.RetrieveMultiple(query).Entities;
        Assert.True(filas.Count == 1, $"La propiedad de respuesta {propiedad} de {api} no existe (F2).");
        Assert.Equal(tipo, filas[0].GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("type").Value);
    }

    // ── CF-D05: smoke E2E — la salida de F1 (doc 10) ─────────────────────────
    // No prueba REGISTRO sino FUNCIÓN real: invoca CreateTransaction con un PDF real,
    // lo recupera con GetDocumentContent y verifica el round-trip byte a byte. Crea y
    // borra su propio dato (cleanup en finally) — jamás deja basura en Dev.

    [SkippableFact]
    public void CF_D05_SmokeE2E_CrearBorradorConPdfReal_YLeerloDeVuelta()
    {
        var client = fx.RequireClient();
        var yo = ((WhoAmIResponse)client.Execute(new WhoAmIRequest())).UserId;

        var pdf = PdfDeUnaPagina();
        var pdfBase64 = Convert.ToBase64String(pdf);

        var participantsJson = JsonSerializer.Serialize(new[] { new { userId = yo } });
        var zonesJson = JsonSerializer.Serialize(new[]
        {
            new { userId = yo, page = 1, x = 40.0, y = 40.0, w = 20.0, h = 8.0 },
        });

        var crear = new OrganizationRequest("sanic_sigil_capi_CreateTransaction")
        {
            ["Name"] = "CF-D05 smoke E2E",
            ["RoutingType"] = "parallel",
            ["PdfBase64"] = pdfBase64,
            ["ParticipantsJson"] = participantsJson,
            ["ZonesJson"] = zonesJson,
        };

        Guid txId = Guid.Empty;
        try
        {
            var resp = client.Execute(crear);
            txId = (Guid)resp.Results["TransactionId"];
            Assert.NotEqual(Guid.Empty, txId);

            // Leer el contenido de vuelta (el llamante es el creador → autorizado en Borrador).
            var leer = new OrganizationRequest("sanic_sigil_capi_GetDocumentContent")
            {
                ["Target"] = new EntityReference("sanic_sigil_tbl_transaction", txId),
                ["DocumentType"] = "content",
            };
            var pdfDeVuelta = (string)client.Execute(leer).Results["PdfBase64"];

            // Round-trip exacto: los bytes que subimos son los que bajan (doc 04 §3.1 GetDocumentContent).
            Assert.Equal(pdfBase64, pdfDeVuelta);
        }
        finally
        {
            if (txId != Guid.Empty)
            {
                try
                {
                    client.Execute(new OrganizationRequest("sanic_sigil_capi_DeleteDraft")
                    {
                        ["Target"] = new EntityReference("sanic_sigil_tbl_transaction", txId),
                    }); // cleanup — no dejar borradores de prueba en Dev
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[cleanup] No se pudo borrar el borrador {txId}: {ex.Message} — borrarlo a mano.");
                }
            }
        }
    }

    // ── CF-D06: smoke del CICLO DE VIDA — crear → enviar → verificar anclas → cancelar ──
    // Ejercita T4 (send: contenthash, turnos, share) y T13 (cancel) contra el ambiente real.
    // SubmitSignature queda fuera del smoke hasta que exista ValidateMasterSignature (el SP
    // no tiene Firma Maestra). Limpia TODO al final (eventos primero — Delete Restrict).

    [SkippableFact]
    public void CF_D06_SmokeCicloDeVida_CrearEnviarYCancelar()
    {
        var client = fx.RequireClient();
        var yo = ((WhoAmIResponse)client.Execute(new WhoAmIRequest())).UserId;

        var participantsJson = JsonSerializer.Serialize(new[] { new { userId = yo } });
        var zonesJson = JsonSerializer.Serialize(new[]
        {
            new { userId = yo, page = 1, x = 40.0, y = 40.0, w = 20.0, h = 8.0 },
        });

        Guid txId = Guid.Empty;
        try
        {
            var crear = new OrganizationRequest("sanic_sigil_capi_CreateTransaction")
            {
                ["Name"] = "CF-D06 smoke ciclo de vida",
                ["RoutingType"] = "parallel",
                ["PdfBase64"] = Convert.ToBase64String(PdfDeUnaPagina()),
                ["ParticipantsJson"] = participantsJson,
                ["ZonesJson"] = zonesJson,
            };
            txId = (Guid)client.Execute(crear).Results["TransactionId"];
            var txRef = new EntityReference("sanic_sigil_tbl_transaction", txId);

            // T4 — enviar
            client.Execute(new OrganizationRequest("sanic_sigil_capi_SendTransaction") { ["Target"] = txRef });

            var tx = client.Retrieve("sanic_sigil_tbl_transaction", txId,
                new ColumnSet("sanic_sigil_status", "sanic_sigil_contenthash", "sanic_sigil_senton", "sanic_sigil_expireson"));
            Assert.Equal(159460001, tx.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_status").Value); // Pendiente de Firma
            Assert.Matches("^[0-9A-F]{64}$", tx.GetAttributeValue<string>("sanic_sigil_contenthash")); // ancla temprana
            Assert.True(tx.Contains("sanic_sigil_senton") && tx.Contains("sanic_sigil_expireson"));

            var pQuery = new QueryExpression("sanic_sigil_tbl_participant")
            {
                ColumnSet = new ColumnSet("sanic_sigil_status", "sanic_sigil_turnactivatedon"),
            };
            pQuery.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
            var participante = Assert.Single(client.RetrieveMultiple(pQuery).Entities);
            Assert.Equal(159460001, participante.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_status").Value); // Turno Activo
            Assert.True(participante.Contains("sanic_sigil_turnactivatedon"));

            // T13 — cancelar
            client.Execute(new OrganizationRequest("sanic_sigil_capi_CancelTransaction")
            {
                ["Target"] = txRef,
                ["Reason"] = "smoke CF-D06 — limpieza automática",
            });
            var txFinal = client.Retrieve("sanic_sigil_tbl_transaction", txId, new ColumnSet("sanic_sigil_status"));
            Assert.Equal(159460008, txFinal.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_status").Value); // Cancelado

            // evento 12 registrado
            var evQuery = new QueryExpression("sanic_sigil_tbl_event") { ColumnSet = new ColumnSet("sanic_sigil_type") };
            evQuery.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
            var tipos = client.RetrieveMultiple(evQuery).Entities
                .Select(ev => ev.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_type").Value).ToList();
            Assert.Contains(159460011, tipos); // Cancelada por el creador
        }
        finally
        {
            if (txId != Guid.Empty)
                LimpiarTransaccion(client, txId);
        }
    }

    // Cleanup por SDK directo (una cancelada no se puede DeleteDraft): eventos primero
    // (Delete Restrict), después la transacción (cascada elimina participantes/zonas).
    // NUNCA lanza: un fallo del cleanup en el finally pisaría la aserción real del test
    // (antagonista A5) — se reporta a consola y queda para limpieza manual.
    private static void LimpiarTransaccion(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client, Guid txId)
    {
        try
        {
            var evQuery = new QueryExpression("sanic_sigil_tbl_event") { ColumnSet = new ColumnSet(false) };
            evQuery.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
            foreach (var ev in client.RetrieveMultiple(evQuery).Entities)
                client.Delete("sanic_sigil_tbl_event", ev.Id);
            client.Delete("sanic_sigil_tbl_transaction", txId);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cleanup] No se pudo limpiar la transacción {txId}: {ex.Message} — borrarla a mano.");
        }
    }

    // ── CF-D07: Firma Maestra — validar, versionar y leer de vuelta ─────────
    // Ejercita ADR-009 real en el sandbox: el motor de Imaging valida/normaliza y el
    // versionado crea la vigente. El roundtrip Get == Normalized cierra el contrato.

    [SkippableFact]
    public void CF_D07_FirmaMaestra_ValidarYLeerDeVuelta()
    {
        var client = fx.RequireClient();
        var yo = ((WhoAmIResponse)client.Execute(new WhoAmIRequest())).UserId;

        try
        {
            var validar = new OrganizationRequest("sanic_sigil_capi_ValidateMasterSignature")
            {
                ["ImageBase64"] = Convert.ToBase64String(PngDeFirmaSintetica()),
            };
            var r = client.Execute(validar).Results;
            Assert.True((bool)r["IsValid"], "la firma sintética debía pasar los umbrales: " +
                (r.Contains("FailureReasons") ? r["FailureReasons"] : "(sin motivos)"));
            var normalizada = (string)r["NormalizedImageBase64"];
            Assert.False(string.IsNullOrEmpty(normalizada));

            var g = client.Execute(new OrganizationRequest("sanic_sigil_capi_GetMasterSignature")).Results;
            Assert.Equal(normalizada, (string)g["ImageBase64"]); // roundtrip byte a byte
            Assert.IsType<DateTime>(g["ValidatedOn"]);
        }
        finally
        {
            LimpiarFirmasMaestrasDe(client, yo);
        }
    }

    // ── CF-D08: el E2E de FIRMA — crear → enviar → FIRMAR → Sellando ─────────
    // El propósito del sistema de punta a punta hasta donde existe motor (el worker de
    // sellado llega en F2.3; la transacción queda en Sellando y se limpia).

    [SkippableFact]
    public void CF_D08_SmokeDeFirma_CrearEnviarFirmar_QuedaSellando()
    {
        var client = fx.RequireClient();
        var yo = ((WhoAmIResponse)client.Execute(new WhoAmIRequest())).UserId;

        Guid txId = Guid.Empty;
        try
        {
            // firma maestra vigente para el SP
            var v = client.Execute(new OrganizationRequest("sanic_sigil_capi_ValidateMasterSignature")
            {
                ["ImageBase64"] = Convert.ToBase64String(PngDeFirmaSintetica()),
            }).Results;
            Assert.True((bool)v["IsValid"]);

            // crear + enviar
            txId = (Guid)client.Execute(new OrganizationRequest("sanic_sigil_capi_CreateTransaction")
            {
                ["Name"] = "CF-D08 smoke de firma",
                ["RoutingType"] = "parallel",
                ["PdfBase64"] = Convert.ToBase64String(PdfDeUnaPagina()),
                ["ParticipantsJson"] = JsonSerializer.Serialize(new[] { new { userId = yo } }),
                ["ZonesJson"] = JsonSerializer.Serialize(new[]
                    { new { userId = yo, page = 1, x = 40.0, y = 40.0, w = 20.0, h = 8.0 } }),
            }).Results["TransactionId"];
            var txRef = new EntityReference("sanic_sigil_tbl_transaction", txId);
            client.Execute(new OrganizationRequest("sanic_sigil_capi_SendTransaction") { ["Target"] = txRef });

            // FIRMAR — el único firmante: IsLastSigner true, la tx queda Sellando (T6)
            var s = client.Execute(new OrganizationRequest("sanic_sigil_capi_SubmitSignature") { ["Target"] = txRef }).Results;
            Assert.True((bool)s["IsLastSigner"]);

            var tx = client.Retrieve("sanic_sigil_tbl_transaction", txId, new ColumnSet("sanic_sigil_status", "sanic_sigil_contenthash"));
            Assert.Equal(159460003, tx.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_status").Value); // Sellando

            // el participante quedó Firmado, con snapshots y lookup a la versión exacta
            var pQuery = new QueryExpression("sanic_sigil_tbl_participant")
            {
                ColumnSet = new ColumnSet("sanic_sigil_status", "sanic_sigil_signedon",
                    "sanic_sigil_signername", "sanic_sigil_mastersignatureid"),
            };
            pQuery.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
            var p = Assert.Single(client.RetrieveMultiple(pQuery).Entities);
            Assert.Equal(159460002, p.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_status").Value); // Firmado
            Assert.True(p.Contains("sanic_sigil_signedon"));
            Assert.NotNull(p.GetAttributeValue<EntityReference>("sanic_sigil_mastersignatureid"));

            // el evento de firma trae el documenthash == contenthash (verificación cruzada, doc 03 §4.6)
            var evQuery = new QueryExpression("sanic_sigil_tbl_event")
            {
                ColumnSet = new ColumnSet("sanic_sigil_type", "sanic_sigil_documenthash"),
            };
            evQuery.Criteria.AddCondition("sanic_sigil_transactionid", ConditionOperator.Equal, txId);
            var eventos = client.RetrieveMultiple(evQuery).Entities;
            var firma = eventos.Single(ev => ev.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_type").Value == 159460002);
            Assert.Equal(tx.GetAttributeValue<string>("sanic_sigil_contenthash"),
                firma.GetAttributeValue<string>("sanic_sigil_documenthash"));
            Assert.Contains(eventos, ev => ev.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sanic_sigil_type").Value == 159460005); // Sellado iniciado
        }
        finally
        {
            if (txId != Guid.Empty)
                LimpiarTransaccion(client, txId);
            LimpiarFirmasMaestrasDe(client, yo);
        }
    }

    private static void LimpiarFirmasMaestrasDe(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client, Guid userId)
    {
        try
        {
            var q = new QueryExpression("sanic_sigil_tbl_mastersignature") { ColumnSet = new ColumnSet(false) };
            q.Criteria.AddCondition("sanic_sigil_userid", ConditionOperator.Equal, userId);
            foreach (var f in client.RetrieveMultiple(q).Entities)
                client.Delete("sanic_sigil_tbl_mastersignature", f.Id);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[cleanup] No se pudieron limpiar las firmas maestras del SP: {ex.Message}.");
        }
    }

    private static byte[] PngDeFirmaSintetica()
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(600, 200);
        var negro = new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 255);
        for (var i = 0; i < 5; i++)
        {
            double x0 = 30 + i * 100, y0 = 170, x1 = x0 + 70, y1 = 30;
            for (var s = 0; s <= 400; s++)
            {
                var t = s / 400.0;
                int cx = (int)(x0 + (x1 - x0) * t), cy = (int)(y0 + (y1 - y0) * t);
                for (var dy = -4; dy <= 4; dy++)
                for (var dx = -4; dx <= 4; dx++)
                    if (cx + dx >= 0 && cx + dx < 600 && cy + dy >= 0 && cy + dy < 200 && dx * dx + dy * dy <= 16)
                        img[cx + dx, cy + dy] = negro;
            }
        }
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return ms.ToArray();
    }

    private static byte[] PdfDeUnaPagina()
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }
}

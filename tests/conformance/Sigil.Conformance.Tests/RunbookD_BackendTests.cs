// CF-D — Despliegue del backend (F2): existencia y configuración del plugin package y de
// las primeras 4 Custom APIs (doc 04 §3.1/§3.2). TDD de infraestructura (doc 11 §1 regla 5):
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
        // api, propiedad de respuesta, tipo (10=String, 12=Guid)
        { "sanic_sigil_capi_CreateTransaction", "TransactionId", 12 },
        { "sanic_sigil_capi_GetDocumentContent", "PdfBase64", 10 },
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
                var borrar = new OrganizationRequest("sanic_sigil_capi_DeleteDraft")
                {
                    ["Target"] = new EntityReference("sanic_sigil_tbl_transaction", txId),
                };
                client.Execute(borrar); // cleanup — no dejar borradores de prueba en Dev
            }
        }
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

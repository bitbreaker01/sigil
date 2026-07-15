namespace Sigil.Conformance.Tests;

using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Conformidad de los pasos fundacionales del Runbook A (doc 09 §7) — IDs CF-A*.
/// Cada test es la PRUEBA DE EXISTENCIA de un paso manual (doc 11 §1 regla 5):
/// rojo hasta que el paso se ejecuta; verde cuando quedó como el doc 03/12 especifica.
/// </summary>
[Collection("dataverse")]
public class RunbookA_FundacionesTests(DataverseFixture fx, ITestOutputHelper output)
{
    // ── Paso 2 del Runbook A: publisher ──────────────────────────────────────

    [SkippableFact] // CF-A01
    public void CF_A01_PublisherSanic_ExisteConPrefijoCorrecto()
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("publisher")
        {
            ColumnSet = new ColumnSet("uniquename", "customizationprefix", "customizationoptionvalueprefix", "friendlyname"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, "Sistemas_Abiertos_Nicaragua") } },
        };

        var publishers = client.RetrieveMultiple(query).Entities;

        Assert.True(publishers.Count == 1,
            $"Debe existir exactamente un publisher con Name (nombre único) = 'Sistemas_Abiertos_Nicaragua' (doc 12 §2). {DiagnosticoPublishers(client)}");
        Assert.Equal("sanic", publishers[0].GetAttributeValue<string>("customizationprefix"));
        Assert.Equal("Sistemas Abiertos Nicaragua", publishers[0].GetAttributeValue<string>("friendlyname"));
    }

    /// <summary>Cuando el publisher esperado no aparece, listar lo que SÍ hay — diagnóstico accionable:
    /// distingue "el Name no coincide con la convención (doc 12 §2)" de "la URL apunta a otro ambiente".</summary>
    private static string DiagnosticoPublishers(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client)
    {
        var todos = client.RetrieveMultiple(new QueryExpression("publisher")
        {
            ColumnSet = new ColumnSet("uniquename", "customizationprefix", "friendlyname"),
        }).Entities;

        var lista = string.Join(" | ", todos.Select(p =>
            $"Name='{p.GetAttributeValue<string>("uniquename")}' Prefix='{p.GetAttributeValue<string>("customizationprefix")}' Display='{p.GetAttributeValue<string>("friendlyname")}'"));

        return $"Publishers presentes en este ambiente ({todos.Count}): {lista}. " +
               "Si tu publisher aparece con otro Name: el campo 'Name' quedó autogenerado — ver instrucciones del Runbook A §A2. " +
               "Si solo ves los de sistema (Default, microsoft…): la SIGIL_DATAVERSE_URL apunta a OTRO ambiente.";
    }

    [SkippableFact] // CF-A02
    public void CF_A02_PublisherSanic_OptionValuePrefix_NoEsElDelDefaultPublisher()
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("publisher")
        {
            ColumnSet = new ColumnSet("customizationoptionvalueprefix"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, "Sistemas_Abiertos_Nicaragua") } },
        };
        var resultados = client.RetrieveMultiple(query).Entities;
        Assert.True(resultados.Count == 1,
            $"No se encontró el publisher con Name = 'Sistemas_Abiertos_Nicaragua'. {DiagnosticoPublishers(client)}");
        var publisher = resultados[0];

        var optionValuePrefix = publisher.GetAttributeValue<int>("customizationoptionvalueprefix");

        // LÍMITE HONESTO de este test: la plataforma restringe el valor a 10000–99999 y el maker
        // portal autogenera uno — lo único detectable acá es el 10000 del Default Publisher.
        // La garantía REAL del paso 4 del checklist es la tabla canónica de choices que este
        // valor alimenta (apéndice del doc 12 — bloqueante para los flows, doc 08 §2).
        output.WriteLine($"Option Value Prefix del publisher 'sanic': {optionValuePrefix} → registrar en el apéndice del doc 12.");
        Assert.NotEqual(10000, optionValuePrefix);
    }

    // ── Paso 3: solución ─────────────────────────────────────────────────────

    [SkippableFact] // CF-A03
    public void CF_A03_Solucion_SigilCoreSigil_ExisteYPerteneceAlPublisherSanic()
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("uniquename", "friendlyname"),
            Criteria = { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, "sigil_core_sigil") } },
        };
        var link = query.AddLink("publisher", "publisherid", "publisherid");
        link.Columns = new ColumnSet("uniquename");
        link.EntityAlias = "pub";

        var solution = Assert.Single(client.RetrieveMultiple(query).Entities);

        Assert.Equal("Sigil | Core | Sigil", solution.GetAttributeValue<string>("friendlyname"));
        Assert.Equal("Sistemas_Abiertos_Nicaragua", ((Microsoft.Xrm.Sdk.AliasedValue)solution["pub.uniquename"]).Value);
    }

    // ── Paso "modelo de datos" (doc 03 — se construye en F1): las 6 tablas ───

    [SkippableTheory] // CF-A04
    [InlineData("sanic_sigil_tbl_transaction")]
    [InlineData("sanic_sigil_tbl_participant")]
    [InlineData("sanic_sigil_tbl_signaturezone")]
    [InlineData("sanic_sigil_tbl_ledgerentry")]
    [InlineData("sanic_sigil_tbl_mastersignature")]
    [InlineData("sanic_sigil_tbl_event")]
    public void CF_A04_Tabla_ExisteConElSchemaNameDelDoc03(string logicalName)
    {
        var client = fx.RequireClient();

        try
        {
            var response = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Entity,
            });
            Assert.Equal(logicalName, response.EntityMetadata.LogicalName);
        }
        catch (System.ServiceModel.FaultException)
        {
            // Fallo LEGIBLE para quien ejecuta el Runbook, no un stack trace de plataforma.
            Assert.Fail($"La tabla '{logicalName}' no existe todavía en el ambiente (doc 03 §4 — paso 7 del checklist F1).");
        }
    }

    // ── Ledger: ownership y alternate key ACTIVO (docs 03 §4.4, 04 §9, 07 §2) ─

    [SkippableFact] // CF-A05
    public void CF_A05_Ledger_EsOrganizationOwned()
    {
        var client = fx.RequireClient();
        var response = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = "sanic_sigil_tbl_ledgerentry",
            EntityFilters = EntityFilters.Entity,
        });

        Assert.Equal(OwnershipTypes.OrganizationOwned, response.EntityMetadata.OwnershipType);
    }

    [SkippableFact] // CF-A06 — el gate 1 del Runbook B, como test
    public void CF_A06_Ledger_AlternateKeyDeTransaccion_EstaACTIVO()
    {
        var client = fx.RequireClient();
        // EntityFilters.All deliberado: qué filtro puebla EntityMetadata.Keys NO está documentado
        // por Microsoft (verificado) — All es el superset. Si en la primera corrida real contra Dev
        // Keys llegara null con el key creado, es hallazgo de plataforma a registrar, no bug nuestro.
        var response = (RetrieveEntityResponse)client.Execute(new RetrieveEntityRequest
        {
            LogicalName = "sanic_sigil_tbl_ledgerentry",
            EntityFilters = EntityFilters.All,
            RetrieveAsIfPublished = false,
        });

        var keys = response.EntityMetadata.Keys ?? [];
        var txKey = keys.FirstOrDefault(k =>
            k.KeyAttributes is { Length: 1 } attrs && attrs[0] == "sanic_sigil_transactionid");

        Assert.True(txKey is not null,
            "Debe existir el alternate key sobre sanic_sigil_transactionid — la idempotencia del sellado depende de él (doc 04 §7).");
        Assert.True(string.Equals(txKey!.EntityKeyIndexStatus.ToString(), "Active", StringComparison.Ordinal),
            $"El índice del alternate key debe estar ACTIVE (estado actual: {txKey.EntityKeyIndexStatus}). " +
            "Se crea asíncronamente al importar; ante Failed → ReactivateEntityKey (doc 09 gate 1).");
    }

    // ── Paso 4: roles y perfil de column security (docs 03 §5/§6, 12 §4) ─────

    [SkippableTheory] // CF-A07
    [InlineData("Sigil | SR | User")]
    [InlineData("Sigil | SR | Service")]
    public void CF_A07_RolDeSeguridad_Existe(string roleName)
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, roleName) } },
        };

        Assert.NotEmpty(client.RetrieveMultiple(query).Entities);
    }

    [SkippableFact] // CF-A08
    public void CF_A08_PerfilColumnSecurity_EvidenceWriter_Existe()
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("fieldsecurityprofile")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, "Sigil | FLS | Evidence Writer") } },
        };

        Assert.NotEmpty(client.RetrieveMultiple(query).Entities);
    }

    // ── Variables de entorno (doc 03 §8, nombres doc 12 §3) ──────────────────

    [SkippableTheory] // CF-A09
    [InlineData("sanic_sigil_env_TsaEnabled")]
    [InlineData("sanic_sigil_env_TsaEndpoints")]
    [InlineData("sanic_sigil_env_MaxPdfSizeKB")]
    [InlineData("sanic_sigil_env_MaxParticipants")]
    [InlineData("sanic_sigil_env_SignatureImageSpec")]
    [InlineData("sanic_sigil_env_ExpirationDefaultDays")]
    [InlineData("sanic_sigil_env_ReminderCadenceDays")]
    [InlineData("sanic_sigil_env_AppPlayUrl")]
    [InlineData("sanic_sigil_env_DefaultLanguage")]
    public void CF_A09_VariableDeEntorno_DefinicionExiste(string schemaName)
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("schemaname"),
            Criteria = { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName) } },
        };

        Assert.NotEmpty(client.RetrieveMultiple(query).Entities);
    }
}

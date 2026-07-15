namespace Sigil.Conformance.Tests;

using Microsoft.Xrm.Sdk.Query;

/// <summary>
/// Conformidad de seguridad y operación del Runbook A — CF-A10/A11/A13/A14/A15.
/// Escritos ANTES de ejecutar los pasos que prueban (doc 11 §1 regla 5):
/// están en rojo hasta que el paso correspondiente se completa.
/// (CF-A12 — CSP — no tiene test: el setting se lee con la Power Platform API, una auth
///  distinta a la de esta suite; su verificación es el gate 5 del Runbook B. Límite documentado.)
/// </summary>
[Collection("dataverse")]
public class RunbookA_SeguridadOperacionTests(DataverseFixture fx)
{
    [SkippableFact] // CF-A10 — Runbook A §A4c (ejecutable después de A7)
    public void CF_A10_ElAppUser_EsMiembroDelPerfil_EvidenceWriter()
    {
        var client = fx.RequireClient();
        var clientIdCrudo = Environment.GetEnvironmentVariable("SIGIL_CLIENT_ID");
        Assert.True(Guid.TryParse(clientIdCrudo, out var clientId),
            $"SIGIL_CLIENT_ID no es un GUID válido: '{clientIdCrudo}' — revisar la variable del runner.");

        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("applicationid"),
            Criteria = { Conditions = { new ConditionExpression("applicationid", ConditionOperator.Equal, clientId) } },
        };
        var link = query.AddLink("systemuserprofiles", "systemuserid", "systemuserid");
        var perfil = link.AddLink("fieldsecurityprofile", "fieldsecurityprofileid", "fieldsecurityprofileid");
        perfil.LinkCriteria.AddCondition("name", ConditionOperator.Equal, "Sigil | FLS | Evidence Writer");

        // NOTA (antagonista, a validar en la primera corrida real): existe un segundo intersect
        // `applicationuserprofile` (applicationuser ↔ fieldsecurityprofile). Si el portal registró
        // la membresía ahí y este test da rojo con la membresía visible en PPAC, agregar la
        // consulta al segundo intersect como fallback y registrar el hallazgo.
        Assert.True(client.RetrieveMultiple(query).Entities.Count > 0,
            "El application user del Service Principal NO figura como miembro del perfil 'Sigil | FLS | Evidence Writer' " +
            "vía el intersect systemuserprofiles (Runbook A §A4c — la membresía NO viaja en soluciones). " +
            "Si la membresía SE VE en PPAC: probable intersect alternativo applicationuserprofile — ver nota en el test.");
    }

    [SkippableFact] // CF-A11 — gate 3 (connection references, aplica desde F4)
    public void CF_A11_ConnectionReferences_TodasConConexionAsociada()
    {
        var client = fx.RequireClient();
        // SOLO las de Sigil (prefijo del publisher): las soluciones first-party de Microsoft
        // instalan connection references sin conexión y un gate que llora por basura ajena
        // termina ignorado (hallazgo del antagonista). Verificar el prefijo real al crear la
        // primera en F4 (el logical name lleva el prefijo del publisher del maker).
        var query = new QueryExpression("connectionreference")
        {
            ColumnSet = new ColumnSet("connectionreferencelogicalname", "connectionid"),
        };
        query.Criteria.AddCondition("connectionreferencelogicalname", ConditionOperator.BeginsWith, "sanic_");
        var refs = client.RetrieveMultiple(query).Entities;

        Skip.If(refs.Count == 0, "Aún no hay connection references de Sigil (los flows se crean en F4).");

        var sinConexion = refs
            .Where(r => string.IsNullOrEmpty(r.GetAttributeValue<string>("connectionid")))
            .Select(r => r.GetAttributeValue<string>("connectionreferencelogicalname"))
            .ToList();

        Assert.True(sinConexion.Count == 0,
            $"Connection references SIN conexión asociada (los flows llegarían apagados — gate 3): [{string.Join(", ", sinConexion)}]");
    }

    [SkippableFact] // CF-A13 — Runbook A §A8
    public void CF_A13_Auditoria_HabilitadaANivelOrganizacion()
    {
        var client = fx.RequireClient();
        var org = Assert.Single(client.RetrieveMultiple(new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("isauditenabled"),
        }).Entities);

        Assert.True(org.GetAttributeValue<bool>("isauditenabled"),
            "La auditoría org-level está APAGADA (Runbook A §A8 — sin ella no hay registro forense; doc 07 capa de auditabilidad).");
    }

    [SkippableFact] // CF-A14 — Runbook A §A10
    public void CF_A14_ExisteTeamDeGrupoEntra_ConElRol_User()
    {
        var client = fx.RequireClient();
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("name", "azureactivedirectoryobjectid"),
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("azureactivedirectoryobjectid", ConditionOperator.NotNull),
                    // teamtype 2 = Entra ID Security Group (lo que manda el Runbook §A10 —
                    // un Office group con el rol no cuenta como el paso bien hecho)
                    new ConditionExpression("teamtype", ConditionOperator.Equal, 2),
                },
            },
        };
        var roles = query.AddLink("teamroles", "teamid", "teamid");
        var rol = roles.AddLink("role", "roleid", "roleid");
        rol.LinkCriteria.AddCondition("name", ConditionOperator.Equal, "Sigil | SR | User");

        Assert.True(client.RetrieveMultiple(query).Entities.Count > 0,
            "Ningún team vinculado a un grupo de Entra tiene el rol 'Sigil | SR | User' " +
            "(Runbook A §A10 — los usuarios finales entran por grupo, jamás uno a uno).");
    }

    [SkippableFact] // CF-A15 — Runbook A §A12 (datos semilla)
    public void CF_A15_UsuariosSemilla_ExistenYHabilitados()
    {
        var client = fx.RequireClient();
        var upns = Environment.GetEnvironmentVariable("SIGIL_SEED_UPNS");
        Skip.If(string.IsNullOrWhiteSpace(upns),
            "SIGIL_SEED_UPNS no configurada (lista separada por comas de los UPN semilla — se define al ejecutar A12).");

        foreach (var upn in upns!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("domainname", "isdisabled"),
                Criteria = { Conditions = { new ConditionExpression("domainname", ConditionOperator.Equal, upn) } },
            };
            var usuarios = client.RetrieveMultiple(query).Entities;

            Assert.True(usuarios.Count > 0, $"El usuario semilla '{upn}' no existe en el ambiente (Runbook A §A12).");
            Assert.True(usuarios.Count == 1,
                $"Hay {usuarios.Count} registros de systemuser con UPN '{upn}' — duplicado (¿usuario deshabilitado y recreado?): resolver antes de usarlo como semilla.");
            Assert.False(usuarios[0].GetAttributeValue<bool>("isdisabled"), $"El usuario semilla '{upn}' está deshabilitado.");
        }
    }
}

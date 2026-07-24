namespace Sigil.Conformance.Tests;

using Microsoft.PowerPlatform.Dataverse.Client;

/// <summary>
/// Conexión compartida de la suite de conformidad.
/// Config por variables de entorno del runner (jamás en el repo):
///   SIGIL_DATAVERSE_URL   — https://{org}.crm.dynamics.com
///   SIGIL_CLIENT_ID       — app registration del Service Principal
///   SIGIL_CLIENT_SECRET   — para el runner de conformidad; la credencial productiva es certificado
/// (El tenant se descubre desde la URL del org — `TenantId` NO es un parámetro del
/// connection string de ServiceClient; verificado contra la referencia de XRM Tooling.)
/// Sin SIGIL_DATAVERSE_URL, los tests se OMITEN con motivo (no hay ambiente todavía)
/// — nunca fingen verde. Con URL configurada, cualquier problema de conexión FALLA ruidoso.
/// </summary>
public sealed class DataverseFixture : IDisposable
{
    public ServiceClient? Client { get; }
    public string? SkipReason { get; }

    public DataverseFixture()
    {
        var url = Environment.GetEnvironmentVariable("SIGIL_DATAVERSE_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            SkipReason = "SIGIL_DATAVERSE_URL no configurada: el ambiente Dev aún no está aprovisionado.";
            return;
        }

        var clientId = Requerida("SIGIL_CLIENT_ID");
        var clientSecret = Requerida("SIGIL_CLIENT_SECRET");

        // Secret entre comillas simples: un ';' o '=' en el valor no corrompe el parse.
        var connectionString =
            $"AuthType=ClientSecret;Url={url};ClientId={clientId};ClientSecret='{clientSecret}';RequireNewInstance=true";

        ServiceClient? client = null;
        try
        {
            client = new ServiceClient(connectionString);
            if (!client.IsReady)
            {
                throw new InvalidOperationException(
                    $"Conexión a Dataverse configurada pero no establecida: {client.LastError}");
            }
        }
        catch
        {
            client?.Dispose();
            throw; // Fallo ruidoso deliberado: todos los tests de la colección quedan en error.
        }

        Client = client;
    }

    private static string Requerida(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } valor
            ? valor
            : throw new InvalidOperationException(
                $"SIGIL_DATAVERSE_URL está configurada pero falta {variable} — configuración incompleta del runner.");

    /// <summary>Salta el test SOLO cuando no hay ambiente configurado.</summary>
    public ServiceClient RequireClient()
    {
        Skip.If(Client is null, SkipReason);
        return Client!;
    }

    public void Dispose() => Client?.Dispose();
}

[CollectionDefinition("dataverse")]
public sealed class DataverseCollection : ICollectionFixture<DataverseFixture>;

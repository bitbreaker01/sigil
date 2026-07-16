// Arnés de pruebas de la capa Apis/: contexto de plugin + stub + seam de archivos en
// memoria + siembras del modelo de datos (doc 03). Patrón del proyecto (doc 11 §2):
// datos semilla explícitos, verificación de efectos consultando el stub — jamás mocks.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Xrm.Sdk;
using PdfSharp.Pdf;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Tests.Stub;

public sealed class StubFileTransfer : IFileTransfer
{
    public Dictionary<string, byte[]> Archivos { get; } = new();
    public List<string> Subidas { get; } = new();

    public void Subir(EntityReference registro, string columna, string nombreDeArchivo, byte[] bytes, string mimeType)
    {
        Archivos[Clave(registro, columna)] = bytes;
        Subidas.Add(Clave(registro, columna));
        MimeTypes[Clave(registro, columna)] = mimeType;
    }

    public Dictionary<string, string> MimeTypes { get; } = new();

    public byte[] Descargar(EntityReference registro, string columna)
        => Archivos.TryGetValue(Clave(registro, columna), out var bytes)
            ? bytes
            : throw new InvalidOperationException($"No hay archivo en {Clave(registro, columna)}.");

    public static string Clave(EntityReference registro, string columna)
        => $"{registro.LogicalName}:{registro.Id}:{columna}";
}

public sealed class StubTracingService : ITracingService
{
    public List<string> Lineas { get; } = new();

    public void Trace(string format, params object[] args)
        => Lineas.Add(args is { Length: > 0 } ? string.Format(CultureInfo.InvariantCulture, format, args) : format);
}

public sealed class StubPluginExecutionContext : IPluginExecutionContext
{
    public int Mode { get; set; }
    public int IsolationMode { get; set; } = 2;
    public int Depth { get; set; } = 1;
    public string MessageName { get; set; } = string.Empty;
    public string PrimaryEntityName { get; set; } = string.Empty;
    public Guid? RequestId { get; set; }
    public string SecondaryEntityName { get; set; } = string.Empty;
    public ParameterCollection InputParameters { get; set; } = new();
    public ParameterCollection OutputParameters { get; set; } = new();
    public ParameterCollection SharedVariables { get; set; } = new();
    public Guid UserId { get; set; }
    public Guid InitiatingUserId { get; set; }
    public Guid BusinessUnitId { get; set; }
    public Guid PrimaryEntityId { get; set; }
    public EntityImageCollection PreEntityImages { get; set; } = new();
    public EntityImageCollection PostEntityImages { get; set; } = new();
    public EntityReference OwningExtension { get; set; } = new("sdkmessageprocessingstep", Guid.NewGuid());
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public bool IsExecutingOffline { get; set; }
    public bool IsOfflinePlayback { get; set; }
    public bool IsInTransaction { get; set; } = true;
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public DateTime OperationCreatedOn { get; set; } = DateTime.UtcNow;
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = "stub";
    public int Stage { get; set; } = 30;
    public IPluginExecutionContext? ParentContext { get; set; }
}

public sealed class ArnesDeApi : IServiceProvider, IOrganizationServiceFactory
{
    public StubOrganizationService Servicio { get; } = new();
    public StubFileTransfer Archivos { get; } = new();
    public StubTracingService Trace { get; } = new();
    public StubPluginExecutionContext Contexto { get; } = new();

    public ArnesDeApi()
    {
        // Env vars con los defaults del doc (los tests que necesiten otros valores los pisan).
        ConfigurarEnv(SchemaNames.EnvVars.MaxPdfSizeKB, "64");
        ConfigurarEnv(SchemaNames.EnvVars.MaxParticipants, "20");
        ConfigurarEnv(SchemaNames.EnvVars.ExpirationDefaultDays, "15");
    }

    private readonly Dictionary<string, string> _env = new();

    public void ConfigurarEnv(string schemaName, string valor)
    {
        _env[schemaName] = valor;
        Servicio.Manejadores["RetrieveEnvironmentVariableValue"] = req =>
        {
            var nombre = (string)req["DefinitionSchemaName"];
            var respuesta = new OrganizationResponse { Results = new ParameterCollection() };
            if (_env.TryGetValue(nombre, out var v))
                respuesta.Results["Value"] = v;
            return respuesta;
        };
    }

    /// <summary>El "usuario de aplicación" bajo el que corre el plugin — DISTINTO del llamante.</summary>
    public static readonly Guid UsuarioDeAplicacion = Guid.Parse("a9911111-0000-0000-0000-0000000000ff");

    public void Ejecutar(SigilApiPlugin plugin, string messageName, Guid llamante)
    {
        Contexto.MessageName = messageName;
        Contexto.InitiatingUserId = llamante;         // identidad real del que invoca (autorización, snapshots)
        Contexto.UserId = UsuarioDeAplicacion;        // ≠ llamante: usar UserId donde va InitiatingUserId revienta
        plugin.Execute(this);
    }

    // ── siembras del modelo (doc 03) ─────────────────────────────────────────

    public Guid SembrarUsuario(string nombre, string email, bool deshabilitado = false)
    {
        var u = new Entity(SchemaNames.Usuario.Entidad);
        u[SchemaNames.Usuario.FullName] = nombre;
        u[SchemaNames.Usuario.Email] = email;
        u[SchemaNames.Usuario.IsDisabled] = deshabilitado;
        return Servicio.Sembrar(u).Id;
    }

    public Guid SembrarTransaccion(Guid creador, TransactionStatus estado,
        RoutingType routing = RoutingType.Paralelo, string nombre = "Documento de prueba")
    {
        var tx = new Entity(SchemaNames.Tx.Entidad);
        tx[SchemaNames.Tx.Name] = nombre;
        tx[SchemaNames.Tx.Status] = new OptionSetValue((int)estado);
        tx[SchemaNames.Tx.RoutingType] = new OptionSetValue((int)routing);
        tx[SchemaNames.Tx.OwnerId] = new EntityReference(SchemaNames.Usuario.Entidad, creador);
        return Servicio.Sembrar(tx).Id;
    }

    public Guid SembrarParticipante(Guid transactionId, Guid userId, ParticipantStatus estado = ParticipantStatus.Pendiente)
    {
        var p = new Entity(SchemaNames.Participante.Entidad);
        p[SchemaNames.Participante.Name] = $"Participante {userId}";
        p[SchemaNames.Participante.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, transactionId);
        p[SchemaNames.Participante.UserId] = new EntityReference(SchemaNames.Usuario.Entidad, userId);
        p[SchemaNames.Participante.Status] = new OptionSetValue((int)estado);
        return Servicio.Sembrar(p).Id;
    }

    public Guid SembrarZona(Guid participantId, int page, string nombre = "Zona sembrada")
    {
        var z = new Entity(SchemaNames.Zona.Entidad);
        z[SchemaNames.Zona.Name] = nombre;
        z[SchemaNames.Zona.ParticipantId] = new EntityReference(SchemaNames.Participante.Entidad, participantId);
        z[SchemaNames.Zona.Page] = page;
        z[SchemaNames.Zona.PosX] = 10m;
        z[SchemaNames.Zona.PosY] = 10m;
        z[SchemaNames.Zona.Width] = 20m;
        z[SchemaNames.Zona.Height] = 8m;
        return Servicio.Sembrar(z).Id;
    }

    public Guid SembrarEvento(Guid transactionId)
    {
        var ev = new Entity(SchemaNames.Evento.Entidad);
        ev[SchemaNames.Evento.TransactionId] = new EntityReference(SchemaNames.Tx.Entidad, transactionId);
        ev[SchemaNames.Evento.Type] = new OptionSetValue((int)EventType.TransaccionCreada);
        return Servicio.Sembrar(ev).Id;
    }

    public void SembrarArchivo(Guid transactionId, string columna, byte[] bytes)
        => Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.Tx.Entidad, transactionId), columna)] = bytes;

    /// <summary>Firma Maestra VIGENTE del usuario, con su PNG en el seam de archivos (doc 03 §4.5).</summary>
    public Guid SembrarFirmaMaestra(Guid userId, byte[] png, int version = 1, bool vigente = true)
    {
        var fm = new Entity(SchemaNames.FirmaMaestra.Entidad);
        fm[SchemaNames.FirmaMaestra.Name] = $"firma v{version}";
        fm[SchemaNames.FirmaMaestra.UserId] = new EntityReference(SchemaNames.Usuario.Entidad, userId);
        fm[SchemaNames.FirmaMaestra.Version] = version;
        fm[SchemaNames.FirmaMaestra.IsActive] = vigente;
        var id = Servicio.Sembrar(fm).Id;
        Archivos.Archivos[StubFileTransfer.Clave(
            new EntityReference(SchemaNames.FirmaMaestra.Entidad, id), SchemaNames.FirmaMaestra.SignatureFile)] = png;
        return id;
    }

    public static byte[] PdfDePrueba(int paginas)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < paginas; i++) doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    object? IServiceProvider.GetService(Type serviceType)
    {
        if (serviceType == typeof(IPluginExecutionContext)) return Contexto;
        if (serviceType == typeof(ITracingService)) return Trace;
        if (serviceType == typeof(IOrganizationServiceFactory)) return this;
        if (serviceType == typeof(IFileTransfer)) return Archivos; // seam (doc 11 §2)
        return null;
    }

    IOrganizationService IOrganizationServiceFactory.CreateOrganizationService(Guid? userId) => Servicio;
}

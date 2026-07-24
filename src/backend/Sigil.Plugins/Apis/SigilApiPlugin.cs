// Base de todos los plugins de Custom APIs: plumbing del contexto, servicio
// ELEVADO (toda escritura es del sistema; el llamante — InitiatingUserId — se usa SOLO
// para autorización y snapshots), manejo de errores del patrón del proyecto y el seam
// de archivos sustituible en tests.

using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Data;

namespace Sigil.Plugins.Apis;

public abstract class SigilApiPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var contexto = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

        // null = contexto de sistema (servicio elevado — "todo escribe el sistema").
        var servicio = factory.CreateOrganizationService(null);

        // Seams: si el provider trae dobles (tests), se usan; en Dataverse
        // real GetService devuelve null y se crean los de producción.
        var archivos = serviceProvider.GetService(typeof(IFileTransfer)) as IFileTransfer
                       ?? CrearFileTransfer(servicio);
        var selladorTsa = serviceProvider.GetService(typeof(ISelladorTsa)) as ISelladorTsa
                          ?? new SelladorTsaReal();
        var entorno = new EntornoDeApi(contexto, servicio, trace, archivos, selladorTsa);

        try
        {
            trace.Trace("{0}: inicio (mensaje={1}, depth={2})", GetType().Name, contexto.MessageName, contexto.Depth);
            Ejecutar(entorno);
            trace.Trace("{0}: fin", GetType().Name);
        }
        catch (InvalidPluginExecutionException)
        {
            throw; // ya viene con mensaje accionable para el usuario
        }
        catch (Exception ex)
        {
            // Tracing seguro: jamás PII ni payloads — el detalle técnico va al trace.
            trace.Trace("{0}: error inesperado: {1}", GetType().Name, ex);
            throw new InvalidPluginExecutionException(
                $"{GetType().Name}: error inesperado — revisar el trace del plugin.", ex);
        }
    }

    protected abstract void Ejecutar(EntornoDeApi entorno);

    /// <summary>Seam de file blocks: los tests lo sustituyen con un doble en memoria.</summary>
    protected virtual IFileTransfer CrearFileTransfer(IOrganizationService servicio)
        => new FileTransferDataverse(servicio);
}

/// <summary>Todo lo que un handler de Custom API necesita, ya resuelto.</summary>
public sealed class EntornoDeApi(
    IPluginExecutionContext contexto,
    IOrganizationService servicio,
    ITracingService trace,
    IFileTransfer archivos,
    ISelladorTsa? selladorTsa = null)
{
    public IPluginExecutionContext Contexto { get; } = contexto;
    public IOrganizationService Servicio { get; } = servicio;
    public ITracingService Trace { get; } = trace;
    public IFileTransfer Archivos { get; } = archivos;
    public ISelladorTsa SelladorTsa { get; } = selladorTsa ?? new SelladorTsaReal();

    /// <summary>Identidad del llamante — SOLO para autorización y snapshots.</summary>
    public Guid Llamante => Contexto.InitiatingUserId;

    /// <summary>Parámetro de entrada opcional (los obligatorios los valida cada handler).</summary>
    public T? Input<T>(string nombre) where T : class
        => Contexto.InputParameters.TryGetValue(nombre, out var valor) ? valor as T : null;

    /// <summary>Parámetro Integer opcional del contrato de la Custom API.</summary>
    public int? InputInt(string nombre)
        => Contexto.InputParameters.TryGetValue(nombre, out var valor) && valor is int i ? i : (int?)null;

    /// <summary>
    /// Integer OPCIONAL de una Custom API. La plataforma materializa un parámetro Integer
    /// opcional ausente como <c>0</c> (no como "ausente") — indistinguible de un 0 explícito.
    /// Por eso 0 se trata como "no provisto"; un negativo llega tal cual y lo rechaza la
    /// validación del dominio. Sin esto, TODO llamante que omita el parámetro sería rechazado.
    /// </summary>
    public int? InputOptionalInt(string nombre)
    {
        var v = InputInt(nombre);
        return v is 0 ? null : v;
    }

    /// <summary>El Target de una Custom API bound — su ausencia es un error de contrato.</summary>
    public EntityReference Target
        => Input<EntityReference>("Target")
           ?? throw new InvalidPluginExecutionException("Falta el Target de la operación (API bound).");

    public void Output(string nombre, object valor) => Contexto.OutputParameters[nombre] = valor;

    /// <summary>Corta la ejecución con TODOS los errores de validación juntos.</summary>
    public void Rechazar(IReadOnlyList<string> errores)
        => throw new InvalidPluginExecutionException(string.Join(" ", errores));
}

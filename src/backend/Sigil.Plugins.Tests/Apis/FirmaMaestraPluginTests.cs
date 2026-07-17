// sanic_sigil_capi_ValidateMasterSignature + GetMasterSignature (ADR-009, doc 03 §4.5).
// Los asserts del versionado: cada carga válida crea una versión NUEVA, exactamente una
// vigente por usuario, el historial jamás se pisa. Un rechazo es VEREDICTO (IsValid=false),
// no excepción, y no crea nada.

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sigil.Plugins.Apis;
using Sigil.Plugins.Core.Domain;
using Sigil.Plugins.Tests.Stub;
using Xunit;

namespace Sigil.Plugins.Tests.Apis;

public class FirmaMaestraPluginTests
{
    private readonly ArnesDeApi _arnes = new();
    private readonly Guid _usuario;

    public FirmaMaestraPluginTests()
    {
        _usuario = _arnes.SembrarUsuario("Beto Firmante", "beto@bac.test");
    }

    private void Validar(string imageBase64, Guid llamante)
    {
        _arnes.Contexto.OutputParameters.Clear();
        _arnes.Contexto.InputParameters["ImageBase64"] = imageBase64;
        _arnes.Ejecutar(new ValidateMasterSignaturePlugin(), SchemaNames.Apis.ValidateMasterSignature, llamante);
    }

    private void Obtener(Guid llamante)
    {
        _arnes.Contexto.OutputParameters.Clear();
        _arnes.Contexto.InputParameters.Clear();
        _arnes.Ejecutar(new GetMasterSignaturePlugin(), SchemaNames.Apis.GetMasterSignature, llamante);
    }

    private JsonElement Historial(Guid llamante)
    {
        _arnes.Contexto.OutputParameters.Clear();
        _arnes.Contexto.InputParameters.Clear();
        _arnes.Ejecutar(new GetMasterSignatureHistoryPlugin(), SchemaNames.Apis.GetMasterSignatureHistory, llamante);
        return JsonDocument.Parse((string)_arnes.Contexto.OutputParameters["HistoryJson"]).RootElement.Clone();
    }

    [Fact]
    public void Validate_Feliz_CreaLaV1Vigente_ConArchivoYDetalles()
    {
        Validar(Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida()), _usuario);

        Assert.Equal(true, _arnes.Contexto.OutputParameters["IsValid"]);
        Assert.NotNull(_arnes.Contexto.OutputParameters["NormalizedImageBase64"]);
        Assert.NotNull(_arnes.Contexto.OutputParameters["MetricsJson"]);

        var firma = Assert.Single(_arnes.Servicio.FilasDe(SchemaNames.FirmaMaestra.Entidad));
        Assert.Equal(1, firma.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version));
        Assert.True(firma.GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive));
        Assert.Equal(_usuario, firma.GetAttributeValue<EntityReference>(SchemaNames.FirmaMaestra.UserId).Id);
        Assert.Equal(_usuario, firma.GetAttributeValue<EntityReference>(SchemaNames.FirmaMaestra.OwnerId).Id);
        Assert.True(firma.Contains(SchemaNames.FirmaMaestra.ValidatedOn));
        Assert.Contains("alphaRatio", firma.GetAttributeValue<string>(SchemaNames.FirmaMaestra.ValidationDetails));

        // el PNG normalizado quedó en la columna File, etiquetado como imagen
        var clave = StubFileTransfer.Clave(
            new EntityReference(SchemaNames.FirmaMaestra.Entidad, firma.Id), SchemaNames.FirmaMaestra.SignatureFile);
        Assert.True(_arnes.Archivos.Archivos.ContainsKey(clave));
        Assert.Equal("image/png", _arnes.Archivos.MimeTypes[clave]);
    }

    [Fact] // versionado doc 03 §4.5: nueva vigente + anterior desactivada, historial intacto
    public void Validate_Resubida_CreaLaV2_YDesactivaLaV1_SinBorrarNada()
    {
        var png = Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida());
        Validar(png, _usuario);
        Validar(png, _usuario);

        var versiones = _arnes.Servicio.FilasDe(SchemaNames.FirmaMaestra.Entidad)
            .OrderBy(f => f.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version)).ToList();
        Assert.Equal(2, versiones.Count); // el historial jamás se pisa
        Assert.False(versiones[0].GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive));
        Assert.True(versiones[1].GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive));
        Assert.Equal(2, versiones[1].GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version));
    }

    [Fact] // max(version)+1 sobre TODAS las versiones — con huecos e inactivas incluidas
    public void Validate_ConHistorialConHuecos_AsignaMaxMasUno()
    {
        _arnes.SembrarFirmaMaestra(_usuario, [1, 2, 3], version: 7, vigente: false); // historial viejo

        Validar(Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida()), _usuario);

        var nueva = _arnes.Servicio.FilasDe(SchemaNames.FirmaMaestra.Entidad)
            .Single(f => f.GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive));
        Assert.Equal(8, nueva.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version));
    }

    [Fact] // auto-sanación: si el riesgo residual dejó DOS vigentes, la subida desactiva TODAS
    public void Validate_ConDosVigentesCorruptas_DesactivaAmbas_YDejaUnaSolaVigente()
    {
        _arnes.SembrarFirmaMaestra(_usuario, [1], version: 1, vigente: true);
        _arnes.SembrarFirmaMaestra(_usuario, [2], version: 2, vigente: true); // corrupción residual

        Validar(Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida()), _usuario);

        var vigentes = _arnes.Servicio.FilasDe(SchemaNames.FirmaMaestra.Entidad)
            .Where(f => f.GetAttributeValue<bool>(SchemaNames.FirmaMaestra.IsActive)).ToList();
        var unica = Assert.Single(vigentes);
        Assert.Equal(3, unica.GetAttributeValue<int>(SchemaNames.FirmaMaestra.Version));
    }

    [Fact] // un rechazo es veredicto con motivos — jamás excepción, jamás crea registros
    public void Validate_ImagenSinTransparencia_DevuelveVeredictoNegativo_SinCrearNada()
    {
        // PNG 100% opaco (foto sobre papel)
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(200, 100,
            new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255, 255));
        using var ms = new System.IO.MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());

        Validar(Convert.ToBase64String(ms.ToArray()), _usuario);

        Assert.Equal(false, _arnes.Contexto.OutputParameters["IsValid"]);
        Assert.Contains("transparen", (string)_arnes.Contexto.OutputParameters["FailureReasons"],
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_arnes.Servicio.FilasDe(SchemaNames.FirmaMaestra.Entidad));
        Assert.Empty(_arnes.Archivos.Subidas);
    }

    [Fact] // errores de CONTRATO sí son excepciones (base64 roto)
    public void Validate_Base64Invalido_EsRechazadoConExcepcion()
    {
        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Validar("esto-no-es-base64!!", _usuario));
        Assert.Contains("base64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // orden mandatorio: tamaño ANTES de decodificar (doc 04 §3.4)
    public void Validate_SobreElLimiteDeCarga_FallaPorTamano_NoPorDecodificacion()
    {
        var gigante = new string('!', 150 * 10 * 1024 * 4 / 3 + 200); // > 10×maxKB y ni siquiera base64
        var ex = Assert.Throws<InvalidPluginExecutionException>(() => Validar(gigante, _usuario));
        Assert.Contains("tamaño", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("base64", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_SinFirmaVigente_NoDevuelveOutputs()
    {
        Obtener(_usuario);
        Assert.False(_arnes.Contexto.OutputParameters.Contains("ImageBase64"));
    }

    [Fact] // el roundtrip completo: lo que Validate normalizó es lo que Get devuelve
    public void Get_ConFirmaVigente_DevuelveElPngNormalizadoYSuFecha()
    {
        Validar(Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida()), _usuario);
        var normalizada = (string)_arnes.Contexto.OutputParameters["NormalizedImageBase64"];

        Obtener(_usuario);

        Assert.Equal(normalizada, _arnes.Contexto.OutputParameters["ImageBase64"]);
        Assert.IsType<DateTime>(_arnes.Contexto.OutputParameters["ValidatedOn"]);
    }

    [Fact] // doc 04 §3.3: SOLO la firma propia — la de otro usuario es invisible
    public void Get_DevuelveLaFirmaDelLlamante_NoLaDeOtro()
    {
        Validar(Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida()), _usuario);
        var otra = _arnes.SembrarUsuario("Otra Persona", "otra@bac.test");

        Obtener(otra);

        Assert.False(_arnes.Contexto.OutputParameters.Contains("ImageBase64"));
    }

    [Fact] // sin firmas: historial vacío, no es un error
    public void History_SinFirmas_DevuelveArrayVacio()
    {
        var arr = Historial(_usuario);
        Assert.Equal(JsonValueKind.Array, arr.ValueKind);
        Assert.Equal(0, arr.GetArrayLength());
    }

    [Fact] // doc 03 §4.5: todas las versiones, más nueva primero, con la vigente marcada y su imagen
    public void History_TrasDosCargas_DevuelveAmbasVersiones_MasNuevaPrimero_ConVigenteMarcada()
    {
        var png = Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida());
        Validar(png, _usuario);
        Validar(png, _usuario);

        var arr = Historial(_usuario);

        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(2, arr[0].GetProperty("version").GetInt32()); // más nueva primero
        Assert.True(arr[0].GetProperty("isActive").GetBoolean());
        Assert.Equal(1, arr[1].GetProperty("version").GetInt32());
        Assert.False(arr[1].GetProperty("isActive").GetBoolean()); // la anterior quedó desactivada
        Assert.False(string.IsNullOrEmpty(arr[0].GetProperty("imageBase64").GetString())); // trae el PNG
        Assert.NotEqual("0001-01-01T00:00:00.0000000", arr[0].GetProperty("validatedOn").GetString());
    }

    [Fact] // doc 04 §3.3: el historial es SOLO el propio
    public void History_SoloDelLlamante_NoDeOtro()
    {
        Validar(Convert.ToBase64String(ArnesDeApi.PngDeFirmaQueValida()), _usuario);
        var otra = _arnes.SembrarUsuario("Otra Persona", "otra@bac.test");

        var arr = Historial(otra);

        Assert.Equal(0, arr.GetArrayLength());
    }
}

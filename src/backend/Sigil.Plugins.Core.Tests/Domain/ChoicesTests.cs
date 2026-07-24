// Los enums de Domain/ son el espejo en código del Apéndice A
// (docs/referencia/12-convenciones-nomenclatura.md) (valores REALES
// del portal, prefijo 15946 — cotejados en el ambiente por CF-A16). Este test los mantiene
// sincronizados con el Apéndice: si el Apéndice cambia, esto se pone rojo ANTES que un flow
// compare contra un número viejo. Los estados se referencian por nombre lógico,
// jamás por número mágico — el número vive UNA vez, acá, verificado.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sigil.Plugins.Core.Domain;

namespace Sigil.Plugins.Core.Tests.Domain;

public class ChoicesTests
{
    // choice global del Apéndice A → enum de Core que lo espeja
    private static readonly Dictionary<string, Type> Espejo = new()
    {
        ["sanic_sigil_choice_transactionstatus"] = typeof(TransactionStatus),
        ["sanic_sigil_choice_participantstatus"] = typeof(ParticipantStatus),
        ["sanic_sigil_choice_routingtype"] = typeof(RoutingType),
        ["sanic_sigil_choice_tsastatus"] = typeof(TsaStatus),
        ["sanic_sigil_choice_eventtype"] = typeof(EventType),
    };

    [Fact]
    public void CadaChoiceDelApendiceA_TieneSuEnumEnCore_ConLosMismosValores()
    {
        var apendice = LeerApendiceA();
        Assert.Equal(5, apendice.Count); // los 5 choices globales

        foreach (var (choice, opciones) in apendice)
        {
            Assert.True(Espejo.TryGetValue(choice, out var enumType),
                $"El choice {choice} del Apéndice A no tiene enum espejo en Core.Domain.");

            var miembros = Enum.GetValues(enumType!).Cast<object>()
                .ToDictionary(v => v.ToString()!, v => Convert.ToInt32(v));

            Assert.Equal(opciones.Count, miembros.Count);
            foreach (var (etiqueta, valor) in opciones)
            {
                var esperado = NombreLogico(etiqueta);
                Assert.True(miembros.TryGetValue(esperado, out var valorEnum),
                    $"{enumType!.Name}: falta el miembro '{esperado}' (etiqueta \"{etiqueta}\").");
                Assert.True(valor == valorEnum,
                    $"{enumType.Name}.{esperado}: el doc dice {valor}, el enum dice {valorEnum}.");
            }
        }
    }

    [Fact]
    public void LosValores_UsanElOptionValuePrefixDelPublisher()
    {
        // 15946 → 15946xxxx (Apéndice A, docs/referencia/12-convenciones-nomenclatura.md). Un valor fuera del prefijo = choice de otro publisher.
        foreach (var enumType in Espejo.Values)
        foreach (var v in Enum.GetValues(enumType).Cast<object>())
            Assert.InRange(Convert.ToInt32(v), 159460000, 159469999);
    }

    // ── etiqueta del portal → nombre lógico de miembro de enum ──────────────
    // "Pendiente de Firma" → PendienteDeFirma; "Transacción creada" → TransaccionCreada;
    // "Re-sellado TSA obtenido" → ReSelladoTsaObtenido.

    private static string NombreLogico(string etiqueta)
    {
        var limpia = SinAcentos(etiqueta).Replace("-", " ");
        var partes = limpia.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in partes)
            sb.Append(char.ToUpperInvariant(p[0])).Append(p.Substring(1).ToLowerInvariant());
        return sb.ToString();
    }

    private static string SinAcentos(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ── lectura del Apéndice A (mismo contrato que CF-A16 en conformidad) ───

    private static Dictionary<string, Dictionary<string, int>> LeerApendiceA()
    {
        var ruta = Path.Combine(BuscarRaizDelRepo(), "docs", "referencia", "12-convenciones-nomenclatura.md");
        Assert.True(File.Exists(ruta), $"No se encontró el Apéndice A en {ruta}.");

        var resultado = new Dictionary<string, Dictionary<string, int>>();
        var patron = new Regex(@"^\|\s*(sanic_sigil_choice_\w+)\s*\|\s*(.+?)\s*\|\s*(\d+)\s*\|", RegexOptions.Compiled);
        foreach (var linea in File.ReadAllLines(ruta))
        {
            var m = patron.Match(linea);
            if (!m.Success) continue;
            if (!resultado.TryGetValue(m.Groups[1].Value, out var opciones))
                resultado[m.Groups[1].Value] = opciones = [];
            opciones[m.Groups[2].Value] = int.Parse(m.Groups[3].Value);
        }
        return resultado;
    }

    private static string BuscarRaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sigil.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "No se encontró la raíz del repo (Sigil.slnx).");
        return dir!.FullName;
    }
}

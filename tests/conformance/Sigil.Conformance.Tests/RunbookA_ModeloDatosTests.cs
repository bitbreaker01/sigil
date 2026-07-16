namespace Sigil.Conformance.Tests;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

/// <summary>
/// Conformidad del MODELO DE DATOS (doc 03) — CF-A16/A17/A18.
/// CF-A16: valores de choices del ambiente == Apéndice A del doc 12 (lo que consumen los flows).
/// CF-A17: cada columna existe con su tipo Y SU BINDING (a qué choice apunta cada Picklist,
///         a qué tabla cada Lookup — sin binding, el tipo solo es un falso verde).
/// CF-A18: autonumber del ledger con el formato exacto del doc 03 §4.4.
/// Pendiente registrado: behaviors de DateTime (User Local) — se agrega con la suite CF-B.
/// </summary>
[Collection("dataverse")]
public class RunbookA_ModeloDatosTests(DataverseFixture fx, ITestOutputHelper output)
{
    // ── CF-A16: choices vs Apéndice A ────────────────────────────────────────

    [Fact] // CF-A16a — corre SIEMPRE (no necesita ambiente): el formato del apéndice es un contrato
    public void CF_A16a_ApendiceA_TieneFormatoParseable()
    {
        var apendice = LeerApendiceA();
        Assert.True(apendice.Count == 5,
            $"El Apéndice A del doc 12 debe tener los 5 choices; el parser leyó {apendice.Count}. ¿Cambió el formato de la tabla?");
        var totalOpciones = apendice.Values.Sum(o => o.Count);
        // 31 = 9+4+2+3+13 (doc 03 §3; eventtype creció a 13 el 2026-07-16 con "TSA abandonada")
        Assert.True(totalOpciones == 31,
            $"El Apéndice A debe tener 31 opciones (9+4+2+3+13 — doc 03 §3); el parser leyó {totalOpciones}.");
    }

    [SkippableFact] // CF-A16
    public void CF_A16_ValoresDeChoices_CoincidenConElApendiceA_DelDoc12()
    {
        var apendice = LeerApendiceA(); // ANTES de RequireClient: una regresión del doc se ve sin ambiente
        var client = fx.RequireClient();

        foreach (var (choiceName, esperadas) in apendice)
        {
            OptionSetMetadata optionSet;
            try
            {
                var response = (RetrieveOptionSetResponse)client.Execute(new RetrieveOptionSetRequest { Name = choiceName });
                optionSet = Assert.IsType<OptionSetMetadata>(response.OptionSetMetadata);
            }
            catch (System.ServiceModel.FaultException)
            {
                Assert.Fail($"El choice global '{choiceName}' no existe en el ambiente (Runbook A §A7, doc 03 §3).");
                return;
            }

            // Diccionario a mano con detección de duplicados: un ToDictionary que revienta
            // con ArgumentException no es un mensaje de conformidad.
            var reales = new Dictionary<string, int>();
            foreach (var o in optionSet.Options)
            {
                var etiqueta = Normalizar(o.Label?.UserLocalizedLabel?.Label ?? "");
                Assert.True(!reales.ContainsKey(etiqueta),
                    $"{choiceName}: dos opciones normalizan a la misma etiqueta '{etiqueta}' — revisar etiquetas duplicadas o vacías en el portal.");
                reales[etiqueta] = o.Value ?? -1;
            }

            Assert.True(esperadas.Count == reales.Count,
                $"{choiceName}: el Apéndice A tiene {esperadas.Count} opciones y el ambiente {reales.Count}. Ambiente: [{string.Join(", ", reales.Keys)}]");

            foreach (var (etiqueta, valorEsperado) in esperadas)
            {
                Assert.True(reales.TryGetValue(Normalizar(etiqueta), out var valorReal),
                    $"{choiceName}: la opción '{etiqueta}' del Apéndice A no existe en el ambiente. Ambiente: [{string.Join(", ", reales.Keys)}]");
                Assert.True(valorEsperado == valorReal,
                    $"{choiceName} / '{etiqueta}': el Apéndice A dice {valorEsperado} pero el ambiente tiene {valorReal}. " +
                    "Corregir el APÉNDICE copiando el valor real del portal (el ambiente manda). Un flow con el valor del apéndice NO dispararía.");
                output.WriteLine($"{choiceName} · {etiqueta} = {valorReal} ✔");
            }
        }
    }

    /// <summary>Case-insensitive Y sin diacríticos: 'Transacción' == 'Transaccion' (decisión
    /// deliberada — los VALORES son el contrato de los flows; una tilde de diferencia en la
    /// etiqueta no debe frenar el gate, pero el mismatch de número SÍ).</summary>
    private static string Normalizar(string s)
    {
        var formD = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static Dictionary<string, Dictionary<string, int>> LeerApendiceA()
    {
        var raiz = BuscarRaizDelRepo();
        var ruta = Path.Combine(raiz, "docs", "fase-0", "12-convenciones-nomenclatura.md");
        Assert.True(File.Exists(ruta), $"No se encontró el doc 12 en {ruta}.");

        var resultado = new Dictionary<string, Dictionary<string, int>>();
        var patron = new Regex(@"^\|\s*(sanic_sigil_choice_\w+)\s*\|\s*(.+?)\s*\|\s*(\d+)\s*\|", RegexOptions.Compiled);
        foreach (var linea in File.ReadAllLines(ruta))
        {
            var m = patron.Match(linea);
            if (!m.Success) continue;
            var choice = m.Groups[1].Value;
            if (!resultado.TryGetValue(choice, out var opciones))
                resultado[choice] = opciones = [];
            opciones[m.Groups[2].Value] = int.Parse(m.Groups[3].Value);
        }
        return resultado;
    }

    private static string BuscarRaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sigil.slnx")))
            dir = dir.Parent;
        Assert.True(dir is not null, "No se encontró la raíz del repo (Sigil.slnx) subiendo desde el directorio del test.");
        return dir!.FullName;
    }

    // ── CF-A17: columnas (nombre + tipo + BINDING) según doc 03 §4 ───────────
    // binding: para PicklistType = nombre del choice global; para LookupType = tabla destino;
    // para el resto = null. Tipos en notación AttributeTypeName (FileType = columnas File).

    public static TheoryData<string, string, string, string?> ColumnasEsperadas()
    {
        var data = new TheoryData<string, string, string, string?>();
        void T(string tabla, params (string col, string tipo, string? binding)[] cols)
        {
            foreach (var (col, tipo, binding) in cols) data.Add(tabla, col, tipo, binding);
        }

        T("sanic_sigil_tbl_transaction",
            ("sanic_sigil_status", "PicklistType", "sanic_sigil_choice_transactionstatus"),
            ("sanic_sigil_routingtype", "PicklistType", "sanic_sigil_choice_routingtype"),
            ("sanic_sigil_message", "MemoType", null), ("sanic_sigil_expirationdays", "IntegerType", null),
            ("sanic_sigil_senton", "DateTimeType", null), ("sanic_sigil_expireson", "DateTimeType", null),
            ("sanic_sigil_completedon", "DateTimeType", null), ("sanic_sigil_contentfile", "FileType", null),
            ("sanic_sigil_contenthash", "StringType", null), ("sanic_sigil_finalfile", "FileType", null),
            ("sanic_sigil_locktoken", "IntegerType", null));

        T("sanic_sigil_tbl_participant",
            ("sanic_sigil_transactionid", "LookupType", "sanic_sigil_tbl_transaction"),
            ("sanic_sigil_userid", "LookupType", "systemuser"),
            ("sanic_sigil_order", "IntegerType", null),
            ("sanic_sigil_status", "PicklistType", "sanic_sigil_choice_participantstatus"),
            ("sanic_sigil_turnactivatedon", "DateTimeType", null), ("sanic_sigil_lastreminderon", "DateTimeType", null),
            ("sanic_sigil_signedon", "DateTimeType", null), ("sanic_sigil_rejectionreason", "MemoType", null),
            ("sanic_sigil_signaturesnapshot", "FileType", null),
            ("sanic_sigil_mastersignatureid", "LookupType", "sanic_sigil_tbl_mastersignature"),
            ("sanic_sigil_signername", "StringType", null), ("sanic_sigil_signeremail", "StringType", null),
            ("sanic_sigil_signerentraobjectid", "StringType", null));

        T("sanic_sigil_tbl_signaturezone",
            ("sanic_sigil_participantid", "LookupType", "sanic_sigil_tbl_participant"),
            ("sanic_sigil_page", "IntegerType", null),
            ("sanic_sigil_posx", "DecimalType", null), ("sanic_sigil_posy", "DecimalType", null),
            ("sanic_sigil_width", "DecimalType", null), ("sanic_sigil_height", "DecimalType", null));

        T("sanic_sigil_tbl_ledgerentry",
            ("sanic_sigil_transactionid", "LookupType", "sanic_sigil_tbl_transaction"),
            ("sanic_sigil_contenthash", "StringType", null), ("sanic_sigil_finalhash", "StringType", null),
            ("sanic_sigil_tsatoken", "MemoType", null),
            ("sanic_sigil_tsastatus", "PicklistType", "sanic_sigil_choice_tsastatus"),
            ("sanic_sigil_sealedon", "DateTimeType", null), ("sanic_sigil_signersummary", "MemoType", null));

        T("sanic_sigil_tbl_mastersignature",
            ("sanic_sigil_userid", "LookupType", "systemuser"),
            ("sanic_sigil_signaturefile", "FileType", null),
            ("sanic_sigil_version", "IntegerType", null), ("sanic_sigil_isactive", "BooleanType", null),
            ("sanic_sigil_validatedon", "DateTimeType", null), ("sanic_sigil_validationdetails", "MemoType", null));

        T("sanic_sigil_tbl_event",
            ("sanic_sigil_transactionid", "LookupType", "sanic_sigil_tbl_transaction"),
            ("sanic_sigil_type", "PicklistType", "sanic_sigil_choice_eventtype"),
            ("sanic_sigil_actorname", "StringType", null), ("sanic_sigil_actoremail", "StringType", null),
            ("sanic_sigil_participantid", "LookupType", "sanic_sigil_tbl_participant"),
            ("sanic_sigil_documenthash", "StringType", null),
            ("sanic_sigil_occurredon", "DateTimeType", null), ("sanic_sigil_details", "MemoType", null));

        return data;
    }

    private static readonly ConcurrentDictionary<string, EntityMetadata> _metadataCache = new();

    private EntityMetadata MetadataDe(string tabla) => _metadataCache.GetOrAdd(tabla, t =>
    {
        try
        {
            var response = (RetrieveEntityResponse)fx.RequireClient().Execute(new RetrieveEntityRequest
            {
                LogicalName = t,
                EntityFilters = EntityFilters.Attributes,
            });
            return response.EntityMetadata;
        }
        catch (System.ServiceModel.FaultException)
        {
            Assert.Fail($"La tabla '{t}' no existe en el ambiente (doc 03 §4 — Runbook A §A7).");
            throw; // inalcanzable
        }
    });

    [SkippableTheory] // CF-A17
    [MemberData(nameof(ColumnasEsperadas))]
    public void CF_A17_Columna_ExisteConTipoYBindingDelDoc03(string tabla, string columna, string tipoEsperado, string? bindingEsperado)
    {
        fx.RequireClient();
        var metadata = MetadataDe(tabla);

        var attr = metadata.Attributes?.FirstOrDefault(a => a.LogicalName == columna);
        Assert.True(attr is not null,
            $"La columna '{columna}' no existe en '{tabla}' (doc 03 §4). Columnas sanic_* presentes: " +
            $"[{string.Join(", ", (metadata.Attributes ?? []).Where(a => a.LogicalName.StartsWith("sanic_")).Select(a => a.LogicalName))}]");

        var tipoReal = attr!.AttributeTypeName?.Value ?? attr.AttributeType?.ToString() ?? "?";
        Assert.True(tipoEsperado == tipoReal,
            $"'{tabla}.{columna}': el doc 03 exige tipo {tipoEsperado} pero el ambiente tiene {tipoReal}. " +
            "El TIPO no se puede cambiar después de creada la columna: borrarla y recrearla.");

        // El BINDING es parte del tipo (hallazgo del antagonista): un Picklist atado al choice
        // equivocado o un Lookup a la tabla equivocada pasan un chequeo de tipo — y rompen
        // los flows/el motor en silencio.
        switch (attr)
        {
            case PicklistAttributeMetadata picklist:
                var choiceReal = picklist.OptionSet?.Name ?? "(sin option set)";
                Assert.True(bindingEsperado == choiceReal,
                    $"'{tabla}.{columna}': debe usar el choice global '{bindingEsperado}' pero usa '{choiceReal}'. " +
                    "Recrear la columna eligiendo el choice correcto (doc 03 §4).");
                Assert.True(picklist.OptionSet?.IsGlobal == true,
                    $"'{tabla}.{columna}': el choice debe ser GLOBAL (doc 03 §3), no local de la tabla.");
                break;
            case LookupAttributeMetadata lookup:
                var targets = lookup.Targets ?? [];
                Assert.True(targets.Contains(bindingEsperado),
                    $"'{tabla}.{columna}': el lookup debe apuntar a '{bindingEsperado}' pero apunta a [{string.Join(", ", targets)}].");
                break;
        }
    }

    // ── CF-A18: autonumber del ledger (doc 03 §4.4 — "Formato exacto") ───────

    [SkippableFact] // CF-A18
    public void CF_A18_Ledger_PrimariaAutonumber_ConElFormatoExacto()
    {
        fx.RequireClient();
        var metadata = MetadataDe("sanic_sigil_tbl_ledgerentry");

        var primaria = metadata.Attributes?.OfType<StringAttributeMetadata>()
            .FirstOrDefault(a => a.LogicalName == metadata.PrimaryNameAttribute);
        Assert.True(primaria is not null, "No se encontró la columna primaria del ledger.");

        Assert.True(primaria!.AutoNumberFormat == "SIGIL-{DATETIMEUTC:yyyy}-{SEQNUM:6}",
            $"La primaria del ledger debe ser autonumber con formato exacto 'SIGIL-{{DATETIMEUTC:yyyy}}-{{SEQNUM:6}}' " +
            $"(doc 03 §4.4); el ambiente tiene: '{primaria.AutoNumberFormat ?? "(sin autonumber)"}'.");
    }
}

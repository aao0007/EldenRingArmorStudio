using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.VisualBasic.Logging;
using Serilog;
using System.Globalization;
using System.IO;

namespace EldenRingArmorStudio.Core;

/// <summary>
/// Preset de invisibleFlags para EquipParamProtector.
/// </summary>
public record FlagPreset(
    string Key,
    string Label,
    string Description,
    string Category,
    int[] Flags
);

/// <summary>
/// Motor de InvisibleFlags para EquipParamProtector de Elden Ring.
/// Los flags invisibleFlag_SexVer00..95 ocultan partes del cuerpo del personaje
/// cuando lleva equipada una pieza de armadura.
/// </summary>
public static class InvisibleFlags
{
    // Todas las columnas SexVer (00..95)
    public static readonly string[] AllSexVerColumns =
        Enumerable.Range(0, 96).Select(i => $"invisibleFlag_SexVer{i:D2}").ToArray();

    public static readonly List<FlagPreset> Presets = new()
    {
        // HEAD
        new("head_face_cover",
            "🪖 Ocultar cara (casco completo)",
            "Oculta la cara bajo el casco.\nFlags SexVer60-69, 75, 76, 78, 79\nUso: cascos cerrados, máscaras integrales.",
            "Head",
            new[]{60,61,62,63,64,65,66,67,68,69,75,76,78,79}),

        new("head_face_and_hair",
            "🪖 Ocultar cara + pelo",
            "Oculta cara completa y pelo.\nFlags SexVer0-6 + 17,23,32-37 + 60-69, 75,76\nUso: cascos que cubren cabeza entera.",
            "Head",
            new[]{0,1,2,3,4,5,6,17,23,32,33,34,35,36,37,60,61,62,63,64,65,66,67,68,69,75,76,78,79}),

        new("head_hair_only",
            "🪖 Ocultar solo pelo/barba",
            "Oculta el pelo pero no la cara.\nFlags SexVer32, 33, 34, 37\nUso: coronas, sombreros.",
            "Head",
            new[]{32,33,34,37}),

        new("head_face_partial",
            "🪖 Ocultar cara parcial (visera)",
            "Oculta parte de la cara (nariz, boca).\nFlags SexVer60-65, 75\nUso: cascos con visera abierta.",
            "Head",
            new[]{60,61,62,63,64,65,75}),

        // BODY
        new("body_full",
            "🥋 Ocultar cuerpo completo",
            "Oculta el torso y partes del cuerpo.\nFlags SexVer19, 40-59, 73, 77\nUso: armaduras de torso completas.",
            "Body",
            new[]{19,40,41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,56,57,58,59,73,77}),

        new("body_torso",
            "🥋 Ocultar torso",
            "Oculta solo el torso.\nFlags SexVer40-50\nUso: armaduras ligeras.",
            "Body",
            new[]{40,41,42,43,44,45,46,47,48,49,50}),

        // ARMS
        new("arms_full",
            "🧤 Ocultar brazos completos",
            "Oculta brazos y manos.\nFlags SexVer10-13, 18, 24-31\nUso: guanteletes largos.",
            "Arms",
            new[]{10,11,12,13,18,24,25,26,27,28,29,30,31}),

        new("arms_hands",
            "🧤 Ocultar manos",
            "Oculta solo las manos.\nFlags SexVer18, 24, 25\nUso: guanteletes cortos.",
            "Arms",
            new[]{18,24,25}),

        // LEGS
        new("legs_full",
            "👢 Ocultar piernas completas",
            "Oculta las piernas y pies.\nFlags SexVer12,14,15,16,19,71,72\nUso: armaduras largas.",
            "Legs",
            new[]{12,14,15,16,19,71,72}),

        new("legs_feet",
            "👢 Ocultar solo pies",
            "Oculta solo los pies.\nFlags SexVer71, 72\nUso: botas largas.",
            "Legs",
            new[]{71,72}),
    };

    public static List<FlagPreset> ForCategory(string category) =>
        Presets.Where(p => p.Category == category || p.Category == "Any").ToList();

    /// <summary>
    /// Genera un CSV importable en Smithbox → Param Editor → EquipParamProtector → Import CSV.
    /// </summary>
    public static bool GenerateSmithboxCsv(
        string outputPath,
        IEnumerable<(string ParamId, IEnumerable<int> Flags)> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var writer = new StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

            // Header
            csv.WriteField("ID");
            foreach (var col in AllSexVerColumns) csv.WriteField(col);
            csv.NextRecord();

            // Filas
            foreach (var (pid, flags) in entries)
            {
                var flagSet = flags.ToHashSet();
                csv.WriteField(pid);
                for (int i = 0; i < 96; i++)
                    csv.WriteField(flagSet.Contains(i) ? "1" : "0");
                csv.NextRecord();
            }
            Log.Information("CSV de flags generado: {Path}", outputPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando CSV de flags");
            return false;
        }
    }

    /// <summary>
    /// Busca todos los IDs de param en el CSV del juego que corresponden
    /// al equipModelId dado (ej. "1360").
    /// </summary>
    public static List<string> GetParamIdsForModelId(string modelIdNum, string gameCsvPath)
    {
        if (!File.Exists(gameCsvPath)) return [];
        var ids = new List<string>();
        try
        {
            using var reader = new StreamReader(gameCsvPath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            csv.Read(); csv.ReadHeader();
            while (csv.Read())
            {
                var mid = csv.GetField("equipModelId")?.Trim();
                if (mid == modelIdNum)
                {
                    var id = csv.GetField("ID")?.Trim();
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
            }
        }
        catch (Exception ex) { Log.Error(ex, "Error leyendo CSV del juego"); }
        return ids;
    }
}
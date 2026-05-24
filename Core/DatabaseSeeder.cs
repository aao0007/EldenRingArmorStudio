using CsvHelper;
using CsvHelper.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EldenRingArmorStudio.Core
{
    public static class DatabaseSeeder
    {
        public static void SeedFromCsv(string csvPath, ArmorDatabase db)
        {
            if (!File.Exists(csvPath)) return;

            var catMap = new Dictionary<string, string> { { "0", "Head" }, { "1", "Body" }, { "2", "Arms" }, { "3", "Legs" } };
            var prefixMap = new Dictionary<string, string> { { "Head", "hd_m_" }, { "Body", "bd_m_" }, { "Arms", "am_m_" }, { "Legs", "lg_m_" } };

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = "," });

            csv.Read();
            csv.ReadHeader();

            var listaParaGuardar = new List<ArmorRecord>();
            int count = 0;

            while (csv.Read())
            {
                string id = csv.GetField("ID");
                string nameEn = csv.GetField("Name") ?? $"Armor {id}";
                string nameEs = csv.GetField("name_es");
                if (string.IsNullOrWhiteSpace(nameEs)) nameEs = nameEn;

                string modelIdStr = csv.GetField("equipModelId");
                string catNum = csv.GetField("protectorCategory");

                if (string.IsNullOrWhiteSpace(modelIdStr) || modelIdStr == "0" || string.IsNullOrWhiteSpace(catNum))
                    continue;

                if (!catMap.TryGetValue(catNum, out string category))
                    continue;

                string prefix = prefixMap[category];
                string modelIdPadded = modelIdStr.PadLeft(4, '0');
                string equipModelId = (prefix + modelIdPadded).ToUpper();
                string fileName = prefix + modelIdPadded + ".partsbnd.dcx";
                bool isAltered = nameEn.Contains("Altered") || nameEs.Contains("(Alterad");

                // Leer iconIdM, con fallback a iconIdF
                string iconIdStr = csv.GetField("iconIdM");
                if (string.IsNullOrWhiteSpace(iconIdStr) || iconIdStr.Trim() == "0")
                    iconIdStr = csv.GetField("iconIdF");

                // Parsear los IDs a entero
                int.TryParse(csv.GetField("iconIdM")?.Trim(), out int finalIconM);
                int.TryParse(csv.GetField("iconIdF")?.Trim(), out int finalIconF);

                // Construir thumbnailPath RELATIVA con el ID formateado a 5 dígitos
                string thumbnailPath = "";
                if (!string.IsNullOrWhiteSpace(iconIdStr) && iconIdStr.Trim() != "0")
                {
                    if (int.TryParse(iconIdStr.Trim(), out int iconIdParsed) && iconIdParsed != 0)
                    {
                        // "310" → "00310",  "10009" → "10009"
                        string idFormateado = iconIdParsed.ToString("D5");
                        string nombreImagen = $"MENU_Knowledge_{idFormateado}.png";
                        // Ruta relativa: funciona tanto desde bin/Debug como desde la raíz del proyecto
                        thumbnailPath = Path.Combine("data", "icons", nombreImagen);
                    }
                }

                var part = new ArmorRecord
                {
                    EquipModelId = equipModelId,
                    NameEn = nameEn,
                    NameEs = nameEs,
                    Category = category,
                    IsAltered = isAltered,
                    FileName = fileName,
                    SetName = "",
                    ThumbnailPath = thumbnailPath,
                    IconIdM = finalIconM,
                    IconIdF = finalIconF
                };

                listaParaGuardar.Add(part);
                count++;
            }

            if (listaParaGuardar.Count > 0)
            {
                db.UpsertBulk(listaParaGuardar);
                Log.Information("DatabaseSeeder: {Count} registros importados desde {Csv}", count, csvPath);
            }
        }
    }
}
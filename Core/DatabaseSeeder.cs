using CsvHelper;
using CsvHelper.Configuration;
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

            // Diccionarios de mapeo igual que en el scraper de Python
            var catMap = new Dictionary<string, string> { { "0", "Head" }, { "1", "Body" }, { "2", "Arms" }, { "3", "Legs" } };
            var prefixMap = new Dictionary<string, string> { { "Head", "hd_m_" }, { "Body", "bd_m_" }, { "Arms", "am_m_" }, { "Legs", "lg_m_" } };

            using var reader = new StreamReader(csvPath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = "," });

            csv.Read();
            csv.ReadHeader();

            int count = 0;
            while (csv.Read())
            {
                string id = csv.GetField("ID");
                string nameEn = csv.GetField("Name") ?? $"Armor {id}";
                string nameEs = csv.GetField("name_es");
                if (string.IsNullOrWhiteSpace(nameEs)) nameEs = nameEn;

                string modelIdStr = csv.GetField("equipModelId");
                string catNum = csv.GetField("protectorCategory");

                // Ignorar entradas inválidas o vacías
                if (string.IsNullOrWhiteSpace(modelIdStr) || modelIdStr == "0" || string.IsNullOrWhiteSpace(catNum))
                    continue;

                if (!catMap.TryGetValue(catNum, out string category))
                    continue;

                // Formatear el EquipModelId (ej. 1360 -> HD_M_1360)
                string prefix = prefixMap[category];
                string modelIdPadded = modelIdStr.PadLeft(4, '0');

                string equipModelId = (prefix + modelIdPadded).ToUpper();
                string fileName = prefix + modelIdPadded + ".partsbnd.dcx";

                bool isAltered = nameEn.Contains("Altered") || nameEs.Contains("(Alterad");

                var part = new ArmorPart
                {
                    EquipModelId = equipModelId,
                    NameEn = nameEn,
                    NameEs = nameEs,
                    Category = category,
                    IsAltered = isAltered,
                    FileName = fileName,
                    SetName = "" // Se puede añadir si existe en el CSV
                };

                db.Upsert(part);
                count++;
            }
        }
    }
}
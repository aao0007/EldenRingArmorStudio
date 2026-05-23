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

            // Diccionarios de mapeo basados en las categorías del archivo param protector
            var catMap = new Dictionary<string, string> { { "0", "Head" }, { "1", "Body" }, { "2", "Arms" }, { "3", "Legs" } };
            var prefixMap = new Dictionary<string, string> { { "Head", "hd_m_" }, { "Body", "bd_m_" }, { "Arms", "am_m_" }, { "Legs", "lg_m_" } };

            using var reader = new StreamReader(csvPath);
            // Agregamos PrepareHeaderForMatch para que no importe si la columna es "Name", "name", "Name " con espacios, etc.
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ",",
                PrepareHeaderForMatch = args => args.Header.ToLower().Trim(),
                HeaderValidated = null, // Evita que el programa estalle si falta alguna columna secundaria
                MissingFieldFound = null
            };

            using var csv = new CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();

            var listaParaGuardar = new List<ArmorRecord>();

            while (csv.Read())
            {
                string id = csv.GetField("id");

                // Buscamos "name" (CsvHelper lo emparejará gracias al PrepareHeaderForMatch)
                string nameEn = csv.GetField("name");
                string nameEs = csv.GetField("name_es");

                // Si el nombre en inglés está vacío (como en los IDs 1000-5000), le asignamos un nombre temporal para que no sea nulo
                if (string.IsNullOrWhiteSpace(nameEn)) nameEn = $"Unused Armor {id}";
                if (string.IsNullOrWhiteSpace(nameEs)) nameEs = nameEn;

                string modelIdStr = csv.GetField("equipmodelid");
                string catNum = csv.GetField("protectorcategory");
                string genderStr = csv.GetField("equipmodelgender") ?? "0";

                // Validamos rigurosamente que existan los datos esenciales del modelo
                if (string.IsNullOrWhiteSpace(modelIdStr) || modelIdStr == "0" || string.IsNullOrWhiteSpace(catNum))
                    continue;

                if (!catMap.TryGetValue(catNum, out string category))
                    continue;

                // Formatear el EquipModelId (ej. 1010 -> HD_M_1010)
                string prefix = prefixMap[category];
                string modelIdPadded = modelIdStr.PadLeft(4, '0');

                string equipModelId = (prefix + modelIdPadded).ToUpper();
                string fileName = prefix + modelIdPadded + ".partsbnd.dcx";

                bool isAltered = nameEn.Contains("Altered") || nameEs.Contains("(Alterad");

                // Forzamos estrictamente el icono masculino "iconidm"
                string iconIdStr = csv.GetField("iconidm");

                string thumbnailPath = "";
                int? finalIconId = null;

                if (!string.IsNullOrWhiteSpace(iconIdStr) && int.TryParse(iconIdStr, out int iconIdVal) && iconIdVal != 0)
                {
                    finalIconId = iconIdVal;
                    string idLimpio = iconIdStr.Trim();
                    string nombreImagen = $"MENU_Knowledge_{idLimpio}.png";
                    thumbnailPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "icons", nombreImagen);
                }

                // Parseo de los valores REAL / double utilizando la cultura invariante
                double? weight = null;
                if (double.TryParse(csv.GetField("weight"), CultureInfo.InvariantCulture, out double wVal)) weight = wVal;

                double? defensePhys = null;
                if (double.TryParse(csv.GetField("defensephysics"), CultureInfo.InvariantCulture, out double dpVal)) defensePhys = dpVal;

                double? defenseMagic = null;
                if (double.TryParse(csv.GetField("defensemagic"), CultureInfo.InvariantCulture, out double dmVal)) defenseMagic = dmVal;

                double? defenseFire = null;
                if (double.TryParse(csv.GetField("defensefire"), CultureInfo.InvariantCulture, out double dfVal)) defenseFire = dfVal;

                double? defenseLightning = null;
                if (double.TryParse(csv.GetField("defensethunder"), CultureInfo.InvariantCulture, out double dlVal)) defenseLightning = dlVal;

                double? poise = null;
                if (double.TryParse(csv.GetField("sadurability"), CultureInfo.InvariantCulture, out double pVal)) poise = pVal;

                // Construcción del SetName limpiando el tipo de pieza del nombre original
                string setName = nameEn.Replace(" Helmet", "").Replace(" Helm", "").Replace(" Hood", "").Replace(" Mask", "")
                                       .Replace(" Armor", "").Replace(" Robe", "").Replace(" Garb", "").Replace(" Coat", "").Replace(" Gown", "")
                                       .Replace(" Gauntlets", "").Replace(" Bracers", "").Replace(" Bracelets", "").Replace(" Gloves", "")
                                       .Replace(" Greaves", "").Replace(" Trousers", "").Replace(" Slops", "").Replace(" Boots", "")
                                       .Replace(" (Altered)", "").Trim();

                var part = new ArmorRecord(
                    EquipModelId: equipModelId,
                    NameEn: nameEn,
                    NameEs: nameEs,
                    Category: category,
                    IsAltered: isAltered,
                    Gender: genderStr,
                    SetName: setName,
                    FileName: fileName,
                    Weight: weight,
                    DefensePhys: defensePhys,
                    DefenseMagic: defenseMagic,
                    DefenseFire: defenseFire,
                    DefenseLightning: defenseLightning,
                    Poise: poise,
                    IconId: finalIconId,
                    ThumbnailPath: thumbnailPath
                );

                listaParaGuardar.Add(part);
            }

            // Guardado indexado en SQLite
            if (listaParaGuardar.Count > 0)
            {
                foreach (var armor in listaParaGuardar)
                {
                    db.Upsert(armor);
                }
            }
        }
    }
}
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;

namespace EldenRingArmorStudio.Core
{
    // Esta es la clase que dice que falta
    public class ArmorPart
    {
        public string EquipModelId { get; set; }
        public string NameEn { get; set; }
        public string NameEs { get; set; }
        public string Category { get; set; }
        public bool IsAltered { get; set; }
        public string SetName { get; set; }
        public string FileName { get; set; }
    }

    public class ArmorDatabase
    {
        private readonly string _connectionString;

        public ArmorDatabase(string dbPath = "data/armor_db.sqlite")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
            _connectionString = $"Data Source={dbPath};Version=3;";
        }

        // Este es el método que daba error de que no existía
        public List<ArmorPart> SearchArmor(string query, string category = null)
        {
            using var connection = new SQLiteConnection(_connectionString);
            string sql = @"SELECT equip_model_id AS EquipModelId, name_en AS NameEn, 
                                  name_es AS NameEs, category AS Category, 
                                  is_altered AS IsAltered, set_name AS SetName, 
                                  file_name AS FileName 
                           FROM armor_parts 
                           WHERE (name_en LIKE @q OR name_es LIKE @q OR equip_model_id LIKE @q)";

            if (!string.IsNullOrEmpty(category) && category != "Todos")
                sql += " AND category = @cat";

            return connection.Query<ArmorPart>(sql, new { q = $"%{query}%", cat = category }).ToList();
        }
    }
}
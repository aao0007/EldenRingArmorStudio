using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;

namespace EldenRingArmorStudio.Core
{
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
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            _connectionString = $"Data Source={dbPath};Version=3;";
            InitDb();
        }

        private void InitDb()
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS armor_parts (
                    equip_model_id TEXT PRIMARY KEY,
                    name_en TEXT, name_es TEXT, category TEXT,
                    is_altered INTEGER, set_name TEXT, file_name TEXT
                )");
        }

        // --- NUEVO: Obtener la cantidad de armaduras para saber si está vacía ---
        public int Count()
        {
            using var connection = new SQLiteConnection(_connectionString);
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM armor_parts");
        }

        // --- NUEVO: Insertar o actualizar una armadura en la BD ---
        public void Upsert(ArmorPart part)
        {
            using var connection = new SQLiteConnection(_connectionString);
            string sql = @"
                INSERT OR REPLACE INTO armor_parts 
                (equip_model_id, name_en, name_es, category, is_altered, set_name, file_name) 
                VALUES (@EquipModelId, @NameEn, @NameEs, @Category, @IsAltered, @SetName, @FileName)";

            connection.Execute(sql, part);
        }

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
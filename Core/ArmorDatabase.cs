using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Dapper;

namespace EldenRingArmorStudio.Core
{
    public class ArmorRecord
    {
        public string EquipModelId { get; set; }
        public string NameEn { get; set; }
        public string NameEs { get; set; }
        public string Category { get; set; }
        public bool IsAltered { get; set; }
        public string SetName { get; set; }
        public string FileName { get; set; }
        public string ThumbnailPath { get; set; }
        public int IconIdM { get; set; }
        public int IconIdF { get; set; }
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
            connection.Open();

            // 🚀 OPTIMIZACIONES EXTREMAS DE RENDIMIENTO PARA SQLITE
            connection.Execute("PRAGMA journal_mode = WAL;"); // Escritura en memoria primero (10x más rápido)
            connection.Execute("PRAGMA synchronous = NORMAL;");
            connection.Execute("PRAGMA temp_store = MEMORY;");

            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS armor_parts (
                    equip_model_id TEXT PRIMARY KEY,
                    name_en TEXT, name_es TEXT, category TEXT,
                    is_altered INTEGER, set_name TEXT, file_name TEXT,
                    thumbnail_path TEXT
                )");

            // 🚀 ÍNDICES PARA QUE LA BÚSQUEDA VAYA FLUIDA Y NO SE CONGELE LA APP
            connection.Execute("CREATE INDEX IF NOT EXISTS idx_name_en ON armor_parts(name_en);");
            connection.Execute("CREATE INDEX IF NOT EXISTS idx_name_es ON armor_parts(name_es);");

            try { connection.Execute("ALTER TABLE armor_parts ADD COLUMN thumbnail_path TEXT"); } catch { }
        }

        public int Count()
        {
            using var connection = new SQLiteConnection(_connectionString);
            return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM armor_parts");
        }

        // 🚀 NUEVO: MÉTODO PARA GUARDAR TODO EL CSV DE GOLPE EN 0.1 SEGUNDOS
        public void UpsertBulk(IEnumerable<ArmorRecord> parts)
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction(); // La clave para escritura ultrarrápida

            string sql = @"
                INSERT OR REPLACE INTO armor_parts 
                (equip_model_id, name_en, name_es, category, is_altered, set_name, file_name, thumbnail_path) 
                VALUES (@EquipModelId, @NameEn, @NameEs, @Category, @IsAltered, @SetName, @FileName, @ThumbnailPath)";

            connection.Execute(sql, parts, transaction: transaction);
            transaction.Commit();
        }

        // Método antiguo por si guardas armaduras sueltas
        public void Upsert(ArmorRecord part)
        {
            using var connection = new SQLiteConnection(_connectionString);
            string sql = @"
                INSERT OR REPLACE INTO armor_parts 
                (equip_model_id, name_en, name_es, category, is_altered, set_name, file_name, thumbnail_path) 
                VALUES (@EquipModelId, @NameEn, @NameEs, @Category, @IsAltered, @SetName, @FileName, @ThumbnailPath)";
            connection.Execute(sql, part);
        }

        public List<ArmorRecord> SearchArmor(string query, string category = null, bool alteredOnly = false)
        {
            using var connection = new SQLiteConnection(_connectionString);

            string sql = @"SELECT equip_model_id AS EquipModelId, name_en AS NameEn, 
                                  name_es AS NameEs, category AS Category, 
                                  is_altered AS IsAltered, set_name AS SetName, 
                                  file_name AS FileName, thumbnail_path AS ThumbnailPath 
                           FROM armor_parts 
                           WHERE (name_en LIKE @q OR name_es LIKE @q OR equip_model_id LIKE @q)";

            if (!string.IsNullOrEmpty(category) && category != "Todos")
                sql += " AND category = @cat";

            return connection.Query<ArmorRecord>(sql, new { q = $"%{query}%", cat = category }).ToList();
        }
    }
}
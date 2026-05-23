using Microsoft.Data.Sqlite;
using Serilog;
using System.IO;
using System.Collections.Generic;
using System;

namespace EldenRingArmorStudio.Core;

/// <summary>
/// Registro de una pieza de armadura en la base de datos local ajustado a EquipParamProtector.csv.
/// </summary>
public record ArmorRecord(
    string EquipModelId,    // ID o equipModelId del CSV
    string NameEn,          // Columna 'Name'
    string NameEs,          // Columna 'name_es'
    string Category,        // protectorCategory (Head, Body, Arms, Legs)
    bool IsAltered,         // Determinado si el nombre contiene (Altered) o lógicas del set
    string Gender,          // equipModelGender
    string SetName,         // Nombre común del conjunto
    string FileName,        // Nombre del archivo o recurso asociado
    double? Weight = null,  // Columna 'weight' (REAL)
    double? DefensePhys = null,  // Columna 'defensePhysics' o 'neutralDamageCutRate' (REAL)
    double? DefenseMagic = null, // Columna 'defenseMagic' o 'magicDamageCutRate' (REAL)
    double? DefenseFire = null,  // Columna 'defenseFire' o 'fireDamageCutRate' (REAL)
    double? DefenseLightning = null, // Columna 'defenseThunder' o 'thunderDamageCutRate' (REAL)
    double? Poise = null,        // Columna 'toughnessCorrectRate' o 'saDurability' (REAL)
    int? IconId = null,          // Columna 'iconIdM' o 'iconIdF'
    string ThumbnailPath = null
);

public class ArmorDatabase : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public ArmorDatabase(string dbPath = "data/armor_db.sqlite")
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        Init();
    }

    private void Init()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        using var cmd = _connection.CreateCommand();
        // Se actualizan los tipos de datos de las defensas a REAL (double) para admitir los valores del CSV
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS armor_parts (
                equip_model_id   TEXT PRIMARY KEY,
                name_en          TEXT,
                name_es          TEXT,
                category         TEXT,
                is_altered       INTEGER DEFAULT 0,
                gender           TEXT,
                set_name         TEXT,
                file_name        TEXT,
                weight           REAL,
                defense_phys     REAL,
                defense_magic    REAL,
                defense_fire     REAL,
                defense_lightning REAL,
                poise            REAL,
                icon_id          INTEGER,
                thumbnail_path   TEXT
            )
        """;
        cmd.ExecuteNonQuery();

        // Migración silenciosa adaptada con los nuevos tipos de datos reales
        var newCols = new[] {
            ("set_name", "TEXT"), ("weight", "REAL"), ("defense_phys", "REAL"),
            ("defense_magic", "REAL"), ("defense_fire", "REAL"),
            ("defense_lightning", "REAL"), ("poise", "REAL"),
            ("icon_id", "INTEGER"), ("thumbnail_path", "TEXT")
        };
        foreach (var (col, type) in newCols)
        {
            try
            {
                using var alter = _connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE armor_parts ADD COLUMN {col} {type}";
                alter.ExecuteNonQuery();
            }
            catch { /* columna ya existe */ }
        }
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    public void Upsert(ArmorRecord r)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO armor_parts
              (equip_model_id,name_en,name_es,category,is_altered,gender,set_name,
               file_name,weight,defense_phys,defense_magic,defense_fire,
               defense_lightning,poise,icon_id,thumbnail_path)
            VALUES
              (@id,@en,@es,@cat,@alt,@gen,@set,@file,@w,@dp,@dm,@df,@dl,@p,@ic,@th)
        """;
        cmd.Parameters.AddWithValue("@id", r.EquipModelId);
        cmd.Parameters.AddWithValue("@en", r.NameEn);
        cmd.Parameters.AddWithValue("@es", r.NameEs);
        cmd.Parameters.AddWithValue("@cat", r.Category);
        cmd.Parameters.AddWithValue("@alt", r.IsAltered ? 1 : 0);
        cmd.Parameters.AddWithValue("@gen", r.Gender ?? "");
        cmd.Parameters.AddWithValue("@set", r.SetName ?? "");
        cmd.Parameters.AddWithValue("@file", r.FileName ?? "");
        cmd.Parameters.AddWithValue("@w", (object?)r.Weight ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dp", (object?)r.DefensePhys ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dm", (object?)r.DefenseMagic ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@df", (object?)r.DefenseFire ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dl", (object?)r.DefenseLightning ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p", (object?)r.Poise ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ic", (object?)r.IconId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@th", (object?)r.ThumbnailPath ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<ArmorRecord> Search(string query, string? category = null, bool alteredOnly = false)
    {
        using var cmd = _connection!.CreateCommand();
        var sql = """
            SELECT * FROM armor_parts
            WHERE (name_en LIKE @q OR name_es LIKE @q OR equip_model_id LIKE @q
                   OR set_name LIKE @q)
        """;
        if (!string.IsNullOrEmpty(category) && category != "Todos")
            sql += " AND category = @cat";
        if (alteredOnly)
            sql += " AND is_altered = 1";
        sql += " ORDER BY set_name, category, equip_model_id LIMIT 1000";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        if (!string.IsNullOrEmpty(category) && category != "Todos")
            cmd.Parameters.AddWithValue("@cat", category);

        var results = new List<ArmorRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadRecord(reader));
        return results;
    }

    public ArmorRecord? GetById(string equipModelId)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT * FROM armor_parts WHERE equip_model_id = @id";
        cmd.Parameters.AddWithValue("@id", equipModelId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRecord(r) : null;
    }

    public int Count()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM armor_parts";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Clear()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM armor_parts";
        cmd.ExecuteNonQuery();
    }

    public List<string> GetAllSets()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT set_name FROM armor_parts WHERE set_name IS NOT NULL AND set_name != '' ORDER BY set_name";
        var sets = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) sets.Add(r.GetString(0));
        return sets;
    }

    private static ArmorRecord ReadRecord(SqliteDataReader r) => new(
        EquipModelId: r["equip_model_id"]?.ToString() ?? "",
        NameEn: r["name_en"]?.ToString() ?? "",
        NameEs: r["name_es"]?.ToString() ?? "",
        Category: r["category"]?.ToString() ?? "",
        IsAltered: Convert.ToBoolean(r["is_altered"]),
        Gender: r["gender"]?.ToString() ?? "",
        SetName: r["set_name"]?.ToString() ?? "",
        FileName: r["file_name"]?.ToString() ?? "",
        Weight: r["weight"] == DBNull.Value ? null : Convert.ToDouble(r["weight"]),
        // Conversión corregida a Convert.ToDouble para soportar los ratios de absorción de daño
        DefensePhys: r["defense_phys"] == DBNull.Value ? null : Convert.ToDouble(r["defense_phys"]),
        DefenseMagic: r["defense_magic"] == DBNull.Value ? null : Convert.ToDouble(r["defense_magic"]),
        DefenseFire: r["defense_fire"] == DBNull.Value ? null : Convert.ToDouble(r["defense_fire"]),
        DefenseLightning: r["defense_lightning"] == DBNull.Value ? null : Convert.ToDouble(r["defense_lightning"]),
        Poise: r["poise"] == DBNull.Value ? null : Convert.ToDouble(r["poise"]),
        IconId: r["icon_id"] == DBNull.Value ? null : Convert.ToInt32(r["icon_id"]),
        ThumbnailPath: r["thumbnail_path"]?.ToString()
    );

    public void Dispose() { _connection?.Close(); _connection?.Dispose(); }
}

using System;
using System.IO;
using Newtonsoft.Json;

namespace EldenRingArmorStudio.Core
{
    /// <summary>
    /// Configuración persistente de la aplicación (data/settings.json).
    /// Singleton accesible desde cualquier parte de la app.
    /// </summary>
    public class AppConfig
    {
        public static string Get(string key, string defaultValue = "")
        {
            switch (key)
            {
                case "modengine2.root_path":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "modengine2");
                case "project.parts_library_path":
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "parts");
                case "tools.witchybnd_path":
                    return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\tools\WitchyBND\WitchyBND.exe"));
                default:
                    return defaultValue;
            }
        }

        private static AppConfig _instance;
        public static AppConfig Instance => _instance ??= new AppConfig();

        private const string SettingsPath = "data/settings.json";

        // ── Secciones de configuración ────────────────────────────────────────────

        public ToolsSettings Tools { get; set; } = new();
        public ModEngine2Settings ModEngine2 { get; set; } = new();
        public ProjectSettings Project { get; set; } = new();
        public UiSettings Ui { get; set; } = new();

        // ── Load / Save ───────────────────────────────────────────────────────────

        public void Load()
        {
            try
            {
                Directory.CreateDirectory("data");
                if (!File.Exists(SettingsPath))
                {
                    Save();
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonConvert.DeserializeObject<AppConfig>(json);
                if (loaded != null)
                {
                    Tools = loaded.Tools ?? new();
                    ModEngine2 = loaded.ModEngine2 ?? new();
                    Project = loaded.Project ?? new();
                    Ui = loaded.Ui ?? new();
                }
            }
            catch (Exception)
            {
                // Fallback por si el JSON está corrupto o no se puede leer
                Tools = new();
                ModEngine2 = new();
                Project = new();
                Ui = new();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory("data");
                var json = JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception)
            {
                // Log o manejo de error silencioso en fallback
            }
        }
    }

    public class ToolsSettings
    {
        [JsonProperty("witchybnd_path")]
        public string WitchyBndPath { get; set; } = "tools/witchybnd/WitchyBND.exe";

        [JsonProperty("flver_editor_path")]
        public string FlverEditorPath { get; set; } = "tools/flver_editor/FLVER_Editor.exe";

        [JsonProperty("smithbox_path")]
        public string SmithboxPath { get; set; } = "tools/smithbox/Smithbox.exe";
    }

    public class ModEngine2Settings
    {
        [JsonProperty("root_path")]
        public string RootPath { get; set; } = "";
    }

    public class ProjectSettings
    {
        [JsonProperty("parts_library_path")]
        public string PartsLibraryPath { get; set; } = "";
    }

    public class UiSettings
    {
        [JsonProperty("dark_mode")]
        public bool DarkMode { get; set; } = true;

        [JsonProperty("language")]
        public string Language { get; set; } = "es";

        [JsonProperty("grid_size")]
        public int GridSize { get; set; } = 140;
    }
}
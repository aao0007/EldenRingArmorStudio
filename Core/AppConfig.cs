using System;
using System.IO;
using Newtonsoft.Json;

namespace EldenRingArmorStudio.Core
{
    public class AppConfig
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        private static AppConfig _instance;
        public static AppConfig Instance => _instance ??= new AppConfig();

        private const string SettingsPath = "data/settings.json";

        // ── Secciones ─────────────────────────────────────────────────────────
        public ToolsSettings Tools { get; set; } = new();
        public ModEngine2Settings ModEngine2 { get; set; } = new();
        public ProjectSettings Project { get; set; } = new();
        public UiSettings Ui { get; set; } = new();

        // ── Acceso rápido por clave (compatibilidad con código existente) ──────
        public static string Get(string key, string defaultValue = "")
        {
            var i = Instance;
            return key switch
            {
                "modengine2.root_path" => i.ModEngine2.RootPath,
                "project.parts_library_path" => i.Project.PartsLibraryPath,
                "tools.witchybnd_path" => i.Tools.WitchyBndPath,
                "tools.flver_editor_path" => i.Tools.FlverEditorPath,
                "tools.smithbox_path" => i.Tools.SmithboxPath,
                _ => defaultValue
            };
        }

        // ── Load / Save ───────────────────────────────────────────────────────
        public void Load()
        {
            try
            {
                Directory.CreateDirectory("data");
                if (!File.Exists(SettingsPath)) { Save(); return; }

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
            catch { Tools = new(); ModEngine2 = new(); Project = new(); Ui = new(); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory("data");
                File.WriteAllText(SettingsPath,
                    JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch { }
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
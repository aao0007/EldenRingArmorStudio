using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EldenRingArmorStudio.Core
{
    public class DuplicateResult
    {
        public bool Success { get; init; }
        public string SourceFile { get; init; }
        public string DestFile { get; init; }
        public string Error { get; init; }
        public string TargetId { get; init; }
    }

    public class DuplicatorService
    {
        /// <summary>
        /// Duplica sourceFile en baseDir\parts\ renombrándolo con cada targetId.
        ///
        /// Naming rules:
        ///   - Prefijo inferido del archivo origen (hd_m_, bd_m_, am_m_, lg_m_)
        ///   - La letra de género (m/f) viene del parámetro isFemale:
        ///       hd_m_XXXX  o  hd_f_XXXX
        ///   - Si withAltered=true se genera además una copia con sufijo _l:
        ///       hd_m_XXXX_l.partsbnd.dcx
        /// </summary>
        public async Task<List<DuplicateResult>> DuplicateToIdsAsync(
            string sourceFile,
            string destBaseDir,
            IEnumerable<string> targetIds,
            bool isFemale = false,
            bool withAltered = false,
            IProgress<string> progress = null)
        {
            var results = new List<DuplicateResult>();

            if (!File.Exists(sourceFile))
            {
                Log.Error("[Dup] Origen no encontrado: {F}", sourceFile);
                return results;
            }

            string destDir = ResolvePartsDir(destBaseDir);
            Directory.CreateDirectory(destDir);

            // Inferir la categoría del prefijo del archivo origen
            string srcName = Path.GetFileName(sourceFile).ToLower();
            string category = InferCategory(srcName);   // Head, Body, Arms, Legs
            string genderChar = isFemale ? "f" : "m";

            foreach (var rawId in targetIds)
            {
                string id = rawId.Trim();
                if (string.IsNullOrEmpty(id)) continue;

                // Nombre principal: hd_m_1840.partsbnd.dcx
                string mainName = BuildFileName(category, genderChar, id, false);
                await CopyOne(sourceFile, destDir, mainName, id, results, progress);

                // Versión alterada: hd_m_1840_l.partsbnd.dcx
                if (withAltered)
                {
                    string altName = BuildFileName(category, genderChar, id, true);
                    await CopyOne(sourceFile, destDir, altName, id + "_l", results, progress);
                }
            }

            return results;
        }

        private static Task CopyOne(
            string source, string destDir, string newName,
            string logId, List<DuplicateResult> results,
            IProgress<string> progress)
        {
            string dest = Path.Combine(destDir, newName);
            progress?.Report($"Copiando → {newName}");
            try
            {
                File.Copy(source, dest, overwrite: true);
                Log.Information("[Dup] {Src} → {Dst}", Path.GetFileName(source), newName);
                results.Add(new DuplicateResult
                {
                    Success = true,
                    SourceFile = source,
                    DestFile = dest,
                    TargetId = logId
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Dup] Error → {Dst}", dest);
                results.Add(new DuplicateResult
                {
                    Success = false,
                    SourceFile = source,
                    DestFile = dest,
                    TargetId = logId,
                    Error = ex.Message
                });
            }
            return Task.CompletedTask;
        }

        // ── Naming helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Construye el nombre de archivo final.
        /// Ej: category=Head, gender=m, id=1840, altered=false → "hd_m_1840.partsbnd.dcx"
        ///     category=Head, gender=m, id=1840, altered=true  → "hd_m_1840_l.partsbnd.dcx"
        /// </summary>
        private static string BuildFileName(
            string category, string gender, string id, bool altered)
        {
            string prefix = category switch
            {
                "Head" => "hd",
                "Body" => "bd",
                "Arms" => "am",
                "Legs" => "lg",
                _ => "hd"
            };
            string altSuffix = altered ? "_l" : "";
            return $"{prefix}_{gender}_{id}{altSuffix}.partsbnd.dcx";
        }

        private static string InferCategory(string fileName)
        {
            if (fileName.StartsWith("hd")) return "Head";
            if (fileName.StartsWith("bd")) return "Body";
            if (fileName.StartsWith("am")) return "Arms";
            if (fileName.StartsWith("lg")) return "Legs";
            return "Head";
        }

        // ── Carpeta parts ─────────────────────────────────────────────────────

        /// <summary>Devuelve baseDir\parts (sin crear nada).</summary>
        public static string ResolvePartsDir(string baseDir) =>
            Path.Combine(
                baseDir.TrimEnd(Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar),
                "parts");

        /// <summary>Elimina todos los .partsbnd.dcx de baseDir\parts.</summary>
        public static int ClearPartsFolder(string baseDir)
        {
            string partsDir = ResolvePartsDir(baseDir);
            if (!Directory.Exists(partsDir)) return 0;

            int count = 0;
            foreach (var f in Directory.GetFiles(partsDir, "*.partsbnd.dcx"))
            {
                try { File.Delete(f); count++; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[Dup] No se pudo eliminar {F}", f);
                }
            }
            Log.Information("[Dup] Vaciada: {Dir} ({N} archivos)", partsDir, count);
            return count;
        }
    }
}
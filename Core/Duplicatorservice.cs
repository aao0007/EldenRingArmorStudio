using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EldenRingArmorStudio.Core
{
    /// <summary>
    /// Resultado de una operación de duplicado.
    /// </summary>
    public class DuplicateResult
    {
        public bool Success { get; init; }
        public string SourceFile { get; init; }
        public string DestFile { get; init; }
        public string Error { get; init; }
        public string TargetId { get; init; }
    }

    /// <summary>
    /// Servicio que duplica un archivo .partsbnd.dcx cambiando su ID de modelo.
    /// Flujo: copia el archivo origen → renombra al nuevo ID.
    /// Opcionalmente desempaqueta+reempaqueta con WitchyBND si se necesita
    /// cambiar el ID interno del FLVER (actualmente hace solo renombrado de archivo,
    /// que es suficiente para que ModEngine2 lo sirva correctamente).
    /// </summary>
    public class DuplicatorService
    {
        private readonly string _witchyPath;

        public DuplicatorService(string witchyPath = null)
        {
            _witchyPath = witchyPath ?? AppConfig.Get("tools.witchybnd_path");
        }

        /// <summary>
        /// Duplica sourceFile hacia destDir con cada uno de los targetIds.
        /// targetId es el número numérico del modelo, ej "1840".
        /// El prefijo (hd_m_, bd_m_, etc.) se infiere del nombre del archivo origen.
        /// </summary>
        public async Task<List<DuplicateResult>> DuplicateToIdsAsync(
            string sourceFile,
            string destDir,
            IEnumerable<string> targetIds,
            IProgress<string> progress = null)
        {
            var results = new List<DuplicateResult>();

            if (!File.Exists(sourceFile))
            {
                Log.Error("[Dup] Archivo origen no encontrado: {F}", sourceFile);
                return results;
            }

            Directory.CreateDirectory(destDir);

            // Inferir prefijo del nombre origen: hd_m_1010.partsbnd.dcx → "hd_m_"
            string srcName = Path.GetFileName(sourceFile).ToLower();
            string prefix = InferPrefix(srcName);

            foreach (var rawId in targetIds)
            {
                string idTrimmed = rawId.Trim();
                if (string.IsNullOrEmpty(idTrimmed)) continue;

                // Formatear: "1840" → "1840" (sin padding extra, igual que los originales)
                string newName = $"{prefix}{idTrimmed}.partsbnd.dcx";
                string destFile = Path.Combine(destDir, newName);

                try
                {
                    progress?.Report($"Copiando → {newName}");
                    File.Copy(sourceFile, destFile, overwrite: true);

                    Log.Information("[Dup] {Src} → {Dst}", Path.GetFileName(sourceFile), newName);
                    results.Add(new DuplicateResult
                    {
                        Success = true,
                        SourceFile = sourceFile,
                        DestFile = destFile,
                        TargetId = idTrimmed
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Dup] Error copiando a {Dst}", destFile);
                    results.Add(new DuplicateResult
                    {
                        Success = false,
                        SourceFile = sourceFile,
                        DestFile = destFile,
                        TargetId = idTrimmed,
                        Error = ex.Message
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Infiere el prefijo de nombre a partir del nombre del archivo.
        /// hd_m_1010.partsbnd.dcx → "hd_m_"
        /// AM_M_1360.partsbnd.dcx → "am_m_"
        /// </summary>
        private static string InferPrefix(string fileName)
        {
            fileName = fileName.ToLower();
            foreach (var p in new[] { "hd_m_", "bd_m_", "am_m_", "lg_m_" })
                if (fileName.StartsWith(p)) return p;
            // Fallback: usar todo hasta el primer dígito
            int firstDigit = fileName.IndexOfAny("0123456789".ToCharArray());
            return firstDigit > 0 ? fileName[..firstDigit] : "";
        }

        /// <summary>
        /// Devuelve la lista de subcarpetas del directorio dado, más el propio directorio.
        /// Útil para poblar el combo "Pack / subcarpeta".
        /// </summary>
        public static List<string> GetPackSubfolders(string modPartsDir)
        {
            var list = new List<string>();
            if (!Directory.Exists(modPartsDir)) return list;

            list.Add(modPartsDir); // La propia carpeta parts

            try
            {
                foreach (var d in Directory.GetDirectories(modPartsDir, "*", SearchOption.AllDirectories))
                    list.Add(d);
            }
            catch { }

            return list;
        }
    }
}
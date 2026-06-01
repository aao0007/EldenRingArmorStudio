using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats; // Descomentado para usar la librería real

namespace EldenRingArmorStudio.UI.Viewer3D;

/// <summary>
/// Carga un archivo FLVER2 (Elden Ring) usando SoulsFormats y extrae las texturas del TPF.
/// </summary>
public static class FlverLoader
{
    public static FlverModel LoadFromDirectory(string unpackedDir)
    {
        var flverFiles = Directory.GetFiles(unpackedDir, "*.flver", SearchOption.AllDirectories);
        if (flverFiles.Length == 0)
        {
            Log.Warning("No se encontró .flver en {Dir}", unpackedDir);
            return null;
        }

        var flverPath = flverFiles[0];
        Log.Information("Cargando FLVER: {File}", Path.GetFileName(flverPath));

        // Construye el mapa de texturas leyendo los TPF y DDS de la carpeta
        var textureMap = BuildTextureMap(unpackedDir);

        return LoadFlver(flverPath, textureMap);
    }

    private static FlverModel LoadFlver(string flverPath, Dictionary<string, byte[]> textureMap)
    {
        try
        {
            // Usar SoulsFormats directamente
            var flver = FLVER2.Read(flverPath);
            return ParseWithSoulsFormats(flver, textureMap, flverPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando FLVER: {File}", flverPath);
            return null;
        }
    }

    private static FlverModel ParseWithSoulsFormats(FLVER2 flver, Dictionary<string, byte[]> textureMap, string filePath)
    {
        var model = new FlverModel { FilePath = filePath };

        // ── 1. Cargar Materiales y asociar Texturas ────────────────────────────
        foreach (var mat in flver.Materials)
        {
            var matData = new FlverMaterialData { Name = mat.Name, MtdPath = mat.MTD };

            foreach (var tex in mat.Textures)
            {
                if (string.IsNullOrEmpty(tex.Path)) continue;

                var texName = Path.GetFileNameWithoutExtension(tex.Path).ToLower();
                var texType = tex.Type.ToLower();

                // Convención Elden Ring: buscar Albedo (_a), Normal (_n), Specular (_s)
                if (texType.Contains("albedo") || texType.Contains("diffuse") || texType.EndsWith("_a"))
                {
                    matData.AlbedoPath = tex.Path;
                    matData.AlbedoData = FindTextureData(texName, textureMap);
                }
                else if (texType.Contains("normal") || texType.Contains("bump") || texType.EndsWith("_n"))
                {
                    matData.NormalPath = tex.Path;
                    matData.NormalData = FindTextureData(texName, textureMap);
                }
                else if (texType.Contains("specular") || texType.Contains("shininess") || texType.EndsWith("_s"))
                {
                    matData.SpecularPath = tex.Path;
                    matData.SpecularData = FindTextureData(texName, textureMap);
                }
            }
            model.Materials.Add(matData);
        }

        // ── 2. Cargar Meshes (Geometría) ─────────────────────────────────────────
        foreach (var mesh in flver.Meshes)
        {
            var meshData = new FlverMeshData
            {
                MaterialIndex = mesh.MaterialIndex,
                MaterialName = mesh.MaterialIndex < flver.Materials.Count ? flver.Materials[mesh.MaterialIndex].Name : ""
            };

            var verts = new FlverVertex[mesh.Vertices.Count];
            for (int vi = 0; vi < mesh.Vertices.Count; vi++)
            {
                var v = mesh.Vertices[vi];

                // Forzamos el color de vértice a blanco (1f) para que no apague la textura
                verts[vi] = new FlverVertex
                {
                    X = v.Position.X,
                    Y = v.Position.Y,
                    Z = v.Position.Z,

                    NX = v.Normal.X,
                    NY = v.Normal.Y,
                    NZ = v.Normal.Z,

                    // Si el modelo tiene UVs, asignamos la principal (UV set 0)
                    U = (v.UVs != null && v.UVs.Count > 0) ? v.UVs[0].X : 0f,
                    V = (v.UVs != null && v.UVs.Count > 0) ? 1.0f - v.UVs[0].Y : 0f,

                    R = 1f,
                    G = 1f,
                    B = 1f,
                    A = 1f
                };
            }
            meshData.Vertices = verts;

            // Leer índices (Triangulación) del FaceSet principal
            var mainFaceSet = mesh.FaceSets.FirstOrDefault(fs => fs.Flags == FLVER2.FaceSet.FSFlags.None)
                              ?? mesh.FaceSets.FirstOrDefault();

            if (mainFaceSet != null)
            {
                // SUSTITUYE 'mesh.Vertices.Count' por 'false'
                var rawIdx = mainFaceSet.Triangulate(false);
                meshData.Indices = rawIdx.Select(i => (uint)i).ToArray();
            }

            if (meshData.Vertices.Length > 0 && meshData.Indices.Length > 0)
                model.Meshes.Add(meshData);
        }

        NormalizeModel(model);
        return model;
    }

    private static void NormalizeModel(FlverModel model)
    {
        if (model.Meshes.Count == 0) return;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var m in model.Meshes)
            foreach (var v in m.Vertices)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }

        // Centrar y escalar
        float cx = (minX + maxX) * 0.5f;
        float cy = (minY + maxY) * 0.5f;
        float cz = (minZ + maxZ) * 0.5f;
        float ext = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        float scale = ext > 0 ? 1.0f / (ext * 0.5f) : 1f;

        foreach (var m in model.Meshes)
            for (int i = 0; i < m.Vertices.Length; i++)
            {
                m.Vertices[i].X = (m.Vertices[i].X - cx) * scale;
                m.Vertices[i].Y = (m.Vertices[i].Y - cy) * scale;
                m.Vertices[i].Z = (m.Vertices[i].Z - cz) * scale;
            }
    }

    // ── 3. Lector de Texturas TPF / DDS ──────────────────────────────────────

    private static Dictionary<string, byte[]> BuildTextureMap(string dir)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        // 1. Busca DDS sueltos
        foreach (var dds in Directory.GetFiles(dir, "*.dds", SearchOption.AllDirectories))
        {
            var key = Path.GetFileNameWithoutExtension(dds).ToLower();
            map.TryAdd(key, File.ReadAllBytes(dds));
        }

        // 2. Busca TPF y los abre usando SoulsFormats
        foreach (var tpf in Directory.GetFiles(dir, "*.tpf", SearchOption.AllDirectories))
        {
            try
            {
                var tpfFile = TPF.Read(tpf);
                foreach (var tex in tpfFile.Textures)
                {
                    var key = Path.GetFileNameWithoutExtension(tex.Name).ToLower();
                    map.TryAdd(key, tex.Bytes);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error extrayendo TPF: {File}", tpf);
            }
        }

        return map;
    }

    private static byte[] FindTextureData(string texName, Dictionary<string, byte[]> map)
    {
        texName = texName.ToLower();

        // Limpieza de extensiones si viene con ".tif" en el modelo
        if (texName.EndsWith(".tif"))
            texName = texName.Replace(".tif", "");

        if (map.TryGetValue(texName, out var data))
            return data;

        // Intentar con sufijos comunes en caso de que no haga un match exacto
        foreach (var suffix in new[] { "_a", "_n", "_s" })
        {
            if (map.TryGetValue(texName + suffix, out data)) return data;
        }

        return null;
    }
}
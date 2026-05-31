using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoulsFormats;

namespace EldenRingArmorStudio.UI.Viewer3D;

public static class FlverLoader
{
    public static FlverModel LoadFromDirectory(string unpackedDir)
    {
        var flverFiles = Directory.GetFiles(unpackedDir, "*.flver", SearchOption.AllDirectories);
        if (flverFiles.Length == 0)
        {
            Log.Warning("[FlverLoader] No se encontró .flver en {Dir}", unpackedDir);
            return null;
        }

        var flverPath = flverFiles[0];
        Log.Information("[FlverLoader] Cargando: {File}", Path.GetFileName(flverPath));

        var textureMap = BuildTextureMap(unpackedDir);
        Log.Information("[FlverLoader] Texturas encontradas: {N}", textureMap.Count);

        return LoadFlver(flverPath, textureMap);
    }

    private static FlverModel LoadFlver(string flverPath, Dictionary<string, byte[]> textureMap)
    {
        try
        {
            var flver = FLVER2.Read(flverPath);
            return Parse(flver, textureMap, flverPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[FlverLoader] Error leyendo FLVER: {F}", flverPath);
            return null;
        }
    }

    private static FlverModel Parse(FLVER2 flver, Dictionary<string, byte[]> textureMap, string filePath)
    {
        var model = new FlverModel { FilePath = filePath };

        // ── Materiales ────────────────────────────────────────────────────────
        foreach (var mat in flver.Materials)
        {
            var matData = new FlverMaterialData { Name = mat.Name, MtdPath = mat.MTD };

            foreach (var tex in mat.Textures)
            {
                if (string.IsNullOrEmpty(tex.Path)) continue;

                string texName = Path.GetFileNameWithoutExtension(tex.Path).ToLower();
                string texType = tex.Type.ToLower();

                if (texType.Contains("albedo") || texType.Contains("diffuse") || texType.EndsWith("_a"))
                {
                    matData.AlbedoPath = tex.Path;
                    matData.AlbedoData = FindTexture(texName, textureMap);
                    if (matData.AlbedoData != null)
                        Log.Information("[FlverLoader] Albedo OK: {T}", texName);
                    else
                        Log.Warning("[FlverLoader] Albedo NO encontrado: {T}", texName);
                }
                else if (texType.Contains("normal") || texType.Contains("bump") || texType.EndsWith("_n"))
                {
                    matData.NormalPath = tex.Path;
                    matData.NormalData = FindTexture(texName, textureMap);
                }
                else if (texType.Contains("specular") || texType.Contains("shininess") || texType.EndsWith("_s"))
                {
                    matData.SpecularPath = tex.Path;
                    matData.SpecularData = FindTexture(texName, textureMap);
                }
            }
            model.Materials.Add(matData);
        }

        // ── Mallas ────────────────────────────────────────────────────────────
        foreach (var mesh in flver.Meshes)
        {
            var meshData = new FlverMeshData
            {
                MaterialIndex = mesh.MaterialIndex,
                MaterialName = mesh.MaterialIndex < flver.Materials.Count
                                    ? flver.Materials[mesh.MaterialIndex].Name : ""
            };

            var verts = new FlverVertex[mesh.Vertices.Count];
            for (int vi = 0; vi < mesh.Vertices.Count; vi++)
            {
                var v = mesh.Vertices[vi];

                // NO invertir V aquí — el vertex shader hace vec2(aUV.x, 1.0 - aUV.y)
                float u = v.UVs != null && v.UVs.Count > 0 ? v.UVs[0].X : 0f;
                float vv = v.UVs != null && v.UVs.Count > 0 ? v.UVs[0].Y : 0f;

                verts[vi] = new FlverVertex
                {
                    X = v.Position.X,
                    Y = v.Position.Y,
                    Z = v.Position.Z,
                    NX = v.Normal.X,
                    NY = v.Normal.Y,
                    NZ = v.Normal.Z,
                    U = u,
                    V = vv,
                    R = 1f,
                    G = 1f,
                    B = 1f,
                    A = 1f
                };
            }
            meshData.Vertices = verts;

            var faceSet = mesh.FaceSets
                .FirstOrDefault(fs => fs.Flags == FLVER2.FaceSet.FSFlags.None)
                ?? mesh.FaceSets.FirstOrDefault();

            if (faceSet != null)
                meshData.Indices = faceSet.Triangulate(false).Select(i => (uint)i).ToArray();

            if (meshData.Vertices.Length > 0 && meshData.Indices.Length > 0)
                model.Meshes.Add(meshData);
        }

        NormalizeModel(model);
        Log.Information("[FlverLoader] OK — {V} verts, {T} tris, {M} materiales",
            model.TotalVertices, model.TotalTriangles, model.Materials.Count);
        return model;
    }

    private static void NormalizeModel(FlverModel model)
    {
        if (model.Meshes.Count == 0) return;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var m in model.Meshes)
            foreach (var v in m.Vertices)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
                if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
            }

        float cx = (minX + maxX) * .5f;
        float cy = (minY + maxY) * .5f;
        float cz = (minZ + maxZ) * .5f;
        float ext = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        float scale = ext > 0 ? 1f / (ext * .5f) : 1f;

        foreach (var m in model.Meshes)
            for (int i = 0; i < m.Vertices.Length; i++)
            {
                m.Vertices[i].X = (m.Vertices[i].X - cx) * scale;
                m.Vertices[i].Y = (m.Vertices[i].Y - cy) * scale;
                m.Vertices[i].Z = (m.Vertices[i].Z - cz) * scale;
            }
    }

    // ── Texturas ──────────────────────────────────────────────────────────────

    private static Dictionary<string, byte[]> BuildTextureMap(string dir)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var dds in Directory.GetFiles(dir, "*.dds", SearchOption.AllDirectories))
            map.TryAdd(Path.GetFileNameWithoutExtension(dds).ToLower(),
                       File.ReadAllBytes(dds));

        foreach (var tpf in Directory.GetFiles(dir, "*.tpf", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var tex in TPF.Read(tpf).Textures)
                    map.TryAdd(Path.GetFileNameWithoutExtension(tex.Name).ToLower(),
                               tex.Bytes);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FlverLoader] Error leyendo TPF: {F}", tpf);
            }
        }

        return map;
    }

    private static byte[] FindTexture(string name, Dictionary<string, byte[]> map)
    {
        name = name.ToLower().Replace(".tif", "");
        if (map.TryGetValue(name, out var d)) return d;
        foreach (var s in new[] { "_a", "_n", "_s" })
            if (map.TryGetValue(name + s, out d)) return d;
        return null;
    }
}
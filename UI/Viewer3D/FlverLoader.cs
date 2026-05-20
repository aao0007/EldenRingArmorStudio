using Microsoft.VisualBasic.Logging;
using Serilog;
using System.IO;
// SoulsFormats se referencia como proyecto local o NuGet
// using SoulsFormats;

namespace EldenRingArmorStudio.UI.Viewer3D;

/// <summary>
/// Carga un archivo FLVER2 (Elden Ring) usando SoulsFormats y produce
/// un FlverModel con geometría completa + texturas DDS listas para la GPU.
///
/// Flujo:
///   1. WitchyBND desempaqueta el .partsbnd.dcx → carpeta con .flver + .tpf / .tpfbhd
///   2. FlverLoader.LoadFromDirectory() encuentra el .flver y los archivos de textura
///   3. Parsea el FLVER con SoulsFormats → extrae verts, normales, UVs por sub-malla
///   4. Por cada material, busca la textura correspondiente y carga los bytes DDS
///   5. Devuelve un FlverModel listo para subir a OpenGL
/// </summary>
public static class FlverLoader
{
    // ── Entry points ──────────────────────────────────────────────────────────

    /// <summary>
    /// Carga un modelo desde la carpeta donde WitchyBND desempaquetó el BND.
    /// </summary>
    public static FlverModel? LoadFromDirectory(string unpackedDir)
    {
        var flverFiles = Directory.GetFiles(unpackedDir, "*.flver", SearchOption.AllDirectories);
        if (flverFiles.Length == 0)
        {
            Log.Warning("No se encontró .flver en {Dir}", unpackedDir);
            return null;
        }

        var flverPath = flverFiles[0];
        Log.Information("Cargando FLVER: {File}", Path.GetFileName(flverPath));

        // Buscar archivos de textura en la misma carpeta
        var textureMap = BuildTextureMap(unpackedDir);

        return LoadFlver(flverPath, textureMap);
    }

    // ── FLVER parsing ─────────────────────────────────────────────────────────

    private static FlverModel? LoadFlver(string flverPath, Dictionary<string, byte[]> textureMap)
    {
        try
        {
#if SOULSTRUCT_AVAILABLE
            // ── Rama con SoulsFormats disponible ─────────────────────────────
            var flver = SoulsFormats.FLVER2.Read(flverPath);
            return ParseWithSoulsFormats(flver, textureMap, flverPath);
#else
            // ── Rama fallback: parser binario nativo ──────────────────────────
            return ParseNative(flverPath, textureMap);
#endif
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando FLVER: {File}", flverPath);
            return null;
        }
    }

#if SOULSTRUCT_AVAILABLE
    /// <summary>
    /// Parseo completo usando SoulsFormats — máxima fidelidad, incluye todas las
    /// sub-mallas, UVs correctos, normales, y asociación material→textura exacta.
    /// </summary>
    private static FlverModel ParseWithSoulsFormats(
        SoulsFormats.FLVER2 flver,
        Dictionary<string, byte[]> textureMap,
        string filePath)
    {
        var model = new FlverModel { FilePath = filePath };

        // ── Materiales ────────────────────────────────────────────────────────
        foreach (var mat in flver.Materials)
        {
            var matData = new FlverMaterialData
            {
                Name    = mat.Name,
                MtdPath = mat.MTD,
            };

            // Buscar texturas asociadas al material
            foreach (var tex in mat.Textures)
            {
                var texName = Path.GetFileNameWithoutExtension(tex.Path).ToLower();
                var texType = tex.Type.ToLower();

                // Convención Elden Ring:
                //   _a.tpf → albedo/diffuse
                //   _n.tpf → normal map
                //   _s.tpf → specular/roughness
                if (texType.Contains("albedo") || texType.Contains("diffuse") || texType.EndsWith("_a"))
                {
                    matData.AlbedoPath = tex.Path;
                    matData.AlbedoData = FindTextureData(texName, textureMap);
                }
                else if (texType.Contains("normal") || texType.EndsWith("_n"))
                {
                    matData.NormalPath = tex.Path;
                    matData.NormalData = FindTextureData(texName, textureMap);
                }
                else if (texType.Contains("specular") || texType.EndsWith("_s"))
                {
                    matData.SpecularPath = tex.Path;
                    matData.SpecularData = FindTextureData(texName, textureMap);
                }
            }

            model.Materials.Add(matData);
        }

        // ── Meshes ────────────────────────────────────────────────────────────
        foreach (var mesh in flver.Meshes)
        {
            var layout = flver.BufferLayouts[mesh.VertexBuffers[0].LayoutIndex];
            var meshData = new FlverMeshData
            {
                MaterialIndex = mesh.MaterialIndex,
                MaterialName  = mesh.MaterialIndex < flver.Materials.Count
                    ? flver.Materials[mesh.MaterialIndex].Name : "",
            };

            // Leer vértices — SoulsFormats ya los decodifica correctamente
            var verts = new FlverVertex[mesh.Vertices.Count];
            for (int vi = 0; vi < mesh.Vertices.Count; vi++)
            {
                var v = mesh.Vertices[vi];
                verts[vi] = new FlverVertex
                {
                    X  = v.Position.X,
                    Y  = v.Position.Y,
                    Z  = v.Position.Z,
                    NX = v.Normal.X,
                    NY = v.Normal.Y,
                    NZ = v.Normal.Z,
                    U  = v.UVs.Count > 0 ? v.UVs[0].X : 0f,
                    V  = v.UVs.Count > 0 ? v.UVs[0].Y : 0f,
                    R  = v.Colors.Count > 0 ? v.Colors[0].R / 255f : 1f,
                    G  = v.Colors.Count > 0 ? v.Colors[0].G / 255f : 1f,
                    B  = v.Colors.Count > 0 ? v.Colors[0].B / 255f : 1f,
                    A  = v.Colors.Count > 0 ? v.Colors[0].A / 255f : 1f,
                };
            }
            meshData.Vertices = verts;

            // Leer índices — usar LOD 0 (el face set con flags == 0)
            var mainFaceSet = mesh.FaceSets
                .FirstOrDefault(fs => fs.Flags == SoulsFormats.FLVER2.FaceSet.FSFlags.None)
                ?? mesh.FaceSets.FirstOrDefault();

            if (mainFaceSet != null)
            {
                var rawIdx = mainFaceSet.Triangulate(mesh.Vertices.Count);
                meshData.Indices = rawIdx.Select(i => (uint)i).ToArray();
            }

            if (meshData.Vertices.Length > 0)
                model.Meshes.Add(meshData);
        }

        Log.Information("FLVER cargado con SoulsFormats: {V} verts, {T} tris, {M} materiales",
            model.TotalVertices, model.TotalTriangles, model.Materials.Count);
        return model;
    }
#endif

    /// <summary>
    /// Parser binario nativo de FLVER2 (fallback sin SoulsFormats).
    /// Lee posición, normal y UV del primer buffer de cada sub-malla.
    /// </summary>
    private static FlverModel? ParseNative(string flverPath, Dictionary<string, byte[]> textureMap)
    {
        var data = File.ReadAllBytes(flverPath);
        if (data.Length < 0x80 || data[0] != 'F' || data[1] != 'L' || data[2] != 'V') return null;

        var model = new FlverModel { FilePath = flverPath };

        // Header offsets
        uint dataOff = BitConverter.ToUInt32(data, 0x0C);
        int dummyCnt = BitConverter.ToInt32(data, 0x14);
        int matCnt = BitConverter.ToInt32(data, 0x18);
        int boneCnt = BitConverter.ToInt32(data, 0x1C);
        int meshCnt = BitConverter.ToInt32(data, 0x20);
        int vbCnt = BitConverter.ToInt32(data, 0x24);
        int fsCnt = BitConverter.ToInt32(data, 0x50);
        int blCnt = BitConverter.ToInt32(data, 0x54);

        if (meshCnt <= 0) return null;

        // Tabla offsets
        int offMat = 0x80 + dummyCnt * 64;
        int offBone = offMat + matCnt * 32;
        int offMesh = offBone + boneCnt * 128;
        int offFs = offMesh + meshCnt * 48;
        int offVb = offFs + fsCnt * 32;
        int offBl = offVb + vbCnt * 32;

        // BufferLayouts
        var layouts = ReadLayouts(data, offBl, blCnt);

        // VBuffers metadata
        var vbs = ReadVBuffers(data, offVb, vbCnt);

        // FaceSets metadata
        var fss = ReadFaceSets(data, offFs, fsCnt);

        // Procesar meshes
        for (int mi = 0; mi < meshCnt; mi++)
        {
            int mb = offMesh + mi * 48;
            if (mb + 48 > data.Length) break;

            int nFs = BitConverter.ToInt32(data, mb + 0x20);
            int oFs = BitConverter.ToInt32(data, mb + 0x24);
            int nVb = BitConverter.ToInt32(data, mb + 0x28);
            int oVb = BitConverter.ToInt32(data, mb + 0x2C);

            var vbIds = ReadIntList(data, oVb, nVb);
            var fsIds = ReadIntList(data, oFs, nFs);

            if (vbIds.Count == 0) continue;

            var vb = vbs[vbIds[0]];
            if (vb.LayoutIndex < 0 || vb.LayoutIndex >= layouts.Count) continue;

            var layout = layouts[vb.LayoutIndex];
            var verts = ExtractVertices(data, vb, layout, (int)dataOff);
            if (verts.Length == 0) continue;

            var idx = ExtractIndices(data, fss, fsIds, verts.Length, (int)dataOff);

            var meshData = new FlverMeshData { Vertices = verts, Indices = idx };
            model.Meshes.Add(meshData);
        }

        // Normalizar posiciones para el viewer
        NormalizeModel(model);

        Log.Information("FLVER nativo: {V} verts, {T} tris",
            model.TotalVertices, model.TotalTriangles);
        return model;
    }

    // ── Layout / Buffer readers ───────────────────────────────────────────────

    private record MemberInfo(int Semantic, int Type, int Size);
    private record LayoutInfo(List<MemberInfo> Members, int Stride);
    private record VBufferInfo(int LayoutIndex, int VertexSize, int VertexCount, int BufferOffset);
    private record FaceSetInfo(bool IsStrip, int IndexCount, int IndicesOffset, int IndexSize);

    private static List<LayoutInfo> ReadLayouts(byte[] data, int offset, int count)
    {
        var layouts = new List<LayoutInfo>();
        for (int i = 0; i < count; i++)
        {
            int hoff = offset + i * 16;
            if (hoff + 16 > data.Length) break;
            int memberCnt = BitConverter.ToInt32(data, hoff);
            int memberOff = BitConverter.ToInt32(data, hoff + 0x0C);
            var members = new List<MemberInfo>();
            for (int j = 0; j < memberCnt; j++)
            {
                int mo = memberOff + j * 16;
                if (mo + 16 > data.Length) break;
                int type = BitConverter.ToInt32(data, mo + 0x08);
                int sem = BitConverter.ToInt32(data, mo + 0x0C);
                members.Add(new(sem, type, TypeSize(type)));
            }
            layouts.Add(new(members, members.Sum(m => m.Size)));
        }
        return layouts;
    }

    private static List<VBufferInfo> ReadVBuffers(byte[] data, int offset, int count)
    {
        var list = new List<VBufferInfo>();
        for (int i = 0; i < count; i++)
        {
            int b = offset + i * 32;
            if (b + 32 > data.Length) break;
            list.Add(new(
                BitConverter.ToInt32(data, b + 0x04),
                BitConverter.ToInt32(data, b + 0x08),
                BitConverter.ToInt32(data, b + 0x0C),
                BitConverter.ToInt32(data, b + 0x1C)));
        }
        return list;
    }

    private static List<FaceSetInfo> ReadFaceSets(byte[] data, int offset, int count)
    {
        var list = new List<FaceSetInfo>();
        for (int i = 0; i < count; i++)
        {
            int b = offset + i * 32;
            if (b + 32 > data.Length) break;
            list.Add(new(
                data[b + 0x04] != 0,
                BitConverter.ToInt32(data, b + 0x08),
                BitConverter.ToInt32(data, b + 0x0C),
                BitConverter.ToInt32(data, b + 0x18)));
        }
        return list;
    }

    private static List<int> ReadIntList(byte[] data, int offset, int count)
    {
        var list = new List<int>();
        for (int k = 0; k < count; k++)
        {
            int o = offset + k * 4;
            if (o + 4 > data.Length) break;
            list.Add(BitConverter.ToInt32(data, o));
        }
        return list;
    }

    // ── Vertex extraction ─────────────────────────────────────────────────────

    // Semantic IDs de SoulsFormats
    private const int SEM_POSITION = 0;
    private const int SEM_NORMAL = 3;
    private const int SEM_UV = 5;
    private const int SEM_COLOR = 10;

    private static FlverVertex[] ExtractVertices(
        byte[] data, VBufferInfo vb, LayoutInfo layout, int dataOff)
    {
        int stride = vb.VertexSize > 0 ? vb.VertexSize : layout.Stride;
        int vcnt = vb.VertexCount;
        int rawOff = dataOff + vb.BufferOffset;
        if (stride <= 0 || vcnt <= 0) return [];

        int available = (data.Length - rawOff) / stride;
        if (available < vcnt) vcnt = available;
        if (vcnt <= 0) return [];

        var verts = new FlverVertex[vcnt];

        for (int vi = 0; vi < vcnt; vi++)
        {
            int vbase = rawOff + vi * stride;
            int cursor = vbase;
            var vert = new FlverVertex { R = 1, G = 1, B = 1, A = 1 };

            foreach (var m in layout.Members)
            {
                if (cursor + m.Size > data.Length) break;

                switch (m.Semantic)
                {
                    case SEM_POSITION when m.Type == 0x02:  // Float3
                        vert.X = BitConverter.ToSingle(data, cursor);
                        vert.Y = BitConverter.ToSingle(data, cursor + 4);
                        vert.Z = BitConverter.ToSingle(data, cursor + 8);
                        break;

                    case SEM_NORMAL:
                        (vert.NX, vert.NY, vert.NZ) = DecodeNormal(data, cursor, m.Type);
                        break;

                    case SEM_UV:
                        (vert.U, vert.V) = DecodeUV(data, cursor, m.Type);
                        break;

                    case SEM_COLOR:
                        vert.R = data[cursor + 0] / 255f;
                        vert.G = data[cursor + 1] / 255f;
                        vert.B = data[cursor + 2] / 255f;
                        vert.A = data[cursor + 3] / 255f;
                        break;
                }
                cursor += m.Size;
            }

            // Validar posición
            if (!float.IsFinite(vert.X)) vert.X = 0;
            if (!float.IsFinite(vert.Y)) vert.Y = 0;
            if (!float.IsFinite(vert.Z)) vert.Z = 0;

            verts[vi] = vert;
        }
        return verts;
    }

    private static (float nx, float ny, float nz) DecodeNormal(byte[] d, int o, int type)
    {
        return type switch
        {
            0x02 => (BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8)),
            0x10 => ((sbyte)d[o] / 127f, (sbyte)d[o + 1] / 127f, (sbyte)d[o + 2] / 127f),
            0x11 => (d[o] / 255f * 2 - 1, d[o + 1] / 255f * 2 - 1, d[o + 2] / 255f * 2 - 1),
            _ => (0, 1, 0),
        };
    }

    private static (float u, float v) DecodeUV(byte[] d, int o, int type)
    {
        return type switch
        {
            0x01 => (BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4)),
            0x15 => (BitConverter.ToInt16(d, o) / 2048f, BitConverter.ToInt16(d, o + 2) / 2048f),
            0x16 => (BitConverter.ToInt16(d, o) / 2048f, BitConverter.ToInt16(d, o + 2) / 2048f),
            0x1A => (HalfToFloat(BitConverter.ToUInt16(d, o)), HalfToFloat(BitConverter.ToUInt16(d, o + 2))),
            _ => (0, 0),
        };
    }

    private static float HalfToFloat(ushort h) => (float)(System.Half)h;

    // ── Index extraction ──────────────────────────────────────────────────────

    private static uint[] ExtractIndices(
        byte[] data, List<FaceSetInfo> fss, List<int> fsIds, int vertCount, int dataOff)
    {
        // Buscar LOD0 (el face set con índice más pequeño, asumiendo que el primero es LOD0)
        FaceSetInfo? chosen = null;
        foreach (var fsi in fsIds)
        {
            if (fsi < 0 || fsi >= fss.Count) continue;
            chosen ??= fss[fsi];
        }
        if (chosen is null || chosen.IndexCount == 0) return GenerateSequential(vertCount);

        int iAbs = dataOff + chosen.IndicesOffset;
        int iCnt = chosen.IndexCount;
        bool u32 = chosen.IndexSize == 32;
        int bSz = iCnt * (u32 ? 4 : 2);
        const uint SENT16 = 0xFFFF, SENT32 = 0xFFFFFFFF;

        if (iAbs + bSz > data.Length) return GenerateSequential(vertCount);

        var result = new List<uint>(iCnt);

        if (chosen.IsStrip)
        {
            bool flip = false;
            for (int i = 0; i < iCnt - 2;)
            {
                uint a = ReadIdx(data, iAbs, i, u32);
                uint b = ReadIdx(data, iAbs, i + 1, u32);
                uint c = ReadIdx(data, iAbs, i + 2, u32);
                uint sent = u32 ? SENT32 : SENT16;
                if (a == sent || b == sent || c == sent) { i += 3; flip = false; continue; }
                if (a < (uint)vertCount && b < (uint)vertCount && c < (uint)vertCount && a != b && b != c && a != c)
                {
                    if (!flip) { result.Add(a); result.Add(b); result.Add(c); }
                    else { result.Add(a); result.Add(c); result.Add(b); }
                }
                flip = !flip; i++;
            }
        }
        else
        {
            for (int i = 0; i + 2 < iCnt; i += 3)
            {
                uint a = ReadIdx(data, iAbs, i, u32);
                uint b = ReadIdx(data, iAbs, i + 1, u32);
                uint c = ReadIdx(data, iAbs, i + 2, u32);
                if (a < (uint)vertCount && b < (uint)vertCount && c < (uint)vertCount && a != b && b != c && a != c)
                { result.Add(a); result.Add(b); result.Add(c); }
            }
        }

        return result.Count > 0 ? result.ToArray() : GenerateSequential(vertCount);
    }

    private static uint ReadIdx(byte[] d, int baseOff, int i, bool u32) =>
        u32 ? BitConverter.ToUInt32(d, baseOff + i * 4)
            : BitConverter.ToUInt16(d, baseOff + i * 2);

    private static uint[] GenerateSequential(int n)
    {
        int triN = (n / 3) * 3;
        var idx = new uint[triN];
        for (int i = 0; i < triN; i++) idx[i] = (uint)i;
        return idx;
    }

    // ── Type sizes ────────────────────────────────────────────────────────────

    private static int TypeSize(int t) => t switch
    {
        0x00 => 4,
        0x01 => 8,
        0x02 => 12,
        0x03 => 16,
        0x10 => 4,
        0x11 => 4,
        0x12 => 4,
        0x13 => 8,
        0x14 => 4,
        0x15 => 4,
        0x16 => 8,
        0x18 => 8,
        0x1A => 4,
        0x1C => 8,
        0x1E => 12,
        0x2E => 4,
        0x2F => 4,
        0xFF => 0,
        _ => 4,
    };

    // ── Normalization ─────────────────────────────────────────────────────────

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

        // Sentar sobre el grid
        float newMinY = model.Meshes.SelectMany(m => m.Vertices).Min(v => v.Y);
        foreach (var m in model.Meshes)
            for (int i = 0; i < m.Vertices.Length; i++)
                m.Vertices[i].Y -= newMinY;
    }

    // ── Texture map builder ───────────────────────────────────────────────────

    private static Dictionary<string, byte[]> BuildTextureMap(string dir)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        // Buscar archivos .dds directamente extraídos
        foreach (var dds in Directory.GetFiles(dir, "*.dds", SearchOption.AllDirectories))
        {
            var key = Path.GetFileNameWithoutExtension(dds);
            map.TryAdd(key, File.ReadAllBytes(dds));
        }

        // Buscar archivos .tpf extraídos por WitchyBND (contienen DDS internos)
        foreach (var tpf in Directory.GetFiles(dir, "*.tpf", SearchOption.AllDirectories))
        {
            try { ExtractTpfTextures(tpf, map); }
            catch (Exception ex) { Log.Warning(ex, "Error extrayendo TPF: {File}", tpf); }
        }

        Log.Debug("TextureMap: {N} texturas encontradas en {Dir}", map.Count, dir);
        return map;
    }

    private static void ExtractTpfTextures(string tpfPath, Dictionary<string, byte[]> map)
    {
#if SOULSTRUCT_AVAILABLE
        var tpf = SoulsFormats.TPF.Read(tpfPath);
        foreach (var tex in tpf.Textures)
        {
            var key = Path.GetFileNameWithoutExtension(tex.Name);
            map.TryAdd(key, tex.Bytes);
        }
#else
        // Sin SoulsFormats, intentar leer TPF de forma básica
        // Los TPF de Elden Ring tienen los DDS embebidos con un header simple
        // Esta es una implementación mínima que funciona para la mayoría de casos
        var data = File.ReadAllBytes(tpfPath);
        if (data.Length < 12) return;

        // Buscar magic "DDS " dentro del TPF (offset variable)
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == 'D' && data[i + 1] == 'D' && data[i + 2] == 'S' && data[i + 3] == ' ')
            {
                // Extraer hasta el final como posible DDS
                var ddsData = data[i..];
                var key = Path.GetFileNameWithoutExtension(tpfPath) + $"_{i}";
                map.TryAdd(key, ddsData);
                break;
            }
        }
#endif
    }

    private static byte[]? FindTextureData(string texName, Dictionary<string, byte[]> map)
    {
        // Busca por nombre exacto, luego sin sufijos
        if (map.TryGetValue(texName, out var data)) return data;
        // Intentar con sufijos comunes de Elden Ring (_a, _n, _s)
        foreach (var suffix in new[] { "_a", "_n", "_s", "" })
        {
            if (map.TryGetValue(texName + suffix, out data)) return data;
        }
        return null;
    }
}
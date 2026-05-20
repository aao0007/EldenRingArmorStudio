namespace EldenRingArmorStudio.UI.Viewer3D;

/// <summary>
/// Vértice con todos los atributos necesarios para renderizado con texturas.
/// </summary>
public struct FlverVertex
{
    public float X, Y, Z;           // Posición
    public float NX, NY, NZ;        // Normal
    public float U, V;              // UV set 0
    public float R, G, B, A;        // Color de vértice (si existe)
}

/// <summary>
/// Sub-malla lista para subir a la GPU.
/// </summary>
public class FlverMeshData
{
    public FlverVertex[] Vertices { get; set; } = [];
    public uint[] Indices { get; set; } = [];
    public int MaterialIndex { get; set; }
    public string MaterialName { get; set; } = "";
}

/// <summary>
/// Material con todas sus texturas asociadas.
/// </summary>
public class FlverMaterialData
{
    public string Name { get; set; } = "";
    public string MtdPath { get; set; } = "";

    // Rutas de texturas extraídas del BND
    public string? AlbedoPath { get; set; }   // _a  (Albedo / Diffuse)
    public string? NormalPath { get; set; }   // _n  (Normal map)
    public string? SpecularPath { get; set; }   // _s  (Specular)

    // Datos binarios DDS ya cargados (antes de subir a GPU)
    public byte[]? AlbedoData { get; set; }
    public byte[]? NormalData { get; set; }
    public byte[]? SpecularData { get; set; }
}

/// <summary>
/// Modelo FLVER completo listo para renderizar.
/// </summary>
public class FlverModel
{
    public List<FlverMeshData> Meshes { get; } = new();
    public List<FlverMaterialData> Materials { get; } = new();
    public string FilePath { get; set; } = "";
    public int TotalVertices => Meshes.Sum(m => m.Vertices.Length);
    public int TotalTriangles => Meshes.Sum(m => m.Indices.Length / 3);
}
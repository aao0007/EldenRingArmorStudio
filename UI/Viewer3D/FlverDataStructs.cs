using System.Collections.Generic;
using System.Linq;

namespace EldenRingArmorStudio.UI.Viewer3D
{
    /// <summary>
    /// Vértice con todos los atributos necesarios para renderizado con texturas.
    /// </summary>
    public struct FlverVertex
    {
        public float X, Y, Z;           // Posición
        public float NX, NY, NZ;        // Normal
        public float U, V;              // UV set 0
        public float R, G, B, A;        // Color de vértice
    }

    /// <summary>
    /// Sub-malla que contiene la geometría.
    /// </summary>
    public class FlverMeshData
    {
        public int MaterialIndex { get; set; }
        public string MaterialName { get; set; } = "";
        public FlverVertex[] Vertices { get; set; } = [];
        public uint[] Indices { get; set; } = [];

        // Identificadores de OpenGL
        public int VaoId { get; set; }
        public int VboId { get; set; }
        public int EboId { get; set; }
    }

    public class FlverMaterialData
    {
        public string Name { get; set; } = "";
        public string MtdPath { get; set; } = "";

        // Rutas de texturas extraídas del BND
        public string AlbedoPath { get; set; }     // _a  
        public string NormalPath { get; set; }     // _n  
        public string SpecularPath { get; set; }   // _s  

        // Datos binarios DDS ya cargados
        public byte[] AlbedoData { get; set; }
        public byte[] NormalData { get; set; }
        public byte[] SpecularData { get; set; }

        // AÑADE ESTO para que tu código antiguo no se queje:
        public int GlAlbedoTextureId { get; set; } = -1;
    }

    /// <summary>
    /// Modelo FLVER completo.
    /// </summary>
    public class FlverModel
    {
        public string FilePath { get; set; } = "";
        public List<FlverMeshData> Meshes { get; set; } = new();
        public List<FlverMaterialData> Materials { get; set; } = new();

        public int TotalVertices => Meshes.Sum(m => m.Vertices?.Length ?? 0);
        public int TotalTriangles => Meshes.Sum(m => m.Indices?.Length ?? 0) / 3;
    }
}
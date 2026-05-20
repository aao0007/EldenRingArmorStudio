using System.Collections.Generic;
using System.Linq;

namespace EldenRingArmorStudio.UI.Viewer3D
{
    public struct FlverVertex
    {
        public float X, Y, Z;
        public float NX, NY, NZ;
        public float U, V;
        public float R, G, B, A;
    }

    public class FlverMaterialData
    {
        public string Name { get; set; }
        public string MtdPath { get; set; }
        public string AlbedoPath { get; set; }
        public byte[] AlbedoData { get; set; }

        // Identificador de textura de OpenGL (se llena al cargarlo a la GPU)
        public int GlAlbedoTextureId { get; set; } = -1;
    }

    public class FlverMeshData
    {
        public int MaterialIndex { get; set; }
        public string MaterialName { get; set; }
        public FlverVertex[] Vertices { get; set; }
        public uint[] Indices { get; set; }

        // Identificadores de OpenGL
        public int VaoId { get; set; }
        public int VboId { get; set; }
        public int EboId { get; set; }
    }

    public class FlverModel
    {
        public string FilePath { get; set; }
        public List<FlverMeshData> Meshes { get; set; } = new();
        public List<FlverMaterialData> Materials { get; set; } = new();
        public int TotalVertices => Meshes.Sum(m => m.Vertices?.Length ?? 0);
        public int TotalTriangles => Meshes.Sum(m => m.Indices?.Length ?? 0) / 3;
    }
}
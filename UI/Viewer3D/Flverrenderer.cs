using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Pfim;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;

namespace EldenRingArmorStudio.UI.Viewer3D;

// ─────────────────────────────────────────────────────────────────────────────
// GPU handles por sub-malla
// ─────────────────────────────────────────────────────────────────────────────
internal class GpuMesh : IDisposable
{
    public int Vao, Vbo, Ebo;
    public int IndexCount;
    public int MaterialIndex;

    public void Dispose()
    {
        if (Vbo != 0) GL.DeleteBuffer(Vbo);
        if (Ebo != 0) GL.DeleteBuffer(Ebo);
        if (Vao != 0) GL.DeleteVertexArray(Vao);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Material GPU
// ─────────────────────────────────────────────────────────────────────────────
internal class GpuMaterial : IDisposable
{
    public int AlbedoTex = 0;   // 0 = sin textura
    public int NormalTex = 0;
    public int SpecularTex = 0;
    public bool HasAlbedo => AlbedoTex != 0;
    public bool HasNormal => NormalTex != 0;
    public bool HasSpecular => SpecularTex != 0;

    public void Dispose()
    {
        if (AlbedoTex != 0) { GL.DeleteTexture(AlbedoTex); AlbedoTex = 0; }
        if (NormalTex != 0) { GL.DeleteTexture(NormalTex); NormalTex = 0; }
        if (SpecularTex != 0) { GL.DeleteTexture(SpecularTex); SpecularTex = 0; }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Renderer principal
// ─────────────────────────────────────────────────────────────────────────────
public class FlverRenderer : IDisposable
{
    // ── Shaders ───────────────────────────────────────────────────────────────

    // Vertex shader: posición, normal, UV, tangente (para normal map)
    private const string VERT_SRC = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNorm;
layout(location=2) in vec2 aUV;
layout(location=3) in vec4 aColor;   // vertex color (si existe)

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
uniform mat3 uNormalMat;

out vec3 vWorldPos;
out vec3 vNorm;
out vec2 vUV;
out vec4 vColor;

void main()
{
    vec4 worldPos = uModel * vec4(aPos, 1.0);
    vWorldPos     = worldPos.xyz;
    vNorm         = normalize(uNormalMat * aNorm);
    vUV           = aUV;
    vColor        = aColor;
    gl_Position   = uProj * uView * worldPos;
}";

    // Fragment shader: albedo + normal map + specular + IBL-style lighting
    // Replica el look de FLVER Editor / in-game:
    //   - luz principal cenital (key light)
    //   - luz de relleno lateral (fill)
    //   - luz de rebote desde abajo (ambient bounce)
    //   - fresnel rim sutíl
    //   - normal map tangente-espacio
    private const string FRAG_SRC = @"
#version 330 core

in vec3 vWorldPos;
in vec3 vNorm;
in vec2 vUV;
in vec4 vColor;

out vec4 FragColor;

// Texturas
uniform sampler2D uAlbedo;
uniform sampler2D uNormalMap;
uniform sampler2D uSpecular;

// Flags de textura disponible
uniform bool uHasAlbedo;
uniform bool uHasNormal;
uniform bool uHasSpecular;

// Cámara
uniform vec3 uCamPos;

// Modo de render
uniform int  uRenderMode;    // 0=texturado, 1=sólido, 2=wireframe, 3=normales
uniform bool uWireframe;

// ── Funciones auxiliares ──────────────────────────────────────────────────────

// Reconstruye la normal desde normal map en espacio tangente
// Usa derivadas de pantalla para calcular TBN (no necesita tangentes en VBO)
vec3 perturbNormal(vec3 N, vec3 V, vec2 uv, vec3 mapNorm)
{
    vec3 q1  = dFdx(vWorldPos);
    vec3 q2  = dFdy(vWorldPos);
    vec2 st1 = dFdx(uv);
    vec2 st2 = dFdy(uv);

    vec3 T = normalize(q1 * st2.t - q2 * st1.t);
    vec3 B = -normalize(cross(N, T));
    mat3 TBN = mat3(T, B, N);
    return normalize(TBN * mapNorm);
}

void main()
{
    // ── Modo wireframe ────────────────────────────────────────────────────────
    if(uWireframe)
    {
        FragColor = vec4(0.08, 0.65, 1.0, 1.0);
        return;
    }

    vec3 V = normalize(uCamPos - vWorldPos);

    // ── Normal base ───────────────────────────────────────────────────────────
    vec3 N = normalize(vNorm);

    if(uHasNormal && uRenderMode == 0)
    {
        vec3 mapSample = texture(uNormalMap, vUV).rgb * 2.0 - 1.0;
        // Las normal maps de ER están en DX convention (Y invertido)
        mapSample.y = -mapSample.y;
        N = perturbNormal(N, V, vUV, mapSample);
    }

    // ── Modo normales (debug) ─────────────────────────────────────────────────
    if(uRenderMode == 3)
    {
        FragColor = vec4(N * 0.5 + 0.5, 1.0);
        return;
    }

    // ── Albedo ────────────────────────────────────────────────────────────────
    vec3 albedo;
    float alpha = 1.0;

    if(uHasAlbedo && uRenderMode == 0)
    {
        vec4 albedoSample = texture(uAlbedo, vUV);
        albedo = albedoSample.rgb;
        alpha  = albedoSample.a;
        // Combinar con vertex color (como hace el juego)
        albedo *= vColor.rgb;
    }
    else if(uRenderMode == 1)  // modo sólido
    {
        albedo = vec3(0.62, 0.60, 0.58);
    }
    else
    {
        // Sin textura: usar color gris + vertex color
        albedo = vec3(0.72, 0.70, 0.68) * vColor.rgb;
    }

    // ── Roughness / metallic desde mapa especular ─────────────────────────────
    // En Elden Ring: canal R = metallic, G = roughness, B = AO
    float metallic  = 0.0;
    float roughness = 0.6;
    float ao        = 1.0;

    if(uHasSpecular && uRenderMode == 0)
    {
        vec3 spec = texture(uSpecular, vUV).rgb;
        metallic  = spec.r;
        roughness = spec.g;
        ao        = spec.b;
    }

    // ── Iluminación Phong extendida (replica estilo FLVER/juego) ─────────────
    // 3 luces + ambiente global

    // Key light (sol / luz principal desde arriba-frente)
    vec3 L1    = normalize(vec3(0.6,  1.4,  0.8));
    float d1   = max(dot(N, L1), 0.0);
    vec3  col1 = vec3(1.05, 0.98, 0.90);   // ligeramente cálido

    // Fill light (relleno lateral-trasero)
    vec3 L2    = normalize(vec3(-0.8, 0.3, -0.6));
    float d2   = max(dot(N, L2), 0.0) * 0.35;
    vec3  col2 = vec3(0.65, 0.70, 0.80);   // frío/azulado

    // Bounce light (rebote desde suelo)
    vec3 L3    = normalize(vec3(0.0, -1.0, 0.2));
    float d3   = max(dot(N, L3), 0.0) * 0.12;
    vec3  col3 = vec3(0.40, 0.35, 0.30);

    // Especular Blinn-Phong
    float shininess = mix(8.0, 128.0, 1.0 - roughness);
    vec3 H1  = normalize(L1 + V);
    float sp = pow(max(dot(N, H1), 0.0), shininess);
    // Fresnel simple (Schlick)
    float F0    = mix(0.04, 0.7, metallic);
    float cosV  = max(dot(N, V), 0.0);
    float fresn  = F0 + (1.0 - F0) * pow(1.0 - cosV, 5.0);

    vec3 specColor = mix(vec3(1.0), albedo, metallic);
    vec3 specTerm  = specColor * sp * fresn * (1.0 - roughness * 0.7);

    // Rim light (borde de silueta, sutil)
    float rim = pow(1.0 - cosV, 3.0) * 0.15;
    vec3 rimCol = vec3(0.55, 0.60, 0.70) * rim;

    // Ambiente (IBL simplificado)
    vec3 ambient = albedo * vec3(0.08, 0.09, 0.11) * ao;

    // Ensamblar
    vec3 diffuse = albedo * (d1 * col1 + d2 * col2 + d3 * col3);
    vec3 color   = ambient + diffuse + specTerm + rimCol;

    // Tonemapping ACES simple
    color = color * (color + 0.0245786) / (color * (0.983729 * color + 0.432951) + 0.238081);

    // Corrección gamma
    color = pow(clamp(color, 0.0, 1.0), vec3(1.0 / 2.2));

    FragColor = vec4(color, alpha);
}";

    // Grid shader
    private const string GRID_VERT = @"
#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4 uVP;
void main(){ gl_Position = uVP * vec4(aPos,1.0); }";

    private const string GRID_FRAG = @"
#version 330 core
out vec4 FragColor;
uniform vec4 uColor;
void main(){ FragColor = uColor; }";

    // ── Estado interno ────────────────────────────────────────────────────────
    private int _progMain, _progGrid;
    private List<GpuMesh> _meshes = new();
    private List<GpuMaterial> _materials = new();

    // Grid
    private int _gridVao, _gridVbo, _axisVao, _axisVbo;
    private int _gridN, _axisN;

    // Cámara pública (el control la maneja)
    public float Yaw = 0.5f;
    public float Pitch = 0.22f;
    public float Zoom = 3.2f;
    public Vector3 Target = new(0, 1, 0);

    // Opciones render
    public int RenderMode = 0;   // 0=tex, 1=solid, 2=wire, 3=normals
    public bool ShowGrid = true;
    public bool Wireframe => RenderMode == 2;

    private int _totalTris;
    private int _totalVerts;
    public string StatsText => $"{_totalVerts:N0} verts · {_totalTris:N0} tris";

    // ── Inicialización ─────────────────────────────────────────────────────────

    public void Initialize()
    {
        GL.ClearColor(0.09f, 0.09f, 0.11f, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.Multisample);

        _progMain = CompileProgram(VERT_SRC, FRAG_SRC);
        _progGrid = CompileProgram(GRID_VERT, GRID_FRAG);

        BuildGrid();
    }

    private static int CompileProgram(string vert, string frag)
    {
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, vert);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int okV);
        if (okV == 0) Log.Error("Vert shader: {E}", GL.GetShaderInfoLog(vs));

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, frag);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out int okF);
        if (okF == 0) Log.Error("Frag shader: {E}", GL.GetShaderInfoLog(fs));

        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vs);
        GL.AttachShader(prog, fs);
        GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int okL);
        if (okL == 0) Log.Error("Link: {E}", GL.GetProgramInfoLog(prog));

        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
        return prog;
    }

    private void BuildGrid()
    {
        var main = new List<float>();
        var axis = new List<float>();
        float h = 3f;
        for (int i = -24; i <= 24; i++)
        {
            if (i == 0) continue;
            float v = i * (h / 24f);
            main.AddRange(new[] { v, 0, -h, v, 0, h, -h, 0, v, h, 0, v });
        }
        axis.AddRange(new[] { -h, 0, 0, h, 0, 0, 0, -h, 0, 0, h, 0, 0, 0, -h, 0, 0, h });

        var mg = main.ToArray(); _gridN = mg.Length / 3;
        var ag = axis.ToArray(); _axisN = ag.Length / 3;

        _gridVao = GL.GenVertexArray(); _gridVbo = GL.GenBuffer();
        GL.BindVertexArray(_gridVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _gridVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, mg.Length * 4, mg, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
        GL.EnableVertexAttribArray(0);

        _axisVao = GL.GenVertexArray(); _axisVbo = GL.GenBuffer();
        GL.BindVertexArray(_axisVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _axisVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, ag.Length * 4, ag, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
        GL.EnableVertexAttribArray(0);
        GL.BindVertexArray(0);
    }

    // ── Cargar modelo ─────────────────────────────────────────────────────────

    public void LoadModel(FlverModel model)
    {
        DisposeModel();

        // Subir materiales (texturas DDS → GPU)
        foreach (var mat in model.Materials)
        {
            var gm = new GpuMaterial();
            if (mat.AlbedoData != null) gm.AlbedoTex = UploadDds(mat.AlbedoData);
            if (mat.NormalData != null) gm.NormalTex = UploadDds(mat.NormalData);
            if (mat.SpecularData != null) gm.SpecularTex = UploadDds(mat.SpecularData);
            _materials.Add(gm);
        }

        // Si no hay materiales, añadir uno vacío por defecto
        if (_materials.Count == 0)
            _materials.Add(new GpuMaterial());

        // Subir mallas
        _totalVerts = 0; _totalTris = 0;
        foreach (var mesh in model.Meshes)
        {
            if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0) continue;

            var gpu = UploadMesh(mesh);
            _meshes.Add(gpu);
            _totalVerts += mesh.Vertices.Length;
            _totalTris += mesh.Indices.Length / 3;
        }

        FitCamera(model);
        Log.Information("GPU: {V} verts, {T} tris, {M} materiales, {TX} texturas",
            _totalVerts, _totalTris, _materials.Count,
            _materials.Count(m => m.HasAlbedo));
    }

    // ── Upload DDS → OpenGL texture ───────────────────────────────────────────

    private static int UploadDds(byte[] data)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(data);
            using var img = Pfimage.FromStream(ms);

            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);

            PixelFormat format = PixelFormat.Rgba;
            PixelInternalFormat internalFormat = PixelInternalFormat.Rgba8;

            // Pfim ya descomprime los DXT por defecto a formatos RGBA/RGB
            switch (img.Format)
            {
                case ImageFormat.Rgba32:
                    format = PixelFormat.Bgra; // Pfim usa BGR/BGRA internamente
                    internalFormat = PixelInternalFormat.Rgba8;
                    break;
                case ImageFormat.Rgb24:
                    format = PixelFormat.Bgr;
                    internalFormat = PixelInternalFormat.Rgb8;
                    break;
                default:
                    format = PixelFormat.Bgra;
                    internalFormat = PixelInternalFormat.Rgba8;
                    break;
            }

            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, img.Width, img.Height, 0, format, PixelType.UnsignedByte, img.Data);

            // Mipmaps + filtrado
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // Repetición de texturas
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            return tex;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error subiendo textura DDS");
            return 0;
        }
    }

    // ── Upload malla → VAO/VBO/EBO ────────────────────────────────────────────

    // Layout de vértice en GPU:
    //   location 0: XYZ     (3×float = 12 bytes)
    //   location 1: NxNyNz  (3×float = 12 bytes)
    //   location 2: UV      (2×float =  8 bytes)
    //   location 3: RGBA    (4×float = 16 bytes)
    // Total stride = 48 bytes
    private const int STRIDE = 48;

    private static GpuMesh UploadMesh(FlverMeshData mesh)
    {
        // Aplanar a array de floats interleaved
        var vdata = new float[mesh.Vertices.Length * (STRIDE / 4)];
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            var v = mesh.Vertices[i];
            int b = i * 12;
            vdata[b + 0] = v.X; vdata[b + 1] = v.Y; vdata[b + 2] = v.Z;
            vdata[b + 3] = v.NX; vdata[b + 4] = v.NY; vdata[b + 5] = v.NZ;
            vdata[b + 6] = v.U; vdata[b + 7] = v.V;
            vdata[b + 8] = v.R; vdata[b + 9] = v.G;
            vdata[b + 10] = v.B; vdata[b + 11] = v.A;
        }

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vdata.Length * 4, vdata,
            BufferUsageHint.StaticDraw);

        // Posición
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, STRIDE, 0);
        GL.EnableVertexAttribArray(0);
        // Normal
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, STRIDE, 12);
        GL.EnableVertexAttribArray(1);
        // UV
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, STRIDE, 24);
        GL.EnableVertexAttribArray(2);
        // Color vértice
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, STRIDE, 32);
        GL.EnableVertexAttribArray(3);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer,
            mesh.Indices.Length * 4, mesh.Indices, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(0);

        return new GpuMesh
        {
            Vao = vao,
            Vbo = vbo,
            Ebo = ebo,
            IndexCount = mesh.Indices.Length,
            MaterialIndex = mesh.MaterialIndex,
        };
    }

    // ── Render ────────────────────────────────────────────────────────────────

    public void Render(int viewportW, int viewportH)
    {
        GL.Viewport(0, 0, viewportW, viewportH);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var cam = CamPos();
        var view = Matrix4.LookAt(cam, Target, Vector3.UnitY);
        var proj = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(45f),
            viewportW / (float)Math.Max(viewportH, 1),
            0.005f, 500f);
        var model = Matrix4.Identity;
        var vp = view * proj;

        // ── Grid ──────────────────────────────────────────────────────────────
        if (ShowGrid)
        {
            GL.UseProgram(_progGrid);
            SetUniform(_progGrid, "uVP", vp);
            SetUniform4(_progGrid, "uColor", 0.20f, 0.20f, 0.24f, 0.6f);
            GL.BindVertexArray(_gridVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _gridN);

            GL.BindVertexArray(_axisVao);
            // X rojo
            SetUniform4(_progGrid, "uColor", 0.85f, 0.22f, 0.22f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            // Y verde
            SetUniform4(_progGrid, "uColor", 0.22f, 0.80f, 0.22f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 2, 2);
            // Z azul
            SetUniform4(_progGrid, "uColor", 0.22f, 0.42f, 0.90f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 4, 2);
            GL.BindVertexArray(0);
        }

        if (_meshes.Count == 0) return;

        // ── Modelo ────────────────────────────────────────────────────────────
        GL.UseProgram(_progMain);

        // Matrices
        var normalMat = new Matrix3(Matrix4.Transpose(Matrix4.Invert(model)));
        SetUniform(_progMain, "uModel", model);
        SetUniform(_progMain, "uView", view);
        SetUniform(_progMain, "uProj", proj);
        SetUniform(_progMain, "uNormalMat", normalMat);
        SetUniform3(_progMain, "uCamPos", cam.X, cam.Y, cam.Z);
        GL.Uniform1(GL.GetUniformLocation(_progMain, "uRenderMode"), RenderMode);

        if (RenderMode == 2)   // wireframe
        {
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            GL.LineWidth(1.0f);
            GL.Disable(EnableCap.CullFace);
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uWireframe"), 1);
        }
        else
        {
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            GL.Disable(EnableCap.CullFace);   // doble cara como FLVER Editor
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uWireframe"), 0);
        }

        // Unidades de textura fijas
        GL.Uniform1(GL.GetUniformLocation(_progMain, "uAlbedo"), 0);
        GL.Uniform1(GL.GetUniformLocation(_progMain, "uNormalMap"), 1);
        GL.Uniform1(GL.GetUniformLocation(_progMain, "uSpecular"), 2);

        foreach (var gpuMesh in _meshes)
        {
            // Obtener material (clamp al rango disponible)
            var matIdx = Math.Clamp(gpuMesh.MaterialIndex, 0, _materials.Count - 1);
            var mat = _materials[matIdx];

            // Bindear texturas en sus unidades
            BindTex(mat.AlbedoTex, 0);
            BindTex(mat.NormalTex, 1);
            BindTex(mat.SpecularTex, 2);

            GL.Uniform1(GL.GetUniformLocation(_progMain, "uHasAlbedo"), mat.HasAlbedo ? 1 : 0);
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uHasNormal"), mat.HasNormal ? 1 : 0);
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uHasSpecular"), mat.HasSpecular ? 1 : 0);

            GL.BindVertexArray(gpuMesh.Vao);
            GL.DrawElements(PrimitiveType.Triangles,
                gpuMesh.IndexCount, DrawElementsType.UnsignedInt, 0);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
    }

    private static void BindTex(int tex, int unit)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + unit);
        GL.BindTexture(TextureTarget.Texture2D, tex);   // 0 = sin textura → negro
    }

    // ── Cámara ────────────────────────────────────────────────────────────────

    public Vector3 CamPos()
    {
        float cp = MathF.Cos(Pitch);
        return Target + Zoom * new Vector3(
            cp * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            cp * MathF.Cos(Yaw));
    }

    private void FitCamera(FlverModel model)
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
        Target = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
        float ext = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        Zoom = Math.Max(ext * 1.8f, 0.2f);
        Yaw = 0.5f;
        Pitch = 0.22f;
    }

    // ── Uniform helpers ───────────────────────────────────────────────────────

    private static void SetUniform(int prog, string name, Matrix4 m)
    {
        int loc = GL.GetUniformLocation(prog, name);
        GL.UniformMatrix4(loc, false, ref m);
    }
    private static void SetUniform(int prog, string name, Matrix3 m)
    {
        int loc = GL.GetUniformLocation(prog, name);
        GL.UniformMatrix3(loc, false, ref m);
    }
    private static void SetUniform3(int prog, string name, float x, float y, float z)
        => GL.Uniform3(GL.GetUniformLocation(prog, name), x, y, z);
    private static void SetUniform4(int prog, string name, float x, float y, float z, float w)
        => GL.Uniform4(GL.GetUniformLocation(prog, name), x, y, z, w);

    // ── Limpieza ──────────────────────────────────────────────────────────────

    public void DisposeModel()
    {
        foreach (var m in _meshes) m.Dispose();
        foreach (var m in _materials) m.Dispose();
        _meshes.Clear();
        _materials.Clear();
        _totalVerts = _totalTris = 0;
    }

    public void Dispose()
    {
        DisposeModel();
        if (_gridVao != 0) GL.DeleteVertexArray(_gridVao);
        if (_gridVbo != 0) GL.DeleteBuffer(_gridVbo);
        if (_axisVao != 0) GL.DeleteVertexArray(_axisVao);
        if (_axisVbo != 0) GL.DeleteBuffer(_axisVbo);
        if (_progMain != 0) GL.DeleteProgram(_progMain);
        if (_progGrid != 0) GL.DeleteProgram(_progGrid);
    }
}
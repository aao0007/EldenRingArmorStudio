using EldenRingArmorStudio.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EldenRingArmorStudio.UI.Viewer3D
{
    public partial class Viewport3DControl : UserControl
    {
        // ── Modelo ────────────────────────────────────────────────────────────
        private FlverModel _currentModel;
        private FlverModel _pendingModel;   // esperando a que GL esté listo
        private bool _glReady;

        // ── Shaders ───────────────────────────────────────────────────────────
        private int _progMain;
        private int _progGrid;

        // GPU meshes / materials (igual que FlverRenderer original)
        private readonly List<GpuMesh> _meshes = new();
        private readonly List<GpuMaterial> _materials = new();

        // Grid
        private int _gridVao, _gridVbo, _gridN;
        private int _axisVao, _axisVbo;

        // ── Cámara ────────────────────────────────────────────────────────────
        private float _yaw = 0.5f;
        private float _pitch = 0.22f;
        private float _zoom = 3.2f;
        private Vector3 _target = Vector3.Zero;

        // ── Ratón ─────────────────────────────────────────────────────────────
        private Point _lastMousePos;
        private bool _isDraggingCam;
        private bool _isDraggingPan;

        // ── Opciones render ───────────────────────────────────────────────────
        private int _renderMode = 0;   // 0=tex 1=solid 2=wire 3=normals
        private bool _showGrid = true;
        private Color4 _bgColor = new(0.07f, 0.07f, 0.10f, 1f);

        // ── Stats ─────────────────────────────────────────────────────────────
        private int _totalVerts, _totalTris;

        // ══════════════════════════════════════════════════════════════════════
        // SHADERS — idénticos al FlverRenderer original que sí funcionaba
        // ══════════════════════════════════════════════════════════════════════

        private const string VERT_SRC = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNorm;
layout(location=2) in vec2 aUV;
layout(location=3) in vec4 aColor;

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

        private const string FRAG_SRC = @"
#version 330 core

in vec3 vWorldPos;
in vec3 vNorm;
in vec2 vUV;
in vec4 vColor;

out vec4 FragColor;

uniform sampler2D uAlbedo;
uniform sampler2D uNormalMap;
uniform sampler2D uSpecular;

uniform bool uHasAlbedo;
uniform bool uHasNormal;
uniform bool uHasSpecular;

uniform vec3 uCamPos;
uniform int  uRenderMode;
uniform bool uWireframe;

vec3 perturbNormal(vec3 N, vec3 V, vec2 uv, vec3 mapNorm)
{
    vec3 q1  = dFdx(vWorldPos);
    vec3 q2  = dFdy(vWorldPos);
    vec2 st1 = dFdx(uv);
    vec2 st2 = dFdy(uv);
    vec3 T   = normalize(q1 * st2.t - q2 * st1.t);
    vec3 B   = -normalize(cross(N, T));
    mat3 TBN = mat3(T, B, N);
    return normalize(TBN * mapNorm);
}

void main()
{
    if(uWireframe) { FragColor = vec4(0.08, 0.65, 1.0, 1.0); return; }

    vec3 V = normalize(uCamPos - vWorldPos);
    vec3 N = normalize(vNorm);

    if(uHasNormal && uRenderMode == 0)
    {
        vec3 s = texture(uNormalMap, vUV).rgb * 2.0 - 1.0;
        s.y = -s.y;
        N = perturbNormal(N, V, vUV, s);
    }

    if(uRenderMode == 3) { FragColor = vec4(N * 0.5 + 0.5, 1.0); return; }

    vec3 albedo;
    float alpha = 1.0;

    if(uHasAlbedo && uRenderMode == 0)
    {
        vec4 s = texture(uAlbedo, vUV);
        albedo = s.rgb * vColor.rgb;
        alpha  = s.a;
    }
    else if(uRenderMode == 1)
    {
        albedo = vec3(0.62, 0.60, 0.58);
    }
    else
    {
        albedo = vec3(0.72, 0.70, 0.68) * vColor.rgb;
    }

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

    vec3  L1 = normalize(vec3(0.6,  1.4,  0.8));
    float d1 = max(dot(N, L1), 0.0);
    vec3  c1 = vec3(1.05, 0.98, 0.90);

    vec3  L2 = normalize(vec3(-0.8, 0.3, -0.6));
    float d2 = max(dot(N, L2), 0.0) * 0.35;
    vec3  c2 = vec3(0.65, 0.70, 0.80);

    vec3  L3 = normalize(vec3(0.0, -1.0, 0.2));
    float d3 = max(dot(N, L3), 0.0) * 0.12;
    vec3  c3 = vec3(0.40, 0.35, 0.30);

    float shininess = mix(8.0, 128.0, 1.0 - roughness);
    vec3  H1  = normalize(L1 + V);
    float sp  = pow(max(dot(N, H1), 0.0), shininess);
    float F0  = mix(0.04, 0.7, metallic);
    float cosV = max(dot(N, V), 0.0);
    float fresn = F0 + (1.0 - F0) * pow(1.0 - cosV, 5.0);
    vec3 specColor = mix(vec3(1.0), albedo, metallic);
    vec3 specTerm  = specColor * sp * fresn * (1.0 - roughness * 0.7);

    float rim   = pow(1.0 - cosV, 3.0) * 0.15;
    vec3 rimCol = vec3(0.55, 0.60, 0.70) * rim;

    vec3 ambient = albedo * vec3(0.08, 0.09, 0.11) * ao;
    vec3 diffuse = albedo * (d1*c1 + d2*c2 + d3*c3);
    vec3 color   = ambient + diffuse + specTerm + rimCol;

    color = color*(color+0.0245786)/(color*(0.983729*color+0.432951)+0.238081);
    color = pow(clamp(color,0.0,1.0), vec3(1.0/2.2));

    FragColor = vec4(color, alpha);
}";

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

        // ══════════════════════════════════════════════════════════════════════

        public Viewport3DControl()
        {
            InitializeComponent();
            var settings = new GLWpfControlSettings
            {
                MajorVersion = 3,
                MinorVersion = 3,
                Profile = OpenTK.Windowing.Common.ContextProfile.Core
            };
            GlControl.Start(settings);
        }

        // ── Carga de modelo ───────────────────────────────────────────────────

        public void LoadModel(FlverModel model)
        {
            if (model == null) return;
            // No llamar a GL aquí: el contexto solo está activo en GlControl_Render.
            // Guardamos el modelo como pendiente; se subirá a GPU en el próximo frame.
            _pendingModel = model;

            TxtStats.Text =
                $"Vértices: {model.TotalVertices:N0} | Triángulos: {model.TotalTriangles:N0}";
        }

        // ── Render loop ───────────────────────────────────────────────────────

        private void GlControl_Render(TimeSpan delta)
        {
            if (!_glReady) { InitGL(); _glReady = true; }

            // Subir modelo pendiente a GPU ahora que el contexto GL está activo
            if (_pendingModel != null)
            {
                UploadModelToGpu(_pendingModel);
                _pendingModel = null;
            }

            GL.Viewport(0, 0, (int)GlControl.ActualWidth, (int)GlControl.ActualHeight);
            GL.ClearColor(_bgColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            float aspect = (float)Math.Max(GlControl.ActualWidth, 1) /
                           (float)Math.Max(GlControl.ActualHeight, 1);

            var cam = CamPos();
            var view = Matrix4.LookAt(cam, _target, Vector3.UnitY);
            var proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f), aspect, 0.005f, 500f);
            var vp = view * proj;

            // Grid
            if (_showGrid) DrawGrid(vp);

            if (_meshes.Count == 0) return;

            // ── Modelo ────────────────────────────────────────────────────────
            GL.UseProgram(_progMain);

            var model = Matrix4.Identity;
            var normalMat = new Matrix3(Matrix4.Transpose(Matrix4.Invert(model)));

            SetUniform(_progMain, "uModel", model);
            SetUniform(_progMain, "uView", view);
            SetUniform(_progMain, "uProj", proj);
            SetUniform(_progMain, "uNormalMat", normalMat);
            GL.Uniform3(GL.GetUniformLocation(_progMain, "uCamPos"), cam);
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uRenderMode"), _renderMode);

            // Unidades de textura fijas
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uAlbedo"), 0);
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uNormalMap"), 1);
            GL.Uniform1(GL.GetUniformLocation(_progMain, "uSpecular"), 2);

            if (_renderMode == 2)
            {
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                GL.Uniform1(GL.GetUniformLocation(_progMain, "uWireframe"), 1);
            }
            else
            {
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                GL.Uniform1(GL.GetUniformLocation(_progMain, "uWireframe"), 0);
            }
            GL.Disable(EnableCap.CullFace);

            foreach (var gpuMesh in _meshes)
            {
                int matIdx = Math.Clamp(gpuMesh.MaterialIndex, 0, _materials.Count - 1);
                var mat = _materials[matIdx];

                BindTex(mat.AlbedoTex, 0);
                BindTex(mat.NormalTex, 1);
                BindTex(mat.SpecularTex, 2);

                GL.Uniform1(GL.GetUniformLocation(_progMain, "uHasAlbedo"),
                    mat.AlbedoTex != 0 ? 1 : 0);
                GL.Uniform1(GL.GetUniformLocation(_progMain, "uHasNormal"),
                    mat.NormalTex != 0 ? 1 : 0);
                GL.Uniform1(GL.GetUniformLocation(_progMain, "uHasSpecular"),
                    mat.SpecularTex != 0 ? 1 : 0);

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
            GL.BindTexture(TextureTarget.Texture2D, tex);
        }

        // ── Upload modelo a GPU (solo llamar desde dentro del render loop) ────

        private void UploadModelToGpu(FlverModel model)
        {
            // Limpiar anterior
            foreach (var m in _meshes) DisposeGpuMesh(m);
            foreach (var m in _materials) DisposeGpuMaterial(m);
            _meshes.Clear();
            _materials.Clear();

            _currentModel = model;

            foreach (var mat in model.Materials)
            {
                var gm = new GpuMaterial();
                if (mat.AlbedoData != null) gm.AlbedoTex = UploadDds(mat.AlbedoData);
                if (mat.NormalData != null) gm.NormalTex = UploadDds(mat.NormalData);
                if (mat.SpecularData != null) gm.SpecularTex = UploadDds(mat.SpecularData);
                _materials.Add(gm);
            }
            if (_materials.Count == 0) _materials.Add(new GpuMaterial());

            _totalVerts = 0; _totalTris = 0;
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0) continue;
                _meshes.Add(UploadMesh(mesh));
                _totalVerts += mesh.Vertices.Length;
                _totalTris += mesh.Indices.Length / 3;
            }

            FitCamera(model);
        }

        // ── Init GL ───────────────────────────────────────────────────────────

        private void InitGL()
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.Multisample);

            _progMain = CompileProgram(VERT_SRC, FRAG_SRC);
            _progGrid = CompileProgram(GRID_VERT, GRID_FRAG);
            BuildGrid();
        }

        // ── Grid ──────────────────────────────────────────────────────────────

        private void BuildGrid()
        {
            var main = new List<float>();
            float h = 3f;
            for (int i = -24; i <= 24; i++)
            {
                if (i == 0) continue;
                float v = i * (h / 24f);
                main.AddRange(new[] { v, 0f, -h, v, 0f, h });
                main.AddRange(new[] { -h, 0f, v, h, 0f, v });
            }
            float[] axis = {
                -h,0f,0f, h,0f,0f,
                0f,-h,0f, 0f,h,0f,
                0f,0f,-h, 0f,0f,h
            };

            var mg = main.ToArray(); _gridN = mg.Length / 3;

            _gridVao = GL.GenVertexArray(); _gridVbo = GL.GenBuffer();
            GL.BindVertexArray(_gridVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _gridVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, mg.Length * 4, mg, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);

            _axisVao = GL.GenVertexArray(); _axisVbo = GL.GenBuffer();
            GL.BindVertexArray(_axisVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _axisVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, axis.Length * 4, axis, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
        }

        private void DrawGrid(Matrix4 vp)
        {
            GL.UseProgram(_progGrid);
            int loc = GL.GetUniformLocation(_progGrid, "uVP");
            GL.UniformMatrix4(loc, false, ref vp);

            GL.Uniform4(GL.GetUniformLocation(_progGrid, "uColor"),
                0.20f, 0.20f, 0.24f, 0.6f);
            GL.BindVertexArray(_gridVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _gridN);

            GL.BindVertexArray(_axisVao);
            GL.Uniform4(GL.GetUniformLocation(_progGrid, "uColor"), 0.85f, 0.22f, 0.22f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            GL.Uniform4(GL.GetUniformLocation(_progGrid, "uColor"), 0.22f, 0.80f, 0.22f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 2, 2);
            GL.Uniform4(GL.GetUniformLocation(_progGrid, "uColor"), 0.22f, 0.42f, 0.90f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 4, 2);
            GL.BindVertexArray(0);
        }

        // ── Upload DDS ────────────────────────────────────────────────────────

        private static int UploadDds(byte[] data)
        {
            try
            {
                using var ms = new System.IO.MemoryStream(data);
                using var img = Pfim.Pfimage.FromStream(ms);

                int tex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tex);

                Pfim.ImageFormat fmt = img.Format;
                PixelFormat pf = fmt == Pfim.ImageFormat.Rgba32
                    ? PixelFormat.Bgra : PixelFormat.Bgr;
                PixelInternalFormat pif = fmt == Pfim.ImageFormat.Rgba32
                    ? PixelInternalFormat.Rgba8 : PixelInternalFormat.Rgb8;

                GL.TexImage2D(TextureTarget.Texture2D, 0, pif,
                    img.Width, img.Height, 0, pf, PixelType.UnsignedByte, img.Data);

                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                GL.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear);
                GL.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureMagFilter,
                    (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
                GL.TexParameter(TextureTarget.Texture2D,
                    TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

                GL.BindTexture(TextureTarget.Texture2D, 0);
                return tex;
            }
            catch { return 0; }
        }

        // ── Upload malla (stride = 48 bytes, igual que FlverRenderer) ─────────

        private const int STRIDE = 48;

        private static GpuMesh UploadMesh(FlverMeshData mesh)
        {
            var vdata = new float[mesh.Vertices.Length * 12];
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
            GL.BufferData(BufferTarget.ArrayBuffer,
                vdata.Length * 4, vdata, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, STRIDE, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, STRIDE, 12);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, STRIDE, 24);
            GL.EnableVertexAttribArray(2);
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
                MaterialIndex = mesh.MaterialIndex
            };
        }

        // ── Compile shader ────────────────────────────────────────────────────

        private static int CompileProgram(string vert, string frag)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vert); GL.CompileShader(vs);
            GL.GetShader(vs, ShaderParameter.CompileStatus, out int okV);
            if (okV == 0) Serilog.Log.Error("VS: {E}", GL.GetShaderInfoLog(vs));

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, frag); GL.CompileShader(fs);
            GL.GetShader(fs, ShaderParameter.CompileStatus, out int okF);
            if (okF == 0) Serilog.Log.Error("FS: {E}", GL.GetShaderInfoLog(fs));

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vs); GL.AttachShader(prog, fs);
            GL.LinkProgram(prog);
            GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int okL);
            if (okL == 0) Serilog.Log.Error("Link: {E}", GL.GetProgramInfoLog(prog));

            GL.DeleteShader(vs); GL.DeleteShader(fs);
            return prog;
        }

        // ── Uniform helpers ───────────────────────────────────────────────────

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

        // ── Cámara ────────────────────────────────────────────────────────────

        private Vector3 CamPos()
        {
            float cp = MathF.Cos(_pitch);
            return _target + _zoom * new Vector3(
                cp * MathF.Sin(_yaw),
                MathF.Sin(_pitch),
                cp * MathF.Cos(_yaw));
        }

        private void FitCamera(FlverModel model)
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
            _target = new((minX + maxX) * .5f, (minY + maxY) * .5f, (minZ + maxZ) * .5f);
            float ext = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            _zoom = Math.Max(ext * 1.8f, 0.2f);
            _yaw = 0.5f;
            _pitch = 0.22f;
        }

        // ── Mouse ─────────────────────────────────────────────────────────────

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastMousePos = e.GetPosition(this);
            if (e.LeftButton == MouseButtonState.Pressed) _isDraggingCam = true;
            if (e.MiddleButton == MouseButtonState.Pressed) _isDraggingPan = true;
            this.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(this);
            float dx = (float)(pos.X - _lastMousePos.X);
            float dy = (float)(pos.Y - _lastMousePos.Y);

            if (_isDraggingCam)
            {
                _yaw -= dx * 0.01f;
                _pitch += dy * 0.01f;
                _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);
            }
            else if (_isDraggingPan)
            {
                var cam = CamPos();
                var forward = Vector3.Normalize(_target - cam);
                var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                var up = Vector3.Cross(right, forward);
                float spd = _zoom * 0.002f;
                _target -= right * dx * spd;
                _target += up * dy * spd;
            }
            _lastMousePos = pos;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released) _isDraggingCam = false;
            if (e.MiddleButton == MouseButtonState.Released) _isDraggingPan = false;
            if (!_isDraggingCam && !_isDraggingPan) this.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            float f = e.Delta > 0 ? 0.85f : 1.15f;
            _zoom = Math.Clamp(_zoom * f, 0.05f, 500f);
        }

        // ── Controles barra inferior ──────────────────────────────────────────

        private void SetRenderMode(int mode)
        {
            _renderMode = mode;
            var btns = new[] { BtnTexture, BtnSolid, BtnWireframe, BtnNormals };
            for (int i = 0; i < btns.Length; i++)
            {
                bool active = i == mode;
                btns[i].Background = new System.Windows.Media.SolidColorBrush(
                    active
                    ? System.Windows.Media.Color.FromArgb(64, 26, 95, 180)
                    : System.Windows.Media.Color.FromArgb(40, 40, 40, 60));
                btns[i].BorderBrush = new System.Windows.Media.SolidColorBrush(
                    active
                    ? System.Windows.Media.Color.FromRgb(90, 143, 221)
                    : System.Windows.Media.Color.FromRgb(80, 80, 96));
                btns[i].Foreground = new System.Windows.Media.SolidColorBrush(
                    active
                    ? System.Windows.Media.Color.FromRgb(216, 232, 255)
                    : System.Windows.Media.Color.FromRgb(176, 176, 192));
            }
        }

        private void OnRenderTexture(object s, RoutedEventArgs e) => SetRenderMode(0);
        private void OnRenderSolid(object s, RoutedEventArgs e) => SetRenderMode(1);
        private void OnRenderWireframe(object s, RoutedEventArgs e) => SetRenderMode(2);
        private void OnRenderNormals(object s, RoutedEventArgs e) => SetRenderMode(3);

        private void OnGridToggle(object s, RoutedEventArgs e) =>
            _showGrid = ChkGrid.IsChecked == true;

        private void OnResetCamera(object s, RoutedEventArgs e)
        {
            if (_currentModel != null) FitCamera(_currentModel);
            else { _yaw = 0.5f; _pitch = 0.22f; _zoom = 3.2f; _target = Vector3.Zero; }
        }

        private void OnBgChanged(object s, SelectionChangedEventArgs e)
        {
            if (ComboBg.SelectedItem is ComboBoxItem ci)
                _bgColor = (ci.Tag as string) switch
                {
                    "dark" => new Color4(0.07f, 0.07f, 0.10f, 1f),
                    "grey" => new Color4(0.25f, 0.25f, 0.27f, 1f),
                    "blue" => new Color4(0.05f, 0.08f, 0.18f, 1f),
                    "black" => new Color4(0.01f, 0.01f, 0.01f, 1f),
                    _ => new Color4(0.07f, 0.07f, 0.10f, 1f)
                };
        }

        // ── Dispose helpers ───────────────────────────────────────────────────

        private static void DisposeGpuMesh(GpuMesh m)
        {
            if (m.Vbo != 0) GL.DeleteBuffer(m.Vbo);
            if (m.Ebo != 0) GL.DeleteBuffer(m.Ebo);
            if (m.Vao != 0) GL.DeleteVertexArray(m.Vao);
        }

        private static void DisposeGpuMaterial(GpuMaterial m)
        {
            if (m.AlbedoTex != 0) GL.DeleteTexture(m.AlbedoTex);
            if (m.NormalTex != 0) GL.DeleteTexture(m.NormalTex);
            if (m.SpecularTex != 0) GL.DeleteTexture(m.SpecularTex);
        }
    }
}
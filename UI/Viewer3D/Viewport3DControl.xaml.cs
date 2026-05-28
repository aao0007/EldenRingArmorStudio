using EldenRingArmorStudio.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Wpf;
using System;
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
        private int _shaderProgram;
        private bool _glReady;

        // ── Cámara ────────────────────────────────────────────────────────────
        private float _yaw = 0.5f;
        private float _pitch = 0.22f;
        private float _zoom = 3.2f;
        private Vector3 _target = new(0, 0, 0);

        // ── Ratón ─────────────────────────────────────────────────────────────
        private Point _lastMousePos;
        private bool _isDraggingCam;  // LMB = orbitar
        private bool _isDraggingPan;  // MMB = pan

        // ── Render ────────────────────────────────────────────────────────────
        private int _renderMode = 0;   // 0=tex 1=solid 2=wire 3=normals
        private bool _showGrid = true;
        private Color4 _bgColor = new(0.07f, 0.07f, 0.10f, 1f);

        // Grid / axis VAO/VBO
        private int _gridVao, _gridVbo, _gridN;
        private int _axisVao, _axisVbo;
        private int _gridProg;

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
            if (model == null || !_glReady) return;
            _currentModel = model;

            // Subir texturas
            foreach (var mat in _currentModel.Materials)
                if (mat.AlbedoData != null)
                    mat.GlAlbedoTextureId = TextureManager.LoadDdsTextureFromBytes(mat.AlbedoData);

            // Subir mallas
            foreach (var mesh in _currentModel.Meshes)
            {
                mesh.VaoId = GL.GenVertexArray();
                mesh.VboId = GL.GenBuffer();
                mesh.EboId = GL.GenBuffer();

                GL.BindVertexArray(mesh.VaoId);
                GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.VboId);
                int stride = Marshal.SizeOf<FlverVertex>();
                GL.BufferData(BufferTarget.ArrayBuffer,
                    mesh.Vertices.Length * stride, mesh.Vertices, BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.EboId);
                GL.BufferData(BufferTarget.ElementArrayBuffer,
                    mesh.Indices.Length * sizeof(uint), mesh.Indices, BufferUsageHint.StaticDraw);

                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 12);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 24);
                GL.EnableVertexAttribArray(2);
                GL.BindVertexArray(0);
            }

            // Centrar cámara
            FitCamera(model);

            TxtStats.Text =
                $"Vértices: {_currentModel.TotalVertices:N0} | " +
                $"Triángulos: {_currentModel.TotalTriangles:N0}";
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
            _target = new Vector3((minX + maxX) * .5f, (minY + maxY) * .5f, (minZ + maxZ) * .5f);
            float ext = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            _zoom = Math.Max(ext * 1.8f, 0.5f);
            _yaw = 0.5f;
            _pitch = 0.22f;
        }

        // ── Render loop ───────────────────────────────────────────────────────

        private void GlControl_Render(TimeSpan delta)
        {
            if (!_glReady)
            {
                InitGL();
                _glReady = true;
            }

            GL.Viewport(0, 0, (int)GlControl.ActualWidth, (int)GlControl.ActualHeight);
            GL.ClearColor(_bgColor);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            float aspect = (float)Math.Max(GlControl.ActualWidth, 1) /
                           (float)Math.Max(GlControl.ActualHeight, 1);

            Vector3 camPos = GetCameraPos();
            var view = Matrix4.LookAt(camPos, _target, Vector3.UnitY);
            var proj = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.DegreesToRadians(45f), aspect, 0.005f, 500f);
            var vp = view * proj;

            // Grid
            if (_showGrid) DrawGrid(vp);

            if (_currentModel == null) return;

            GL.UseProgram(_shaderProgram);

            var model = Matrix4.Identity;
            var normalMat = new Matrix3(Matrix4.Transpose(Matrix4.Invert(model)));

            SetMat4("uMVP", model * view * proj);
            SetMat4("uModel", model);
            SetMat3("uNormalMat", normalMat);
            SetVec3("uCamPos", camPos);
            GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uRenderMode"), _renderMode);

            if (_renderMode == 2)
            {
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uWireframe"), 1);
            }
            else
            {
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uWireframe"), 0);
            }
            GL.Disable(EnableCap.CullFace);

            GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uDiffuseMap"), 0);
            GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uUseTexture"), 0);

            foreach (var mesh in _currentModel.Meshes)
            {
                int texId = -1;
                if (mesh.MaterialIndex >= 0 &&
                    mesh.MaterialIndex < _currentModel.Materials.Count)
                    texId = _currentModel.Materials[mesh.MaterialIndex].GlAlbedoTextureId;

                if (texId > 0 && _renderMode == 0)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, texId);
                    GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uUseTexture"), 1);
                }
                else
                {
                    GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uUseTexture"), 0);
                }

                GL.BindVertexArray(mesh.VaoId);
                GL.DrawElements(PrimitiveType.Triangles,
                    mesh.Indices.Length, DrawElementsType.UnsignedInt, 0);
            }

            GL.BindVertexArray(0);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }

        // ── GL init ───────────────────────────────────────────────────────────

        private void InitGL()
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _shaderProgram = CompileProgram(VERT_SRC, FRAG_SRC);
            _gridProg = CompileProgram(GRID_VERT, GRID_FRAG);
            BuildGrid();
        }

        // ── Grid ──────────────────────────────────────────────────────────────

        private void BuildGrid()
        {
            var lines = new System.Collections.Generic.List<float>();
            float h = 3f;
            for (int i = -20; i <= 20; i++)
            {
                if (i == 0) continue;
                float v = i * (h / 20f);
                lines.AddRange(new[] { v, 0f, -h, v, 0f, h });
                lines.AddRange(new[] { -h, 0f, v, h, 0f, v });
            }
            var axis = new[] {
                -h,0f,0f,  h,0f,0f,
                0f,-h,0f,  0f,h,0f,
                0f,0f,-h,  0f,0f,h
            };

            var mg = lines.ToArray(); _gridN = mg.Length / 3;
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
            GL.UseProgram(_gridProg);
            SetMat4Grid("uVP", vp);

            GL.Uniform4(GL.GetUniformLocation(_gridProg, "uColor"),
                0.18f, 0.18f, 0.22f, 0.5f);
            GL.BindVertexArray(_gridVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _gridN);

            GL.BindVertexArray(_axisVao);
            GL.Uniform4(GL.GetUniformLocation(_gridProg, "uColor"), 0.9f, 0.2f, 0.2f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
            GL.Uniform4(GL.GetUniformLocation(_gridProg, "uColor"), 0.2f, 0.85f, 0.2f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 2, 2);
            GL.Uniform4(GL.GetUniformLocation(_gridProg, "uColor"), 0.2f, 0.4f, 0.95f, 0.9f);
            GL.DrawArrays(PrimitiveType.Lines, 4, 2);
            GL.BindVertexArray(0);
        }

        // ── Shaders ───────────────────────────────────────────────────────────

        private const string VERT_SRC = @"
#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNorm;
layout(location=2) in vec2 aUV;
uniform mat4 uMVP; uniform mat4 uModel; uniform mat3 uNormalMat;
out vec3 vNorm; out vec2 vUV; out vec3 vWorldPos;
void main(){
    vWorldPos = (uModel * vec4(aPos,1.0)).xyz;
    vNorm = normalize(uNormalMat * aNorm);
    vUV   = vec2(aUV.x, 1.0 - aUV.y);
    gl_Position = uMVP * vec4(aPos,1.0);
}";

        private const string FRAG_SRC = @"
#version 330 core
in vec3 vNorm; in vec2 vUV; in vec3 vWorldPos;
out vec4 FragColor;
uniform sampler2D uDiffuseMap;
uniform int  uUseTexture;
uniform int  uRenderMode;
uniform bool uWireframe;
uniform vec3 uCamPos;

void main(){
    if(uWireframe){ FragColor = vec4(0.08,0.65,1.0,1.0); return; }

    vec3 N = normalize(vNorm);
    vec3 V = normalize(uCamPos - vWorldPos);

    if(uRenderMode == 3){ FragColor = vec4(N*0.5+0.5,1.0); return; }

    vec3 albedo = (uUseTexture==1 && uRenderMode==0)
        ? texture(uDiffuseMap, vUV).rgb
        : vec3(0.62,0.60,0.58);

    // Key light (cenital cálida)
    vec3  L1 = normalize(vec3(0.6, 1.4, 0.8));
    float d1 = max(dot(N, L1), 0.0);
    vec3  c1 = vec3(1.05, 0.98, 0.90);

    // Fill lateral frío
    vec3  L2 = normalize(vec3(-0.8, 0.3, -0.6));
    float d2 = max(dot(N, L2), 0.0) * 0.35;
    vec3  c2 = vec3(0.65, 0.70, 0.80);

    // Bounce desde el suelo
    vec3  L3 = normalize(vec3(0.0, -1.0, 0.2));
    float d3 = max(dot(N, L3), 0.0) * 0.12;
    vec3  c3 = vec3(0.40, 0.35, 0.30);

    // Specular Blinn-Phong
    vec3  H  = normalize(L1 + V);
    float sp = pow(max(dot(N,H),0.0), 64.0);
    float F0 = 0.04;
    float cosV = max(dot(N,V),0.0);
    float fresn = F0 + (1.0-F0)*pow(1.0-cosV,5.0);
    vec3 specTerm = vec3(sp * fresn * 0.5);

    // Rim sutil
    float rim   = pow(1.0 - cosV, 3.0) * 0.15;
    vec3 rimCol = vec3(0.55, 0.60, 0.70) * rim;

    vec3 ambient = albedo * vec3(0.08, 0.09, 0.11);
    vec3 diff    = albedo * (d1*c1 + d2*c2 + d3*c3);
    vec3 color   = ambient + diff + specTerm + rimCol;

    // ACES tonemap
    color = color*(color+0.0245786)/(color*(0.983729*color+0.432951)+0.238081);
    color = pow(clamp(color,0.0,1.0), vec3(1.0/2.2));
    FragColor = vec4(color, 1.0);
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

        private static int CompileProgram(string vert, string frag)
        {
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, vert); GL.CompileShader(vs);

            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, frag); GL.CompileShader(fs);

            int prog = GL.CreateProgram();
            GL.AttachShader(prog, vs); GL.AttachShader(prog, fs);
            GL.LinkProgram(prog);
            GL.DeleteShader(vs); GL.DeleteShader(fs);
            return prog;
        }

        // ── Uniform helpers ───────────────────────────────────────────────────

        private void SetMat4(string n, Matrix4 m)
        {
            int loc = GL.GetUniformLocation(_shaderProgram, n);
            GL.UniformMatrix4(loc, false, ref m);
        }
        private void SetMat3(string n, Matrix3 m)
        {
            int loc = GL.GetUniformLocation(_shaderProgram, n);
            GL.UniformMatrix3(loc, false, ref m);
        }
        private void SetVec3(string n, Vector3 v) =>
            GL.Uniform3(GL.GetUniformLocation(_shaderProgram, n), v);
        private void SetMat4Grid(string n, Matrix4 m)
        {
            int loc = GL.GetUniformLocation(_gridProg, n);
            GL.UniformMatrix4(loc, false, ref m);
        }

        // ── Cámara ────────────────────────────────────────────────────────────

        private Vector3 GetCameraPos()
        {
            float cp = MathF.Cos(_pitch);
            return _target + _zoom * new Vector3(
                cp * MathF.Sin(_yaw),
                MathF.Sin(_pitch),
                cp * MathF.Cos(_yaw));
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
                Vector3 camPos = GetCameraPos();
                Vector3 forward = Vector3.Normalize(_target - camPos);
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                Vector3 up = Vector3.Cross(right, forward);
                float speed = _zoom * 0.002f;
                _target -= right * dx * speed;
                _target += up * dy * speed;
            }

            _lastMousePos = pos;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released) _isDraggingCam = false;
            if (e.MiddleButton == MouseButtonState.Released) _isDraggingPan = false;
            if (!_isDraggingCam && !_isDraggingPan)
                this.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            float factor = e.Delta > 0 ? 0.85f : 1.15f;
            _zoom = Math.Clamp(_zoom * factor, 0.05f, 500f);
        }

        // ── Controles de la barra inferior ────────────────────────────────────

        private void SetRenderMode(int mode)
        {
            _renderMode = mode;
            foreach (var btn in new[] { BtnTexture, BtnSolid, BtnWireframe, BtnNormals })
            {
                btn.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(40, 40, 40, 60));
                btn.BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(80, 80, 96));
                btn.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(176, 176, 192));
            }
            var active = mode switch { 0 => BtnTexture, 1 => BtnSolid, 2 => BtnWireframe, _ => BtnNormals };
            active.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(64, 26, 95, 180));
            active.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(90, 143, 221));
            active.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(216, 232, 255));
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
            {
                _bgColor = (ci.Tag as string) switch
                {
                    "dark" => new Color4(0.07f, 0.07f, 0.10f, 1f),
                    "grey" => new Color4(0.25f, 0.25f, 0.27f, 1f),
                    "blue" => new Color4(0.05f, 0.08f, 0.18f, 1f),
                    "black" => new Color4(0.01f, 0.01f, 0.01f, 1f),
                    _ => new Color4(0.07f, 0.07f, 0.10f, 1f)
                };
            }
        }
    }
}
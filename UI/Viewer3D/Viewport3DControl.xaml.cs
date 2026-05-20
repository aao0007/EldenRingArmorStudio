using EldenRingArmorStudio.Core;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EldenRingArmorStudio.UI.Viewer3D
{
    public partial class Viewport3DControl : UserControl
    {
        private FlverModel _currentModel;
        private int _shaderProgram;

        // Cámara
        private float _yaw = 0.5f, _pitch = 0.22f, _zoom = 3.2f;
        private Vector3 _target = new Vector3(0, 1f, 0);
        private Point _lastMousePos;
        private bool _isDragging;

        public Viewport3DControl()
        {
            InitializeComponent();
            var settings = new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3, GraphicsProfile = OpenTK.Windowing.Common.ContextProfile.Core };
            GLControl.Start(settings);
            InitializeShaders();
        }

        // ── 1. Cargar el Modelo desde la memoria principal a la Tarjeta Gráfica ──
        public void LoadModel(FlverModel model)
        {
            if (model == null) return;
            _currentModel = model;

            // 1. Cargar texturas de los materiales a OpenGL
            foreach (var mat in _currentModel.Materials)
            {
                if (mat.AlbedoData != null)
                {
                    mat.GlAlbedoTextureId = TextureManager.LoadDdsTextureFromBytes(mat.AlbedoData);
                }
            }

            // 2. Cargar vértices de las sub-mallas a OpenGL
            foreach (var mesh in _currentModel.Meshes)
            {
                mesh.VaoId = GL.GenVertexArray();
                mesh.VboId = GL.GenBuffer();
                mesh.EboId = GL.GenBuffer();

                GL.BindVertexArray(mesh.VaoId);

                // Subir Vértices (Struct Size: 12 floats * 4 bytes = 48 bytes)
                GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.VboId);
                int vertexSize = Marshal.SizeOf<FlverVertex>();
                GL.BufferData(BufferTarget.ArrayBuffer, mesh.Vertices.Length * vertexSize, mesh.Vertices, BufferUsageHint.StaticDraw);

                // Subir Índices
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, mesh.EboId);
                GL.BufferData(BufferTarget.ElementArrayBuffer, mesh.Indices.Length * sizeof(uint), mesh.Indices, BufferUsageHint.StaticDraw);

                // Configurar Layout (Posición: loc 0, Normal: loc 1, UV: loc 2)
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vertexSize, 0);
                GL.EnableVertexAttribArray(0);

                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, vertexSize, 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);

                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, vertexSize, 6 * sizeof(float));
                GL.EnableVertexAttribArray(2);

                GL.BindVertexArray(0);
            }

            TxtStats.Text = $"Vértices: {_currentModel.TotalVertices:N0} | Triángulos: {_currentModel.TotalTriangles:N0}";

            // Autoenfoque de cámara
            _zoom = 3.0f;
            _target = new Vector3(0, 1, 0);
        }

        // ── 2. Bucle de Dibujado (Render Loop) ──
        private void GlControl_Render(TimeSpan delta)
        {
            GL.ClearColor(0.08f, 0.08f, 0.10f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            if (_currentModel == null || _currentModel.Meshes.Count == 0) return;

            GL.UseProgram(_shaderProgram);

            // Calcular Matrices de Cámara
            Vector3 camPos = GetCameraPos();
            Matrix4 view = Matrix4.LookAt(camPos, _target, Vector3.UnitY);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(45f), (float)GlControl.ActualWidth / (float)GlControl.ActualHeight, 0.01f, 500f);
            Matrix4 modelMatrix = Matrix4.Identity;

            Matrix4 mvp = modelMatrix * view * projection;

            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "uMVP"), false, ref mvp);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram, "uModel"), false, ref modelMatrix);

            // Dibujar cada malla
            foreach (var mesh in _currentModel.Meshes)
            {
                // Configurar Textura del Material
                int useTextureLoc = GL.GetUniformLocation(_shaderProgram, "uUseTexture");
                int texHandle = -1;

                if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < _currentModel.Materials.Count)
                {
                    texHandle = _currentModel.Materials[mesh.MaterialIndex].GlAlbedoTextureId;
                }

                if (texHandle != -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, texHandle);
                    GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uDiffuseMap"), 0);
                    GL.Uniform1(useTextureLoc, 1); // 1 = True
                }
                else
                {
                    GL.Uniform1(useTextureLoc, 0); // 0 = False (Color por defecto)
                }

                GL.BindVertexArray(mesh.VaoId);
                GL.DrawElements(PrimitiveType.Triangles, mesh.Indices.Length, DrawElementsType.UnsignedInt, 0);
            }
            GL.BindVertexArray(0);
        }

        // ── 3. Shaders (Manejan la textura y la luz) ──
        private void InitializeShaders()
        {
            string vertexShaderSource = @"
                #version 330 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNorm;
                layout(location = 2) in vec2 aUV;

                uniform mat4 uMVP;
                uniform mat4 uModel;

                out vec3 vNorm;
                out vec2 vUV;

                void main() {
                    vNorm = mat3(transpose(inverse(uModel))) * aNorm;
                    // Elden Ring/DirectX usualmente tienen la V invertida respecto a OpenGL
                    vUV = vec2(aUV.x, 1.0 - aUV.y); 
                    gl_Position = uMVP * vec4(aPos, 1.0);
                }
            ";

            string fragmentShaderSource = @"
                #version 330 core
                in vec3 vNorm;
                in vec2 vUV;

                out vec4 FragColor;

                uniform sampler2D uDiffuseMap;
                uniform int uUseTexture;

                void main() {
                    vec4 baseColor = vec4(0.5, 0.5, 0.5, 1.0);
                    if (uUseTexture == 1) {
                        baseColor = texture(uDiffuseMap, vUV);
                    }

                    // Iluminación suave (Diffuse/Phong básico)
                    vec3 N = normalize(vNorm);
                    vec3 L = normalize(vec3(1.0, 1.5, 1.0));
                    float diff = max(dot(N, L), 0.2); // 0.2 Ambient
                    
                    FragColor = vec4(baseColor.rgb * diff, baseColor.a);
                }
            ";

            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);

            _shaderProgram = GL.CreateProgram();
            GL.AttachShader(_shaderProgram, vertexShader);
            GL.AttachShader(_shaderProgram, fragmentShader);
            GL.LinkProgram(_shaderProgram);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        // ── 4. Controles de Cámara ──
        private Vector3 GetCameraPos()
        {
            return _target + _zoom * new Vector3(
                (float)(Math.Cos(_pitch) * Math.Sin(_yaw)),
                (float)Math.Sin(_pitch),
                (float)(Math.Cos(_pitch) * Math.Cos(_yaw)));
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _lastMousePos = e.GetPosition(this);
                this.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPos = e.GetPosition(this);
                float dx = (float)(currentPos.X - _lastMousePos.X);
                float dy = (float)(currentPos.Y - _lastMousePos.Y);

                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    // Orbitar
                    _yaw -= dx * 0.01f;
                    _pitch += dy * 0.01f;
                    _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);
                }
                else if (e.MiddleButton == MouseButtonState.Pressed)
                {
                    // Paneo
                    Vector3 camPos = GetCameraPos();
                    Vector3 forward = Vector3.Normalize(_target - camPos);
                    Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                    Vector3 up = Vector3.Cross(right, forward);

                    float speed = _zoom * 0.002f;
                    _target -= right * dx * speed;
                    _target += up * dy * speed;
                }

                _lastMousePos = currentPos;
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            this.ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            float factor = e.Delta > 0 ? 0.85f : 1.15f;
            _zoom = Math.Clamp(_zoom * factor, 0.1f, 300f);
        }
    }

    internal class GLWpfControlSettings
    {
        public int MajorVersion { get; set; }
        public int MinorVersion { get; set; }
        public OpenTK.Windowing.Common.ContextProfile GraphicsProfile { get; set; }
    }
}
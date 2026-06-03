using EldenRingArmorStudio.Core;
using OpenTK.Mathematics;
using OpenTK.Wpf;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EldenRingArmorStudio.UI.Viewer3D
{
    public partial class Viewport3DControl : UserControl
    {
        // ── Único renderer — contiene todos los shaders y la GPU ──────────────
        private FlverRenderer _renderer;
        private bool _glReady = false;

        // ── Cámara input ──────────────────────────────────────────────────────
        private Point _lastMousePos;
        private bool _isDragging;

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
            // NO llamamos nada GL aquí — el contexto no existe todavía.
            // Se inicializa en el primer GlControl_Render.
        }

        // ── Carga de modelo ───────────────────────────────────────────────────

        public void LoadModel(FlverModel model)
        {
            if (model == null) return;

            // Si el GL todavía no está listo (aún no se disparó Render),
            // creamos e inicializamos el renderer ahora que el contexto existe
            // porque LoadModel se llama siempre desde el hilo UI con GL activo.
            if (!_glReady)
            {
                _renderer = new FlverRenderer();
                _renderer.Initialize();
                _glReady = true;
            }

            _renderer.LoadModel(model);
            TxtStats.Text = _renderer.StatsText;
        }

        // ── Loop GL ───────────────────────────────────────────────────────────

        private void GlControl_Render(TimeSpan delta)
        {
            if (!_glReady)
            {
                _renderer = new FlverRenderer();
                _renderer.Initialize();
                _glReady = true;
            }

            int w = Math.Max((int)GlControl.ActualWidth, 1);
            int h = Math.Max((int)GlControl.ActualHeight, 1);
            _renderer.Render(w, h);
        }

        // ── Botones de la barra inferior ──────────────────────────────────────

        private void OnRenderTexture(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;
            _renderer.RenderMode = 0;
            SetActiveButton(BtnTexture);
        }

        private void OnRenderSolid(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;
            _renderer.RenderMode = 1;
            SetActiveButton(BtnSolid);
        }

        private void OnRenderWireframe(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;
            _renderer.RenderMode = 2;
            SetActiveButton(BtnWireframe);
        }

        private void OnRenderNormals(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;
            _renderer.RenderMode = 3;
            SetActiveButton(BtnNormals);
        }

        private void OnGridToggle(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;
            _renderer.ShowGrid = ChkGrid.IsChecked == true;
        }

        private void OnResetCamera(object sender, RoutedEventArgs e)
        {
            if (_renderer == null) return;
            _renderer.Yaw = 0.5f;
            _renderer.Pitch = 0.22f;
            _renderer.Zoom = 3.2f;
            _renderer.Target = new Vector3(0, 1, 0);
        }

        private void OnBgChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_renderer == null) return;
            if (ComboBg.SelectedItem is ComboBoxItem item)
                _renderer.BgPreset = item.Tag as string ?? "dark";
        }

        // ── Visual: resalta botón activo ──────────────────────────────────────

        private void SetActiveButton(Button active)
        {
            var all = new[] { BtnTexture, BtnSolid, BtnWireframe, BtnNormals };
            foreach (var btn in all)
            {
                bool on = btn == active;
                btn.Background = new SolidColorBrush(on
                    ? Color.FromArgb(0x40, 0x1A, 0x5F, 0xB4)
                    : Color.FromRgb(0x28, 0x28, 0x2C));
                btn.BorderBrush = new SolidColorBrush(on
                    ? Color.FromRgb(0x5A, 0x8F, 0xDD)
                    : Color.FromRgb(0x50, 0x50, 0x60));
            }
        }

        // ── Mouse: orbitar / pan / zoom ───────────────────────────────────────

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed ||
                e.MiddleButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _lastMousePos = e.GetPosition(this);
                CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _renderer == null) return;

            Point cur = e.GetPosition(this);
            float dx = (float)(cur.X - _lastMousePos.X);
            float dy = (float)(cur.Y - _lastMousePos.Y);

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _renderer.Yaw -= dx * 0.01f;
                _renderer.Pitch += dy * 0.01f;
                _renderer.Pitch = Math.Clamp(_renderer.Pitch, -1.5f, 1.5f);
            }
            else if (e.MiddleButton == MouseButtonState.Pressed)
            {
                var cam = _renderer.CamPos();
                var forward = Vector3.Normalize(_renderer.Target - cam);
                var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                var up = Vector3.Cross(right, forward);
                float speed = _renderer.Zoom * 0.002f;
                _renderer.Target -= right * (dx * speed);
                _renderer.Target += up * (dy * speed);
            }

            _lastMousePos = cur;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_renderer == null) return;
            float factor = e.Delta > 0 ? 0.85f : 1.15f;
            _renderer.Zoom = Math.Clamp(_renderer.Zoom * factor, 0.1f, 300f);
        }
    }
}
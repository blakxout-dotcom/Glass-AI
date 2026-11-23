using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace GlassSidebar
{
    // Simple damped spring for buttery motion with a hint of overshoot.
    internal struct Spring
    {
        public double X, V, Target;
        public void Step(double dt, double k, double zeta)
        {
            double c = 2 * Math.Sqrt(k) * zeta;
            int sub = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
            double h = dt / sub;
            for (int i = 0; i < sub; i++)
            {
                double a = -k * (X - Target) - c * V;
                V += a * h; X += V * h;
            }
        }
        public bool Settled { get { return Math.Abs(X - Target) < 0.5 && Math.Abs(V) < 5; } }
        public void Snap(double v) { X = Target = v; V = 0; }
    }

    internal sealed partial class SidebarWindow : Window
    {
        const double PanelWidthDip = 420, GapDip = 12, RadiusDip = 30, FullInsetDip = 14;

        enum Mode { Hidden, Sidebar, Full }
        Mode mode = Mode.Hidden;
        bool hiding, pinned, suppressDeactivate, rendering;
        int side = 1;
        Drawing.Rectangle monBounds, monWork;
        Drawing.Rectangle winRect;  // window bounds in physical px
        double scale = 1;           // DIP -> px
        IntPtr hwnd;

        Spring sx, sy, sw, sh;
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastT;
        double tintA = 0.3;

        readonly ScreenCapture capture = new ScreenCapture();
        WriteableBitmap wb;
        bool excludeOk;

        // UI elements from ui.xaml
        Grid root, panel, content, chatView;
        FrameworkElement composer;
        Canvas stage;
        Image glassImage;
        Border rim;
        ScaleTransform panelScale;
        Button pinBtn, fullBtn;

        public SidebarWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = true;
            Title = "Glass";
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui.xaml"))
                root = (Grid)XamlReader.Load(s);
            Content = root;
            stage = F<Canvas>("Stage");
            panel = F<Grid>("Panel");
            content = F<Grid>("Content");
            chatView = F<Grid>("ChatView");
            composer = F<FrameworkElement>("Composer");
            glassImage = F<Image>("GlassImage");
            rim = F<Border>("Rim");
            panelScale = (ScaleTransform)root.FindName("PanelScale");
            pinBtn = F<Button>("PinBtn");
            fullBtn = F<Button>("FullBtn");
            rim.CornerRadius = new CornerRadius(RadiusDip);
            F<Border>("Shadow").CornerRadius = new CornerRadius(RadiusDip);

            F<Button>("CloseBtn").Click += delegate { HidePanel(); };
            fullBtn.Click += delegate { ToggleFull(); };
            pinBtn.Click += delegate
            {
                pinned = !pinned;
                pinBtn.Content = pinned ? "" : "";
                pinBtn.ToolTip = pinned ? "Unpin" : "Pin (stay open)";
                pinBtn.Content = pinned ? "" : "";
            };

            pinBtn.Click += delegate { pinBtn.Content = G(pinned ? 0xE840 : 0xE718); }; // pinned / unpinned icon

            InitChat();

            SourceInitialized += OnSourceInitialized;
            Deactivated += delegate
            {
                if (!pinned && !suppressDeactivate && mode != Mode.Hidden) HidePanel();
            };
            PreviewKeyDown += OnPreviewKeyDown;
        }

        T F<T>(string name) where T : class { return (T)root.FindName(name); }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            var src = HwndSource.FromHwnd(hwnd);
            scale = src.CompositionTarget.TransformToDevice.M11;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW);
            // The key trick: our window is invisible to screen capture, so we can
            // capture what's *behind* it every frame and refract it in real time.
            excludeOk = Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
            if (!excludeOk) // pre-2004 Windows: no live capture possible, fall back to a tinted panel
                panel.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1E, 0x28));
            src.AddHook(WndProc);
            Native.RegisterHotKey(hwnd, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20);
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wp.ToInt32() == 1)
            {
                if (mode == Mode.Hidden || hiding)
                {
                    Native.POINT p; Native.GetCursorPos(out p);
                    var scr = Forms.Screen.FromPoint(new Drawing.Point(p.X, p.Y));
                    ShowPanel(1, scr);
                }
                else HidePanel();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ---------------- show / hide / fullscreen ----------------

        public void OnSwipe(int swipeSide, Drawing.Rectangle bounds)
        {
            var scr = Forms.Screen.FromRectangle(bounds);
            if (mode == Mode.Hidden || hiding) { ShowPanel(swipeSide, scr); return; }
            if (mode == Mode.Full) return;
            if (swipeSide == side) { HidePanel(); return; }
            mode = Mode.Hidden; hiding = false; Hide();
            ShowPanel(swipeSide, scr);
        }

        public void ShowPanel(int newSide, Forms.Screen scr)
        {
            bool wasHidden = mode == Mode.Hidden;
            side = newSide;
            monBounds = scr.Bounds;
            monWork = scr.WorkingArea;
            mode = Mode.Sidebar;
            hiding = false;
            SetWindowRect(StripRect());

            var shown = SidebarTarget();
            var off = HiddenTarget();
            if (wasHidden)
            {
                sx.Snap(off.X); sy.Snap(off.Y); sw.Snap(off.Width); sh.Snap(off.Height);
                tintA = 0.3;
            }
            SetTargets(shown);
            SetFullUi(false);
            StartRendering();
            Show();
            ForceForeground();
            InputFocus();
        }

        public void HidePanel()
        {
            if (mode == Mode.Hidden) return;
            if (mode == Mode.Full)
            {
                // collapse back to the sidebar first, then slide away
                mode = Mode.Sidebar;
                SetFullUi(false);
            }
            hiding = true;
            SetTargets(HiddenTarget());
        }

        void ToggleFull()
        {
            if (mode == Mode.Full)
            {
                mode = Mode.Sidebar;
                SetTargets(SidebarTarget());
                SetFullUi(false);
            }
            else if (mode == Mode.Sidebar)
            {
                mode = Mode.Full;
                SetWindowRect(monBounds);       // grow the window first, then the glass grows inside it
                SetTargets(FullTarget());
                SetFullUi(true);
            }
        }

        void SetFullUi(bool full)
        {
            fullBtn.Content = full ? "" : "";
            fullBtn.ToolTip = full ? "Exit fullscreen" : "Fullscreen";
            fullBtn.Content = full ? "" : "";
            double max = full ? 820 : double.PositiveInfinity;
            chatView.MaxWidth = max; composer.MaxWidth = max;
            fullBtn.Content = G(full ? 0xE73F : 0xE740); // back-to-window / fullscreen icon
            ShowChatsPane(full);
            F<ScrollViewer>("SettingsView").MaxWidth = full ? 560 : double.PositiveInfinity;
        }

        Drawing.Rectangle StripRect()
        {
            int w = (int)Math.Ceiling((PanelWidthDip + GapDip * 2 + 80) * scale);
            int x = side > 0 ? monWork.Right - w : monWork.Left;
            return new Drawing.Rectangle(x, monWork.Top, w, monWork.Height);
        }

        Rect SidebarTarget()
        {
            double w = PanelWidthDip * scale, g = GapDip * scale;
            double x = side > 0 ? monWork.Right - g - w : monWork.Left + g;
            return new Rect(x, monWork.Top + g, w, monWork.Height - 2 * g);
        }

        Rect HiddenTarget()
        {
            var r = SidebarTarget();
            r.X += side * (r.Width + GapDip * scale + 60 * scale);
            return r;
        }

        Rect FullTarget()
        {
            double i = FullInsetDip * scale;
            return new Rect(monBounds.Left + i, monBounds.Top + i, monBounds.Width - 2 * i, monBounds.Height - 2 * i);
        }

        void SetTargets(Rect r) { sx.Target = r.X; sy.Target = r.Y; sw.Target = r.Width; sh.Target = r.Height; }

        void SetWindowRect(Drawing.Rectangle r)
        {
            winRect = r;
            Left = r.Left / scale; Top = r.Top / scale;
            Width = r.Width / scale; Height = r.Height / scale;
        }

        void ForceForeground()
        {
            // Windows blocks focus stealing; a synthetic Alt tap unlocks SetForegroundWindow.
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            Activate();
            if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
        }

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, UIntPtr extra);

        void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (InSettings) { ShowSettings(false); return; }
            if (mode == Mode.Full) { ToggleFull(); return; }
            HidePanel();
        }

        // ---------------- per-frame: springs + live refraction ----------------

        void StartRendering()
        {
            if (rendering) return;
            rendering = true;
            lastT = clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnFrame;
        }

        void StopRendering()
        {
            if (!rendering) return;
            rendering = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        void OnFrame(object sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, Math.Max(0.001, t - lastT));
            lastT = t;

            // snappy, slightly bouncy slide; size morphs a touch softer
            double zeta = hiding ? 1.0 : 0.72;
            sx.Step(dt, 210, zeta); sy.Step(dt, 210, 0.85);
            sw.Step(dt, 170, 0.82); sh.Step(dt, 170, 0.82);

            if (hiding && sx.Settled)
            {
                hiding = false; mode = Mode.Hidden;
                StopRendering(); Hide();
                return;
            }
            if (mode == Mode.Sidebar && winRect.Width > StripRect().Width && sw.Settled && sh.Settled)
                SetWindowRect(StripRect());     // fullscreen collapsed: shrink window back to the strip

            // liquid squash & stretch from horizontal velocity
            double v = Math.Min(1, Math.Abs(sx.V) / (4000 * scale));
            panelScale.ScaleX = 1 + 0.06 * v;
            panelScale.ScaleY = 1 - 0.035 * v;

            int px = (int)Math.Round(sx.X), py = (int)Math.Round(sy.X);
            int pw = Math.Max(40, (int)Math.Round(sw.X)), ph = Math.Max(40, (int)Math.Round(sh.X));

            Canvas.SetLeft(panel, (px - winRect.Left) / scale);
            Canvas.SetTop(panel, (py - winRect.Top) / scale);
            panel.Width = pw / scale; panel.Height = ph / scale;

            RenderGlass(px, py, pw, ph);
        }

        void RenderGlass(int px, int py, int pw, int ph)
        {
            double s = (double)pw * ph > 900000 ? 0.5 : 1.0; // half-res for big surfaces
            float bezel = (float)(20 * scale);
            float amp = (float)(26 * scale);
            int blur = (int)Math.Round(Math.Max(0, settings.Blur) * scale * s);
            int M = (int)Math.Ceiling(amp * 1.2 + 6 + settings.Blur * scale);

            int cx = px - M, cy = py - M, cw = pw + 2 * M, ch = ph + 2 * M;
            capture.Capture(cx, cy, cw, ch, s);
            int bw = capture.W, bh = capture.H;

            // Only the on-screen part of the capture is valid; samples clamp to it.
            var vs = Forms.SystemInformation.VirtualScreen;
            var v = new ValidRect();
            v.X0 = Clamp((int)Math.Ceiling((vs.Left - cx) * s), 0, bw - 1);
            v.Y0 = Clamp((int)Math.Ceiling((vs.Top - cy) * s), 0, bh - 1);
            v.X1 = Clamp((int)Math.Floor((vs.Right - cx) * s), v.X0 + 1, bw);
            v.Y1 = Clamp((int)Math.Floor((vs.Bottom - cy) * s), v.Y0 + 1, bh);

            if (!excludeOk) { glassImage.Source = null; return; }

            Glass.Blur(capture.Bits, capture.Temp, bw, bh, v, blur);

            // your tint strength, nudged deeper over bright backdrops so white text stays readable
            double lum = Glass.AverageLuminance(capture.Bits, bw, v);
            double targetTint = Math.Min(0.95, settings.TintStrength + 0.32 * Math.Max(0, lum - 0.4));
            tintA += (targetTint - tintA) * 0.12;

            int ow = Math.Max(1, (int)Math.Round(pw * s)), oh = Math.Max(1, (int)Math.Round(ph * s));
            if (wb == null || wb.PixelWidth != ow || wb.PixelHeight != oh)
            {
                wb = new WriteableBitmap(ow, oh, 96, 96, PixelFormats.Pbgra32, null);
                glassImage.Source = wb;
            }
            wb.Lock();
            Glass.Refract(capture.Bits, bw, v, (int)Math.Round(M * s), wb.BackBuffer, wb.BackBufferStride, ow, oh,
                          (float)(RadiusDip * scale * s), (float)(bezel * s), (float)(amp * s), (float)(scale * s),
                          (float)tintA, (byte)settings.TintR, (byte)settings.TintG, (byte)settings.TintB);
            wb.AddDirtyRect(new Int32Rect(0, 0, ow, oh));
            wb.Unlock();
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        protected override void OnClosed(EventArgs e)
        {
            StopRendering();
            if (hwnd != IntPtr.Zero) Native.UnregisterHotKey(hwnd, 1);
            capture.Dispose();
            base.OnClosed(e);
        }
    }
}

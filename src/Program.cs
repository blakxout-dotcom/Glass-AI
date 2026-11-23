using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace GlassSidebar
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool fresh;
            using (var mutex = new Mutex(true, "GlassSidebar.SingleInstance", out fresh))
            {
                if (!fresh) return;
                Native.SetProcessDPIAware();

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var win = new SidebarWindow();
                new WindowInteropHelper(win).EnsureHandle(); // hooks up hotkey + capture exclusion now

                var swipe = new SwipeDetector();
                swipe.Sensitivity = win.settings.Sensitivity;
                win.SettingsChanged += delegate { swipe.Sensitivity = win.settings.Sensitivity; };
                swipe.Swiped += delegate (int side, Drawing.Rectangle bounds)
                {
                    win.Dispatcher.BeginInvoke(new Action(delegate { win.OnSwipe(side, bounds); }));
                };

                var tray = new Forms.NotifyIcon();
                tray.Icon = MakeIcon();
                tray.Text = "Glass — flick cursor to a screen edge";
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("Open sidebar (Ctrl+Alt+Space)", null, delegate { OpenFromTray(win); });
                menu.Items.Add("-");
                menu.Items.Add("Quit", null, delegate { tray.Visible = false; swipe.Dispose(); win.Close(); app.Shutdown(); });
                tray.ContextMenuStrip = menu;
                tray.MouseClick += delegate (object s, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) OpenFromTray(win); };
                tray.Visible = true;
                tray.ShowBalloonTip(3500, "Glass is running", "Flick your cursor hard into the left or right edge of the screen, or press Ctrl+Alt+Space.", Forms.ToolTipIcon.None);

                app.Run();
                tray.Dispose();
            }
        }

        static void OpenFromTray(SidebarWindow win)
        {
            Native.POINT p; Native.GetCursorPos(out p);
            win.ShowPanel(1, Forms.Screen.FromPoint(new Drawing.Point(p.X, p.Y)));
        }

        // Little glassy orb for the tray.
        static Drawing.Icon MakeIcon()
        {
            var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new Drawing.Drawing2D.LinearGradientBrush(new Drawing.Rectangle(2, 2, 28, 28),
                       Drawing.Color.FromArgb(255, 154, 230, 255), Drawing.Color.FromArgb(255, 182, 156, 255), 45f))
                    g.FillEllipse(b, 2, 2, 28, 28);
                using (var hl = new Drawing.SolidBrush(Drawing.Color.FromArgb(170, 255, 255, 255)))
                    g.FillEllipse(hl, 8, 6, 12, 7);
                using (var pen = new Drawing.Pen(Drawing.Color.FromArgb(200, 255, 255, 255), 1.5f))
                    g.DrawEllipse(pen, 2.5f, 2.5f, 27, 27);
            }
            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }
    }
}

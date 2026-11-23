using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GlassSidebar
{
    // Glass tint / blur controls. Everything applies live (the render loop reads settings every frame)
    // and is saved automatically a moment after you stop fiddling.
    internal sealed partial class SidebarWindow
    {
        static readonly object[][] TintPresets =
        {
            new object[] { "Semi-gray", 92, 94, 100 },
            new object[] { "Graphite", 30, 32, 38 },
            new object[] { "Smoke", 140, 142, 148 },
            new object[] { "Midnight", 22, 34, 70 },
            new object[] { "Ocean", 18, 78, 118 },
            new object[] { "Violet", 72, 44, 124 },
            new object[] { "Rose", 132, 44, 82 },
            new object[] { "Forest", 28, 82, 60 },
            new object[] { "Amber", 128, 82, 22 },
        };
        const double DefaultTintStrength = 0.42, DefaultBlur = 9;

        Slider tintSlider, blurSlider;
        TextBox tintHex;
        DispatcherTimer appearanceSave;
        bool syncingAppearance;

        void InitAppearance()
        {
            tintSlider = F<Slider>("TintSlider");
            blurSlider = F<Slider>("BlurSlider");
            tintHex = F<TextBox>("TintHex");

            var wrap = F<WrapPanel>("TintSwatches");
            foreach (var p in TintPresets)
            {
                string name = (string)p[0];
                byte r = (byte)(int)p[1], g = (byte)(int)p[2], b = (byte)(int)p[3];
                var dot = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                    Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
                };
                var ring = new Border
                {
                    Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Child = dot,
                    BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Tag = Color.FromRgb(r, g, b)
                };
                var btn = new Button { Content = ring, ToolTip = name, Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(0, 0, 4, 4) };
                btn.Template = BareTemplate();
                btn.Click += delegate { SetTint(r, g, b); };
                wrap.Children.Add(btn);
            }

            tintSlider.ValueChanged += delegate
            {
                if (syncingAppearance) return;
                settings.TintStrength = tintSlider.Value;
                RefreshAppearanceLabels(); QueueAppearanceSave();
            };
            blurSlider.ValueChanged += delegate
            {
                if (syncingAppearance) return;
                settings.Blur = Math.Round(blurSlider.Value);
                RefreshAppearanceLabels(); QueueAppearanceSave();
            };
            tintHex.KeyDown += delegate (object s, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; } };
            tintHex.LostKeyboardFocus += delegate { ApplyHex(); };
            F<Button>("GlassReset").Click += delegate
            {
                settings.TintStrength = DefaultTintStrength;
                settings.Blur = DefaultBlur;
                SetTint(92, 94, 100);
                SyncAppearanceControls();
            };

            appearanceSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            appearanceSave.Tick += delegate { appearanceSave.Stop(); try { settings.Save(); } catch { } };

            SyncAppearanceControls();
        }

        static ControlTemplate BareTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            t.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            return t;
        }

        void SetTint(byte r, byte g, byte b)
        {
            settings.TintR = r; settings.TintG = g; settings.TintB = b;
            SyncAppearanceControls();
            QueueAppearanceSave();
        }

        void ApplyHex()
        {
            string h = tintHex.Text.Trim().TrimStart('#');
            int v;
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if (h.Length == 6 && int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                SetTint((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            else SyncAppearanceControls(); // revert bad input
        }

        void SyncAppearanceControls()
        {
            syncingAppearance = true;
            tintSlider.Value = settings.TintStrength;
            blurSlider.Value = settings.Blur;
            syncingAppearance = false;

            var c = Color.FromRgb((byte)settings.TintR, (byte)settings.TintG, (byte)settings.TintB);
            if (!tintHex.IsKeyboardFocused)
                tintHex.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
            F<Border>("TintHexDot").Background = new SolidColorBrush(c);
            foreach (var child in F<WrapPanel>("TintSwatches").Children)
            {
                var ring = (Border)((Button)child).Content;
                ring.BorderBrush = ((Color)ring.Tag) == c ? Brushes.White : Brushes.Transparent;
            }
            RefreshAppearanceLabels();
        }

        void RefreshAppearanceLabels()
        {
            F<TextBlock>("TintValue").Text = Math.Round(settings.TintStrength / 0.85 * 100) + "%";
            F<TextBlock>("BlurValue").Text = settings.Blur <= 0 ? "Off" : Math.Round(settings.Blur) + " px";
        }

        void QueueAppearanceSave()
        {
            appearanceSave.Stop();
            appearanceSave.Start();
        }
    }
}

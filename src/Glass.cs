using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GlassSidebar
{
    // Grabs the pixels behind the glass. Our own window is excluded from capture
    // (WDA_EXCLUDEFROMCAPTURE), so this sees the live desktop underneath us.
    internal sealed class ScreenCapture : IDisposable
    {
        IntPtr memDC, dib, oldObj;
        public IntPtr Bits;
        public int W, H;
        uint[] temp;
        GCHandle tempHandle;

        void Ensure(int w, int h)
        {
            if (w == W && h == H && dib != IntPtr.Zero) return;
            Free();
            W = w; H = h;
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            memDC = Native.CreateCompatibleDC(screen);
            var bmi = new Native.BITMAPINFOHEADER();
            bmi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bmi.biWidth = w; bmi.biHeight = -h; bmi.biPlanes = 1; bmi.biBitCount = 32;
            dib = Native.CreateDIBSection(screen, ref bmi, 0, out Bits, IntPtr.Zero, 0);
            oldObj = Native.SelectObject(memDC, dib);
            Native.ReleaseDC(IntPtr.Zero, screen);
            temp = new uint[w * h];
            tempHandle = GCHandle.Alloc(temp, GCHandleType.Pinned);
        }

        // Captures screen rect (sx,sy,sw,sh) into a buffer scaled by 'scale'.
        public void Capture(int sx, int sy, int sw, int sh, double scale)
        {
            int bw = Math.Max(1, (int)Math.Round(sw * scale));
            int bh = Math.Max(1, (int)Math.Round(sh * scale));
            Ensure(bw, bh);
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (bw == sw && bh == sh)
                Native.BitBlt(memDC, 0, 0, bw, bh, screen, sx, sy, Native.SRCCOPY);
            else
            {
                Native.SetStretchBltMode(memDC, 3); // COLORONCOLOR: fast
                Native.StretchBlt(memDC, 0, 0, bw, bh, screen, sx, sy, sw, sh, Native.SRCCOPY);
            }
            Native.ReleaseDC(IntPtr.Zero, screen);
        }

        public IntPtr Temp { get { return tempHandle.AddrOfPinnedObject(); } }

        void Free()
        {
            if (memDC != IntPtr.Zero) { Native.SelectObject(memDC, oldObj); Native.DeleteDC(memDC); memDC = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
            if (tempHandle.IsAllocated) tempHandle.Free();
            W = H = 0;
        }

        public void Dispose() { Free(); }
    }

    internal struct ValidRect { public int X0, Y0, X1, Y1; } // inclusive-exclusive, buffer px

    internal static unsafe class Glass
    {
        // ---------- Blur (separable box blur, run twice ~ gaussian) ----------
        public static void Blur(IntPtr a, IntPtr b, int w, int h, ValidRect v, int radius)
        {
            if (radius < 1) return;
            for (int i = 0; i < 2; i++)
            {
                BlurH(a, b, w, v, radius);
                BlurV(b, a, w, v, radius);
            }
        }

        static void BlurH(IntPtr srcP, IntPtr dstP, int w, ValidRect v, int r)
        {
            int x0 = v.X0, x1 = v.X1 - 1;
            float inv = 1f / (2 * r + 1);
            Parallel.For(v.Y0, v.Y1, y =>
            {
                uint* src = (uint*)srcP + y * w;
                uint* dst = (uint*)dstP + y * w;
                int sr = 0, sg = 0, sb = 0;
                for (int k = -r; k <= r; k++)
                {
                    uint c = src[Clamp(x0 + k, x0, x1)];
                    sr += (int)((c >> 16) & 255); sg += (int)((c >> 8) & 255); sb += (int)(c & 255);
                }
                for (int x = x0; x <= x1; x++)
                {
                    dst[x] = 0xFF000000u | ((uint)(sr * inv) << 16) | ((uint)(sg * inv) << 8) | (uint)(sb * inv);
                    uint cOut = src[Clamp(x - r, x0, x1)];
                    uint cIn = src[Clamp(x + r + 1, x0, x1)];
                    sr += (int)((cIn >> 16) & 255) - (int)((cOut >> 16) & 255);
                    sg += (int)((cIn >> 8) & 255) - (int)((cOut >> 8) & 255);
                    sb += (int)(cIn & 255) - (int)(cOut & 255);
                }
            });
        }

        static void BlurV(IntPtr srcP, IntPtr dstP, int w, ValidRect v, int r)
        {
            int y0 = v.Y0, y1 = v.Y1 - 1;
            float inv = 1f / (2 * r + 1);
            Parallel.For(v.X0, v.X1, x =>
            {
                uint* src = (uint*)srcP + x;
                uint* dst = (uint*)dstP + x;
                int sr = 0, sg = 0, sb = 0;
                for (int k = -r; k <= r; k++)
                {
                    uint c = src[Clamp(y0 + k, y0, y1) * w];
                    sr += (int)((c >> 16) & 255); sg += (int)((c >> 8) & 255); sb += (int)(c & 255);
                }
                for (int y = y0; y <= y1; y++)
                {
                    dst[y * w] = 0xFF000000u | ((uint)(sr * inv) << 16) | ((uint)(sg * inv) << 8) | (uint)(sb * inv);
                    uint cOut = src[Clamp(y - r, y0, y1) * w];
                    uint cIn = src[Clamp(y + r + 1, y0, y1) * w];
                    sr += (int)((cIn >> 16) & 255) - (int)((cOut >> 16) & 255);
                    sg += (int)((cIn >> 8) & 255) - (int)((cOut >> 8) & 255);
                    sb += (int)(cIn & 255) - (int)(cOut & 255);
                }
            });
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // Average luminance (0..1) of the backdrop, sparse sampled. Used to adapt the tint.
        public static double AverageLuminance(IntPtr bits, int w, ValidRect v)
        {
            uint* p = (uint*)bits;
            double sum = 0; int n = 0;
            int step = Math.Max(4, (v.X1 - v.X0) / 24);
            for (int y = v.Y0; y < v.Y1; y += step)
                for (int x = v.X0; x < v.X1; x += step)
                {
                    uint c = p[y * w + x];
                    sum += 0.299 * ((c >> 16) & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * (c & 255);
                    n++;
                }
            return n == 0 ? 0.5 : sum / n / 255.0;
        }

        // ---------- Liquid glass refraction ----------
        // src: blurred backdrop (bw x bh), output pixel (x,y) maps to src (x+m, y+m).
        // The panel is a rounded rect; near the rim a convex "bezel" bends light outward,
        // so content from beyond the edge is pulled into the rim, with RGB split (dispersion)
        // and a specular rim light.
        public static void Refract(IntPtr srcP, int bw, ValidRect v, int m,
                                   IntPtr dstP, int dstStride, int ow, int oh,
                                   float radius, float bezel, float amp, float scale,
                                   float tintA, byte tintR, byte tintG, byte tintB)
        {
            float hw = ow * 0.5f, hh = oh * 0.5f;
            float r = Math.Min(radius, Math.Min(hw, hh));
            const float Lx = -0.6f, Ly = -0.8f; // light from top-left
            float rimW = 1.4f * scale + 0.6f;
            int vx0 = v.X0, vx1 = v.X1 - 1, vy0 = v.Y0, vy1 = v.Y1 - 1;
            float sat = 1.18f;

            Parallel.For(0, oh, y =>
            {
                uint* src = (uint*)srcP;
                uint* row = (uint*)((byte*)dstP + y * dstStride);
                float py = y + 0.5f - hh;
                float ay = Math.Abs(py), sy = py < 0 ? -1f : 1f;
                float qy = ay - (hh - r);
                float sheen = 0.085f * (float)Math.Pow(1f - (float)y / oh, 2.2); // glossy top, fading down

                for (int x = 0; x < ow; x++)
                {
                    float px = x + 0.5f - hw;
                    float ax = Math.Abs(px), sx = px < 0 ? -1f : 1f;
                    float qx = ax - (hw - r);
                    float sd, nx, ny;
                    if (qx > 0 && qy > 0)
                    {
                        float len = (float)Math.Sqrt(qx * qx + qy * qy);
                        sd = len - r; nx = qx / len * sx; ny = qy / len * sy;
                    }
                    else if (qx > qy) { sd = qx - r; nx = sx; ny = 0; }
                    else { sd = qy - r; nx = 0; ny = sy; }

                    float d = -sd;
                    float cov = d + 0.5f;
                    if (cov <= 0f) { row[x] = 0; continue; }
                    if (cov > 1f) cov = 1f;

                    int bx = x + m, by = y + m;
                    float cr, cg, cb, spec;
                    if (d < bezel)
                    {
                        float t = 1f - d / bezel;
                        float disp = amp * (1f - (float)Math.Sqrt(1f - t * t));
                        uint pr = Sample(src, bw, bx + nx * disp * 1.14f, by + ny * disp * 1.14f, vx0, vx1, vy0, vy1);
                        uint pg = Sample(src, bw, bx + nx * disp, by + ny * disp, vx0, vx1, vy0, vy1);
                        uint pb = Sample(src, bw, bx + nx * disp * 0.86f, by + ny * disp * 0.86f, vx0, vx1, vy0, vy1);
                        cr = (pr >> 16) & 255; cg = (pg >> 8) & 255; cb = pb & 255;

                        float dir = nx * Lx + ny * Ly;
                        float lit = Math.Max(dir, -dir * 0.55f);
                        float rim = d < rimW ? 1f - d / rimW : 0f;
                        spec = rim * (0.30f + 0.70f * lit) + t * t * t * 0.26f * lit;
                    }
                    else
                    {
                        uint p = src[Clamp(by, vy0, vy1) * bw + Clamp(bx, vx0, vx1)];
                        cr = (p >> 16) & 255; cg = (p >> 8) & 255; cb = p & 255;
                        spec = 0f;
                    }

                    // vibrancy, then tint for legibility
                    float lum = 0.299f * cr + 0.587f * cg + 0.114f * cb;
                    cr = lum + (cr - lum) * sat; cg = lum + (cg - lum) * sat; cb = lum + (cb - lum) * sat;
                    cr += (tintR - cr) * tintA; cg += (tintG - cg) * tintA; cb += (tintB - cb) * tintA;

                    // thickness: real glass looks a bit darker just inside its edge (inner shadow),
                    // strongest on the side facing away from the light
                    float band = bezel * 1.8f;
                    if (d < band)
                    {
                        float k = 1f - d / band;
                        float away = Math.Max(0f, -(nx * Lx + ny * Ly));
                        float shade = k * k * (0.10f + 0.12f * away);
                        cr *= 1f - shade; cg *= 1f - shade; cb *= 1f - shade;
                    }
                    spec += sheen;
                    if (spec > 1f) spec = 1f;
                    cr += (255f - cr) * spec; cg += (255f - cg) * spec; cb += (255f - cb) * spec;

                    float a = cov * 255f;
                    uint ir = (uint)Clamp((int)(cr * cov), 0, 255);
                    uint ig = (uint)Clamp((int)(cg * cov), 0, 255);
                    uint ib = (uint)Clamp((int)(cb * cov), 0, 255);
                    row[x] = ((uint)a << 24) | (ir << 16) | (ig << 8) | ib;
                }
            });
        }

        static uint Sample(uint* src, int w, float fx, float fy, int x0, int x1, int y0, int y1)
        {
            int ix = Clamp((int)(fx + 0.5f), x0, x1);
            int iy = Clamp((int)(fy + 0.5f), y0, y1);
            return src[iy * w + ix];
        }
    }
}

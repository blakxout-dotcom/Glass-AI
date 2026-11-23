using System;
using System.Threading;
using Forms = System.Windows.Forms;

namespace GlassSidebar
{
    // Watches the cursor at ~250 Hz. When it slams into the left/right edge of a monitor
    // fast enough (a "flick"), raises Swiped(side, monitorBounds).
    internal sealed class SwipeDetector : IDisposable
    {
        public event Action<int, System.Drawing.Rectangle> Swiped; // side: -1 left, +1 right
        public int Sensitivity = 1;

        const int N = 64;
        readonly long[] ts = new long[N];
        readonly int[] xs = new int[N];
        int head, count;
        bool wasAtEdge;
        long lastFire;
        volatile bool running = true;
        readonly Thread thread;

        public SwipeDetector()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "SwipeDetector" };
            thread.Start();
        }

        void Loop()
        {
            Native.timeBeginPeriod(1);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (running)
            {
                Thread.Sleep(4);
                Native.POINT p;
                if (!Native.GetCursorPos(out p)) continue;
                long now = sw.ElapsedMilliseconds;
                head = (head + 1) % N; ts[head] = now; xs[head] = p.X;
                if (count < N) count++;

                var bounds = Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).Bounds;
                int side = p.X <= bounds.Left + 1 ? -1 : (p.X >= bounds.Right - 2 ? 1 : 0);
                bool atEdge = side != 0;

                if (atEdge && !wasAtEdge && now - lastFire > 700 && !ButtonsDown())
                {
                    // Look back ~90 ms: how far did the cursor travel toward this edge?
                    int oldX = p.X; long oldT = now;
                    for (int i = 1; i < count; i++)
                    {
                        int idx = (head - i + N) % N;
                        if (now - ts[idx] > 90) break;
                        oldX = xs[idx]; oldT = ts[idx];
                    }
                    double dt = Math.Max(1, now - oldT) / 1000.0;
                    double travel = (p.X - oldX) * side;          // positive = toward edge
                    double speed = travel / dt;                   // px/s
                    double w = bounds.Width;
                    double[] speedK = { 2.4, 1.6, 1.05 };           // screen-widths per second
                    double[] travelK = { 0.16, 0.10, 0.06 };
                    int s = Math.Max(0, Math.Min(2, Sensitivity));
                    if (speed > w * speedK[s] && travel > w * travelK[s])
                    {
                        lastFire = now;
                        var h = Swiped;
                        if (h != null) h(side, bounds);
                    }
                }
                wasAtEdge = atEdge;
            }
            Native.timeEndPeriod(1);
        }

        static bool ButtonsDown()
        {
            return (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0 ||
                   (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0;
        }

        public void Dispose() { running = false; }
    }
}

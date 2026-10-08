// Masaüstü Perdesi — ekranı istediğin kadar örten, elle çekilen lale desenli bir kumaş perde.
// Sağ/sol Ctrl tuşuna iki kez bas: o taraftan perde kanadı gelir ya da (PIN ile) toplanır.  Ctrl+Alt+Shift+P: programı kapat
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using WinTimer = System.Windows.Forms.Timer;

[assembly: System.Reflection.AssemblyTitle("Desktop Curtain")]
[assembly: System.Reflection.AssemblyProduct("Desktop Curtain")]

namespace MasaustuPerdesi
{
    static class Native
    {
        public const int WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

        public const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int val);

        public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);

        // Odak kilidine takılmadan pencereyi öne getirir.
        public static void ForceForeground(IntPtr h)
        {
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
            bool attached = fg != me && AttachThreadInput(me, fg, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, fg, false);
        }
    }

    static class Ui
    {
        public static float S = 1f;
        public static void Init() { using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f; }
        public static int D(double v) { return (int)Math.Round(v * S); }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static readonly Color Bg = Color.FromArgb(24, 21, 22);
        public static readonly Color Border = Color.FromArgb(58, 50, 50);
        public static readonly Color Text1 = Color.FromArgb(245, 238, 232);
        public static readonly Color Text2 = Color.FromArgb(170, 158, 150);
        public static readonly Color Accent = Color.FromArgb(214, 150, 128);
        public static readonly Color Error = Color.FromArgb(240, 104, 104);
    }

    // ---------------------------------------------------------------- ayarlar
    static class Config
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetEnvironmentVariable("PERDE_DATA_DIR") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasaustuPerdesi");
        static string PinPath { get { return Path.Combine(Dir, "pin.dat"); } }

        public static bool HasPin { get { return File.Exists(PinPath); } }

        public static void SetPin(string pin)
        {
            Directory.CreateDirectory(Dir);
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            File.WriteAllText(PinPath, Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(Hash(pin, salt)));
        }

        public static bool Verify(string pin)
        {
            try
            {
                string[] parts = File.ReadAllText(PinPath).Trim().Split(':');
                byte[] salt = Convert.FromBase64String(parts[0]);
                byte[] expected = Convert.FromBase64String(parts[1]);
                byte[] actual = Hash(pin, salt);
                int diff = expected.Length ^ actual.Length;
                for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++) diff |= expected[i] ^ actual[i];
                return diff == 0;
            }
            catch { return false; }
        }

        static byte[] Hash(string pin, byte[] salt)
        {
            using (var kdf = new Rfc2898DeriveBytes(pin, salt, 50000)) return kdf.GetBytes(32);
        }
    }

    // ---------------------------------------------------------------- masaüstü simgeleri
    static class DesktopIcons
    {
        static bool hiddenByUs;
        static string FlagPath { get { return Path.Combine(Config.Dir, "simgeler_gizli.flag"); } }

        static IntPtr FindListView()
        {
            IntPtr defView = Native.FindWindowEx(Native.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    IntPtr d = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (d != IntPtr.Zero) { defView = d; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            return defView == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        public static void Hide()
        {
            IntPtr lv = FindListView();
            if (lv == IntPtr.Zero || !Native.IsWindowVisible(lv)) return;
            Native.ShowWindow(lv, 0);
            hiddenByUs = true;
            try { Directory.CreateDirectory(Config.Dir); File.WriteAllText(FlagPath, "1"); } catch { }
        }

        public static void Restore()
        {
            if (!hiddenByUs) return;
            IntPtr lv = FindListView();
            if (lv != IntPtr.Zero) Native.ShowWindow(lv, 5);
            hiddenByUs = false;
            try { File.Delete(FlagPath); } catch { }
        }

        // Program beklenmedik şekilde kapandıysa simgeleri geri getir.
        public static void RecoverAfterCrash()
        {
            if (File.Exists(FlagPath)) { hiddenByUs = true; Restore(); }
        }
    }

    // ---------------------------------------------------------------- perde çizimi
    static class CurtainArt
    {
        // Yerleşim: korniş borusu, halkalar ve kumaşın asıldığı çizgi.
        public static int RodY { get { return Ui.D(8); } }
        public static int RodH { get { return Ui.D(9); } }
        public static int RingTop { get { return Ui.D(2); } }
        public static int RingH { get { return Ui.D(21); } }
        public static int FabricTop { get { return Ui.D(20); } }
        public static int TasselLen { get { return Ui.D(60); } }

        static readonly Color Cream = Color.FromArgb(240, 215, 186);
        static readonly Color Cream2 = Color.FromArgb(228, 194, 160);
        static readonly Color Ink = Color.FromArgb(30, 25, 25);

        // Lale renkleri (üst, alt)
        static readonly Color[] Rose = { Color.FromArgb(204, 134, 112), Color.FromArgb(150, 78, 70) };
        static readonly Color[] Mauve = { Color.FromArgb(160, 92, 104), Color.FromArgb(102, 44, 62) };
        static readonly Color[] Plum = { Color.FromArgb(112, 48, 72), Color.FromArgb(60, 20, 38) };
        static readonly Color[] Wine = { Color.FromArgb(136, 60, 78), Color.FromArgb(78, 26, 42) };

        public static Bitmap RenderFabric(int w, int h)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                Image custom = LoadCustomPattern();
                int yb = h;
                if (custom != null)
                {
                    using (custom) DrawCustom(g, custom, w, h);
                }
                else
                {
                    int chainH = Ui.D(46), wovenH = Ui.D(112), gap = Ui.D(14);
                    int chain2 = yb - chainH, woven = chain2 - wovenH, chain1 = woven - chainH, patternBottom = chain1 - gap;

                    using (var bg = new LinearGradientBrush(new Rectangle(0, 0, 10, h), Cream, Cream2, 90f))
                        g.FillRectangle(bg, 0, 0, w, yb);
                    using (var tile = RenderTulipTile())
                    using (var tb = new TextureBrush(tile, WrapMode.Tile))
                    {
                        tb.TranslateTransform(0, patternBottom);
                        g.FillRectangle(tb, 0, 0, w, patternBottom);
                    }
                    DrawChain(g, w, chain1, chainH);
                    DrawWoven(g, w, woven, wovenH);
                    DrawChain(g, w, chain2, chainH);
                }
                DrawFolds(g, w, yb);
                // Üst kenar: halkaların tuttuğu kalın baş kısmı.
                using (var head = new LinearGradientBrush(new Rectangle(0, 0, 10, Ui.D(14)), Color.FromArgb(70, 60, 30, 15), Color.FromArgb(0, 60, 30, 15), 90f))
                    g.FillRectangle(head, 0, 0, w, Ui.D(14));
            }
            return bmp;
        }

        // Korniş: ekran boyunca bronz boru ve uç topuzları. Arka planı şeffaf.
        public static Bitmap RenderRod(int w)
        {
            int h = Ui.D(26);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int ry = RodY, rh = RodH;
                using (var rb = new LinearGradientBrush(new Rectangle(0, ry, 10, rh), Color.Black, Color.Black, 90f))
                {
                    var cb = new ColorBlend();
                    cb.Colors = new[] { Color.FromArgb(70, 54, 38), Color.FromArgb(184, 152, 108), Color.FromArgb(96, 72, 50), Color.FromArgb(44, 32, 22) };
                    cb.Positions = new[] { 0f, .35f, .7f, 1f };
                    rb.InterpolationColors = cb;
                    g.FillRectangle(rb, 0, ry, w, rh);
                }
                int fd = Ui.D(18), fy = ry + rh / 2 - fd / 2;
                using (var fb = new LinearGradientBrush(new Rectangle(0, fy, 10, fd), Color.FromArgb(214, 184, 136), Color.FromArgb(58, 42, 28), 90f))
                {
                    g.FillEllipse(fb, Ui.D(1), fy, fd, fd);
                    g.FillEllipse(fb, w - fd - Ui.D(1), fy, fd, fd);
                }
            }
            return bmp;
        }

        // Halkaların x konumları: kumaşın kenarından sağa, perde toplandıkça sıklaşır.
        public static List<float> RingPositions(double fabricX, int screenW, double spacing)
        {
            var xs = new List<float>();
            for (double x = fabricX + Ui.D(8); x < screenW - Ui.D(22); x += spacing) xs.Add((float)x);
            return xs;
        }

        // Boruyu saran bronz halkalar ve kumaşa tutturan küçük kancalar.
        public static void DrawRings(Graphics g, List<float> xs)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rw = Ui.D(13), top = RingTop, rh = RingH;
            using (var back = new Pen(Color.FromArgb(70, 52, 36), Math.Max(2f, 2.6f * Ui.S)))
            using (var shine = new Pen(Color.FromArgb(200, 214, 184, 136), Math.Max(1f, 1.1f * Ui.S)))
            using (var hook = new SolidBrush(Color.FromArgb(60, 46, 32)))
                foreach (float x in xs)
                {
                    g.DrawEllipse(back, x - rw / 2, top, rw, rh);
                    g.DrawArc(shine, x - rw / 2 + 1, top + 1, rw - 2, rh - 2, 200, 80);
                    g.FillRectangle(hook, x - Ui.D(1.5), top + rh - Ui.D(1), Ui.D(3), Ui.D(5));
                }
        }

        // Kumaşın üst kenarı: halkalarda yukarı tutulur, aralarda hafifçe sarkar.
        public static GraphicsPath HeadingPath(List<float> rings, float x0, float x1, float baseY)
        {
            float sag = Ui.D(5);
            var pts = new List<PointF>();
            pts.Add(new PointF(x0, baseY + sag));
            float prev = x0;
            foreach (float rx in rings)
            {
                AddSag(pts, prev, rx, baseY, sag);
                prev = rx;
            }
            AddSag(pts, prev, x1, baseY, sag);
            pts.Add(new PointF(x1, baseY + sag));
            var p = new GraphicsPath();
            p.AddLines(pts.ToArray());
            p.AddLine(x1, baseY + sag, x0, baseY + sag);
            p.CloseFigure();
            return p;
        }

        static void AddSag(List<PointF> pts, float a, float b, float y, float sag)
        {
            if (b - a < 1) { pts.Add(new PointF(b, y)); return; }
            float depth = Math.Min(sag, (b - a) * 0.12f);
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f;
                pts.Add(new PointF(a + (b - a) * t, y + depth * (float)Math.Sin(Math.PI * t)));
            }
        }

        // Tek bir gerçekçi püskül: ip, parlak topuz, bilezik ve iplik iplik etek.
        // Asma noktası görüntünün üst ortasında (w/2, 0).
        public static Bitmap RenderTassel(int seed)
        {
            int w = Ui.D(26), h = TasselLen + Ui.D(4);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var r = new Random(seed);
            float cx = w / 2f;
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cordEnd = Ui.D(10), headH = Ui.D(12), headW = Ui.D(11);
                using (var cord = new Pen(Color.FromArgb(34, 28, 28), Math.Max(1.5f, 2f * Ui.S)))
                    g.DrawBezier(cord, cx, 0, cx - Ui.D(1), cordEnd * .4f, cx + Ui.D(1), cordEnd * .7f, cx, cordEnd + 1);

                // Etek: üstte dar, altta açılan, hafif kıvrık iplikler.
                float skirtTop = cordEnd + headH - Ui.D(2), skirtBot = h - Ui.D(3);
                int strands = 22;
                for (int i = 0; i < strands; i++)
                {
                    float u = (i + 0.5f) / strands * 2 - 1;             // -1..1
                    float topX = cx + u * Ui.D(3.5);
                    float botX = cx + u * Ui.D(9.5) + (float)(r.NextDouble() - .5) * Ui.D(1.5);
                    float len = skirtBot - (float)r.NextDouble() * Ui.D(3);
                    int shade = 26 + r.Next(30);
                    if (Math.Abs(u) < .35) shade += 18;                  // ortası ışık alır
                    using (var pen = new Pen(Color.FromArgb(255, shade + 6, shade, shade), Math.Max(1f, 1.15f * Ui.S)))
                        g.DrawBezier(pen, topX, skirtTop, topX + (botX - topX) * .2f, skirtTop + (len - skirtTop) * .4f,
                            botX + (float)(r.NextDouble() - .5) * Ui.D(2), len - Ui.D(6), botX, len);
                }
                // Topuz
                var headRect = new RectangleF(cx - headW / 2, cordEnd, headW, headH);
                using (var hb = new LinearGradientBrush(headRect, Color.FromArgb(96, 86, 82), Color.FromArgb(16, 13, 13), 60f))
                    g.FillEllipse(hb, headRect);
                using (var gl = new SolidBrush(Color.FromArgb(120, 255, 250, 240)))
                    g.FillEllipse(gl, cx - headW * .28f, cordEnd + headH * .18f, headW * .22f, headH * .2f);
                // Bilezik
                using (var band = new LinearGradientBrush(new RectangleF(cx - Ui.D(5), skirtTop - 1, Ui.D(10), Ui.D(4)), Color.FromArgb(110, 98, 92), Color.FromArgb(28, 24, 24), 90f))
                    g.FillRectangle(band, cx - Ui.D(4.5), skirtTop - Ui.D(1), Ui.D(9), Ui.D(3.5));
            }
            return bmp;
        }

        // Kapanan kenardaki kilit rozeti.
        public static void DrawLockBadge(Graphics g, int cx, int cy, bool locked)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int r = Ui.D(15);
            using (var bg = new SolidBrush(Color.FromArgb(215, 36, 28, 28)))
                g.FillEllipse(bg, cx - r, cy - r, r * 2, r * 2);
            Color ink = locked ? Color.FromArgb(240, 215, 186) : Color.FromArgb(150, 200, 150);
            using (var body = new SolidBrush(ink))
            using (var pen = new Pen(ink, Math.Max(1.5f, 2f * Ui.S)))
            {
                int bw = Ui.D(12), bh = Ui.D(9);
                g.FillRectangle(body, cx - bw / 2, cy - Ui.D(1), bw, bh);
                int sw = Ui.D(8), sh = Ui.D(10);
                // Açıkken halka yana kalkık durur.
                int sx = locked ? cx - sw / 2 : cx - sw / 2 + Ui.D(5);
                g.DrawArc(pen, sx, cy - Ui.D(9), sw, sh, 180, 180);
                g.DrawLine(pen, sx, cy - Ui.D(4), sx, cy - Ui.D(1) - (locked ? 0 : Ui.D(3)));
                g.DrawLine(pen, sx + sw, cy - Ui.D(4), sx + sw, cy - Ui.D(1));
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        // Kapanan kenara gölge: kumaşın öne doğru kıvrıldığı hissi.
        public static void ShadeLeadingEdge(Bitmap fabric)
        {
            int ew = Ui.D(26), hh = fabric.Height;
            using (var g = Graphics.FromImage(fabric))
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, ew, 10), Color.FromArgb(110, 40, 20, 10), Color.FromArgb(0, 40, 20, 10), 0f))
                g.FillRectangle(br, 0, 0, ew, hh);
        }

        // ---- desen karosu: krem zemin üzerinde lale demetleri (yarım kaydırmalı tekrar)
        const float TW = 380, TH = 600;

        static Bitmap RenderTulipTile()
        {
            float scale = 1.1f * Ui.S;
            int bw = (int)Math.Round(TW * scale), bh = (int)Math.Round(TH * scale);
            var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(bw / TW, bh / TH);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        DrawPlant(g, dx * TW, dy * TH, false, 0);
                        DrawPlant(g, dx * TW + TW / 2, dy * TH + TH / 2, true, 1);
                    }
            }
            return bmp;
        }

        static void DrawPlant(Graphics g, float ox, float oy, bool mirror, int variant)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(ox + (mirror ? 190 : 0), oy);
            if (mirror) g.ScaleTransform(-1, 1);

            var root = new PointF(95, 600);
            Leaf(g, root, new PointF(14, 352), 13, 10);
            Leaf(g, root, new PointF(180, 392), 12, -11);
            Leaf(g, new PointF(98, 470), new PointF(186, 236), 9, -8);
            Leaf(g, new PointF(90, 384), new PointF(6, 188), 9, 9);
            Leaf(g, new PointF(100, 300), new PointF(176, 128), 8, -22);
            Leaf(g, new PointF(88, 470), new PointF(-14, 520), 8, 16);

            Stem(g, new PointF(104, 420), new PointF(168, 478), 18);
            Stem(g, root, new PointF(70, 162), -38);
            Stem(g, root, new PointF(132, 308), 30);
            Stem(g, root, new PointF(62, 440), -24);
            Stem(g, new PointF(112, 336), new PointF(150, 98), 22);
            Stem(g, new PointF(84, 402), new PointF(30, 264), -16);

            Berries(g, new PointF(168, 206), variant * 10 + 1);
            Berries(g, new PointF(24, 334), variant * 10 + 2);
            Berries(g, new PointF(176, 586), variant * 10 + 3);
            Berries(g, new PointF(44, 56), variant * 10 + 4);

            Bud(g, 150, 98, 52, 14, Plum);
            Bud(g, 30, 264, 46, -22, Wine);

            Tulip(g, 70, 162, 78, -10, variant == 0 ? Rose : Mauve);
            Tulip(g, 132, 308, 88, 14, variant == 0 ? Mauve : Rose);
            Tulip(g, 62, 440, 80, -18, variant == 0 ? Wine : Plum);
            Tulip(g, 168, 478, 64, 38, variant == 0 ? Plum : Wine);

            Leaf(g, root, new PointF(150, 512), 9, -6);
            Leaf(g, root, new PointF(36, 530), 8, 6);
            g.Restore(st);
        }

        public static GraphicsPath LensPath(PointF a, PointF b, float width, float curve)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / len, ny = dx / len;
            Func<float, float, PointF> at = (t, off) => new PointF(a.X + dx * t + nx * off, a.Y + dy * t + ny * off);
            var p = new GraphicsPath();
            p.AddBezier(a, at(0.3f, width + curve), at(0.7f, width * 0.8f + curve), b);
            p.AddBezier(b, at(0.7f, -width * 0.8f + curve), at(0.3f, -width + curve), a);
            p.CloseFigure();
            return p;
        }

        static void FillLens(Graphics g, Brush br, PointF a, PointF b, float width, float curve)
        {
            using (var p = LensPath(a, b, width, curve)) g.FillPath(br, p);
        }

        static void Leaf(Graphics g, PointF a, PointF b, float width, float curve)
        {
            using (var br = new LinearGradientBrush(a, b, Color.FromArgb(44, 58, 44), Color.FromArgb(96, 112, 80)))
                FillLens(g, br, a, b, width, curve);
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / len * curve, ny = dx / len * curve;
            using (var pen = new Pen(Color.FromArgb(120, 150, 165, 120), 0.9f))
                g.DrawBezier(pen, a, new PointF(a.X + dx * .3f + nx, a.Y + dy * .3f + ny),
                    new PointF(a.X + dx * .7f + nx, a.Y + dy * .7f + ny), b);
        }

        static void Stem(Graphics g, PointF a, PointF b, float bend)
        {
            float rise = a.Y - b.Y;
            using (var pen = new Pen(Color.FromArgb(58, 70, 52), 3.2f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, a, new PointF(a.X + bend, a.Y - rise * 0.35f), new PointF(b.X - bend * 0.3f, b.Y + rise * 0.35f), b);
            }
        }

        static Color Darken(Color c, float f) { return Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f)); }
        static Color Lighten(Color c, float f) { return Color.FromArgb(c.A, c.R + (int)((255 - c.R) * f), c.G + (int)((255 - c.G) * f), c.B + (int)((255 - c.B) * f)); }

        static void Tulip(Graphics g, float cx, float cy, float s, float angle, Color[] pal)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            Color top = pal[0], bottom = pal[1], dark = Darken(bottom, 0.72f);

            using (var br = new LinearGradientBrush(new PointF(0, -s), new PointF(0, 0.06f * s), Darken(top, 0.86f), dark))
            {
                FillLens(g, br, new PointF(-0.04f * s, 0), new PointF(-0.34f * s, -1.0f * s), 0.30f * s, -0.06f * s);
                FillLens(g, br, new PointF(0.04f * s, 0), new PointF(0.34f * s, -1.0f * s), 0.30f * s, 0.06f * s);
            }
            using (var br = new LinearGradientBrush(new PointF(0, -0.62f * s), new PointF(0, 0.08f * s), top, bottom))
                g.FillEllipse(br, -0.36f * s, -0.62f * s, 0.72f * s, 0.68f * s);
            using (var br = new LinearGradientBrush(new PointF(0, -0.96f * s), new PointF(0, 0.06f * s), Lighten(top, 0.14f), bottom))
                FillLens(g, br, new PointF(0, 0.04f * s), new PointF(0.02f * s, -0.96f * s), 0.33f * s, 0f);

            using (var pen = new Pen(Color.FromArgb(100, dark), Math.Max(0.6f, 0.014f * s)))
            {
                g.DrawBezier(pen, new PointF(-0.02f * s, 0), new PointF(-0.12f * s, -0.3f * s), new PointF(-0.14f * s, -0.6f * s), new PointF(-0.07f * s, -0.86f * s));
                g.DrawBezier(pen, new PointF(0.02f * s, 0), new PointF(0.12f * s, -0.3f * s), new PointF(0.15f * s, -0.6f * s), new PointF(0.09f * s, -0.86f * s));
                g.DrawLine(pen, 0, -0.05f * s, 0.01f * s, -0.82f * s);
            }
            using (var hb = new SolidBrush(Color.FromArgb(46, 255, 240, 228)))
                FillLens(g, hb, new PointF(-0.07f * s, -0.12f * s), new PointF(-0.12f * s, -0.78f * s), 0.045f * s, -0.02f * s);
            using (var sb = new SolidBrush(Color.FromArgb(52, 66, 50)))
                g.FillEllipse(sb, -0.07f * s, -0.04f * s, 0.14f * s, 0.1f * s);
            g.Restore(st);
        }

        static void Bud(Graphics g, float cx, float cy, float s, float angle, Color[] pal)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            using (var br = new LinearGradientBrush(new PointF(0, -s), new PointF(0, 0), Lighten(pal[0], 0.08f), Darken(pal[1], 0.8f)))
                FillLens(g, br, new PointF(0, 0), new PointF(0, -s), 0.21f * s, 0f);
            using (var gb = new SolidBrush(Color.FromArgb(56, 72, 52)))
            {
                FillLens(g, gb, new PointF(0, 0), new PointF(-0.14f * s, -0.36f * s), 0.05f * s, 0f);
                FillLens(g, gb, new PointF(0, 0), new PointF(0.14f * s, -0.36f * s), 0.05f * s, 0f);
            }
            g.Restore(st);
        }

        static void Berries(Graphics g, PointF root, int seed)
        {
            var r = new Random(seed);
            Color[] cols = { Color.FromArgb(112, 40, 58), Color.FromArgb(142, 64, 76), Color.FromArgb(86, 30, 46) };
            var tip = new PointF(root.X + r.Next(-18, 19), root.Y - r.Next(34, 56));
            using (var pen = new Pen(Color.FromArgb(96, 64, 52), 0.9f))
            {
                g.DrawLine(pen, root, tip);
                for (int i = 0; i < 10; i++)
                {
                    float t = (float)r.NextDouble();
                    var on = new PointF(root.X + (tip.X - root.X) * t, root.Y + (tip.Y - root.Y) * t);
                    var end = new PointF(on.X + r.Next(-14, 15), on.Y - r.Next(2, 12));
                    g.DrawLine(pen, on, end);
                    float rad = 2.2f + (float)r.NextDouble() * 1.6f;
                    using (var b = new SolidBrush(cols[r.Next(cols.Length)]))
                        g.FillEllipse(b, end.X - rad, end.Y - rad, rad * 2, rad * 2);
                }
            }
        }

        // ---- alt bordür
        static void DrawChain(Graphics g, int w, int y, int hh)
        {
            using (var bg = new SolidBrush(Cream2)) g.FillRectangle(bg, 0, y, w, hh);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Ink, Math.Max(1.5f, 2.6f * Ui.S)))
            using (var thin = new Pen(Ink, Math.Max(1f, 1.4f * Ui.S)))
            {
                g.DrawLine(thin, 0, y + Ui.D(4), w, y + Ui.D(4));
                g.DrawLine(thin, 0, y + hh - Ui.D(4), w, y + hh - Ui.D(4));
                float ew = Ui.D(30), eh = hh - Ui.D(18), step = Ui.D(21);
                for (float x = -ew; x < w + ew; x += step) g.DrawEllipse(pen, x, y + Ui.D(9), ew, eh);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        static void DrawWoven(Graphics g, int w, int y, int hh)
        {
            using (var bg = new LinearGradientBrush(new Rectangle(0, y, 10, hh), Color.FromArgb(216, 182, 142), Color.FromArgb(194, 158, 118), 90f))
                g.FillRectangle(bg, 0, y, w, hh);
            int st = Math.Max(3, Ui.D(5));
            using (var p1 = new Pen(Color.FromArgb(90, 70, 45, 30)))
            using (var p2 = new Pen(Color.FromArgb(150, 60, 38, 25), Math.Max(1f, 1.5f * Ui.S)))
                for (int x = 0, i = 0; x < w; x += st, i++)
                    g.DrawLine(i % 4 == 0 ? p2 : p1, x, y + Ui.D(15), x, y + hh - Ui.D(15));
            using (var pw = new Pen(Color.FromArgb(40, 255, 240, 220)))
                for (int yy = y + Ui.D(15); yy < y + hh - Ui.D(15); yy += Math.Max(2, Ui.D(3)))
                    g.DrawLine(pw, 0, yy, w, yy);
            using (var ink = new SolidBrush(Ink))
            {
                g.FillRectangle(ink, 0, y + Ui.D(3), w, Math.Max(1, Ui.D(2)));
                g.FillRectangle(ink, 0, y + hh - Ui.D(5), w, Math.Max(1, Ui.D(2)));
                int dw = Math.Max(2, Ui.D(3)), ds = Ui.D(7);
                for (int x = 0; x < w; x += ds)
                {
                    g.FillRectangle(ink, x, y + Ui.D(7), dw, Ui.D(5));
                    g.FillRectangle(ink, x, y + hh - Ui.D(12), dw, Ui.D(5));
                }
            }
        }

        // ---- kumaş kıvrımlarının ışık-gölgesi
        // Kıvrımlar perde toplandıkça sıklaştığı için her karede çizilir; burada yalnızca geniş gölgeler.
        public static LinearGradientBrush FoldBrush(float period, float strength)
        {
            Color clear = Color.FromArgb(0, 128, 128, 128);
            var br = new LinearGradientBrush(new RectangleF(0, 0, period, 10), Color.Black, Color.Black, 0f);
            var cb = new ColorBlend();
            cb.Colors = new[] {
                Color.FromArgb((int)(50 * strength), 40, 20, 10), clear,
                Color.FromArgb((int)(38 * strength), 255, 248, 236), clear,
                Color.FromArgb((int)(50 * strength), 40, 20, 10) };
            cb.Positions = new[] { 0f, .32f, .52f, .72f, 1f };
            br.InterpolationColors = cb;
            br.WrapMode = WrapMode.Tile;
            return br;
        }

        static void DrawFolds(Graphics g, int w, int yb)
        {
            Color clear = Color.FromArgb(0, 128, 128, 128);
            int fw = Ui.D(150);
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, (int)(fw * 2.37), 10), Color.FromArgb(26, 40, 20, 10), clear, 0f))
            {
                br.SetBlendTriangularShape(0.5f);
                br.WrapMode = WrapMode.Tile;
                g.FillRectangle(br, 0, 0, w, yb);
            }
        }

        // ---- kullanıcının kendi deseni (desen.png / desen.jpg)
        static Image LoadCustomPattern()
        {
            string[] dirs = { Config.Dir, AppDomain.CurrentDomain.BaseDirectory };
            string[] names = { "desen.png", "desen.jpg", "desen.jpeg" };
            foreach (string d in dirs)
                foreach (string n in names)
                {
                    string p = Path.Combine(d, n);
                    if (!File.Exists(p)) continue;
                    try { return Image.FromStream(new MemoryStream(File.ReadAllBytes(p))); } catch { }
                }
            return null;
        }

        static void DrawCustom(Graphics g, Image img, int w, int h)
        {
            float sc = (float)h / img.Height;
            int tw = Math.Max(1, (int)(img.Width * sc));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int x = 0; x < w; x += tw) g.DrawImage(img, new Rectangle(x, 0, tw, h));
        }
    }

    // ---------------------------------------------------------------- yarı saydam pencere yüzeyi
    // Piksel başına saydamlık: yumuşak kenarlar, gölgeler ve şeffaf boşluklar.
    class LayeredSurface : IDisposable
    {
        public readonly Bitmap Bitmap;
        readonly IntPtr memDc, hbmp, oldBmp;
        readonly int w, h;

        public LayeredSurface(int w, int h)
        {
            this.w = w; this.h = h;
            var bi = new Native.BITMAPINFOHEADER();
            bi.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32;
            memDc = Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr bits;
            hbmp = Native.CreateDIBSection(memDc, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(memDc, hbmp);
            Bitmap = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        }

        public void Present(IntPtr hwnd, Point at) { Present(hwnd, at, 255); }

        public void Present(IntPtr hwnd, Point at, byte opacity)
        {
            var dst = new Native.POINT { X = at.X, Y = at.Y };
            var src = new Native.POINT();
            var size = new Native.SIZE { cx = w, cy = h };
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }

        public void Dispose()
        {
            Bitmap.Dispose();
            Native.SelectObject(memDc, oldBmp);
            Native.DeleteObject(hbmp);
            Native.DeleteDC(memDc);
        }
    }

    // ---------------------------------------------------------------- korniş penceresi
    // Ekran boyunca uzanan korniş. Tamamen tıklama geçirir: altındaki sekmeler, düğmeler kullanılabilir.
    // Perde pencerelerinin altında durur, böylece halkalar kornişin önünde görünür.
    class RodForm : Form
    {
        readonly LayeredSurface surface;

        public RodForm(Rectangle screen)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = new Rectangle(screen.X, screen.Y, screen.Width, Ui.D(26));
            Text = "Desktop Curtain";
            surface = new LayeredSurface(Width, Height);
            using (Bitmap rod = CurtainArt.RenderRod(Width))
            using (Graphics g = Graphics.FromImage(surface.Bitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(rod, 0, 0, rod.Width, rod.Height);
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); surface.Present(Handle, Location); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) surface.Dispose();
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- perde penceresi
    // Ekranın sağ kenarında toplu duran, halkalarla kornişe asılı bir perde. Tutup sola çekilir;
    // bırakınca kilitlenir. Kilitliyken daha çok kapatmak serbest, geri açmak PIN ister.
    // Dokununca kumaş dalgalanır, püsküller sarkaç gibi sallanır.
    class CurtainForm : Form
    {
        struct Impulse { public double X, T0, Amp; }

        class Tassel
        {
            public double Offset, Angle, Spin, PrevX, PrevV;
            public int Sprite;
            public bool Started;
        }

        readonly int W, H, bundleW, stripW, overlap, pad, fabTop, bodyH, sag;
        readonly Bitmap fabric;
        readonly Bitmap[] tasselSprites;
        readonly List<Tassel> tassels = new List<Tassel>();
        readonly TextureBrush headingBrush;
        readonly LayeredSurface surface;
        readonly WinTimer timer = new WinTimer { Interval = 15 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<Impulse> impulses = new List<Impulse>();
        readonly double[] dx;
        double leftX, target, lockX, grabOffset, lastHoverKick, lastDragKick, lastMoveT, lastTick;
        bool locked, dragging, pinAsked, hiding;
        int lastMouseX;
        Action onHidden;

        public event Action PinRequested;
        public bool Locked { get { return locked; } }

        // Sol kanat: perde mantığı sağ kanatla aynıdır, yalnızca ekran ve fare yatayda aynalanır.
        public readonly bool FromLeft;

        int MX(int x) { return FromLeft ? W - 1 - x : x; }

        public CurtainForm(Rectangle bounds, bool fromLeft)
        {
            FromLeft = fromLeft;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            Text = "Desktop Curtain";
            Cursor = Cursors.Hand;

            W = bounds.Width; H = bounds.Height;
            bundleW = Ui.D(90);
            stripW = Math.Max(6, Ui.D(12));
            overlap = Math.Max(4, Ui.D(6));
            pad = Ui.D(60);
            sag = Ui.D(5);
            fabTop = CurtainArt.FabricTop;
            bodyH = H - fabTop - CurtainArt.TasselLen + Ui.D(4);
            fabric = CurtainArt.RenderFabric(W + pad, bodyH);
            CurtainArt.ShadeLeadingEdge(fabric);
            headingBrush = new TextureBrush(fabric, WrapMode.Tile, new Rectangle(0, 0, fabric.Width, sag * 3));
            tasselSprites = new[] { CurtainArt.RenderTassel(11), CurtainArt.RenderTassel(23), CurtainArt.RenderTassel(37) };
            int spacing = Ui.D(30);
            for (int i = 0; spacing / 2 + i * spacing < W + pad; i++)
            {
                var t = new Tassel();
                t.Offset = spacing / 2 + i * spacing;
                t.Sprite = i % tasselSprites.Length;
                tassels.Add(t);
            }
            dx = new double[(W + pad) / stripW + 4];
            surface = new LayeredSurface(W, H);

            leftX = W;
            target = W - bundleW;
            timer.Tick += Tick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                // Odak çalmaz: perdeyi çekerken arkadaki programda yazmaya devam edebilirsin.
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            lastTick = Now;
            Render();
            Wake();
        }

        double Now { get { return clock.Elapsed.TotalSeconds; } }

        void Wake()
        {
            if (!timer.Enabled) { lastTick = Now; timer.Start(); }
        }

        void Kick(double x, double amp)
        {
            if (impulses.Count >= 10) impulses.RemoveAt(0);
            var im = new Impulse();
            im.X = x; im.T0 = Now; im.Amp = amp;
            impulses.Add(im);
            Wake();
        }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }

        bool Covering(double x) { return W - x > bundleW + Ui.D(24); }

        void SettleLock(double x)
        {
            locked = Covering(x);
            lockX = x;
        }

        public void Unlock()
        {
            locked = false;
            Kick(leftX + Ui.D(30), Ui.D(6));
        }

        public void SlideAway(Action done)
        {
            hiding = true; locked = false; dragging = false;
            onHidden = done;
            target = W + Ui.D(20);
            Wake();
        }

        // ---- fare
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (hiding || e.Button != MouseButtons.Left || MX(e.X) < leftX - Ui.D(12)) return;
            dragging = true;
            pinAsked = false;
            grabOffset = MX(e.X) - leftX;
            Kick(MX(e.X), Ui.D(7));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            double now = Now;
            if (dragging)
            {
                double desired = Clamp(MX(e.X) - grabOffset, 0, W - bundleW);
                if (locked)
                {
                    // Kilitli perde hiçbir yöne gitmez: lastik gibi biraz esner ve PIN ister.
                    double d = desired - lockX;
                    target = lockX + Math.Sign(d) * Ui.D(26) * (1 - Math.Exp(-Math.Abs(d) / Ui.D(70)));
                    if (Math.Abs(d) > Ui.D(30) && !pinAsked)
                    {
                        pinAsked = true;
                        Kick(leftX, Ui.D(8));
                        if (PinRequested != null) PinRequested();
                    }
                }
                else target = desired;
                Wake();
            }
            else if (MX(e.X) >= leftX)
            {
                // Üzerinden geçince fare hızına göre hafif dalga.
                double dt = now - lastMoveT;
                if (dt > 0 && dt < 0.2 && now - lastHoverKick > 0.12)
                {
                    double speed = Math.Abs(MX(e.X) - lastMouseX) / dt;
                    if (speed > 150) { Kick(MX(e.X), Math.Min(Ui.D(5), speed * 0.004)); lastHoverKick = now; }
                }
            }
            lastMouseX = MX(e.X);
            lastMoveT = now;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            if (locked) target = lockX;
            Kick(target, Ui.D(5));
            if (!locked) SettleLock(target);
            Wake();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (hiding || MX(e.X) < leftX) return;
            Kick(MX(e.X), Ui.D(8));
            if (locked) return;
            target = 0;
            SettleLock(0);
        }

        // ---- animasyon
        double Ripple(double x, double now)
        {
            double sum = 0, sigma = Ui.D(260), lambda = Ui.D(45);
            foreach (Impulse im in impulses)
            {
                double t = now - im.T0, d = x - im.X;
                sum += im.Amp * Math.Exp(-(d * d) / (sigma * sigma))
                     * Math.Sin(2 * Math.PI * 1.7 * t - Math.Abs(d) / lambda) * Math.Exp(-t / 0.55);
            }
            return sum;
        }

        void Tick(object sender, EventArgs e)
        {
            double now = Now, dt = Clamp(now - lastTick, 0.001, 0.05);
            lastTick = now;
            impulses.RemoveAll(im => now - im.T0 > 2.0);

            double step = target - leftX;
            if (dragging && Math.Abs(step) > Ui.D(6) && now - lastDragKick > 0.08)
            {
                Kick(leftX, Math.Min(Ui.D(4), Math.Abs(step) * 0.08));
                lastDragKick = now;
            }
            leftX += step * (dragging ? 0.45 : 0.2);
            if (Math.Abs(target - leftX) < 0.5) leftX = target;

            if (hiding && leftX >= W)
            {
                timer.Stop();
                Action d = onHidden;
                onHidden = null;
                if (d != null) d();
                return;
            }

            // Püsküller: asıldıkları nokta hızlanınca ters yöne savrulur, sonra sönümlenerek durur.
            bool swinging = false;
            foreach (Tassel t in tassels)
            {
                double ax = leftX + t.Offset + Ripple(leftX + t.Offset, now);
                if (!t.Started) { t.PrevX = ax; t.PrevV = 0; t.Started = true; }
                double v = (ax - t.PrevX) / dt;
                double acc = Clamp((v - t.PrevV) / dt, -60000, 60000);
                t.PrevX = ax; t.PrevV = v;
                double alpha = -190 * Math.Sin(t.Angle) - 3.2 * t.Spin - acc / (CurtainArt.TasselLen * 1.4);
                t.Spin += alpha * dt;
                t.Angle = Clamp(t.Angle + t.Spin * dt, -0.7, 0.7);
                if (Math.Abs(t.Angle) > 0.002 || Math.Abs(t.Spin) > 0.01) swinging = true;
            }

            Render();
            if (!dragging && !swinging && impulses.Count == 0 && leftX == target) timer.Stop();
        }

        // ---- çizim
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        void Render()
        {
            using (Graphics g = Graphics.FromImage(surface.Bitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                if (FromLeft) { g.TranslateTransform(W, 0); g.ScaleTransform(-1, 1); }
                Draw(g);
            }
            surface.Present(Handle, Location);
        }

        void Draw(Graphics g)
        {
            double now = Now;
            int x0 = (int)Math.Round(leftX);
            double cw = W - leftX, gather = Clamp(cw / W, 0.2, 1);
            int top = fabTop + sag, bottom = fabTop + bodyH;
            List<float> rings = CurtainArt.RingPositions(leftX, W, Math.Max(Ui.D(11), Ui.D(64) * gather));

            if (x0 < W)
            {
                // Perdenin kapladığı sütun baştan aşağı tıklama tutsun: püskül ve halka aralarından
                // arkaya tıklanamasın. Saydamlığı 1/255, gözle görülmez; Windows yalnızca tam saydamı geçirir.
                using (var catcher = new SolidBrush(Color.FromArgb(1, 0, 0, 0)))
                    g.FillRectangle(catcher, x0, 0, W - x0, H);

                // Perdenin ekrana düşen yumuşak gölgesi.
                int sh = Ui.D(18);
                using (var shadow = new LinearGradientBrush(new Rectangle(x0 - sh - 2, 0, sh + 3, 10), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(70, 0, 0, 0), 0f))
                    g.FillRectangle(shadow, x0 - sh, top, sh, bottom - top);

                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                using (var folds = CurtainArt.FoldBrush((float)(Ui.D(150) * gather), (float)(1.6 - 0.6 * gather)))
                {
                    int n = Math.Min(dx.Length, (W - x0) / stripW + 2);
                    for (int i = 0; i < n; i++) dx[i] = impulses.Count == 0 ? 0 : Ripple(x0 + i * stripW + stripW / 2.0, now);

                    if (impulses.Count == 0)
                    {
                        int wdt = Math.Min(fabric.Width, W - x0);
                        g.DrawImage(fabric, new Rectangle(x0, top, wdt, bottom - top), 0, sag, wdt, bottom - top, GraphicsUnit.Pixel);
                        folds.TranslateTransform(x0, 0);
                        g.FillRectangle(folds, x0, top, wdt, bottom - top);
                    }
                    else
                    {
                        // Kumaş dikey şeritler halinde çizilir; üst kenar halkalara bağlı olduğu için
                        // dalga yukarıda sıfır, aşağı indikçe büyür.
                        for (int i = 0; i < n; i++)
                        {
                            int srcX = i * stripW;
                            int wdt = Math.Min(stripW + overlap, fabric.Width - srcX);
                            if (wdt <= 0) break;
                            float xs = x0 + srcX, d = (float)dx[i];
                            var dest = new[] { new PointF(xs, top), new PointF(xs + wdt, top), new PointF(xs + d, bottom) };
                            g.DrawImage(fabric, dest, new RectangleF(srcX, sag, wdt, bottom - top), GraphicsUnit.Pixel);
                            var quad = new[] { new PointF(xs, top), new PointF(xs + wdt, top), new PointF(xs + wdt + d, bottom), new PointF(xs + d, bottom) };
                            folds.ResetTransform();
                            folds.TranslateTransform(x0 + d * 0.5f, 0);
                            g.FillPolygon(folds, quad);
                            if (i > 0)
                            {
                                int a = (int)Clamp((dx[i] - dx[i - 1]) * 14, -55, 55);
                                if (Math.Abs(a) > 2)
                                    using (var sb = new SolidBrush(a > 0 ? Color.FromArgb(a, 255, 248, 236) : Color.FromArgb(-a, 30, 15, 5)))
                                        g.FillPolygon(sb, quad);
                            }
                        }
                    }
                }

                // Üst kenar: halkalardan asılı, aralarda sarkan kumaş.
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.Default;
                using (var heading = CurtainArt.HeadingPath(rings, x0, W, fabTop))
                {
                    headingBrush.ResetTransform();
                    headingBrush.TranslateTransform(x0, fabTop);
                    g.FillPath(headingBrush, heading);
                }

                // Püsküller
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                foreach (Tassel t in tassels)
                {
                    double ax = leftX + t.Offset;
                    if (ax > W + Ui.D(20)) break;
                    float px = (float)(ax + (impulses.Count == 0 ? 0 : Ripple(ax, now)));
                    Bitmap sp = tasselSprites[t.Sprite];
                    GraphicsState st = g.Save();
                    g.TranslateTransform(px, bottom - Ui.D(2));
                    g.RotateTransform((float)(t.Angle * 180 / Math.PI));
                    g.DrawImage(sp, -sp.Width / 2f, 0, sp.Width, sp.Height);
                    g.Restore(st);
                }
            }

            // Korniş ayrı bir pencerede (RodForm), bu pencerenin altında; halkalar onun önünde.
            if (x0 < W) CurtainArt.DrawRings(g, rings);

            if (Covering(leftX) && !hiding)
                CurtainArt.DrawLockBadge(g, x0 + Ui.D(22), fabTop + Ui.D(42), locked);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                headingBrush.Dispose(); fabric.Dispose(); surface.Dispose();
                foreach (Bitmap b in tasselSprites) b.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- çift Ctrl
    // Ctrl tuşuna, araya başka tuş girmeden, kısa aralıkla iki kez basılmasını yakalar.
    // İki basış da aynı Ctrl'ye (sol ya da sağ) olmalı; hangisi olduğu olaya iletilir.
    class DoubleCtrl : IDisposable
    {
        const int WH_KEYBOARD_LL = 13, TapMaxMs = 300, GapMaxMs = 400;
        const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
        readonly Native.LowLevelKeyboardProc proc;
        readonly Control invoker = new Control();
        IntPtr hook;
        int heldVk, lastTapVk, downAt, lastTapAt;
        bool otherKey;

        public event Action<bool> Triggered; // true: sağ Ctrl

        // Basılan tuşu yakalayıp yutmak için (true dönerse tuş başka programa gitmez).
        public Func<int, bool> KeyFilter;

        public DoubleCtrl()
        {
            invoker.CreateControl();
            proc = Hook;
            hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, proc, Native.GetModuleHandle(null), 0);
        }

        IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int vk = Marshal.ReadInt32(lParam), msg = wParam.ToInt32();
                bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                bool ctrl = vk == VK_LCONTROL || vk == VK_RCONTROL;
                int now = Environment.TickCount;
                if (down && !ctrl && KeyFilter != null && KeyFilter(vk)) { otherKey = true; lastTapVk = 0; return (IntPtr)1; }
                if (ctrl && down)
                {
                    if (heldVk != vk) { heldVk = vk; otherKey = false; downAt = now; }
                }
                else if (ctrl && up)
                {
                    heldVk = 0;
                    if (otherKey || now - downAt > TapMaxMs) lastTapVk = 0;
                    else if (lastTapVk == vk && now - lastTapAt < GapMaxMs)
                    {
                        lastTapVk = 0;
                        bool right = vk == VK_RCONTROL;
                        // Kancadan hemen dön; perdeyi açmak mesaj döngüsünde yapılsın.
                        invoker.BeginInvoke(new Action(delegate { if (Triggered != null) Triggered(right); }));
                    }
                    else { lastTapVk = vk; lastTapAt = now; }
                }
                else if (down) { otherKey = true; lastTapVk = 0; }
            }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            invoker.Dispose();
        }
    }

    // ---------------------------------------------------------------- PIN kilidi
    // Pirinç bir asma kilit: kabartma lale amblemi ve perçin gibi PIN noktaları.
    // Yanlış PIN'de sallanır ve noktalar kızarır; doğru PIN'de halkası kalkıp açılır.
    enum PinMode { Setup, Unlock, Exit }

    class PinForm : Form
    {
        const int MaxLen = 8, MinLen = 4, MaxFails = 5;
        static int fails;
        static DateTime lockUntil = DateTime.MinValue;

        static readonly Color Engrave = Color.FromArgb(56, 36, 18);
        static readonly Color EngraveLight = Color.FromArgb(150, 255, 238, 200);
        static readonly Color ErrorInk = Color.FromArgb(150, 24, 24);

        readonly PinMode mode;
        readonly LayeredSurface surface;
        readonly Rectangle body;
        readonly WinTimer anim = new WinTimer { Interval = 15 };
        readonly Stopwatch shakeSw = new Stopwatch(), openSw = new Stopwatch(), errSw = new Stopwatch();
        readonly Stopwatch lastInput = Stopwatch.StartNew();
        string entered = "", first, title, subtitle, message = "";
        bool messageIsError, wasActive, opening, wasLocked;
        int baseX;

        public bool Success { get; private set; }

        static bool Locked { get { return DateTime.Now < lockUntil; } }

        public PinForm(PinMode m)
        {
            mode = m;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Ui.D(320), Ui.D(430));
            TopMost = true;
            ShowInTaskbar = m == PinMode.Setup;
            Text = "Desktop Curtain";
            KeyPreview = true;
            body = new Rectangle(Ui.D(30), Ui.D(150), Ui.D(260), Ui.D(256));
            surface = new LayeredSurface(Width, Height);
            SetTexts(false);
            if (Locked) { SetMessage(LockMessage(), true); wasLocked = true; }
            anim.Tick += AnimTick;
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_LAYERED; return cp; }
        }

        void SetTexts(bool confirming)
        {
            if (mode == PinMode.Setup)
            {
                title = confirming ? "Confirm PIN" : "Set a PIN";
                subtitle = confirming ? "Type the same PIN again" : "4–8 digits to lock your curtain";
            }
            else if (mode == PinMode.Unlock) { title = "Locked"; subtitle = "Enter your PIN to draw it back"; }
            else { title = "Locked"; subtitle = "Enter your PIN to quit"; }
        }

        static string LockMessage()
        {
            return "Too many tries · wait " + Math.Ceiling((lockUntil - DateTime.Now).TotalSeconds) + "s";
        }

        void SetMessage(string text, bool error) { message = text; messageIsError = error; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.ForceForeground(Handle);
            Activate();
            Render(255);
            anim.Start();
        }

        protected override void OnActivated(EventArgs e) { wasActive = true; base.OnActivated(e); }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Açık bırakılan kilit başkasının önünde beklemesin.
            if (mode != PinMode.Setup && wasActive && !Success) BeginInvoke(new Action(Close));
        }

        // ---- klavye
        // Kilit odak alamasa bile tuşlar klavye kancasından buraya gelir.
        public static bool IsPinKey(int vk)
        {
            return (vk >= 0x30 && vk <= 0x39) || (vk >= 0x60 && vk <= 0x69) || vk == 0x08 || vk == 0x0D || vk == 0x1B;
        }

        public void HandleKey(int vk)
        {
            lastInput.Restart();
            if (opening) return;
            if (vk == 0x1B) Close();
            else if (vk == 0x0D) Submit();
            else if (vk == 0x08) { if (entered.Length > 0) entered = entered.Substring(0, entered.Length - 1); }
            else
            {
                char c = (char)('0' + (vk >= 0x60 ? vk - 0x60 : vk - 0x30));
                OnKeyPress(new KeyPressEventArgs(c));
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (opening) return true;
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Enter) { Submit(); return true; }
            if (keyData == Keys.Back)
            {
                if (entered.Length > 0) entered = entered.Substring(0, entered.Length - 1);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            lastInput.Restart();
            if (!opening && e.KeyChar >= '0' && e.KeyChar <= '9' && entered.Length < MaxLen)
            {
                entered += e.KeyChar;
                if (!Locked && messageIsError) message = "";
            }
            e.Handled = true;
        }

        void Submit()
        {
            if (mode != PinMode.Setup && Locked) { Fail(null); return; }
            if (entered.Length < MinLen) { Fail("At least 4 digits"); return; }

            if (mode == PinMode.Setup)
            {
                if (first == null)
                {
                    first = entered; entered = "";
                    SetTexts(true);
                    SetMessage("", false);
                }
                else if (first == entered) { Config.SetPin(entered); Open(); }
                else
                {
                    first = null;
                    SetTexts(false);
                    Fail("PINs didn’t match · try again");
                }
                return;
            }

            if (Config.Verify(entered)) { fails = 0; Open(); return; }
            fails++;
            if (fails >= MaxFails)
            {
                fails = 0;
                lockUntil = DateTime.Now.AddSeconds(30);
                wasLocked = true;
                Fail(LockMessage());
            }
            else
            {
                int left = MaxFails - fails;
                Fail("Wrong PIN · " + left + (left == 1 ? " try" : " tries") + " left");
            }
        }

        void Fail(string text)
        {
            entered = "";
            if (text != null) SetMessage(text, true);
            errSw.Restart();
            if (!shakeSw.IsRunning) baseX = Left;
            shakeSw.Restart();
        }

        void Open()
        {
            Success = true;
            opening = true;
            SetMessage("", false);
            openSw.Restart();
        }

        // ---- animasyon
        void AnimTick(object sender, EventArgs e)
        {
            if (shakeSw.IsRunning)
            {
                double ms = shakeSw.Elapsed.TotalMilliseconds;
                if (ms >= 360) { shakeSw.Reset(); Left = baseX; }
                else Left = baseX + (int)(Math.Sin(ms / 22.0) * Ui.D(12) * (1 - ms / 360));
            }
            // Unutulan kilit ekranda kalmasın.
            if (mode != PinMode.Setup && !opening && lastInput.Elapsed.TotalSeconds > 30) { anim.Stop(); Close(); return; }
            if (wasLocked && !Locked) { wasLocked = false; SetMessage("You can try again", false); }
            else if (Locked) SetMessage(LockMessage(), true);

            byte alpha = 255;
            if (opening)
            {
                double ms = openSw.Elapsed.TotalMilliseconds;
                if (ms >= 680) { anim.Stop(); Close(); return; }
                if (ms > 420) alpha = (byte)(255 * (1 - (ms - 420) / 260));
            }
            Render(alpha);
        }

        void Render(byte alpha)
        {
            using (Graphics g = Graphics.FromImage(surface.Bitmap))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                Draw(g);
            }
            surface.Present(Handle, Location, alpha);
        }

        // ---- çizim
        void Draw(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float cx = Width / 2f, radius = Ui.D(34);

            double openT = opening ? Math.Min(1, openSw.Elapsed.TotalMilliseconds / 260) : 0;
            double ease = 1 - Math.Pow(1 - openT, 3);

            // Yere düşen yumuşak gölge
            for (int i = 7; i >= 0; i--)
            {
                var r = RectangleF.Inflate(body, i * Ui.S * 2.2f, i * Ui.S * 2.2f);
                r.Offset(0, Ui.D(10));
                using (var p = Ui.RoundRect(r, radius + i * 2))
                using (var b = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                    g.FillPath(b, p);
            }

            DrawShackle(g, cx, (float)ease);
            DrawBody(g, radius);
            DrawEmblem(g, cx, body.Y + Ui.D(62), Ui.D(44));

            using (var f = new Font("Segoe UI Semibold", 15f))
                Engraved(g, title, f, body.Y + Ui.D(110));
            using (var f = new Font("Segoe UI", 9f))
            using (var b = new SolidBrush(Color.FromArgb(210, Engrave)))
                Centered(g, subtitle, f, b, body.Y + Ui.D(142));

            DrawDots(g, cx, body.Y + Ui.D(176));

            if (message != "")
                using (var f = new Font("Segoe UI Semibold", 8.5f))
                using (var b = new SolidBrush(messageIsError ? ErrorInk : Engrave))
                    Centered(g, message, f, b, body.Y + Ui.D(201));

            using (var f = new Font("Segoe UI", 8f))
            using (var b = new SolidBrush(Color.FromArgb(150, Engrave)))
                Centered(g, mode == PinMode.Setup ? "Enter  continue   ·   Esc  cancel" : "Enter  unlock   ·   Esc  cancel",
                    f, b, body.Bottom - Ui.D(30));
        }

        // Kilidin halkası: açılırken yukarı kalkar ve sağ bacağı etrafında hafifçe döner.
        void DrawShackle(Graphics g, float cx, float open)
        {
            float leg = Ui.D(76), arcTop = Ui.D(26), lift = open * Ui.D(40);
            float centerY = arcTop + leg;
            GraphicsState st = g.Save();
            g.TranslateTransform(cx + leg, body.Y);
            g.RotateTransform(-open * 18);
            g.TranslateTransform(-(cx + leg), -body.Y - lift);
            using (var path = new GraphicsPath())
            {
                path.AddLine(cx - leg, body.Y + Ui.D(18) - open * Ui.D(26), cx - leg, centerY);
                path.AddArc(cx - leg, centerY - leg, leg * 2, leg * 2, 180, 180);
                path.AddLine(cx + leg, centerY, cx + leg, body.Y + Ui.D(40));
                float w = Ui.D(26);
                using (var p1 = new Pen(Color.FromArgb(86, 64, 36), w))
                using (var p2 = new Pen(Color.FromArgb(176, 144, 92), w * 0.62f))
                using (var p3 = new Pen(Color.FromArgb(200, 250, 230, 186), w * 0.16f))
                {
                    p1.StartCap = p1.EndCap = p2.StartCap = p2.EndCap = p3.StartCap = p3.EndCap = LineCap.Round;
                    g.DrawPath(p1, path);
                    g.DrawPath(p2, path);
                    g.TranslateTransform(-w * 0.12f, -w * 0.12f);
                    g.DrawPath(p3, path);
                }
            }
            g.Restore(st);
        }

        void DrawBody(Graphics g, float radius)
        {
            using (var path = Ui.RoundRect(body, radius))
            {
                using (var br = new LinearGradientBrush(body, Color.Black, Color.Black, 90f))
                {
                    var cb = new ColorBlend();
                    cb.Colors = new[] { Color.FromArgb(206, 170, 108), Color.FromArgb(236, 210, 152), Color.FromArgb(186, 146, 86), Color.FromArgb(142, 104, 58), Color.FromArgb(110, 78, 40) };
                    cb.Positions = new[] { 0f, .1f, .42f, .8f, 1f };
                    br.InterpolationColors = cb;
                    g.FillPath(br, path);
                }
                // Fırçalanmış metal dokusu ve üstten vuran ışık
                GraphicsState st = g.Save();
                g.SetClip(path);
                using (var line = new Pen(Color.FromArgb(12, 255, 255, 255)))
                    for (int y = body.Y; y < body.Bottom; y += 3) g.DrawLine(line, body.X, y, body.Right, y);
                using (var sheen = new LinearGradientBrush(body, Color.FromArgb(60, 255, 250, 235), Color.FromArgb(0, 255, 250, 235), 60f))
                    g.FillEllipse(sheen, body.X - body.Width * .2f, body.Y - body.Height * .55f, body.Width * .9f, body.Height);
                g.Restore(st);
                using (var outer = new Pen(Color.FromArgb(78, 52, 26), Math.Max(1.5f, 2f * Ui.S))) g.DrawPath(outer, path);
            }
            var inner = RectangleF.Inflate(body, -Ui.D(7), -Ui.D(7));
            using (var p = Ui.RoundRect(inner, radius - Ui.D(7)))
            {
                using (var light = new Pen(Color.FromArgb(110, 255, 240, 200), Math.Max(1f, 1.4f * Ui.S))) g.DrawPath(light, p);
                GraphicsState st = g.Save();
                g.TranslateTransform(0, Ui.S);
                using (var dark = new Pen(Color.FromArgb(70, 70, 44, 18), Math.Max(1f, 1.2f * Ui.S))) g.DrawPath(dark, p);
                g.Restore(st);
            }
        }

        // Kabartma lale amblemi (perdedeki lalelerin yalın hali), çevresinde ince bir halka.
        void DrawEmblem(Graphics g, float cx, float cy, float s)
        {
            using (var hi = new SolidBrush(EngraveLight))
            using (var dk = new SolidBrush(Color.FromArgb(215, Engrave)))
            using (var ringHi = new Pen(EngraveLight, Math.Max(1f, 1.5f * Ui.S)))
            using (var ringDk = new Pen(Color.FromArgb(170, Engrave), Math.Max(1f, 1.5f * Ui.S)))
            {
                float rr = s * 0.78f;
                g.DrawEllipse(ringHi, cx - rr, cy - rr + Ui.S, rr * 2, rr * 2);
                g.DrawEllipse(ringDk, cx - rr, cy - rr, rr * 2, rr * 2);
                TulipSilhouette(g, hi, cx, cy + s * 0.18f + Ui.S * 1.2f, s * 0.62f);
                TulipSilhouette(g, dk, cx, cy + s * 0.18f, s * 0.62f);
            }
        }

        static void TulipSilhouette(Graphics g, Brush b, float cx, float baseY, float s)
        {
            using (var stem = new Pen(b, s * 0.08f)) g.DrawLine(stem, cx, baseY, cx, baseY + s * 0.55f);
            Fill(g, b, CurtainArt.LensPath(new PointF(cx, baseY + s * .55f), new PointF(cx - s * .42f, baseY + s * .02f), s * .09f, s * .05f));
            Fill(g, b, CurtainArt.LensPath(new PointF(cx, baseY + s * .55f), new PointF(cx + s * .42f, baseY + s * .02f), s * .09f, -s * .05f));
            Fill(g, b, CurtainArt.LensPath(new PointF(cx - s * .04f, baseY), new PointF(cx - s * .34f, baseY - s * .95f), s * .26f, -s * .05f));
            Fill(g, b, CurtainArt.LensPath(new PointF(cx + s * .04f, baseY), new PointF(cx + s * .34f, baseY - s * .95f), s * .26f, s * .05f));
            Fill(g, b, CurtainArt.LensPath(new PointF(cx, baseY + s * .02f), new PointF(cx, baseY - s * .9f), s * .3f, 0));
            g.FillEllipse(b, cx - s * .33f, baseY - s * .58f, s * .66f, s * .62f);
        }

        static void Fill(Graphics g, Brush b, GraphicsPath p) { using (p) g.FillPath(b, p); }

        // PIN noktaları: boşken gömme perçin, doluyken inci gibi parlak; yanlışta kızarır.
        void DrawDots(Graphics g, float cx, float y)
        {
            int slots = Math.Max(MinLen, entered.Length);
            float d = Ui.D(16), gap = Ui.D(12), total = slots * d + (slots - 1) * gap, x = cx - total / 2;
            double err = errSw.IsRunning ? errSw.Elapsed.TotalMilliseconds : 9999;
            int redA = err < 700 ? (int)(200 * (1 - err / 700)) : 0;
            for (int i = 0; i < slots; i++, x += d + gap)
            {
                var r = new RectangleF(x, y, d, d);
                if (i < entered.Length)
                {
                    using (var pearl = new LinearGradientBrush(r, Color.FromArgb(255, 252, 240), Color.FromArgb(222, 192, 140), 90f))
                        g.FillEllipse(pearl, r);
                    using (var rim = new Pen(Color.FromArgb(120, 90, 60, 30))) g.DrawEllipse(rim, r);
                    using (var spark = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                        g.FillEllipse(spark, x + d * .24f, y + d * .18f, d * .3f, d * .24f);
                }
                else
                {
                    using (var hole = new SolidBrush(Color.FromArgb(110, 60, 38, 16))) g.FillEllipse(hole, r);
                    using (var lip = new Pen(Color.FromArgb(140, 255, 240, 205), Math.Max(1f, 1.2f * Ui.S)))
                        g.DrawArc(lip, r, 20, 140);
                    if (redA > 0)
                        using (var red = new SolidBrush(Color.FromArgb(redA, 190, 30, 30))) g.FillEllipse(red, r);
                }
            }
        }

        void Engraved(Graphics g, string s, Font f, float y)
        {
            using (var hi = new SolidBrush(EngraveLight)) Centered(g, s, f, hi, y + Ui.S * 1.2f);
            using (var dk = new SolidBrush(Engrave)) Centered(g, s, f, dk, y);
        }

        void Centered(Graphics g, string s, Font f, Brush b, float y)
        {
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                g.DrawString(s, f, b, new RectangleF(body.X + Ui.D(10), y, body.Width - Ui.D(20), Ui.D(34)), sf);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { anim.Dispose(); surface.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------- bilgi baloncuğu
    class ToastForm : Form
    {
        readonly string title, body;
        readonly WinTimer timer = new WinTimer { Interval = 15 };
        readonly Stopwatch sw = new Stopwatch();
        const double FadeIn = 200, Hold = 5000, FadeOut = 400;

        public ToastForm(string title, string body)
        {
            this.title = title; this.body = body;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Ui.Bg;
            DoubleBuffered = true;
            Opacity = 0;
            Size = new Size(Ui.D(400), Ui.D(132));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - Ui.D(18), wa.Bottom - Height - Ui.D(18));
            using (var p = Ui.RoundRect(new RectangleF(0, 0, Width, Height), Ui.D(14))) Region = new Region(p);
            timer.Tick += Tick;
            Click += delegate { Close(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return cp; }
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); sw.Start(); timer.Start(); }

        void Tick(object sender, EventArgs e)
        {
            double ms = sw.Elapsed.TotalMilliseconds;
            if (ms < FadeIn) Opacity = ms / FadeIn;
            else if (ms < FadeIn + Hold) Opacity = 1;
            else if (ms < FadeIn + Hold + FadeOut) Opacity = 1 - (ms - FadeIn - Hold) / FadeOut;
            else { timer.Stop(); Close(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), Ui.D(14)))
            using (var pen = new Pen(Ui.Border)) g.DrawPath(pen, path);
            using (var accent = new SolidBrush(Ui.Accent)) g.FillRectangle(accent, 0, Ui.D(18), Ui.D(4), Height - Ui.D(36));
            using (var f = new Font("Segoe UI Semibold", 11f))
                TextRenderer.DrawText(g, title, f, new Point(Ui.D(20), Ui.D(14)), Ui.Text1);
            using (var f = new Font("Segoe UI", 9.5f))
                TextRenderer.DrawText(g, body, f, new Rectangle(Ui.D(20), Ui.D(44), Width - Ui.D(32), Height - Ui.D(50)), Ui.Text2,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // ---------------------------------------------------------------- kısayollar
    class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly List<int> ids = new List<int>();
        public event Action<int> Pressed;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int id, uint mods, Keys key)
        {
            bool ok = Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, (uint)key);
            if (ok) ids.Add(id);
            return ok;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
            ids.Clear();
            DestroyHandle();
        }
    }

    // ---------------------------------------------------------------- uygulama
    class Controller : ApplicationContext
    {
        const int HkToggle = 1, HkExit = 2;
        readonly HotkeyWindow hotkeys = new HotkeyWindow();
        readonly DoubleCtrl doubleCtrl = new DoubleCtrl();
        readonly List<CurtainForm> curtains = new List<CurtainForm>();   // görünen kanatlar
        readonly List<CurtainForm> alive = new List<CurtainForm>();      // toplanmakta olanlar dahil
        readonly Dictionary<Rectangle, RodForm> rods = new Dictionary<Rectangle, RodForm>();
        PinForm prompt;
        bool quitAfterHide;

        public Controller()
        {
            hotkeys.Pressed += delegate (int id) { if (id == HkToggle) Toggle(false); else Exit(); };
            doubleCtrl.Triggered += delegate (bool right) { Toggle(!right); };
            // Kilit açıkken PIN tuşları, odak nerede olursa olsun kilide gider ve başka programa ulaşmaz.
            doubleCtrl.KeyFilter = delegate (int vk)
            {
                PinForm p = prompt;
                if (p == null || !PinForm.IsPinKey(vk)) return false;
                p.BeginInvoke(new Action(delegate { if (!p.IsDisposed) p.HandleKey(vk); }));
                return true;
            };
            // Ctrl+Alt+P yedek olarak sağ kanadı yönetir.
            hotkeys.Register(HkToggle, Native.MOD_CONTROL | Native.MOD_ALT, Keys.P);
            hotkeys.Register(HkExit, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT, Keys.P);
            new ToastForm("Desktop Curtain is ready",
                "Double-tap right Ctrl: right panel · left Ctrl: left panel\nDrag to close, release to lock · double-click: close fully\nCtrl+Alt+Shift+P: quit").Show();
        }

        List<CurtainForm> Side(bool fromLeft) { return curtains.FindAll(c => c.FromLeft == fromLeft); }

        bool BringPromptForward()
        {
            if (prompt == null) return false;
            Native.ForceForeground(prompt.Handle);
            prompt.Activate();
            return true;
        }

        // Bir kanat yoksa getirir; varsa toplar (kilitliyse önce PIN sorar).
        void Toggle(bool fromLeft)
        {
            if (BringPromptForward()) return;
            List<CurtainForm> side = Side(fromLeft);
            if (side.Count == 0) ShowSide(fromLeft);
            else if (side.Exists(c => c.Locked)) AskPin(PinMode.Unlock, delegate { Hide(Side(fromLeft)); });
            else Hide(side);
        }

        void Exit()
        {
            if (BringPromptForward()) return;
            if (!curtains.Exists(c => c.Locked)) { quitAfterHide = true; Hide(new List<CurtainForm>(curtains)); }
            else AskPin(PinMode.Exit, delegate { quitAfterHide = true; Hide(new List<CurtainForm>(curtains)); });
        }

        void AskPin(PinMode mode, Action onSuccess)
        {
            prompt = new PinForm(mode);
            prompt.FormClosed += delegate
            {
                bool ok = prompt.Success;
                prompt.Dispose();
                prompt = null;
                if (ok) onSuccess();
            };
            prompt.Show();
            Native.ForceForeground(prompt.Handle);
            prompt.Activate();
        }

        void ShowSide(bool fromLeft)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                // Korniş önce açılır ki perde kanatları onun önünde dursun.
                if (!rods.ContainsKey(s.Bounds))
                {
                    var rod = new RodForm(s.Bounds);
                    rods[s.Bounds] = rod;
                    rod.Show();
                }
                var c = new CurtainForm(s.Bounds, fromLeft);
                c.PinRequested += delegate { if (prompt == null) AskPin(PinMode.Unlock, c.Unlock); };
                curtains.Add(c);
                alive.Add(c);
                c.Show();
            }
        }

        void Hide(List<CurtainForm> list)
        {
            foreach (CurtainForm c in list) curtains.Remove(c);
            if (quitAfterHide && curtains.Count == 0 && list.Count == 0) { Quit(); return; }
            int remaining = list.Count;
            foreach (CurtainForm c in list)
            {
                Rectangle screen = c.Bounds;
                c.SlideAway(delegate
                {
                    c.Close();
                    c.Dispose();
                    alive.Remove(c);
                    RodForm rod;
                    if (!alive.Exists(x => x.Bounds == screen) && rods.TryGetValue(screen, out rod))
                    {
                        rods.Remove(screen);
                        rod.Close();
                        rod.Dispose();
                    }
                    if (--remaining == 0 && quitAfterHide) Quit();
                });
            }
        }

        void Quit()
        {
            hotkeys.Dispose();
            doubleCtrl.Dispose();
            DesktopIcons.Restore();
            ExitThread();
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "MasaustuPerdesi_TekKopya", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Desktop Curtain is already running.\n\nDouble-tap Ctrl to bring out a curtain.",
                        "Desktop Curtain", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Native.SetProcessDPIAware();
                Ui.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                DesktopIcons.RecoverAfterCrash();
                AppDomain.CurrentDomain.ProcessExit += delegate { DesktopIcons.Restore(); };

                if (!Config.HasPin)
                {
                    var setup = new PinForm(PinMode.Setup);
                    Application.Run(setup);
                    if (!setup.Success) return;
                }
                Application.Run(new Controller());
            }
        }
    }
}

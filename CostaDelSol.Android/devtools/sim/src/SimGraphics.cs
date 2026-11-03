// 시험기용 안드로이드 그림 도구 — WpfLite 가 그린 창을 PNG 로 찍어 보려고 지은 작은 소프트웨어 붓.
using System.IO.Compression;

namespace Android.Content
{
    public class Context { public const string ClipboardService = "clipboard"; public object? GetSystemService(string n) => null; }
    public sealed class ClipboardManager { public ClipData? PrimaryClip { get; set; } }
    public sealed class ClipData { public static ClipData NewPlainText(string a, string b) => new(); }
}
namespace Android.Views
{
    [Flags] public enum MotionEventActions { Down = 0, Up = 1, Move = 2, Cancel = 3, Mask = 255 }
}

namespace Android.Graphics
{
    public readonly struct Color
    {
        public readonly int Argb;
        public Color(int r, int g, int b, int a) { Argb = (Math.Clamp(a, 0, 255) << 24) | (r & 255) << 16 | (g & 255) << 8 | (b & 255); }
        public Color(int argb) { Argb = argb; }
        public static Color Black => new(0, 0, 0, 255);
        public static Color White => new(255, 255, 255, 255);
        public static Color Argb_(int a, int r, int g, int b) => new(r, g, b, a);
        public static implicit operator int(Color c) => c.Argb;
    }

    public enum TypefaceStyle { Normal, Bold }
    public sealed class Typeface
    {
        public static readonly Typeface Default = new();
        public static Typeface Create(Typeface? f, TypefaceStyle s) => new();
    }

    public class Shader
    {
        public sealed class TileMode { public static readonly TileMode Repeat = new(), Clamp = new(), Mirror = new(); }
        internal Matrix Local = new();
        public void SetLocalMatrix(Matrix m) => Local = new Matrix(m);
    }

    public sealed class BitmapShader : Shader
    {
        internal readonly Bitmap Bmp;
        public BitmapShader(Bitmap b, TileMode x, TileMode y) { Bmp = b; }
    }

    public sealed class LinearGradient : Shader
    {
        internal readonly int First;
        public LinearGradient(float x0, float y0, float x1, float y1, int[] colors, float[]? stops, TileMode t) { First = colors[0]; }
    }

    public class PathEffect { }
    public sealed class DashPathEffect(float[] i, float p) : PathEffect { }

    public sealed class Paint
    {
        public enum Style { Fill, Stroke, FillAndStroke }
        public sealed class FontMetrics { public float Ascent, Descent, Top, Bottom, Leading; }
        public bool AntiAlias { get; set; }
        public bool FilterBitmap { get; set; }
        public float StrokeWidth { get; set; } = 1;
        public float TextSize { get; set; } = 12;
        private int _argb = unchecked((int)0xFF000000);
        public Color Color { get => new(_argb); set => _argb = value.Argb; }
        public int Alpha { get => (_argb >> 24) & 255; set => _argb = (_argb & 0xFFFFFF) | (Math.Clamp(value, 0, 255) << 24); }
        internal Shader? Shader;
        internal Style Kind = Style.Fill;
        public void SetShader(Shader? s) => Shader = s;
        public void SetStyle(Style s) => Kind = s;
        public void SetTypeface(Typeface? t) { }
        public void SetPathEffect(PathEffect? e) { }
        public float MeasureText(string t) => t.Sum(c => c < 0x80 ? TextSize * 0.55f : TextSize);
        public int BreakText(string t, int start, int end, bool forward, float max, float[]? measured)
        {
            float w = 0; int n = 0;
            for (int i = start; i < end; i++) { w += t[i] < 0x80 ? TextSize * 0.55f : TextSize; if (w > max) break; n++; }
            return n;
        }
        public FontMetrics GetFontMetrics() => new() { Ascent = -TextSize * 0.9f, Descent = TextSize * 0.25f };
    }

    public sealed class RectF
    {
        public float Left, Top, Right, Bottom;
        public RectF() { }
        public RectF(float l, float t, float r, float b) { Left = l; Top = t; Right = r; Bottom = b; }
        public bool Contains(float x, float y) => x >= Left && x < Right && y >= Top && y < Bottom;
        public float CenterX() => (Left + Right) / 2;
        public float Width() => Right - Left;
        public float Height() => Bottom - Top;
        public void Inset(float dx, float dy) { Left += dx; Top += dy; Right -= dx; Bottom -= dy; }
    }

    public sealed class Path
    {
        public enum Direction { Cw, Ccw }
        internal readonly List<(float X, float Y, bool Move)> Points = [];
        public void MoveTo(float x, float y) => Points.Add((x, y, true));
        public void LineTo(float x, float y) => Points.Add((x, y, false));
        public void Close() { var f = Points.LastOrDefault(p => p.Move); if (Points.Count > 0) Points.Add((f.X, f.Y, false)); }
        public void AddRect(float l, float t, float r, float b, Direction d) { MoveTo(l, t); LineTo(r, t); LineTo(r, b); LineTo(l, b); Close(); }
        public void AddOval(float l, float t, float r, float b, Direction d) => AddRect(l, t, r, b, d);
        public void AddPath(Path p) => Points.AddRange(p.Points);
        public void ComputeBounds(RectF r, bool exact)
        {
            if (Points.Count == 0) { r.Left = r.Top = r.Right = r.Bottom = 0; return; }
            r.Left = Points.Min(p => p.X); r.Top = Points.Min(p => p.Y); r.Right = Points.Max(p => p.X); r.Bottom = Points.Max(p => p.Y);
        }
    }

    /// <summary>2차원 닮음 바꿈(a c e / b d f).</summary>
    public sealed class Matrix
    {
        internal double A = 1, B, C, D = 1, E, F;
        public Matrix() { }
        public Matrix(Matrix? m) { if (m != null) (A, B, C, D, E, F) = (m.A, m.B, m.C, m.D, m.E, m.F); }
        private static Matrix Of(double a, double b, double c, double d, double e, double f) => new() { A = a, B = b, C = c, D = d, E = e, F = f };
        private void Set(Matrix m) => (A, B, C, D, E, F) = (m.A, m.B, m.C, m.D, m.E, m.F);
        // this * m  (m 을 먼저 적용)
        private static Matrix Mul(Matrix x, Matrix y) => Of(
            x.A * y.A + x.C * y.B, x.B * y.A + x.D * y.B,
            x.A * y.C + x.C * y.D, x.B * y.C + x.D * y.D,
            x.A * y.E + x.C * y.F + x.E, x.B * y.E + x.D * y.F + x.F);
        public void SetScale(float sx, float sy, float px = 0, float py = 0) => Set(Of(sx, 0, 0, sy, px - sx * px, py - sy * py));
        public void SetScale(float sx, float sy) => Set(Of(sx, 0, 0, sy, 0, 0));
        public void SetTranslate(float x, float y) => Set(Of(1, 0, 0, 1, x, y));
        public void SetRotate(float deg, float px, float py)
        {
            double r = deg * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            Set(Mul(Mul(Of(1, 0, 0, 1, px, py), Of(cos, sin, -sin, cos, 0, 0)), Of(1, 0, 0, 1, -px, -py)));
        }
        public bool PreTranslate(float x, float y) { Set(Mul(this, Of(1, 0, 0, 1, x, y))); return true; }
        public bool PostTranslate(float x, float y) { Set(Mul(Of(1, 0, 0, 1, x, y), this)); return true; }
        public bool PreScale(float x, float y) { Set(Mul(this, Of(x, 0, 0, y, 0, 0))); return true; }
        public bool PreConcat(Matrix m) { Set(Mul(this, m)); return true; }
        public bool PostConcat(Matrix m) { Set(Mul(m, this)); return true; }
        public bool Invert(Matrix into)
        {
            double det = A * D - B * C;
            if (Math.Abs(det) < 1e-12) return false;
            into.A = D / det; into.B = -B / det; into.C = -C / det; into.D = A / det;
            into.E = (C * F - D * E) / det; into.F = (B * E - A * F) / det;
            return true;
        }
        public void MapPoints(float[] pts)
        {
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                double x = pts[i], y = pts[i + 1];
                pts[i] = (float)(A * x + C * y + E); pts[i + 1] = (float)(B * x + D * y + F);
            }
        }
        internal (double X, double Y) Map(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    }

    public sealed class Bitmap
    {
        public sealed class Config { public static readonly Config Argb8888 = new(); }
        public sealed class CompressFormat { public static readonly CompressFormat Png = new(); }
        public int Width { get; }
        public int Height { get; }
        internal readonly int[] Px;
        public bool IsMutable => true;
        private Bitmap(int w, int h) { Width = w; Height = h; Px = new int[w * h]; }
        public static Bitmap CreateBitmap(int w, int h, Config c) => new(w, h);
        public static Bitmap CreateBitmap(int[] argb, int w, int h, Config c) { var b = new Bitmap(w, h); Array.Copy(argb, b.Px, Math.Min(argb.Length, b.Px.Length)); return b; }
        public static Bitmap CreateBitmap(Bitmap s, int x, int y, int w, int h)
        {
            var b = new Bitmap(w, h);
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) b.Px[j * w + i] = s.Px[(y + j) * s.Width + x + i];
            return b;
        }
        public static Bitmap CreateBitmap(Bitmap s, int x, int y, int w, int h, Matrix m, bool filter) => CreateBitmap(s, x, y, w, h);
        public Bitmap Copy(Config c, bool mutable) => CreateBitmap(Px, Width, Height, c);
        public void EraseColor(int argb) => Array.Fill(Px, argb);
        public void GetPixels(int[] o, int off, int stride, int x, int y, int w, int h)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) o[off + j * stride + i] = Px[(y + j) * Width + x + i];
        }
        public void SetPixels(int[] s, int off, int stride, int x, int y, int w, int h)
        {
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Px[(y + j) * Width + x + i] = s[off + j * stride + i];
        }
        public bool Compress(CompressFormat f, int q, Stream s) { Sim.Png.Write(this, s); return true; }
        public int Get(int x, int y) => Px[y * Width + x];
    }

    public static class BitmapFactory
    {
        public static Bitmap? DecodeFile(string path) { try { using var f = File.OpenRead(path); return DecodeStream(f); } catch { return null; } }
        public static Bitmap? DecodeStream(Stream s)
        {
            var px = Sim.PngRead.Decode(s, out int w, out int h);
            return px == null ? null : Bitmap.CreateBitmap(px, w, h, Bitmap.Config.Argb8888);
        }
    }

    /// <summary>소프트웨어 붓 — 바꿈 · 오림 · 알파 겹침만 한다(글은 줄 하나로 표시).</summary>
    public sealed class Canvas
    {
        private readonly Bitmap _b;
        private Matrix _m = new();
        private (double L, double T, double R, double B) _clip;
        private double _alpha = 1;
        private readonly Stack<(Matrix, (double, double, double, double), double)> _stack = new();
        public Canvas(Bitmap b) { _b = b; _clip = (0, 0, b.Width, b.Height); }

        public int Save() { _stack.Push((new Matrix(_m), _clip, _alpha)); return _stack.Count; }
        public int SaveLayerAlpha(RectF? r, int a) { Save(); _alpha *= a / 255.0; return _stack.Count; }
        public void Restore() { if (_stack.Count > 0) (_m, _clip, _alpha) = _stack.Pop(); }
        public void Translate(float x, float y) => _m.PreTranslate(x, y);
        public void Scale(float x, float y) => _m.PreScale(x, y);
        public void Concat(Matrix m) => _m.PreConcat(m);

        private (double L, double T, double R, double B) Dev(double l, double t, double r, double b)
        {
            var (x0, y0) = _m.Map(l, t); var (x1, y1) = _m.Map(r, b);
            return (Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
        }

        public bool ClipRect(float l, float t, float r, float b)
        {
            var d = Dev(l, t, r, b);
            _clip = (Math.Max(_clip.L, d.L), Math.Max(_clip.T, d.T), Math.Min(_clip.R, d.R), Math.Min(_clip.B, d.B));
            return true;
        }
        public bool ClipRect(RectF r) => ClipRect(r.Left, r.Top, r.Right, r.Bottom);
        public bool ClipPath(Path p) { var r = new RectF(); p.ComputeBounds(r, true); return ClipRect(r); }

        private void Blend(int x, int y, int argb, double alpha)
        {
            if (x < _clip.L || y < _clip.T || x >= _clip.R || y >= _clip.B || x < 0 || y < 0 || x >= _b.Width || y >= _b.Height) return;
            double a = ((argb >> 24) & 255) / 255.0 * alpha * _alpha;
            if (a <= 0) return;
            int i = y * _b.Width + x, d = _b.Px[i];
            double da = ((d >> 24) & 255) / 255.0;
            double oa = a + da * (1 - a);
            int C(int sh) => oa <= 0 ? 0 : (int)Math.Round((((argb >> sh) & 255) * a + ((d >> sh) & 255) * da * (1 - a)) / oa);
            _b.Px[i] = (int)Math.Round(oa * 255) << 24 | C(16) << 16 | C(8) << 8 | C(0);
        }

        public void DrawRect(RectF r, Paint p) => DrawRect(r.Left, r.Top, r.Right, r.Bottom, p);

        public void DrawRect(float l, float t, float rr, float bb, Paint p)
        {
            var d = Dev(l, t, rr, bb);
            int x0 = (int)Math.Round(d.L), y0 = (int)Math.Round(d.T), x1 = (int)Math.Round(d.R), y1 = (int)Math.Round(d.B);
            var inv = new Matrix();
            Matrix? shaderInv = null;
            if (p.Shader is BitmapShader bs) { var full = new Matrix(_m); full.PreConcat(bs.Local); shaderInv = new Matrix(); full.Invert(shaderInv); }
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int c = p.Color.Argb;
                    if (p.Shader is BitmapShader b && shaderInv != null)
                    {
                        var (u, v) = shaderInv.Map(x + 0.5, y + 0.5);
                        int ui = (int)Math.Floor(u) % b.Bmp.Width, vi = (int)Math.Floor(v) % b.Bmp.Height;
                        if (ui < 0) ui += b.Bmp.Width; if (vi < 0) vi += b.Bmp.Height;
                        c = b.Bmp.Get(ui, vi);
                        Blend(x, y, c, p.Alpha / 255.0);
                        continue;
                    }
                    if (p.Shader is LinearGradient lg) c = lg.First;
                    Blend(x, y, c, 1);
                }
        }

        public void DrawBitmap(Bitmap bmp, Rect? src, RectF dst, Paint? p)
        {
            var d = Dev(dst.Left, dst.Top, dst.Right, dst.Bottom);
            int x0 = (int)Math.Round(d.L), y0 = (int)Math.Round(d.T), x1 = (int)Math.Round(d.R), y1 = (int)Math.Round(d.B);
            double alpha = (p?.Alpha ?? 255) / 255.0;
            var inv = new Matrix(); _m.Invert(inv);
            double sw = dst.Width(), sh = dst.Height();
            if (sw <= 0 || sh <= 0) return;
            for (int y = Math.Max(y0, (int)_clip.T); y < Math.Min(y1, (int)Math.Ceiling(_clip.B)); y++)
                for (int x = Math.Max(x0, (int)_clip.L); x < Math.Min(x1, (int)Math.Ceiling(_clip.R)); x++)
                {
                    var (u, v) = inv.Map(x + 0.5, y + 0.5);
                    int bx = (int)((u - dst.Left) / sw * bmp.Width), by = (int)((v - dst.Top) / sh * bmp.Height);
                    if (bx < 0 || by < 0 || bx >= bmp.Width || by >= bmp.Height) continue;
                    Blend(x, y, bmp.Get(bx, by), alpha);
                }
        }

        public void DrawText(string text, float x, float y, Paint p)
        {
            // 글꼴이 없으니 글 폭만큼 밑줄을 긋는다.
            float w = p.MeasureText(text);
            var line = new Paint { Color = p.Color };
            DrawRect(x, y - 1, x + w, y, line);
        }

        public void DrawLine(float x0, float y0, float x1, float y1, Paint p)
        {
            var path = new Path(); path.MoveTo(x0, y0); path.LineTo(x1, y1);
            var q = new Paint { Color = p.Color, StrokeWidth = p.StrokeWidth }; q.SetStyle(Paint.Style.Stroke);
            DrawPath(path, q);
        }
        public void DrawOval(RectF r, Paint p) => DrawRect(r, p);

        public void DrawPath(Path path, Paint p)
        {
            var r = new RectF(); path.ComputeBounds(r, true);
            if (p.Kind == Paint.Style.Stroke)
            {
                for (int i = 1; i < path.Points.Count; i++)
                {
                    if (path.Points[i].Move) continue;
                    var a = path.Points[i - 1]; var b = path.Points[i];
                    int n = (int)Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) * 2 + 1;
                    for (int k = 0; k <= n; k++)
                    {
                        float x = a.X + (b.X - a.X) * k / n, y = a.Y + (b.Y - a.Y) * k / n;
                        DrawRect(x - p.StrokeWidth / 2, y - p.StrokeWidth / 2, x + p.StrokeWidth / 2, y + p.StrokeWidth / 2, new Paint { Color = p.Color });
                    }
                }
                return;
            }
            DrawRect(r, p);
        }
    }

    public sealed class Rect { }
}

namespace Sim
{
    public static class Png
    {
        private static uint[]? _crc;
        private static uint Crc(byte[] data, int off, int len, uint c = 0xFFFFFFFF)
        {
            if (_crc == null)
            {
                _crc = new uint[256];
                for (uint n = 0; n < 256; n++) { uint v = n; for (int k = 0; k < 8; k++) v = (v & 1) != 0 ? 0xEDB88320 ^ (v >> 1) : v >> 1; _crc[n] = v; }
            }
            for (int i = off; i < off + len; i++) c = _crc[(c ^ data[i]) & 0xFF] ^ (c >> 8);
            return c;
        }

        private static void Chunk(Stream s, string type, byte[] body)
        {
            var len = BitConverter.GetBytes(body.Length); Array.Reverse(len); s.Write(len);
            var all = System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            s.Write(all);
            var crc = BitConverter.GetBytes(Crc(all, 0, all.Length) ^ 0xFFFFFFFF); Array.Reverse(crc); s.Write(crc);
        }

        public static void Write(Android.Graphics.Bitmap b, Stream s)
        {
            s.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var hdr = new byte[13];
            BitConverter.GetBytes(b.Width).Reverse().ToArray().CopyTo(hdr, 0);
            BitConverter.GetBytes(b.Height).Reverse().ToArray().CopyTo(hdr, 4);
            hdr[8] = 8; hdr[9] = 6;
            Chunk(s, "IHDR", hdr);
            var raw = new MemoryStream();
            for (int y = 0; y < b.Height; y++)
            {
                raw.WriteByte(0);
                for (int x = 0; x < b.Width; x++)
                {
                    int c = b.Get(x, y);
                    raw.WriteByte((byte)(c >> 16)); raw.WriteByte((byte)(c >> 8)); raw.WriteByte((byte)c); raw.WriteByte((byte)(c >> 24));
                }
            }
            var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(zs); }
            Chunk(s, "IDAT", z.ToArray());
            Chunk(s, "IEND", []);
        }

        public static void Save(Android.Graphics.Bitmap b, string path)
        {
            using var f = File.Create(path);
            Write(b, f);
        }
    }
}

namespace Sim
{
    /// <summary>시험기용 PNG 읽개 — 8비트 회색 · RGB · RGBA · 색표, 비월 없음.</summary>
    public static class PngRead
    {
        public static int[]? Decode(Stream s, out int w, out int h)
        {
            w = h = 0;
            var br = new BinaryReader(s);
            if (br.ReadUInt64() != 0x0A1A0A0D474E5089UL) return null;
            int depth = 0, type = 0, inter = 0;
            byte[]? pal = null, trns = null;
            var idat = new MemoryStream();
            while (true)
            {
                var lb = br.ReadBytes(4); if (lb.Length < 4) break;
                int len = lb[0] << 24 | lb[1] << 16 | lb[2] << 8 | lb[3];
                string t = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
                var data = br.ReadBytes(len); br.ReadBytes(4);
                if (t == "IHDR") { w = data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]; h = data[4] << 24 | data[5] << 16 | data[6] << 8 | data[7]; depth = data[8]; type = data[9]; inter = data[12]; }
                else if (t == "PLTE") pal = data;
                else if (t == "tRNS") trns = data;
                else if (t == "IDAT") idat.Write(data);
                else if (t == "IEND") break;
            }
            if (depth != 8 || inter != 0) return null;
            int bpp = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (bpp == 0) return null;
            idat.Position = 0;
            var raw = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(raw);
            var b = raw.ToArray();
            int stride = w * bpp;
            var cur = new byte[stride]; var prev = new byte[stride];
            var px = new int[w * h];
            int at = 0;
            for (int y = 0; y < h; y++)
            {
                int f = b[at++];
                Array.Copy(b, at, cur, 0, stride); at += stride;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0, up = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int v = f switch
                    {
                        1 => a, 2 => up, 3 => (a + up) / 2,
                        4 => Paeth(a, up, c), _ => 0,
                    };
                    cur[i] = (byte)(cur[i] + v);
                }
                for (int x = 0; x < w; x++)
                {
                    int o = x * bpp;
                    px[y * w + x] = type switch
                    {
                        0 => unchecked((int)0xFF000000) | cur[o] << 16 | cur[o] << 8 | cur[o],
                        2 => unchecked((int)0xFF000000) | cur[o] << 16 | cur[o + 1] << 8 | cur[o + 2],
                        4 => cur[o + 1] << 24 | cur[o] << 16 | cur[o] << 8 | cur[o],
                        6 => cur[o + 3] << 24 | cur[o] << 16 | cur[o + 1] << 8 | cur[o + 2],
                        _ => (trns != null && cur[o] < trns.Length ? trns[cur[o]] : 255) << 24 | (pal == null ? 0 : pal[cur[o] * 3] << 16 | pal[cur[o] * 3 + 1] << 8 | pal[cur[o] * 3 + 2]),
                    };
                }
                (cur, prev) = (prev, cur);
            }
            return px;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}

using ABitmap = Android.Graphics.Bitmap;

namespace System.Windows.Media.Imaging
{
    /// <summary>점 그림 — 안의 것은 안드로이드 그림(ARGB)이다.</summary>
    public class BitmapSource : ImageSource
    {
        internal ABitmap? Pixels;
        private protected BitmapSource() { }
        internal BitmapSource(ABitmap bmp) { Pixels = bmp; }

        public int PixelWidth => Pixels?.Width ?? 0;
        public int PixelHeight => Pixels?.Height ?? 0;
        public override double Width => PixelWidth;
        public override double Height => PixelHeight;
        public double DpiX => 96;
        public double DpiY => 96;
        public PixelFormat Format => PixelFormats.Bgra32;
        internal override ABitmap? Bitmap => Pixels;

        public static BitmapSource Create(int w, int h, double dpiX, double dpiY, PixelFormat format, BitmapPalette? palette, Array pixels, int stride)
        {
            var argb = ToArgb(w, h, format, palette, pixels, stride);
            return new BitmapSource(ABitmap.CreateBitmap(argb, w, h, ABitmap.Config.Argb8888!)!);
        }

        internal static int[] ToArgb(int w, int h, PixelFormat format, BitmapPalette? palette, Array pixels, int stride)
        {
            var argb = new int[w * h];
            switch (pixels)
            {
                case uint[] u when format.Bits == 32:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++) argb[y * w + x] = Fix((int)u[y * (stride / 4) + x], format);
                    break;
                case int[] s when format.Bits == 32:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++) argb[y * w + x] = Fix(s[y * (stride / 4) + x], format);
                    break;
                case byte[] b when format.Bits == 32:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * stride + x * 4;
                            argb[y * w + x] = Fix(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24, format);
                        }
                    break;
                case byte[] b when format.Bits == 24:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * stride + x * 3;
                            argb[y * w + x] = unchecked((int)0xFF000000) | b[i] | b[i + 1] << 8 | b[i + 2] << 16;
                        }
                    break;
                case byte[] b when format.Bits == 8:
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            byte v = b[y * stride + x];
                            argb[y * w + x] = palette != null && v < palette.Colors.Count ? palette.Colors[v].Argb
                                            : unchecked((int)0xFF000000) | v << 16 | v << 8 | v;
                        }
                    break;
            }
            return argb;
        }

        private static int Fix(int bgra, PixelFormat format)
        {
            if (format.Opaque) return bgra | unchecked((int)0xFF000000);
            if (!format.Premultiplied) return bgra;
            int a = (bgra >> 24) & 0xFF;
            if (a == 0 || a == 255) return bgra;
            int r = Math.Min(255, ((bgra >> 16) & 0xFF) * 255 / a), g = Math.Min(255, ((bgra >> 8) & 0xFF) * 255 / a), b = Math.Min(255, (bgra & 0xFF) * 255 / a);
            return a << 24 | r << 16 | g << 8 | b;
        }

        public void CopyPixels(Array pixels, int stride, int offset) => CopyPixels(new Int32Rect(0, 0, PixelWidth, PixelHeight), pixels, stride, offset);

        public void CopyPixels(Int32Rect rect, Array pixels, int stride, int offset)
        {
            if (Pixels == null) return;
            int w = rect.Width == 0 ? PixelWidth : rect.Width, h = rect.Height == 0 ? PixelHeight : rect.Height;
            var argb = new int[w * h];
            Pixels.GetPixels(argb, 0, w, rect.X, rect.Y, w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int v = argb[y * w + x];
                    switch (pixels)
                    {
                        case uint[] u: u[offset / 4 + y * (stride / 4) + x] = (uint)v; break;
                        case int[] s: s[offset / 4 + y * (stride / 4) + x] = v; break;
                        case byte[] b:
                            int i = offset + y * stride + x * 4;
                            b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); b[i + 2] = (byte)(v >> 16); b[i + 3] = (byte)(v >> 24);
                            break;
                    }
                }
        }

        public new BitmapSource Clone() => new(Pixels!.Copy(ABitmap.Config.Argb8888!, true)!);
    }

    public sealed class BitmapPalette
    {
        public BitmapPalette(IList<Color> colors) { Colors = colors; }
        public IList<Color> Colors { get; }
    }

    public sealed class WriteableBitmap : BitmapSource
    {
        public WriteableBitmap(int w, int h, double dpiX, double dpiY, PixelFormat format, BitmapPalette? palette)
        {
            Pixels = ABitmap.CreateBitmap(Math.Max(1, w), Math.Max(1, h), ABitmap.Config.Argb8888!);
            _format = format;
        }

        public WriteableBitmap(BitmapSource source)
        {
            Pixels = source.Pixels!.Copy(ABitmap.Config.Argb8888!, true);
            _format = PixelFormats.Bgra32;
        }

        private readonly PixelFormat _format;

        public void WritePixels(Int32Rect rect, Array pixels, int stride, int offset)
        {
            if (Pixels == null || rect.Width <= 0 || rect.Height <= 0) return;
            var argb = ToArgb(rect.Width, rect.Height, _format, null, Slice(pixels, offset), stride);
            Pixels.SetPixels(argb, 0, rect.Width, rect.X, rect.Y, rect.Width, rect.Height);
            WpfHost.Touch();
        }

        public void WritePixels(Int32Rect rect, Array pixels, int stride, int x, int y) =>
            WritePixels(new Int32Rect(x, y, rect.Width, rect.Height), Crop(pixels, rect, stride), rect.Width * 4, 0);

        private static Array Crop(Array pixels, Int32Rect r, int stride)
        {
            if (pixels is uint[] u)
            {
                var o = new uint[r.Width * r.Height];
                for (int y = 0; y < r.Height; y++) Array.Copy(u, (r.Y + y) * (stride / 4) + r.X, o, y * r.Width, r.Width);
                return o;
            }
            if (pixels is int[] s)
            {
                var o = new int[r.Width * r.Height];
                for (int y = 0; y < r.Height; y++) Array.Copy(s, (r.Y + y) * (stride / 4) + r.X, o, y * r.Width, r.Width);
                return o;
            }
            var b = (byte[])pixels;
            var ob = new byte[r.Width * r.Height * 4];
            for (int y = 0; y < r.Height; y++) Array.Copy(b, (r.Y + y) * stride + r.X * 4, ob, y * r.Width * 4, r.Width * 4);
            return ob;
        }

        private static Array Slice(Array pixels, int offset)
        {
            if (offset == 0) return pixels;
            return pixels switch
            {
                uint[] u => u[(offset / 4)..],
                int[] s => s[(offset / 4)..],
                byte[] b => b[offset..],
                _ => pixels,
            };
        }

        public void Lock() { }
        public void Unlock() { }
        public void AddDirtyRect(Int32Rect rect) => WpfHost.Touch();
    }

    public sealed class CroppedBitmap : BitmapSource
    {
        public CroppedBitmap(BitmapSource source, Int32Rect rect)
        {
            Pixels = source.Pixels == null ? null : ABitmap.CreateBitmap(source.Pixels, rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height));
        }
    }

    public sealed class FormatConvertedBitmap : BitmapSource
    {
        public FormatConvertedBitmap(BitmapSource source, PixelFormat format, BitmapPalette? palette, double alpha) { Pixels = source.Pixels; }
    }

    public sealed class TransformedBitmap : BitmapSource
    {
        public TransformedBitmap(BitmapSource source, Transform t)
        {
            if (source.Pixels == null) return;
            var m = t.Matrix(source.Width, source.Height);
            Pixels = ABitmap.CreateBitmap(source.Pixels, 0, 0, source.PixelWidth, source.PixelHeight, m, false);
        }
    }

    public enum BitmapCacheOption { Default, OnDemand, OnLoad, None }
    public enum BitmapCreateOptions { None, PreservePixelFormat, IgnoreImageCache = 8 }

    /// <summary>파일 · 자원 그림. 주소는 게임 폴더 안의 파일이다.</summary>
    public sealed class BitmapImage : BitmapSource
    {
        public BitmapImage() { }
        public BitmapImage(Uri uri) { UriSource = uri; Load(); }
        public Uri? UriSource { get; set; }
        public Stream? StreamSource { get; set; }
        public BitmapCacheOption CacheOption { get; set; }
        public BitmapCreateOptions CreateOptions { get; set; }
        public int DecodePixelWidth { get; set; }
        public int DecodePixelHeight { get; set; }
        public void BeginInit() { }
        public void EndInit() => Load();

        private void Load()
        {
            try
            {
                if (StreamSource != null)
                {
                    Pixels = Android.Graphics.BitmapFactory.DecodeStream(StreamSource);
                    return;
                }
                if (UriSource == null) return;
                string path = UriSource.IsAbsoluteUri ? UriSource.LocalPath : UriSource.OriginalString;
                if (File.Exists(path)) Pixels = Android.Graphics.BitmapFactory.DecodeFile(path);
            }
            catch { Pixels = null; }
            if (Pixels != null && !Pixels.IsMutable) Pixels = Pixels.Copy(ABitmap.Config.Argb8888!, false);
        }
    }

    public sealed class RenderTargetBitmap : BitmapSource
    {
        public RenderTargetBitmap(int w, int h, double dpiX, double dpiY, PixelFormat format)
        {
            Pixels = ABitmap.CreateBitmap(Math.Max(1, w), Math.Max(1, h), ABitmap.Config.Argb8888!);
        }

        public void Render(Visual v)
        {
            if (Pixels == null || v is not UIElement e) return;
            var c = new Android.Graphics.Canvas(Pixels);
            WpfHost.DrawTree(c, e, 1);
        }
    }

    public abstract class BitmapEncoder
    {
        public List<BitmapFrame> Frames { get; } = [];
        public void Save(Stream s)
        {
            if (Frames.Count > 0 && Frames[0].Source.Pixels is { } bmp) bmp.Compress(ABitmap.CompressFormat.Png!, 100, s);
        }
    }

    public sealed class PngBitmapEncoder : BitmapEncoder { }

    public sealed class BitmapFrame : BitmapSource
    {
        internal BitmapSource Source => this;
        private BitmapFrame(ABitmap? bmp) { Pixels = bmp; }
        public static BitmapFrame Create(BitmapSource s) => new(s.Pixels);
        public static BitmapFrame Create(Uri uri, BitmapCreateOptions options = BitmapCreateOptions.None, BitmapCacheOption cache = BitmapCacheOption.Default) =>
            new(new BitmapImage(uri).Pixels);
        public static BitmapFrame Create(Stream stream, BitmapCreateOptions options = BitmapCreateOptions.None, BitmapCacheOption cache = BitmapCacheOption.Default)
        {
            var img = new BitmapImage { StreamSource = stream };
            img.EndInit();
            return new(img.Pixels);
        }
    }

    public sealed class PngBitmapDecoder
    {
        public PngBitmapDecoder(Stream stream, BitmapCreateOptions options, BitmapCacheOption cache) { Frames = [BitmapFrame.Create(stream)]; }
        public PngBitmapDecoder(Uri uri, BitmapCreateOptions options, BitmapCacheOption cache) { Frames = [BitmapFrame.Create(uri)]; }
        public List<BitmapFrame> Frames { get; }
    }
}

namespace System.IO
{
    public sealed class FileFormatException : FormatException
    {
        public FileFormatException() { }
        public FileFormatException(string message) : base(message) { }
    }
}

using CdsHelper.Game.Local.Helpers;

using Android.App;
using Android.Widget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.UI.Views;

namespace CostaDelSol.Droid.Map;

/// <summary>
/// 지도 위 덧그림 — 내 배(말), 닻 · 대 둔 배, 남의 배, 자동항해 항로, 비 · 눈. 자리와 그림은 원본 지도 엔진(ShipMapHost)이
/// 프레임마다 짠 것 그대로다(화면 픽셀). GL 지도와 같은 크기 · 같은 자리에 겹쳐 선다.
/// </summary>
public sealed class MapOverlay : View
{
    private static readonly Paint Pixel = new() { FilterBitmap = false, AntiAlias = false };
    private readonly ShipMapWindow _map;
    private int _spriteSeen = -1, _overlaySeen = -1;
    private Bitmap? _sprite, _overlay;
    private Bitmap[]? _folk;
    private uint[]? _folkFrom;
    private readonly Paint _route = new() { AntiAlias = true, StrokeWidth = 3 };
    private readonly Paint _rain = new() { AntiAlias = false, StrokeWidth = 2 };
    private readonly Random _dice = new();

    public MapOverlay(Context context, ShipMapWindow map) : base(context)
    {
        _map = map;
        _route.Color = Color.Argb(200, 240, 220, 120);
        _route.SetStyle(Paint.Style.Stroke);
    }

    private static Bitmap? Bmp(uint[]? bgra, int side)
    {
        if (bgra == null || bgra.Length < side * side) return null;
        var argb = new int[side * side];
        for (int i = 0; i < argb.Length; i++) argb[i] = (int)bgra[i];
        return Bitmap.CreateBitmap(argb, side, side, Bitmap.Config.Argb8888!);
    }

    protected override void OnDraw(Canvas canvas)
    {
        var host = _map.Host;
        var view = host.View;
        var layers = host.Layers;
        if (view.Width <= 0) { PostInvalidateOnAnimation(); return; }
        float k = Width / (float)view.Width;   // 엔진이 짠 크기와 판 크기가 다를 때(돌리기 직후)

        MapD3DRenderer.FolkDraw[] folk;
        MapD3DRenderer.RouteDraw[] route;
        lock (layers.Gate)
        {
            if (layers.SpriteVersion != _spriteSeen) { _spriteSeen = layers.SpriteVersion; _sprite = Bmp(layers.Sprite, 48); }
            if (layers.OverlayVersion != _overlaySeen)
            {
                _overlaySeen = layers.OverlayVersion;
                var o = layers.Overlay;
                _overlay = o == null ? null : Bmp(o, o.Length >= 48 * 48 ? 48 : 16);
            }
            if (!ReferenceEquals(layers.FolkSprites, _folkFrom))
            {
                _folkFrom = layers.FolkSprites;
                _folk = _folkFrom == null ? null : [.. Enumerable.Range(0, MapD3DRenderer.FolkFrames)
                    .Select(i => Bmp(_folkFrom.AsSpan(i * 48 * 48, 48 * 48).ToArray(), 48)!)];
            }
            folk = layers.Folk;
            route = layers.Route;
        }

        // 항로 — 이웃한 두 마디가 둘 다 켜져 있을 때만 잇는다.
        for (int i = 1; i < route.Length; i++)
            if (route[i - 1].Active && route[i].Active)
                canvas.DrawLine(route[i - 1].X * k, route[i - 1].Y * k, route[i].X * k, route[i].Y * k, _route);

        if (_folk != null)
            foreach (var f in folk)
            {
                if (f.Frame < 0 || f.Frame >= _folk.Length) continue;
                float side = MapD3DRenderer.FolkSize * f.Scale;
                canvas.DrawBitmap(_folk[f.Frame], null, new RectF(f.X * k, f.Y * k, (f.X + side) * k, (f.Y + side) * k), Pixel);
            }

        var (sx, sy, sw, sh) = view.Ship;
        if (sw > 0 && _sprite != null)
            canvas.DrawBitmap(_sprite, null, new RectF(sx * k, sy * k, (sx + sw) * k, (sy + sh) * k), Pixel);

        // 닻(16x16 이 배 그림 48x48 의 왼쪽 아래에 찍혀 있다) · 뭍에 대 둔 배.
        var (ox, oy, ow, oh) = view.Overlay;
        if (ow > 0 && _overlay != null)
            canvas.DrawBitmap(_overlay, null, new RectF(ox * k, oy * k, (ox + ow) * k, (oy + oh) * k), Pixel);

        DrawArrows(canvas, view, layers, k);
        DrawWeather(canvas);
        // 리메이크 덧그림 상자(좌표 · 컨디션 · 미니맵 · 만난 사람) — 판 점 기준, 지도 윗변은 판 y 29.
        float board = Height / (480f - 58f);   // 지도는 판 높이 422 다(두 띠 사이)
        _map.MapBoardWidth = Width / Math.Max(0.01f, board);
        System.Windows.WpfHost.DrawMapLayers(canvas, board, 29);
        PostInvalidateOnAnimation();
    }

    private readonly Paint _arrow = new() { AntiAlias = true, StrokeCap = Paint.Cap.Round };
    private readonly Android.Graphics.Path _head = new();

    /// <summary>
    /// 바람(위 · 연노랑)과 해류(아래 · 하늘색) 화살표 — 50x25 격자 한 칸마다 하나씩(PC 판 셰이더 ArrowsAt 와 같은 자리 · 크기 · 색).
    /// 모드 창의 「바람 · 해류 화살표」로 켠다.
    /// </summary>
    private void DrawArrows(Canvas canvas, ShipMapHost.Frame view, MapD3DRenderer layers, float k)
    {
        if (!layers.ShowArrows || layers.FlowGrid is not { } grid || _map.Host.Wind is not { } wind) return;
        const int Grid = WindTable.CellRaw / 16;          // 격자 한 칸 = 지도 50칸
        const double World = WindTable.Cols * Grid;        // 2500칸 — 가로로 돈다
        double cpp = view.CellsPerPixel;
        if (cpp <= 0) return;
        float cellPx = (float)(Grid / cpp);
        float h = Math.Clamp(cellPx * 0.22f, 4, 14);
        double first = Math.Floor(view.OriginX / Grid) - 1, last = Math.Ceiling((view.OriginX + view.Width * cpp) / Grid) + 1;
        for (int gy = 0; gy < WindTable.Rows; gy++)
        {
            float cy = (float)(((gy + 0.5) * Grid - view.OriginY) / cpp);
            if (cy < -h * 2 || cy > view.Height + h * 2) continue;
            for (double g = first; g <= last; g++)
            {
                int gx = (int)(((g % WindTable.Cols) + WindTable.Cols) % WindTable.Cols);
                float cx = (float)(((g + 0.5) * Grid - view.OriginX) / cpp);
                uint word = grid[gy * WindTable.Cols + gx];
                Glyph(canvas, wind, cx * k, (cy - h) * k, h * k, word & 0xFFFF, Color.Argb(230, 255, 240, 184));
                Glyph(canvas, wind, cx * k, (cy + h) * k, h * k, word >> 16, Color.Argb(230, 107, 230, 255));
            }
        }
    }

    private void Glyph(Canvas canvas, WindTable wind, float x, float y, float h, uint packed, Color tint)
    {
        int dir = (int)(packed & 0xF), speed = (int)((packed >> 4) & 0xF);
        if (speed <= 0) return;
        var (dx, dy) = wind.Vector(dir);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0) return;
        float ux = dx / len, uy = dy / len;
        float x0 = x - ux * h, y0 = y - uy * h, x1 = x + ux * h, y1 = y + uy * h;
        int alpha = (int)(255 * (0.45f + 0.55f * speed / 7f));
        // 짙은 밑줄을 먼저 깔고 그 위에 색 — 바다 · 뭍 어디서나 읽히게(셰이더의 테두리).
        _arrow.Color = Color.Argb(Math.Min(255, alpha), 10, 10, 18);
        _arrow.StrokeWidth = Math.Max(2, h * 0.32f);
        canvas.DrawLine(x0, y0, x1, y1, _arrow);
        _arrow.Color = Color.Argb(Math.Min(255, alpha), tint.R, tint.G, tint.B);
        _arrow.StrokeWidth = Math.Max(1, h * 0.16f);
        canvas.DrawLine(x0, y0, x1, y1, _arrow);
        _head.Reset();
        _head.MoveTo(x1 + ux * h * 0.2f, y1 + uy * h * 0.2f);
        _head.LineTo(x1 - ux * h * 0.5f - uy * h * 0.4f, y1 - uy * h * 0.5f + ux * h * 0.4f);
        _head.LineTo(x1 - ux * h * 0.5f + uy * h * 0.4f, y1 - uy * h * 0.5f - ux * h * 0.4f);
        _head.Close();
        canvas.DrawPath(_head, _arrow);
    }

    /// <summary>비 · 눈 — 원본 SeaWeather 가 정한 날씨를 화면에 흩뿌린다.</summary>
    private void DrawWeather(Canvas canvas)
    {
        var weather = _map.Weather;
        if (weather == SeaWeather.Kind.None) return;
        bool snow = weather == SeaWeather.Kind.Snow;
        _rain.Color = snow ? Color.Argb(220, 255, 255, 255) : Color.Argb(160, 180, 200, 255);
        int count = Width * Height / (snow ? 9000 : 5000);
        long t = Android.OS.SystemClock.UptimeMillis();
        for (int i = 0; i < count; i++)
        {
            // 자리는 번호로 정해 두고 시간만큼 흘린다 — 매 프레임 새로 굴리면 지글거린다.
            float x = (i * 7919 % Math.Max(1, Width) + t * (snow ? 0.02f : 0.15f) * (i % 3 + 1)) % Math.Max(1, Width);
            float y = (i * 104729 % Math.Max(1, Height) + t * (snow ? 0.06f : 0.9f) * (i % 2 + 1)) % Math.Max(1, Height);
            if (snow) canvas.DrawCircle(x, y, 2.5f, _rain);
            else canvas.DrawLine(x, y, x - 3, y + 14, _rain);
        }
        _ = _dice;
    }
}

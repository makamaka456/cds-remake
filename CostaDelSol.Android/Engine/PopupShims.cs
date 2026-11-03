using Android.App;
using Android.Widget;
using System.Windows;
using Android.Graphics;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;
using CdsHelper.Game.Local.Helpers;
using CostaDelSol.Droid.Game;
using CostaDelSol.Droid.Input;
using CostaDelSol.Droid.Town;
using CostaDelSol.Droid.Ui;
using MotionEventActions = Android.Views.MotionEventActions;

// 그림을 잠깐 띄우는 것들 — 사건 그림(EVSTILL), 효과 애니메이션(MPEFFECT 80x80), 적대 도시 성문 앞 장면.
// PC 판 EventStillPopup · EffectPopup · GateScene 의 크기 · 테 · 박자(140 · 100ms) 그대로다.

namespace CdsHelper.Game.UI.Views
{
    /// <summary>판 위에 그림 한 장(손은 안 받는다). 그림은 바꿔 끼울 수 있다(애니메이션).</summary>
    internal sealed class PictureLayer : ChoicePanel.IGameWindow
    {
        private static readonly Paint Pixel = new() { FilterBitmap = false, AntiAlias = false };
        private static readonly Color FrameEdge = Color.Rgb(0x11, 0x09, 0x09), FrameFill = Color.Rgb(0x4A, 0x2E, 0x24);
        private readonly float _w, _h, _frame, _scale;
        public Bitmap? Art;
        public Bitmap? Effect;

        public PictureLayer(float w, float h, float scale, bool framed)
        {
            _w = w; _h = h; _scale = scale;
            _frame = framed ? 1 + 8 + 1 : 0;
        }

        public float Width => _w * _scale + _frame * 2;
        public float Height => _h * _scale + _frame * 2;

        public void Draw(Android.Content.Context context, Canvas canvas, float x, float y, float s, bool top)
        {
            if (_frame > 0)
            {
                WindowArt.Rect(canvas, x, y, Width, Height, FrameFill, s);
                WindowArt.Edges(canvas, x, y, Width, Height, FrameEdge, 1, s);
                WindowArt.Edges(canvas, x + 9, y + 9, Width - 18, Height - 18, FrameEdge, 1, s);
            }
            if (Art != null)
                canvas.DrawBitmap(Art, null, new RectF((x + _frame) * s, (y + _frame) * s,
                                  (x + _frame + _w * _scale) * s, (y + _frame + _h * _scale) * s), Pixel);
            if (Effect != null)
            {
                float e = EffectAnim.Size;
                float ex = x + (Width - e) / 2, ey = y + (Height - e) / 2;
                canvas.DrawBitmap(Effect, null, new RectF(ex * s, ey * s, (ex + e) * s, (ey + e) * s), Pixel);
            }
        }

        public void Key(GameKey key) { }
        public void Touch(MotionEventActions action, float x, float y) { }
        public void Cancel() { }

        /// <summary>판에 얹는다 — 걷을 때 쓸 창 손잡이를 낸다.</summary>
        public Handle Show()
        {
            var layer = new ChoicePanel.Layer { Kind = ChoicePanel.Kind.Custom, Custom = this };
            GameThread.OnUi(() => GameThread.Panel.Push(layer));
            return new Handle(layer);
        }

        public sealed class Handle(ChoicePanel.Layer layer) : Window
        {
            public override void Close()
            {
                GameThread.OnUi(() => GameThread.Panel.Remove(layer));
                base.Close();
            }
        }

        /// <summary>효과 애니메이션을 그 판 가운데에서 돌린다(게임 줄).</summary>
        public void PlayEffect(Engine.Game game, int anim, bool won)
        {
            if (game.Effects is not { } effects) return;
            int span = anim == EffectAnim.Coin ? 100 : 140;
            var art = new Bitmap?[EffectAnim.FrameCount];
            try
            {
                foreach (int f in EffectAnim.Frames(anim, won))
                {
                    if (art[f] == null)
                    {
                        if (effects.TryGetBgra(anim, f) is not { } bgra) continue;
                        art[f] = GameThread.Face(bgra, EffectAnim.Size, EffectAnim.Size);
                    }
                    Effect = art[f];
                    GameThread.OnUi(() => GameThread.Panel.Invalidate());
                    Thread.Sleep(span);
                }
            }
            finally
            {
                Effect = null;
                GameThread.OnUi(() => GameThread.Panel.Invalidate());
            }
        }
    }



}

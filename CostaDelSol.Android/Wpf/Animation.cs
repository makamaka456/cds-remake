namespace System.Windows.Media.Animation
{
    public struct RepeatBehavior
    {
        internal double Count;
        internal bool IsForever;
        public RepeatBehavior(double count) { Count = count; IsForever = false; }
        public static RepeatBehavior Forever => new() { IsForever = true, Count = double.PositiveInfinity };
    }

    public enum FillBehavior { HoldEnd, Stop }

    public struct KeyTime
    {
        public TimeSpan TimeSpan;
        public static KeyTime FromTimeSpan(TimeSpan t) => new() { TimeSpan = t };
        public static implicit operator KeyTime(TimeSpan t) => FromTimeSpan(t);
    }

    public interface IEasingFunction { }
    public sealed class QuadraticEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
    public sealed class CubicEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
    public sealed class SineEase : IEasingFunction { public EasingMode EasingMode { get; set; } }
    public enum EasingMode { EaseIn, EaseOut, EaseInOut }

    public abstract class AnimationTimeline : Freezable
    {
        public Duration Duration { get; set; } = new(TimeSpan.FromSeconds(1));
        public RepeatBehavior RepeatBehavior { get; set; } = new(1);
        public bool AutoReverse { get; set; }
        public TimeSpan? BeginTime { get; set; } = TimeSpan.Zero;
        public FillBehavior FillBehavior { get; set; }
        public IEasingFunction? EasingFunction { get; set; }
        public event EventHandler? Completed;
        internal void Done() => Completed?.Invoke(this, EventArgs.Empty);

        /// <summary>흐른 몫(0~1)에서의 값.</summary>
        internal abstract object? ValueAt(double t, object? from, TimeSpan elapsed);
    }

    public sealed class DoubleAnimation : AnimationTimeline
    {
        public DoubleAnimation() { }
        public DoubleAnimation(double to, Duration d) { To = to; Duration = d; }
        public DoubleAnimation(double from, double to, Duration d) { From = from; To = to; Duration = d; }
        public double? From { get; set; }
        public double? To { get; set; }
        public double? By { get; set; }
        internal override object? ValueAt(double t, object? from, TimeSpan elapsed)
        {
            double a = From ?? (from is double d ? d : 0);
            double b = To ?? a + (By ?? 0);
            return a + (b - a) * t;
        }
    }

    public sealed class ColorAnimation : AnimationTimeline
    {
        public ColorAnimation() { }
        public ColorAnimation(Color to, Duration d) { To = to; Duration = d; }
        public ColorAnimation(Color from, Color to, Duration d) { From = from; To = to; Duration = d; }
        public Color? From { get; set; }
        public Color? To { get; set; }
        internal override object? ValueAt(double t, object? from, TimeSpan elapsed)
        {
            var a = From ?? (from is Color c ? c : Colors.Transparent);
            var b = To ?? a;
            byte L(byte x, byte y) => (byte)(x + (y - x) * t);
            return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }
    }

    public abstract class ColorKeyFrame
    {
        public Color Value { get; set; }
        public KeyTime KeyTime { get; set; }
    }

    public sealed class DiscreteColorKeyFrame : ColorKeyFrame
    {
        public DiscreteColorKeyFrame() { }
        public DiscreteColorKeyFrame(Color value, KeyTime time) { Value = value; KeyTime = time; }
    }

    public sealed class LinearColorKeyFrame : ColorKeyFrame
    {
        public LinearColorKeyFrame() { }
        public LinearColorKeyFrame(Color value, KeyTime time) { Value = value; KeyTime = time; }
    }

    public sealed class ColorAnimationUsingKeyFrames : AnimationTimeline
    {
        public List<ColorKeyFrame> KeyFrames { get; } = [];
        internal override object? ValueAt(double t, object? from, TimeSpan elapsed)
        {
            Color v = from is Color c ? c : Colors.Transparent;
            foreach (var k in KeyFrames.OrderBy(k => k.KeyTime.TimeSpan))
                if (elapsed >= k.KeyTime.TimeSpan) v = k.Value;
            return v;
        }
    }

    public abstract class DoubleKeyFrame
    {
        public double Value { get; set; }
        public KeyTime KeyTime { get; set; }
    }

    public sealed class DiscreteDoubleKeyFrame : DoubleKeyFrame
    {
        public DiscreteDoubleKeyFrame() { }
        public DiscreteDoubleKeyFrame(double value, KeyTime time) { Value = value; KeyTime = time; }
    }

    public sealed class LinearDoubleKeyFrame : DoubleKeyFrame
    {
        public LinearDoubleKeyFrame() { }
        public LinearDoubleKeyFrame(double value, KeyTime time) { Value = value; KeyTime = time; }
    }

    public sealed class DoubleAnimationUsingKeyFrames : AnimationTimeline
    {
        public List<DoubleKeyFrame> KeyFrames { get; } = [];
        internal override object? ValueAt(double t, object? from, TimeSpan elapsed)
        {
            double v = from is double d ? d : 0;
            foreach (var k in KeyFrames.OrderBy(k => k.KeyTime.TimeSpan))
                if (elapsed >= k.KeyTime.TimeSpan) v = k.Value;
            return v;
        }
    }

    /// <summary>돌고 있는 움직임들 — 게임 줄 시계(WpfHost.Tick)가 민다.</summary>
    internal static class Animator
    {
        private sealed class Run
        {
            public DependencyObject Target = null!;
            public DependencyProperty Property = null!;
            public AnimationTimeline Timeline = null!;
            public object? From;
            public long Start;
        }

        private static readonly List<Run> Runs = [];
        internal static bool Busy => Runs.Count > 0;

        internal static void Begin(DependencyObject target, DependencyProperty p, AnimationTimeline? a)
        {
            Runs.RemoveAll(r => ReferenceEquals(r.Target, target) && ReferenceEquals(r.Property, p));
            if (a == null) return;
            var run = new Run { Target = target, Property = p, Timeline = a, From = target.GetValue(p), Start = WpfHost.Now + (long)(a.BeginTime ?? TimeSpan.Zero).TotalMilliseconds };
            Runs.Add(run);
            Apply(run, WpfHost.Now);
        }

        internal static void Step(long now)
        {
            foreach (var r in Runs.ToList()) Apply(r, now);
        }

        private static void Apply(Run r, long now)
        {
            var a = r.Timeline;
            if (now < r.Start) return;
            double span = Math.Max(1, a.Duration.TimeSpan.TotalMilliseconds);
            double cycle = a.AutoReverse ? span * 2 : span;
            double elapsed = now - r.Start;
            double repeats = a.RepeatBehavior.IsForever ? double.PositiveInfinity : Math.Max(1, a.RepeatBehavior.Count);
            bool done = elapsed >= cycle * repeats;
            if (done) elapsed = cycle * repeats - 0.0001;
            double inCycle = elapsed % cycle;
            double t = inCycle <= span ? inCycle / span : 2 - inCycle / span;
            var value = a.ValueAt(Math.Clamp(t, 0, 1), r.From, TimeSpan.FromMilliseconds(Math.Min(inCycle, span)));
            r.Target.SetValue(r.Property, value);
            if (done)
            {
                Runs.Remove(r);
                if (done && a.FillBehavior == FillBehavior.Stop) r.Target.SetValue(r.Property, r.From);
                a.Done();
            }
        }
    }

    public sealed class Storyboard
    {
        public List<AnimationTimeline> Children { get; } = [];
        public event EventHandler? Completed;
        private static readonly Dictionary<AnimationTimeline, (DependencyObject? Target, DependencyProperty? Prop)> Targets = [];
        public static void SetTarget(AnimationTimeline a, DependencyObject target) => Targets[a] = (target, Targets.GetValueOrDefault(a).Prop);
        public static void SetTargetProperty(AnimationTimeline a, PropertyPath path) => Targets[a] = (Targets.GetValueOrDefault(a).Target, path.Property);

        public void Begin()
        {
            int left = Children.Count;
            foreach (var a in Children)
            {
                var (t, p) = Targets.GetValueOrDefault(a);
                if (t == null || p == null) { left--; continue; }
                a.Completed += (_, _) => { if (--left == 0) Completed?.Invoke(this, EventArgs.Empty); };
                Animator.Begin(t, p, a);
            }
            if (left == 0) Completed?.Invoke(this, EventArgs.Empty);
        }

        public void Begin(FrameworkElement e) => Begin();
        public void Stop() { }
    }
}

namespace System.Windows
{
    public sealed class PropertyPath
    {
        public PropertyPath(object p) { Property = p as DependencyProperty; }
        internal DependencyProperty? Property { get; }
    }
}

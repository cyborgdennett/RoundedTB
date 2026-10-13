using System;

namespace RoundedTB;

internal sealed class HoverTransition
{
    internal bool Initialized { get; private set; }
    internal double Progress { get; private set; }
    internal bool Opening { get; private set; }
    private bool requested, target;
    private long? leftAt;
    private long began;
    private double from;
    private Types.HoverAnimationEffect animationEffect;
    private int animationDuration;

    internal bool Update(bool visible, Types.SegmentSettings settings, long now)
    {
        var previous = Progress;
        if (!Initialized)
        {
            Initialized = true;
            Progress = visible ? 1 : 0;
            requested = target = visible;
            from = Progress;
            began = now;
            Opening = visible;
            animationEffect = settings == null ? Types.HoverAnimationEffect.None : visible ? settings.HoverOpenEffect : settings.HoverCloseEffect;
            animationDuration = Math.Clamp(visible ? settings?.HoverOpenDurationMs ?? 0 : settings?.HoverCloseDurationMs ?? 0, 0, 5000);
            return true;
        }
        if (visible) leftAt = null;
        else if (requested) leftAt = now;
        requested = visible;
        var seconds = settings?.HoverHideDelaySeconds ?? 0;
        var delay = Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, 60) * 1000;
        var next = visible || (leftAt.HasValue && now - leftAt.Value < delay);
        var effect = settings == null ? Types.HoverAnimationEffect.None : next ? settings.HoverOpenEffect : settings.HoverCloseEffect;
        var duration = Math.Clamp(next ? settings?.HoverOpenDurationMs ?? 0 : settings?.HoverCloseDurationMs ?? 0, 0, 5000);
        if (next != target || effect != animationEffect || duration != animationDuration)
        {
            target = next;
            Opening = next;
            from = Progress;
            began = now;
            animationEffect = effect;
            animationDuration = duration;
        }
        if (effect == Types.HoverAnimationEffect.None)
            Progress = target ? 1 : 0;
        else
        {
            var t = duration == 0 ? 1 : Math.Clamp((now - began) / (double)duration, 0, 1);
            var eased = t * t * (3 - 2 * t);
            Progress = from + ((target ? 1 : 0) - from) * eased;
        }
        return previous != Progress;
    }

    internal LocalPInvoke.RECT Clip(LocalPInvoke.RECT bounds, Types.SegmentSettings settings)
    {
        var effect = Opening ? settings.HoverOpenEffect : settings.HoverCloseEffect;
        if (effect == Types.HoverAnimationEffect.None) return bounds;
        if (effect == Types.HoverAnimationEffect.Expand)
        {
            var inset = (int)Math.Round((bounds.Right - bounds.Left) * (1 - Progress) / 2);
            bounds.Left += inset;
            bounds.Right -= inset;
        }
        else
            bounds.Top = bounds.Bottom - (int)Math.Round((bounds.Bottom - bounds.Top) * Progress);
        return bounds;
    }
}

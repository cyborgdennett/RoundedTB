using System;

namespace RoundedTB;

// Explorer reports a layout destination before the composition animation starts.
// Follow the Start button's animated position after that reset, using one center
// for both edges so staggered button animations cannot make the region wobble.
internal sealed class CenteredTaskbarBounds
{
    private bool initialized, waitingForAnimation, fallback;
    private double left, start, target, center;
    private int direction, expectedDirection;
    private long lastSample, transitionStart, targetChanged;
    public bool IsMoving { get; private set; }

    public void Initialize(LocalPInvoke.RECT bounds, long time, double? stableCenter = null)
    {
        center = stableCenter ?? (bounds.Left + (double)bounds.Right) / 2;
        left = start = target = bounds.Left;
        initialized = true;
        IsMoving = waitingForAnimation = fallback = false;
        lastSample = time;
        expectedDirection = 0;
    }

    public void ExpectDirection(int nextDirection)
    {
        if (IsMoving && nextDirection != 0 && nextDirection != direction)
        {
            target = left;
            IsMoving = waitingForAnimation = fallback = false;
            expectedDirection = nextDirection;
        }
    }

    public LocalPInvoke.RECT Follow(LocalPInvoke.RECT native, long time, double scale)
    {
        if (!initialized) Initialize(native, time);
        long elapsed = Math.Max(0, time - lastSample);
        if (elapsed > 750)
        {
            // Returning from a maximized taskbar must use its current layout.
            left = start = target = native.Left;
            IsMoving = waitingForAnimation = fallback = false;
        }
        lastSample = time;
        if (!IsMoving && native.Left != target
            && (expectedDirection == 0 || (native.Left - target) * expectedDirection > 0))
        {
            start = left;
            target = native.Left;
            direction = Math.Sign(target - start);
            expectedDirection = 0;
            IsMoving = true;
            waitingForAnimation = Math.Abs(target - start) > 2 * scale;
            fallback = false;
            transitionStart = targetChanged = time;
        }
        if (IsMoving)
        {
            if (waitingForAnimation)
            {
                double progress = (native.Left - start) * direction;
                double distance = (target - start) * direction;
                if (progress >= 0 && progress < distance) waitingForAnimation = false;
                else if (time - transitionStart >= 450)
                {
                    // Animations may be disabled or their first frames missed.
                    waitingForAnimation = false;
                    fallback = true;
                }
            }
            if (!waitingForAnimation)
            {
                if ((native.Left - target) * direction > 0)
                {
                    target = native.Left;
                    targetChanged = time;
                }
                double candidate = fallback
                    ? left + (target - left) * (1 - Math.Exp(-elapsed / 40.0))
                    : native.Left;
                left = direction > 0
                    ? Math.Clamp(candidate, left, target)
                    : Math.Clamp(candidate, target, left);
                if (Math.Abs(left - target) < 0.5 && native.Left == target && time - targetChanged >= 100)
                {
                    left = target;
                    IsMoving = false;
                }
            }
        }
        native.Left = (int)Math.Round(left);
        native.Right = (int)Math.Round(2 * center - left);
        return native;
    }
}

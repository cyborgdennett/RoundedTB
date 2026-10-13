using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RoundedTB;

public partial class TaskbarEffect : Window
{
    private readonly DispatcherTimer timer;
    public TaskbarEffect()
    {
        InitializeComponent();
        Position = new PixelPoint(0, 0);
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => MoveTheThingy();
        Opened += (_, _) => timer.Start();
        Closed += (_, _) => timer.Stop();
    }
    public void MoveTheThingy()
    {
        if (!LocalPInvoke.GetCursorPos(out var cursor)) return;
        var point = this.PointToClient(new PixelPoint(cursor.x, cursor.y));
        Canvas.SetLeft(eye, point.X - eye.Width / 2);
        Canvas.SetTop(eye, point.Y - eye.Height / 2);
    }
}

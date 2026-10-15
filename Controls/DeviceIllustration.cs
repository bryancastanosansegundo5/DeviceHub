using System.Windows;
using System.Windows.Media;
using NexoHub.Models;

namespace NexoHub.Controls;

/// <summary>Original vector thumbnails for common peripheral classes.</summary>
public sealed class DeviceIllustration : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(DeviceIllustration), new FrameworkPropertyMetadata("Generic", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(DeviceIllustration), new FrameworkPropertyMetadata(Brushes.MediumTurquoise, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(28, 41, 49)), null,
            new Rect(0, 0, ActualWidth, ActualHeight), 14, 14);
        var scale = Math.Min((ActualWidth - 12) / 160, (ActualHeight - 12) / 100);
        if (scale <= 0) return;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - 160 * scale) / 2, (ActualHeight - 100 * scale) / 2));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        var shell = new SolidColorBrush(Color.FromRgb(67, 83, 91));
        var dark = new SolidColorBrush(Color.FromRgb(22, 31, 37));
        var line = new Pen(new SolidColorBrush(Color.FromRgb(166, 183, 188)), 2.2) { LineJoin = PenLineJoin.Round };
        var accentPen = new Pen(Accent, 3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        switch (Kind)
        {
            case nameof(DeviceKind.Mouse): DrawMouse(drawingContext, shell, dark, line, accentPen); break;
            case nameof(DeviceKind.Gamepad): DrawGamepad(drawingContext, shell, dark, line, accentPen); break;
            case nameof(DeviceKind.Keyboard): DrawKeyboard(drawingContext, shell, dark, line, accentPen); break;
            case nameof(DeviceKind.Microphone): DrawMicrophone(drawingContext, shell, dark, line, accentPen); break;
            case nameof(DeviceKind.Headset): DrawHeadset(drawingContext, shell, dark, line, accentPen); break;
            case nameof(DeviceKind.Receiver): DrawReceiver(drawingContext, shell, dark, line, accentPen); break;
            default: DrawGeneric(drawingContext, shell, dark, line, accentPen); break;
        }
        drawingContext.Pop();
        drawingContext.Pop();
    }

    private void DrawMouse(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        var body = Geometry.Parse("M 80,13 C 57,13 48,30 48,54 L 48,78 C 48,94 61,99 80,99 C 99,99 112,94 112,78 L 112,54 C 112,30 103,13 80,13 Z");
        dc.DrawGeometry(shell, line, body);
        dc.DrawLine(new Pen(dark, 2), new Point(80, 14), new Point(80, 50));
        dc.DrawRoundedRectangle(dark, null, new Rect(76, 25, 8, 16), 4, 4);
        dc.DrawLine(accent, new Point(55, 52), new Point(105, 52));
        dc.DrawRoundedRectangle(Accent, null, new Rect(73, 29, 14, 5), 2, 2);
    }

    private void DrawGamepad(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        var body = Geometry.Parse("M 31,37 C 35,25 49,22 64,28 L 72,32 L 88,32 L 96,28 C 111,22 125,25 129,37 L 142,72 C 146,86 136,92 126,84 L 107,68 L 53,68 L 34,84 C 24,92 14,86 18,72 Z");
        dc.DrawGeometry(shell, line, body);
        dc.DrawLine(new Pen(dark, 4), new Point(43, 48), new Point(43, 62));
        dc.DrawLine(new Pen(dark, 4), new Point(36, 55), new Point(50, 55));
        dc.DrawEllipse(dark, null, new Point(73, 52), 8, 8);
        dc.DrawEllipse(null, accent, new Point(110, 46), 5, 5);
        dc.DrawEllipse(null, accent, new Point(121, 56), 5, 5);
        dc.DrawEllipse(null, accent, new Point(99, 56), 5, 5);
        dc.DrawEllipse(null, accent, new Point(110, 66), 5, 5);
        dc.DrawRoundedRectangle(dark, null, new Rect(68, 76, 25, 5), 3, 3);
    }

    private void DrawKeyboard(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        dc.DrawRoundedRectangle(shell, line, new Rect(13, 25, 134, 56), 9, 9);
        for (var row = 0; row < 4; row++)
        for (var col = 0; col < 13; col++)
        {
            var x = 20 + col * 9.6;
            var y = 32 + row * 10.5;
            var special = (row == 0 && col is 0 or 5 or 6 or 12) || (row == 3 && col is 0 or 12);
            var fill = special ? Accent : dark;
            dc.DrawRoundedRectangle(fill, null, new Rect(x, y, 7.3, 7.2), 1.5, 1.5);
        }
        dc.DrawRoundedRectangle(dark, null, new Rect(55, 74, 49, 4), 2, 2);
    }

    private void DrawMicrophone(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        dc.DrawRoundedRectangle(shell, line, new Rect(61, 9, 38, 61), 19, 19);
        for (var i = 0; i < 4; i++)
            dc.DrawLine(new Pen(dark, 2), new Point(68 + i * 7, 19), new Point(68 + i * 7, 42));
        dc.DrawEllipse(Accent, null, new Point(80, 55), 3, 3);
        dc.DrawLine(line, new Point(80, 71), new Point(80, 83));
        dc.DrawLine(accent, new Point(58, 82), new Point(102, 82));
        dc.DrawLine(line, new Point(68, 91), new Point(92, 91));
        dc.DrawLine(line, new Point(80, 83), new Point(68, 91));
        dc.DrawLine(line, new Point(80, 83), new Point(92, 91));
    }

    private void DrawHeadset(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        var arch = new StreamGeometry();
        using (var context = arch.Open())
        {
            context.BeginFigure(new Point(36, 62), false, false);
            context.BezierTo(new Point(35, 18), new Point(125, 18), new Point(124, 62), true, false);
        }
        dc.DrawGeometry(null, new Pen(Accent, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arch);
        dc.DrawRoundedRectangle(shell, line, new Rect(27, 54, 22, 32), 7, 7);
        dc.DrawRoundedRectangle(shell, line, new Rect(111, 54, 22, 32), 7, 7);
        dc.DrawRoundedRectangle(dark, accent, new Rect(31, 59, 7, 22), 3, 3);
        dc.DrawRoundedRectangle(dark, accent, new Rect(122, 59, 7, 22), 3, 3);
        dc.DrawLine(line, new Point(122, 84), new Point(101, 94));
        dc.DrawEllipse(Accent, null, new Point(99, 94), 3, 3);
    }

    private void DrawReceiver(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        dc.DrawRoundedRectangle(shell, line, new Rect(31, 35, 98, 31), 9, 9);
        dc.DrawRoundedRectangle(dark, null, new Rect(14, 43, 24, 15), 3, 3);
        dc.DrawLine(line, new Point(21, 43), new Point(21, 58));
        dc.DrawEllipse(Accent, null, new Point(109, 50), 4, 4);
        dc.DrawLine(accent, new Point(57, 50), new Point(88, 50));
    }

    private void DrawGeneric(DrawingContext dc, Brush shell, Brush dark, Pen line, Pen accent)
    {
        dc.DrawRoundedRectangle(shell, line, new Rect(36, 19, 88, 62), 12, 12);
        dc.DrawRoundedRectangle(dark, null, new Rect(49, 31, 62, 7), 3, 3);
        dc.DrawRoundedRectangle(dark, null, new Rect(49, 45, 62, 7), 3, 3);
        dc.DrawRoundedRectangle(dark, null, new Rect(49, 59, 42, 7), 3, 3);
        dc.DrawEllipse(Accent, null, new Point(103, 63), 4, 4);
        dc.DrawLine(accent, new Point(80, 7), new Point(80, 14));
        dc.DrawLine(accent, new Point(80, 86), new Point(80, 94));
    }
}

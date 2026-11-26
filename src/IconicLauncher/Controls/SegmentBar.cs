using System.Windows;
using System.Windows.Media;

namespace IconicLauncher.Controls;

public sealed class SegmentBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(int), typeof(SegmentBar), new FrameworkPropertyMetadata(10, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SegmentBar), new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(SegmentBar), new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Segments { get => (int)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? Segments * 8 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 8 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var count = Math.Max(1, Segments);
        var gap = Math.Max(0, Gap);
        var width = ActualWidth;
        var height = ActualHeight;
        var seg = (width - gap * (count - 1)) / count;
        if (seg <= 0 || height <= 0) return;
        var ratio = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        var filled = ratio * count;
        for (var i = 0; i < count; i++)
        {
            var rect = new Rect(i * (seg + gap), 0, seg, height);
            dc.DrawRectangle(Track, null, rect);
            var part = Math.Clamp(filled - i, 0, 1);
            if (part > 0)
            {
                dc.DrawRectangle(Fill, null, new Rect(rect.X, 0, seg * part, height));
            }
        }
    }
}

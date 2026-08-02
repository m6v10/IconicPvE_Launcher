using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IconicLauncher.Controls;

public sealed class MarqueeText : Border
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText), new PropertyMetadata("", OnTextChanged));

    private readonly TextBlock _block;
    private readonly TranslateTransform _shift = new();

    public MarqueeText()
    {
        _block = new TextBlock
        {
            RenderTransform = _shift,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap
        };
        Child = _block;
        ClipToBounds = true;
        SizeChanged += (_, _) => Restart();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (MarqueeText)d;
        control._block.Text = e.NewValue as string ?? "";
        control.Restart();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        // Measure unconstrained so DesiredSize reflects the whole string. The control
        // itself still only claims the width it was offered.
        _block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = double.IsInfinity(constraint.Width) ? _block.DesiredSize.Width : constraint.Width;
        return new Size(width, _block.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange the text at its FULL width, not the control's. Border.Child would
        // arrange it at the control width, which clips the glyph run to that width -
        // the transform then only slides truncated text and the tail never appears.
        // ClipToBounds on this Border still hides the overflow.
        _block.Arrange(new Rect(0, 0, Math.Max(_block.DesiredSize.Width, finalSize.Width), finalSize.Height));
        return finalSize;
    }

    private void Restart()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ActualWidth <= 0) return;
            _block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var overflow = _block.DesiredSize.Width - ActualWidth;
            if (overflow <= 1) return;
            var travel = TimeSpan.FromSeconds(Math.Max(2.0, overflow / 25.0));
            var hold = TimeSpan.FromSeconds(1.2);
            var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(hold)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(hold + travel)));
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(hold + travel + hold)));
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(hold + travel + hold + travel)));
            _shift.BeginAnimation(TranslateTransform.XProperty, animation);
        }), DispatcherPriority.Loaded);
    }
}

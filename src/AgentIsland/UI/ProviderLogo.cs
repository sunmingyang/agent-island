using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AgentIsland.Core;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// One provider mark in the island bar: renders the brand geometry and
/// animates it per activity state — spin + breath while working, red pulse
/// for attention states, still otherwise. Mirrors LogoOverlay/
/// StatePreviewLogo on macOS.
public sealed class ProviderLogo : Grid
{
    private readonly System.Windows.Shapes.Path _path;
    private readonly RotateTransform _rotate = new();
    private readonly ScaleTransform _scale = new();
    private readonly DropShadowEffect _glow;
    private TriggerTool _tool = TriggerTool.Claude;
    private ActivityState _state = ActivityState.Idle;

    public const double MarkSize = 20;

    public ProviderLogo()
    {
        _glow = new DropShadowEffect
        {
            ShadowDepth = 0,
            BlurRadius = 0,
            Opacity = 0,
            Color = IslandColors.Claude,
        };
        _path = new System.Windows.Shapes.Path
        {
            Width = MarkSize,
            Height = MarkSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = _glow,
        };
        var transforms = new TransformGroup();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_rotate);
        _path.RenderTransform = transforms;
        Children.Add(_path);
        ApplyTool();
    }

    public TriggerTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            ApplyTool();
        }
    }

    private void ApplyTool()
    {
        _path.Data = Geometry.Parse("F1 " + (_tool == TriggerTool.Claude
            ? BrandGeometry.ClaudePath
            : BrandGeometry.OpenAiPath));
        ApplyTint();
    }

    private void ApplyTint()
    {
        var color = _state.IsAttentionState() ? IslandColors.AlertRed : IslandColors.For(_tool);
        _path.Fill = IslandColors.Brush(color);
        _glow.Color = color;
    }

    public void SetState(ActivityState state)
    {
        if (_state == state) return;
        _state = state;
        ApplyTint();
        StopAnimations();
        switch (state)
        {
            case ActivityState.Working:
                StartSpin();
                StartBreath(from: 0.97, to: 1.06, halfCycle: IslandAnimations.WorkingBreathDuration.TimeSpan / 2);
                StartGlow(from: 5, to: 2, halfCycle: IslandAnimations.WorkingBreathDuration.TimeSpan / 2);
                break;
            case ActivityState.Stalled:
            case ActivityState.RateLimited:
            case ActivityState.AuthRequired:
                StartBreath(from: 0.97, to: 1.14, halfCycle: IslandAnimations.AttentionPulseDuration.TimeSpan / 2);
                StartGlow(from: 10, to: 3, halfCycle: IslandAnimations.AttentionPulseDuration.TimeSpan / 2);
                break;
            case ActivityState.Idle:
            case ActivityState.NeedsYou:
            default:
                break;
        }
    }

    private void StartSpin()
    {
        var spin = new DoubleAnimation(0, 360, IslandAnimations.SpinDuration)
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    private void StartBreath(double from, double to, TimeSpan halfCycle)
    {
        var breath = new DoubleAnimation(from, to, new Duration(halfCycle))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
    }

    private void StartGlow(double from, double to, TimeSpan halfCycle)
    {
        _glow.Opacity = 0.55;
        var pulse = new DoubleAnimation(from, to, new Duration(halfCycle))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, pulse);
    }

    private void StopAnimations()
    {
        _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        _rotate.Angle = 0;
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _glow.BlurRadius = 0;
        _glow.Opacity = 0;
    }
}

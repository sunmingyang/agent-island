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
    // Mutable (unfrozen) fill so the state-change tint can crossfade — a
    // frozen IslandColors.Brush can't be animated.
    private readonly SolidColorBrush _fill = new(IslandColors.Claude);
    private TriggerTool _tool = TriggerTool.Claude;
    private ActivityState _state = ActivityState.Idle;
    private bool _tintSeeded;

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
            Fill = _fill,
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
            // A tool identity change is instant, not a state crossfade — the
            // ctor seeds with the default Claude, so reset the seed here or
            // a Codex logo would fade terracotta→blue on every launch.
            _tintSeeded = false;
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

    /// The attention tint is a brighter alarm red than the chart alertRed —
    /// Color(0.96, 0.34, 0.29) in LogoOverlay.
    private static readonly Color AlarmRed = Color.FromRgb(0xF5, 0x57, 0x4A);

    private void ApplyTint()
    {
        var color = _state.IsAttentionState() ? AlarmRed : IslandColors.For(_tool);
        // First paint is instant; later state changes crossfade over 0.3s,
        // the macOS LogoOverlay easeInOut(0.3) tint transition (e.g.
        // working blue → attention red).
        if (!_tintSeeded)
        {
            _tintSeeded = true;
            _fill.Color = color;
            _glow.Color = color;
            return;
        }
        var fade = new Duration(TimeSpan.FromSeconds(0.3));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        _fill.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(color, fade) { EasingFunction = ease });
        _glow.BeginAnimation(DropShadowEffect.ColorProperty,
            new ColorAnimation(color, fade) { EasingFunction = ease });
    }

    public void SetState(ActivityState state)
    {
        if (_state == state) return;
        var wasWorking = _state == ActivityState.Working;
        _state = state;
        ApplyTint();
        StopAnimations(unwindSpin: wasWorking);
        switch (state)
        {
            case ActivityState.Working:
                StartSpin();
                StartBreath(from: 1.0, to: 1.05, halfCycle: IslandAnimations.WorkingBreathDuration.TimeSpan);
                // macOS radii are gaussian sigmas; WPF BlurRadius is the
                // kernel extent (~3x), else the glow reads as an outline.
                StartGlow(radiusFrom: 6, radiusTo: 15, halfCycle: IslandAnimations.WorkingBreathDuration.TimeSpan);
                break;
            case ActivityState.Stalled:
            case ActivityState.RateLimited:
            case ActivityState.AuthRequired:
                StartBreath(from: 1.0, to: 1.16, halfCycle: IslandAnimations.AttentionPulseDuration.TimeSpan);
                StartGlow(radiusFrom: 12, radiusTo: 33, halfCycle: IslandAnimations.AttentionPulseDuration.TimeSpan);
                break;
            case ActivityState.Idle:
            case ActivityState.NeedsYou:
            default:
                break;
        }
    }

    private void StartSpin()
    {
        // The marks counter-rotate: Claude clockwise, Codex the other way.
        var to = _tool == TriggerTool.Claude ? 360d : -360d;
        var spin = new DoubleAnimation(0, to, IslandAnimations.SpinDuration)
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

    private void StartGlow(double radiusFrom, double radiusTo, TimeSpan halfCycle)
    {
        // Radius and strength breathe together — shadow(tint.opacity(pulse
        // ? 0.9 : 0.25), radius: ...) on macOS.
        var radius = new DoubleAnimation(radiusFrom, radiusTo, new Duration(halfCycle))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        var strength = new DoubleAnimation(0.25, 0.9, new Duration(halfCycle))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, radius);
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, strength);
    }

    private void StopAnimations(bool unwindSpin = false)
    {
        // Capture the live angle before detaching the animation so a
        // finished spin can settle back to upright instead of snapping.
        var angle = _rotate.Angle % 360;
        if (angle > 180) angle -= 360;
        if (angle < -180) angle += 360;
        _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        _rotate.Angle = 0;
        if (unwindSpin && Math.Abs(angle) > 0.5 && _state != ActivityState.Working)
        {
            var settle = new DoubleAnimation(angle, 0, new Duration(TimeSpan.FromSeconds(0.35)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            };
            _rotate.BeginAnimation(RotateTransform.AngleProperty, settle);
        }
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _glow.BlurRadius = 0;
        _glow.Opacity = 0;
    }
}

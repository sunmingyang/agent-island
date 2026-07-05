using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AgentIsland.Core;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// The island itself: a borderless, topmost, per-pixel-transparent window
/// pinned to the top-center of the screen. Fully transparent pixels pass
/// clicks through to whatever is behind, so only the black silhouette is
/// interactive — the WPF equivalent of the macOS hitTest override.
public partial class IslandWindow : Window
{
    private readonly IslandModel _model = IslandModel.Shared;
    private bool _hovering;

    public IslandWindow()
    {
        InitializeComponent();
        ClaudeLogo.Tool = TriggerTool.Claude;
        CodexLogo.Tool = TriggerTool.Codex;
        ClaudePill.Tool = TriggerTool.Claude;
        CodexPill.Tool = TriggerTool.Codex;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOnScreen();
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SystemParameters.WorkArea)
                or nameof(SystemParameters.PrimaryScreenWidth))
            {
                Dispatcher.BeginInvoke(PositionOnScreen);
            }
        };

        ApplySizeInstant();
        ExpandedPlaceholder.Text = "Usage · Cost · Overview · Triggers";

        ActivityMonitor.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateActivityVisuals);
        UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdatePills);
        UpdateActivityVisuals();
        UpdatePills();
    }

    private void PositionOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    // MARK: - State transitions

    private void OnSilhouetteMouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        if (_model.State == IslandState.Compact)
        {
            SetState(IslandState.Peek);
        }
    }

    private void OnSilhouetteMouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        // Pills fade first (~80ms), then the silhouette springs back.
        var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            if (!_hovering && _model.State != IslandState.Compact)
            {
                SetState(IslandState.Compact);
            }
        };
        if (_model.State == IslandState.Peek)
        {
            FadePills(visible: false, delayMs: 0, seconds: 0.08);
        }
        delay.Start();
    }

    private void OnSilhouetteClick(object sender, MouseButtonEventArgs e)
    {
        if (_model.State is IslandState.Peek or IslandState.Compact)
        {
            SetState(IslandState.Expanded);
        }
    }

    private void SetState(IslandState state)
    {
        var previous = _model.State;
        if (previous == state) return;
        _model.State = state;

        var open = state != IslandState.Compact
            && (previous == IslandState.Compact || state == IslandState.Expanded);
        AnimateSize(_model.Size, open);
        Silhouette.CornerRadius = new CornerRadius(0, 0, _model.CornerRadius, _model.CornerRadius);

        switch (state)
        {
            case IslandState.Peek:
                // Shape commits first, pills follow.
                FadePills(visible: true, delayMs: 60, seconds: 0.18);
                break;
            case IslandState.Expanded:
                ShowExpandedContent();
                // Pills travel with the growing shape, then cross-fade out
                // after the expanded content has settled.
                FadePills(visible: false, delayMs: 250, seconds: 0.18);
                break;
            case IslandState.Compact:
            default:
                FadePills(visible: false, delayMs: 0, seconds: 0.08);
                HideExpandedContent();
                break;
        }
    }

    private void AnimateSize(Size target, bool open)
    {
        var duration = open ? IslandAnimations.OpenMorphDuration : IslandAnimations.CloseMorphDuration;
        SpringEase ease = open ? IslandAnimations.OpenMorph() : IslandAnimations.CloseMorph();
        var width = new DoubleAnimation(target.Width, duration) { EasingFunction = ease };
        var height = new DoubleAnimation(target.Height, duration) { EasingFunction = ease };
        Silhouette.BeginAnimation(WidthProperty, width);
        Silhouette.BeginAnimation(HeightProperty, height);
    }

    private void ApplySizeInstant()
    {
        Silhouette.BeginAnimation(WidthProperty, null);
        Silhouette.BeginAnimation(HeightProperty, null);
        var size = _model.Size;
        Silhouette.Width = size.Width;
        Silhouette.Height = size.Height;
        Silhouette.CornerRadius = new CornerRadius(0, 0, _model.CornerRadius, _model.CornerRadius);
    }

    private void FadePills(bool visible, int delayMs, double seconds)
    {
        var fade = new DoubleAnimation(visible ? 1 : 0, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new QuadraticEase
            {
                EasingMode = visible ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };
        ClaudePill.BeginAnimation(OpacityProperty, fade);
        CodexPill.BeginAnimation(OpacityProperty, fade.Clone());
    }

    private void ShowExpandedContent()
    {
        ExpandedContent.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(1, IslandAnimations.StrongEaseOutDuration)
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            EasingFunction = IslandAnimations.StrongEaseOut(),
        };
        var slide = new DoubleAnimation(0, IslandAnimations.StrongEaseOutDuration)
        {
            BeginTime = TimeSpan.FromMilliseconds(180),
            EasingFunction = IslandAnimations.StrongEaseOut(),
        };
        ExpandedContent.BeginAnimation(OpacityProperty, fade);
        ContentSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
    }

    private void HideExpandedContent()
    {
        var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromSeconds(0.12)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            if (_model.State == IslandState.Compact)
            {
                ExpandedContent.Visibility = Visibility.Collapsed;
                ContentSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                ContentSlide.Y = -8;
            }
        };
        ExpandedContent.BeginAnimation(OpacityProperty, fade);
    }

    // MARK: - Live state visuals

    private void UpdateActivityVisuals()
    {
        var monitor = ActivityMonitor.Shared;
        ClaudeLogo.SetState(monitor.Claude);
        CodexLogo.SetState(monitor.Codex);
        UpdateHalo(monitor.Claude.IsAttentionState() || monitor.Codex.IsAttentionState());
    }

    private bool _haloAlerting;

    /// The red attention state bleeds through to the silhouette halo so it's
    /// visible even at compact.
    private void UpdateHalo(bool alerting)
    {
        if (alerting == _haloAlerting) return;
        _haloAlerting = alerting;
        if (alerting)
        {
            Halo.Color = IslandColors.AlertRed;
            Halo.Opacity = 0.55;
            var pulse = new DoubleAnimation(10, 18, new Duration(TimeSpan.FromSeconds(0.21)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, pulse);
        }
        else
        {
            Halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
            Halo.Color = Colors.Black;
            Halo.Opacity = 0.35;
            Halo.BlurRadius = 14;
        }
    }

    private void UpdatePills()
    {
        var store = UsageStore.Shared;
        ClaudePill.Update(store.Claude.FiveHour, store.Loading);
        CodexPill.Update(store.Codex.FiveHour, store.Loading);
    }
}

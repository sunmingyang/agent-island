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
    private System.Windows.Controls.StackPanel? _claudeTitle;
    private System.Windows.Controls.StackPanel? _codexTitle;
    private System.Windows.Controls.TextBlock? _claudeChip;
    private System.Windows.Controls.TextBlock? _codexChip;

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
        BuildExpandedChrome();

        ActivityMonitor.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateActivityVisuals);
        UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdatePills);
        UpdateActivityVisuals();
        UpdatePills();
    }

    /// Pages + footer inside the expanded area; provider titles + plan chips
    /// in the top strip (visible only when expanded, exactly like the macOS
    /// PanelHeader living beside the notch).
    private void BuildExpandedChrome()
    {
        var pages = new PagedContent();
        System.Windows.Controls.Grid.SetRow(pages, 0);
        ExpandedContent.Children.Add(pages);

        var footer = new PanelFooter();
        System.Windows.Controls.Grid.SetRow(footer, 1);
        ExpandedContent.Children.Add(footer);

        (_claudeTitle, _claudeChip) = MakeProviderTitle("Claude");
        _claudeTitle.HorizontalAlignment = HorizontalAlignment.Left;
        _claudeTitle.Margin = new Thickness(37, 0, 0, 0);
        TopStrip.Children.Add(_claudeTitle);

        (_codexTitle, _codexChip) = MakeProviderTitle("Codex");
        _codexTitle.HorizontalAlignment = HorizontalAlignment.Right;
        _codexTitle.Margin = new Thickness(0, 0, 37, 0);
        TopStrip.Children.Add(_codexTitle);

        UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdatePlanChips);
        UpdatePlanChips();

        // Overview needs the taller panel (contribution grid); the size
        // morphs live when paging while expanded.
        ScreenPref.Shared.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(ScreenPref.Screen)) return;
            Dispatcher.BeginInvoke(() =>
            {
                _model.ExpandedContentHeight = ScreenPref.Shared.Screen == IslandScreen.Overview
                    ? IslandModel.OverviewContentHeight
                    : IslandModel.UsageContentHeight;
                if (_model.State == IslandState.Expanded)
                {
                    AnimateSize(_model.Size, open: true);
                }
            });
        };
    }

    private static (System.Windows.Controls.StackPanel Panel, System.Windows.Controls.TextBlock Chip) MakeProviderTitle(string name)
    {
        var panel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = name,
            FontFamily = Charts.IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var chip = new System.Windows.Controls.TextBlock
        {
            FontFamily = Charts.IslandFonts.Ui,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.6)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chipHost = new System.Windows.Controls.Border
        {
            Child = chip,
            CornerRadius = new CornerRadius(3),
            Background = IslandColors.Brush(IslandColors.White(0.06)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.08)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(chipHost);
        return (panel, chip);
    }

    private void UpdatePlanChips()
    {
        var store = UsageStore.Shared;
        UpdateChip(_claudeChip, store.Claude.Plan);
        UpdateChip(_codexChip, store.Codex.Plan);
    }

    private static void UpdateChip(System.Windows.Controls.TextBlock? chip, string? plan)
    {
        if (chip is null) return;
        var host = (System.Windows.Controls.Border)chip.Parent;
        if (string.IsNullOrEmpty(plan))
        {
            host.Visibility = Visibility.Collapsed;
        }
        else
        {
            host.Visibility = Visibility.Visible;
            chip.Text = plan.ToUpperInvariant();
        }
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
            // Take focus so the wheel and arrow keys page the carousel even
            // when Windows' hover-scroll setting is off.
            Activate();
            Focus();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_model.State != IslandState.Expanded) return;
        switch (e.Key)
        {
            case Key.Right:
                ScreenPref.Shared.ShowNext(1);
                e.Handled = true;
                break;
            case Key.Left:
                ScreenPref.Shared.ShowNext(-1);
                e.Handled = true;
                break;
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
        ContentSlide.BeginAnimation(TranslateTransform.YProperty, slide);
        _claudeTitle?.BeginAnimation(OpacityProperty, fade.Clone());
        _codexTitle?.BeginAnimation(OpacityProperty, fade.Clone());
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
                ContentSlide.BeginAnimation(TranslateTransform.YProperty, null);
                ContentSlide.Y = -8;
            }
        };
        ExpandedContent.BeginAnimation(OpacityProperty, fade);
        _claudeTitle?.BeginAnimation(OpacityProperty, fade.Clone());
        _codexTitle?.BeginAnimation(OpacityProperty, fade.Clone());
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

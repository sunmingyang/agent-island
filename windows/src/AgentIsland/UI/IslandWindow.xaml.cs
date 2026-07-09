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
/// docked to an edge of the chosen screen (top-center by default). Fully
/// transparent pixels pass clicks through to whatever is behind, so only the
/// black silhouette is interactive — the WPF equivalent of the macOS hitTest
/// override.
public partial class IslandWindow : Window
{
    private readonly IslandModel _model = IslandModel.Shared;
    // Unsubscribe actions for the singleton-store handlers, run on Closed —
    // the island is discarded and recreated on a language switch, and without
    // this the dead window stays pinned by the stores and keeps handling
    // events (placement/usage/alert) forever.
    private readonly List<Action> _teardown = new();
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
        ApplyEdgeLayout();
        PositionOnScreen();
        // Floating mode: drag the silhouette to move (and remember) the
        // window; a non-drag press still expands.
        Silhouette.MouseLeftButtonDown += OnSilhouetteMouseDown;

        // The sweep ring tracks the silhouette through every spring morph
        // (+4 so half its stroke rides outside the edge).
        Silhouette.SizeChanged += (_, args) =>
        {
            Sweep.Width = args.NewSize.Width + 4;
            Sweep.Height = args.NewSize.Height + 4;
        };
        System.ComponentModel.PropertyChangedEventHandler onLowPower =
            (_, _) => Dispatcher.BeginInvoke(UpdateHalo);
        Model.LowPowerModeStore.Shared.PropertyChanged += onLowPower;
        _teardown.Add(() => Model.LowPowerModeStore.Shared.PropertyChanged -= onLowPower);

        System.ComponentModel.PropertyChangedEventHandler onSysParams = (_, args) =>
        {
            if (args.PropertyName is nameof(SystemParameters.WorkArea)
                or nameof(SystemParameters.PrimaryScreenWidth))
            {
                Dispatcher.BeginInvoke(PositionOnScreen);
            }
        };
        SystemParameters.StaticPropertyChanged += onSysParams;
        _teardown.Add(() => SystemParameters.StaticPropertyChanged -= onSysParams);

        // WorkArea/PrimaryScreenWidth only cover the primary display;
        // plug/unplug or resolution changes on a pinned secondary arrive via
        // SystemEvents (the didChangeScreenParameters analog).
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _teardown.Add(() =>
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged);

        System.ComponentModel.PropertyChangedEventHandler onTargetDisplay =
            (_, _) => Dispatcher.BeginInvoke(PositionOnScreen);
        Model.IslandTargetDisplayStore.Shared.PropertyChanged += onTargetDisplay;
        _teardown.Add(() => Model.IslandTargetDisplayStore.Shared.PropertyChanged -= onTargetDisplay);

        System.ComponentModel.PropertyChangedEventHandler onPlacement = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            ApplyEdgeLayout();
            PositionOnScreen();
            // Tray mode is icon-first: the island hides and only pops up on a
            // tray click. Switching away brings it back.
            if (Model.IslandPositionStore.Shared.Placement == Model.IslandPlacement.Tray) Hide();
            else Show();
        });
        Model.IslandPositionStore.Shared.PropertyChanged += onPlacement;
        _teardown.Add(() => Model.IslandPositionStore.Shared.PropertyChanged -= onPlacement);
        Closed += (_, _) => { foreach (var teardown in _teardown) teardown(); };
        // In tray mode, clicking elsewhere dismisses the popped-up island so
        // it behaves like a tray flyout.
        Deactivated += (_, _) =>
        {
            if (Model.IslandPositionStore.Shared.Placement == Model.IslandPlacement.Tray) Hide();
        };

        ApplySizeInstant();
        BuildExpandedChrome();

        System.ComponentModel.PropertyChangedEventHandler onActivity =
            (_, _) => Dispatcher.BeginInvoke(UpdateActivityVisuals);
        ActivityMonitor.Shared.PropertyChanged += onActivity;
        _teardown.Add(() => ActivityMonitor.Shared.PropertyChanged -= onActivity);

        System.ComponentModel.PropertyChangedEventHandler onUsage = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            UpdatePills();
            // Loading is a glow event: it wakes the sweep in Low Power mode.
            UpdateHalo();
        });
        UsageStore.Shared.PropertyChanged += onUsage;
        _teardown.Add(() => UsageStore.Shared.PropertyChanged -= onUsage);

        System.ComponentModel.PropertyChangedEventHandler onAlert = (_, args) => Dispatcher.BeginInvoke(() =>
        {
            if (args.PropertyName == nameof(Model.AlertEngine.Pulse)) HandleAlertPulse();
            UpdateHalo();
            UpdatePills();
        });
        Model.AlertEngine.Shared.PropertyChanged += onAlert;
        _teardown.Add(() => Model.AlertEngine.Shared.PropertyChanged -= onAlert);

        // Bar-width change (Settings → Display) resizes the silhouette live
        // when it's not expanded; provider visibility hides a side entirely.
        // _model is the IslandModel singleton, so this too must be torn down.
        System.ComponentModel.PropertyChangedEventHandler onModel = (_, args) =>
        {
            if (args.PropertyName == nameof(IslandModel.Size))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_model.State != IslandState.Expanded) ApplySizeInstant();
                });
            }
        };
        _model.PropertyChanged += onModel;
        _teardown.Add(() => _model.PropertyChanged -= onModel);

        System.ComponentModel.PropertyChangedEventHandler onVisibility =
            (_, _) => Dispatcher.BeginInvoke(ApplyProviderVisibility);
        Model.ProviderVisibilityStore.Shared.PropertyChanged += onVisibility;
        _teardown.Add(() => Model.ProviderVisibilityStore.Shared.PropertyChanged -= onVisibility);

        System.ComponentModel.PropertyChangedEventHandler onAlwaysShow = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _model.NotifyAlwaysShowUsageChanged();
            if (_model.State != IslandState.Expanded) ApplySizeInstant();
            UpdatePills();
        });
        AlwaysShowUsageStore.Shared.PropertyChanged += onAlwaysShow;
        _teardown.Add(() => AlwaysShowUsageStore.Shared.PropertyChanged -= onAlwaysShow);

        ApplyProviderVisibility();
        UpdateActivityVisuals();
        UpdatePills();

        // Scripted layout diagnosis: dump geometry once a second.
        if (Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_LAYOUTLOG") == "1")
        {
            var log = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            log.Tick += (_, _) =>
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(Core.IslandPaths.AppSupportDir, "layout.log"),
                        $"{DateTime.Now:HH:mm:ss.f} state={_model.State} winLeft={Left:F0} winW={Width:F0} " +
                        $"silW={Silhouette.Width:F0} silActualW={Silhouette.ActualWidth:F0} " +
                        $"leftSlot={LeftPillColumn.Width} rightSlot={RightPillColumn.Width} " +
                        $"modelSize={_model.Size.Width:F0}x{_model.Size.Height:F0}\n");
                }
                catch
                {
                }
            };
            log.Start();
        }
    }

    /// Hidden providers drop their logo, peek pill, and expanded title —
    /// the balanced peek width is preserved by the model's fixed slots.
    private void ApplyProviderVisibility()
    {
        var visibility = Model.ProviderVisibilityStore.Shared;
        // The logo's fixed grid column reserves its slot either way, so we
        // fade opacity (the macOS openMorph spring) rather than hard-toggle
        // Visibility — toggling a provider springs the mark in/out.
        FadeLogo(ClaudeLogo, visibility.ClaudeVisible);
        FadeLogo(CodexLogo, visibility.CodexVisible);
        if (_claudeTitle is not null)
            _claudeTitle.Visibility = visibility.ClaudeVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_codexTitle is not null)
            _codexTitle.Visibility = visibility.CodexVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdatePills();
    }

    private static void FadeLogo(UIElement logo, bool visible)
    {
        var fade = new DoubleAnimation(visible ? 1 : 0, IslandAnimations.OpenMorphDuration)
        {
            EasingFunction = IslandAnimations.OpenMorph(),
        };
        logo.BeginAnimation(OpacityProperty, fade);
        logo.IsHitTestVisible = visible;
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

        // Titles live in the center column, hugging the logo tabs on each
        // side — the macOS PanelHeader arrangement.
        (_claudeTitle, _claudeChip) = MakeProviderTitle("Claude");
        _claudeTitle.HorizontalAlignment = HorizontalAlignment.Left;
        _claudeTitle.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Controls.Grid.SetColumn(_claudeTitle, 2);
        TopStrip.Children.Add(_claudeTitle);

        (_codexTitle, _codexChip) = MakeProviderTitle("Codex");
        _codexTitle.HorizontalAlignment = HorizontalAlignment.Right;
        _codexTitle.Margin = new Thickness(0, 0, 8, 0);
        System.Windows.Controls.Grid.SetColumn(_codexTitle, 2);
        TopStrip.Children.Add(_codexTitle);

        System.ComponentModel.PropertyChangedEventHandler onPlanChips =
            (_, _) => Dispatcher.BeginInvoke(UpdatePlanChips);
        UsageStore.Shared.PropertyChanged += onPlanChips;
        _teardown.Add(() => UsageStore.Shared.PropertyChanged -= onPlanChips);
        UpdatePlanChips();

        // Overview needs the taller panel (contribution grid); the size
        // morphs live when paging while expanded — and the persisted page
        // must seed the height at startup, or reopening on Overview squashes
        // the grid.
        ApplyPanelHeightForScreen();
        System.ComponentModel.PropertyChangedEventHandler onScreen = (_, args) =>
        {
            if (args.PropertyName != nameof(ScreenPref.Screen)) return;
            Dispatcher.BeginInvoke(() =>
            {
                ApplyPanelHeightForScreen();
                if (_model.State == IslandState.Expanded)
                {
                    AnimateSize(_model.Size, open: true);
                }
            });
        };
        ScreenPref.Shared.PropertyChanged += onScreen;
        _teardown.Add(() => ScreenPref.Shared.PropertyChanged -= onScreen);
    }

    private void ApplyPanelHeightForScreen() =>
        _model.ExpandedContentHeight = ScreenPref.Shared.Screen == IslandScreen.Overview
            ? IslandModel.OverviewContentHeight
            : IslandModel.UsageContentHeight;

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
            FontFamily = Charts.IslandFonts.Mono,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.78)),
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

    /// Transparent canvas kept on an anchored side so the halo glow (blur
    /// radius up to 22 while pulsing) never clips at the window boundary.
    private const double HaloBleed = 28;

    /// Which screen edge the island's flat side faces. Floating faces none.
    private enum DockEdge { Top, Bottom, None }

    private DockEdge CurrentEdge() => Model.IslandPositionStore.Shared.Placement switch
    {
        Model.IslandPlacement.BottomBar or Model.IslandPlacement.Tray => DockEdge.Bottom,
        Model.IslandPlacement.Floating => DockEdge.None,
        _ => DockEdge.Top,
    };

    private void PositionOnScreen()
    {
        var area = WorkAreaDip(Model.IslandTargetDisplayStore.Shared.Resolve());
        var store = Model.IslandPositionStore.Shared;
        switch (store.Placement)
        {
            case Model.IslandPlacement.Floating:
                var pt = store.FloatingPoint;
                if (pt is { } p)
                {
                    // Clamp the VISIBLE silhouette (not the oversized
                    // transparent canvas) so the island can be parked right
                    // at a screen edge; the canvas simply overhangs off-screen.
                    var silW = Silhouette.ActualWidth > 0 ? Silhouette.ActualWidth : 280;
                    var silH = Silhouette.ActualHeight > 0 ? Silhouette.ActualHeight : IslandModel.SilhouetteHeight;
                    var insetX = (Width - silW) / 2; // silhouette is centered in the canvas
                    var minLeft = area.Left - insetX;
                    var maxLeft = area.Right - silW - insetX;
                    var maxTop = area.Bottom - silH;
                    Left = Math.Clamp(p.X, minLeft, Math.Max(minLeft, maxLeft));
                    Top = Math.Clamp(p.Y, area.Top, Math.Max(area.Top, maxTop));
                }
                else
                {
                    Left = area.Left + (area.Width - Width) / 2;
                    Top = area.Top + 72;
                }
                break;
            case Model.IslandPlacement.Tray:
                // Sit OVER the taskbar (full screen bounds, not the work
                // area) at the bottom-right, just left of the notification
                // area — Win11 doesn't allow embedding into the taskbar, so
                // a topmost overlay is the closest "in the bottom bar" spot.
                var full = ScreenBoundsDip(Model.IslandTargetDisplayStore.Shared.Resolve());
                Left = full.Right - Width - TrayNotificationInset;
                Top = full.Bottom - Height;
                break;
            case Model.IslandPlacement.BottomBar:
                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Bottom - Height;
                break;
            case Model.IslandPlacement.TopBar:
            default:
                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Top;
                break;
        }
    }

    /// Room reserved on the right for the Win11 notification area (clock,
    /// tray icons) so the Tray overlay doesn't cover them.
    private const double TrayNotificationInset = 200;

    /// Full monitor bounds (taskbar INCLUDED) in DIP — used by Tray mode to
    /// overlay the bottom bar.
    private Rect ScreenBoundsDip(System.Windows.Forms.Screen screen)
    {
        var b = screen.Bounds;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var device = target.TransformFromDevice;
            return new Rect(
                device.Transform(new Point(b.Left, b.Top)),
                device.Transform(new Point(b.Right, b.Bottom)));
        }
        return new Rect(b.Left, b.Top, b.Width, b.Height);
    }

    /// The chosen monitor's work area (taskbar excluded, so a top-docked
    /// taskbar pushes the island below it) in WPF units. WinForms screens
    /// report physical pixels; TransformFromDevice maps them into this
    /// window's DIP space.
    private Rect WorkAreaDip(System.Windows.Forms.Screen screen)
    {
        var area = screen.WorkingArea;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            var device = target.TransformFromDevice;
            return new Rect(
                device.Transform(new Point(area.Left, area.Top)),
                device.Transform(new Point(area.Right, area.Bottom)));
        }
        return SystemParameters.WorkArea;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(PositionOnScreen);

    /// Rounded corners face away from the docked edge; the flat side sits
    /// flush against it. A floating island rounds all four corners.
    private CornerRadius ShapeRadius(double radius) => CurrentEdge() switch
    {
        DockEdge.Bottom => new CornerRadius(radius, radius, 0, 0),
        DockEdge.None => new CornerRadius(radius),
        _ => new CornerRadius(0, 0, radius, radius),
    };

    /// Hidden panel content parks 8px toward the bar strip so the expand
    /// reveal always slides away from the docked edge.
    private double PanelRestOffset() => CurrentEdge() == DockEdge.Bottom ? 8 : -8;

    /// Re-anchors the silhouette for the current placement: a bottom edge
    /// (BottomBar/Tray) mirrors the layout so the strip hugs the taskbar and
    /// the panel grows upward; Tray pins bottom-right; Floating rounds all
    /// corners and sits where the user dragged it.
    private void ApplyEdgeLayout()
    {
        var store = Model.IslandPositionStore.Shared;
        var edge = CurrentEdge();
        var bottom = edge == DockEdge.Bottom;

        FirstRow.Height = bottom
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(IslandModel.SilhouetteHeight);
        SecondRow.Height = bottom
            ? new GridLength(IslandModel.SilhouetteHeight)
            : new GridLength(1, GridUnitType.Star);
        System.Windows.Controls.Grid.SetRow(TopStrip, bottom ? 1 : 0);
        System.Windows.Controls.Grid.SetRow(ExpandedContent, bottom ? 0 : 1);
        System.Windows.Controls.Grid.SetRow(SettingsGear, bottom ? 0 : 1);
        SettingsGear.VerticalAlignment = bottom ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        SettingsGear.Margin = bottom ? new Thickness(12, 11, 0, 0) : new Thickness(12, 0, 0, 11);

        // Where the silhouette sits inside the oversized transparent canvas.
        // Bars center; Tray hugs the right so it lands by the notification area.
        var horizontal = store.Placement == Model.IslandPlacement.Tray
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        var vertical = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Silhouette.HorizontalAlignment = horizontal;
        Silhouette.VerticalAlignment = vertical;
        Silhouette.Margin = new Thickness(
            horizontal == HorizontalAlignment.Left ? HaloBleed : 0, 0,
            horizontal == HorizontalAlignment.Right ? HaloBleed : 0, 0);
        Sweep.HorizontalAlignment = horizontal;
        Sweep.VerticalAlignment = vertical;
        // The ring is 4px larger than the silhouette: -2 keeps it concentric
        // on anchored sides and rides half its stroke outside the flush edge.
        Sweep.Margin = new Thickness(
            horizontal == HorizontalAlignment.Left ? HaloBleed - 2 : 0,
            bottom ? 0 : -2,
            horizontal == HorizontalAlignment.Right ? HaloBleed - 2 : 0,
            bottom ? -2 : 0);

        Silhouette.CornerRadius = ShapeRadius(_model.CornerRadius);
        Sweep.CornerRadius = ShapeRadius(_model.CornerRadius + 2);
        if (RootHost.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
        {
            shadow.Direction = bottom ? 90 : 270;
        }
        if (_model.State != IslandState.Expanded)
        {
            ContentSlide.BeginAnimation(TranslateTransform.YProperty, null);
            ContentSlide.Y = PanelRestOffset();
        }
    }

    // MARK: - State transitions

    private void OnSilhouetteMouseEnter(object sender, MouseEventArgs e)
    {
        _hovering = true;
        UpdateHalo();
        if (_model.State == IslandState.Compact)
        {
            SetState(IslandState.Peek);
        }
    }

    private void OnSilhouetteMouseLeave(object sender, MouseEventArgs e)
    {
        _hovering = false;
        UpdateHalo();
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
            // With "always show usage" the percentages stay painted on the
            // compact bar, so nothing fades on the way out.
            FadePills(visible: AlwaysShowUsageStore.Shared.Enabled, delayMs: 0, seconds: 0.08);
        }
        delay.Start();
    }

    /// In Floating mode a left-press either drags the window (and persists
    /// the new spot) or, if it barely moved, counts as the click that
    /// expands. DragMove swallows the mouse-up, so we drive expand here and
    /// let OnSilhouetteClick bail for floating.
    private void OnSilhouetteMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Model.IslandPositionStore.Shared.Placement != Model.IslandPlacement.Floating) return;
        if (_model.State != IslandState.Compact && _model.State != IslandState.Peek) return;
        var startLeft = Left;
        var startTop = Top;
        try { DragMove(); } catch { }
        var moved = Math.Abs(Left - startLeft) > 3 || Math.Abs(Top - startTop) > 3;
        if (moved)
        {
            Model.IslandPositionStore.Shared.SetFloatingPoint(Left, Top);
            // Settle into the clamped resting spot now, so it matches where a
            // later reposition (display change / relaunch) would place it.
            PositionOnScreen();
        }
        else if (_model.State is IslandState.Peek or IslandState.Compact)
        {
            SetState(IslandState.Expanded);
            Activate();
            Focus();
        }
        e.Handled = true;
    }

    /// Bring the island up and open it — the tray-icon launcher. Re-positions
    /// first so a tray/bottom placement lands by the tray where the click was.
    public void PopUp()
    {
        Show();
        ApplyEdgeLayout();
        PositionOnScreen();
        if (_model.State != IslandState.Expanded) SetState(IslandState.Expanded);
        Activate();
        Focus();
    }

    private void OnSilhouetteClick(object sender, MouseButtonEventArgs e)
    {
        // Floating handles expand in the mouse-down path (DragMove consumes
        // the up), so ignore the click there to avoid a double expand.
        if (Model.IslandPositionStore.Shared.Placement == Model.IslandPlacement.Floating) return;
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
        AnimatePillSlots(open);
        Silhouette.CornerRadius = ShapeRadius(_model.CornerRadius);
        Sweep.CornerRadius = ShapeRadius(_model.CornerRadius + 2);

        // Expanded panel gains the hairline stroke and grounding shadow of
        // the macOS GlowLayer; both drop on collapse.
        var expanded = state == IslandState.Expanded;
        Silhouette.BorderBrush = expanded ? IslandColors.Brush(IslandColors.White(0.12)) : null;
        Silhouette.BorderThickness = new Thickness(expanded ? 0.5 : 0);
        RootHost.Effect = expanded
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 0.5,
                BlurRadius = 20,
                ShadowDepth = 10,
                // Grounding shadow falls away from the docked edge.
                Direction = CurrentEdge() == DockEdge.Bottom ? 90 : 270,
            }
            : null;

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
                FadePills(visible: AlwaysShowUsageStore.Shared.Enabled, delayMs: 0, seconds: 0.08);
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
        Silhouette.CornerRadius = ShapeRadius(_model.CornerRadius);
        Sweep.CornerRadius = ShapeRadius(_model.CornerRadius + 2);
        LeftPillColumn.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, null);
        RightPillColumn.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, null);
        var slot = new GridLength(PillSlotTarget());
        LeftPillColumn.Width = slot;
        RightPillColumn.Width = slot;
    }

    /// Pill slots exist in peek — and in compact when "always show usage"
    /// keeps the percentages painted; they collapse in expanded so the logo
    /// tabs glide out to the panel corners.
    private double PillSlotTarget() => _model.State switch
    {
        IslandState.Peek => IslandModel.PillSlotWidth,
        IslandState.Compact when AlwaysShowUsageStore.Shared.Enabled => IslandModel.PillSlotWidth,
        _ => 0,
    };

    private void AnimatePillSlots(bool open)
    {
        var duration = open ? IslandAnimations.OpenMorphDuration : IslandAnimations.CloseMorphDuration;
        var animation = new GridLengthAnimation
        {
            From = LeftPillColumn.Width,
            To = new GridLength(PillSlotTarget()),
            Duration = duration,
            EasingFunction = open ? IslandAnimations.OpenMorph() : IslandAnimations.CloseMorph(),
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            var final = new GridLength(PillSlotTarget());
            LeftPillColumn.Width = final;
            RightPillColumn.Width = final;
        };
        LeftPillColumn.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, animation);
        RightPillColumn.BeginAnimation(System.Windows.Controls.ColumnDefinition.WidthProperty, animation.Clone());
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
        SettingsGear.Visibility = Visibility.Visible;
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
        SettingsGear.BeginAnimation(OpacityProperty, fade.Clone());
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
                SettingsGear.Visibility = Visibility.Collapsed;
                ContentSlide.BeginAnimation(TranslateTransform.YProperty, null);
                ContentSlide.Y = PanelRestOffset();
            }
        };
        ExpandedContent.BeginAnimation(OpacityProperty, fade);
        _claudeTitle?.BeginAnimation(OpacityProperty, fade.Clone());
        _codexTitle?.BeginAnimation(OpacityProperty, fade.Clone());
        SettingsGear.BeginAnimation(OpacityProperty, fade.Clone());
    }

    private void OnSettingsGearClick(object sender, MouseButtonEventArgs e)
    {
        SettingsWindow.Open();
        e.Handled = true;
    }

    private void OnSettingsGearEnter(object sender, MouseEventArgs e) =>
        SettingsGear.Foreground = IslandColors.Brush(IslandColors.White(0.9));

    private void OnSettingsGearLeave(object sender, MouseEventArgs e) =>
        SettingsGear.Foreground = IslandColors.Brush(IslandColors.White(0.45));

    // MARK: - Live state visuals

    private void UpdateActivityVisuals()
    {
        var monitor = ActivityMonitor.Shared;
        ClaudeLogo.SetState(monitor.Claude);
        CodexLogo.SetState(monitor.Codex);
        UpdateHalo();
    }

    private enum HaloMode
    {
        Rest,
        WarningTint,
        CriticalTint,
        AttentionPulse,
    }

    private HaloMode _haloMode = HaloMode.Rest;
    private readonly RotateTransform _sweepRotate = new() { CenterX = 0.5, CenterY = 0.5 };
    private Color _sweepTint;
    private bool _sweepActive;
    private bool _sweepSpinning;

    /// Attention states pulse the halo red (opacity and radius breathe
    /// together, macOS GlowLayer numbers); threshold alerts hold a sustained
    /// amber/red tint; at rest the island keeps a soft cobalt aura.
    private void UpdateHalo()
    {
        var monitor = ActivityMonitor.Shared;
        var attention = monitor.Claude.IsAttentionState() || monitor.Codex.IsAttentionState();
        var severity = Model.AlertEngine.Shared.Severity;
        var mode = attention
            ? HaloMode.AttentionPulse
            : severity switch
            {
                Model.AlertSeverity.Critical => HaloMode.CriticalTint,
                Model.AlertSeverity.Warning => HaloMode.WarningTint,
                _ => HaloMode.Rest,
            };
        if (mode != _haloMode)
        {
            _haloMode = mode;
            Halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
            Halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
            switch (mode)
            {
                case HaloMode.AttentionPulse:
                    Halo.Color = IslandColors.AlertRed;
                    var half = IslandAnimations.AttentionPulseDuration.TimeSpan;
                    var radius = new DoubleAnimation(14, 22, new Duration(half))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    var strength = new DoubleAnimation(0.35, 0.85, new Duration(half))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, radius);
                    Halo.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, strength);
                    break;
                case HaloMode.CriticalTint:
                    Halo.Color = IslandColors.AlertRed;
                    Halo.Opacity = 0.35;
                    Halo.BlurRadius = 14;
                    break;
                case HaloMode.WarningTint:
                    Halo.Color = IslandColors.AlertAmber;
                    Halo.Opacity = 0.35;
                    Halo.BlurRadius = 14;
                    break;
                case HaloMode.Rest:
                default:
                    Halo.Color = IslandColors.Cobalt;
                    Halo.Opacity = 0.35;
                    Halo.BlurRadius = 14;
                    break;
            }
        }
        // Low Power drops the resting aura until something glows for a reason.
        if (_haloMode == HaloMode.Rest)
        {
            Halo.Opacity = Model.LowPowerModeStore.Shared.Enabled && !GlowEventActive() ? 0 : 0.35;
        }
        UpdateSweep();
    }

    private bool GlowEventActive() =>
        _hovering
        || UsageStore.Shared.Loading
        || Model.AlertEngine.Shared.Severity != Model.AlertSeverity.None;

    /// The rotating comet ring hugging the island edge — always alive unless
    /// Low Power idles it between glow events. Tint follows the halo.
    private void UpdateSweep()
    {
        var monitor = ActivityMonitor.Shared;
        var attention = monitor.Claude.IsAttentionState() || monitor.Codex.IsAttentionState();
        var tint = attention
            ? IslandColors.AlertRed
            : Model.AlertEngine.Shared.Severity switch
            {
                Model.AlertSeverity.Critical => IslandColors.AlertRed,
                Model.AlertSeverity.Warning => IslandColors.AlertAmber,
                _ => IslandColors.Cobalt,
            };
        var active = !Model.LowPowerModeStore.Shared.Enabled || GlowEventActive();
        if (active == _sweepActive && tint == _sweepTint) return;
        _sweepActive = active;
        _sweepTint = tint;
        if (!active)
        {
            Sweep.Visibility = Visibility.Collapsed;
            _sweepRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _sweepSpinning = false;
            return;
        }
        Sweep.Visibility = Visibility.Visible;
        // The brush swaps with the tint, but the shared transform keeps the
        // rotation phase, so recolors never visibly restart the sweep.
        Sweep.BorderBrush = ConicSweepBrush.Make(tint, _sweepRotate);
        if (!_sweepSpinning)
        {
            _sweepSpinning = true;
            // 100 degrees per second, same as the macOS TimelineView sweep.
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3.6)))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _sweepRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
    }

    private void UpdatePills()
    {
        var store = UsageStore.Shared;
        var engine = Model.AlertEngine.Shared;
        var visibility = Model.ProviderVisibilityStore.Shared;
        ClaudePill.Update(store.Claude.FiveHour, store.Loading, engine.SeverityFor(TriggerTool.Claude));
        CodexPill.Update(store.Codex.FiveHour, store.Loading, engine.SeverityFor(TriggerTool.Codex));

        // In compact, the pills normally hide. "Always show usage" keeps the
        // visible providers' 5h percent painted on the bare silhouette. A
        // finished FadePills animation holds the opacity, so detach it
        // before assigning or the value silently never lands.
        var alwaysShow = AlwaysShowUsageStore.Shared.Enabled && _model.State == IslandState.Compact;
        if (_model.State == IslandState.Peek || alwaysShow)
        {
            ClaudePill.BeginAnimation(OpacityProperty, null);
            CodexPill.BeginAnimation(OpacityProperty, null);
            ClaudePill.Opacity = visibility.ClaudeVisible ? 1 : 0;
            CodexPill.Opacity = visibility.CodexVisible ? 1 : 0;
        }
        else if (_model.State == IslandState.Compact)
        {
            ClaudePill.BeginAnimation(OpacityProperty, null);
            CodexPill.BeginAnimation(OpacityProperty, null);
            ClaudePill.Opacity = 0;
            CodexPill.Opacity = 0;
        }
    }

    /// First threshold crossing inside a reset window auto-peeks the pills
    /// for ~4s — the ambient nudge from the macOS design.
    private void HandleAlertPulse()
    {
        if (Model.AlertEngine.Shared.Pulse is null) return;
        Model.AlertEngine.Shared.ClearPulse();
        if (_model.State != IslandState.Compact) return;
        SetState(IslandState.Peek);
        var collapse = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        collapse.Tick += (_, _) =>
        {
            collapse.Stop();
            if (!_hovering && _model.State == IslandState.Peek)
            {
                SetState(IslandState.Compact);
            }
        };
        collapse.Start();
    }
}

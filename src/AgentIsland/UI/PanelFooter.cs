using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using AgentIsland.Localization;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// Fixed footer under the swipeable pages: style chip on the left, page
/// dots centered, live sync status on the right.
public sealed class PanelFooter : Grid
{
    private readonly TextBlock _chip;
    private readonly StackPanel _dots;
    private readonly LiveDot _liveDot = new();
    private readonly TextBlock _syncLabel;
    private readonly DispatcherTimer _agoTimer;

    public PanelFooter()
    {
        Height = 40;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var hairline = new Border
        {
            Height = 1,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(IslandColors.White(0.06), 0.5),
                    new GradientStop(Colors.Transparent, 1),
                },
                new Point(0, 0),
                new Point(1, 0)),
            Margin = new Thickness(22, 0, 22, 0),
        };
        SetRow(hairline, 0);
        Children.Add(hairline);

        var row = new Grid { Margin = new Thickness(22, 6, 22, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetRow(row, 1);
        Children.Add(row);

        _chip = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(IslandColors.White(0.35)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            // Clear the settings gear that sits in the panel's corner.
            Margin = new Thickness(18, 0, 0, 0),
        };
        SetColumn(_chip, 0);
        row.Children.Add(_chip);

        _dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetColumn(_dots, 1);
        row.Children.Add(_dots);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _syncLabel = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _liveDot.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(_liveDot);
        right.Children.Add(_syncLabel);
        SetColumn(right, 2);
        row.Children.Add(right);

        ScreenPref.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        StylePreferenceStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);

        // Keep the "2m ago" caption honest while the panel sits open.
        _agoTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _agoTimer.Tick += (_, _) => Update();
        _agoTimer.Start();
        Update();
    }

    private void Update()
    {
        // Usage carries no chip (the gear owns that corner, matching macOS);
        // cost shows the cost style, other pages the chart style.
        var pref = ScreenPref.Shared;
        _chip.Text = pref.Screen switch
        {
            IslandScreen.Usage => "",
            IslandScreen.Cost => CostStylePreferenceStore.Shared.ChipLabel,
            _ => StylePreferenceStore.Shared.Style.ToString().ToUpperInvariant(),
        };

        _dots.Children.Clear();
        foreach (var screen in pref.VisibleScreens)
        {
            var isActive = screen == pref.Screen;
            var dot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Margin = new Thickness(2.5, 0, 2.5, 0),
                Fill = IslandColors.Brush(IslandColors.White(isActive ? 0.78 : 0.22)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var target = screen;
            dot.MouseLeftButtonUp += (_, args) =>
            {
                pref.HasSwiped = true;
                pref.Screen = target;
                args.Handled = true;
            };
            _dots.Children.Add(dot);
        }

        var store = UsageStore.Shared;
        if (store.Loading)
        {
            _syncLabel.Text = L10n.Tr("Syncing…");
        }
        else if (store.RefreshWarning is { } warning)
        {
            _syncLabel.Text = warning;
        }
        else if (store.LastUpdated is { } updated)
        {
            _syncLabel.Text = L10n.Tr("Synced") + " " +
                Core.Formatting.RelativeAgo(DateTimeOffset.Now - updated, L10n.IsChinese);
        }
        else
        {
            _syncLabel.Text = "";
        }
        _liveDot.SetActive(!store.Loading && store.RefreshWarning is null && store.LastUpdated is not null);
    }
}

/// Breathing live-status dot: teal with a pulsing outer halo when healthy,
/// dim white otherwise. Bumps briefly when a fresh sync lands.
public sealed class LiveDot : Grid
{
    private readonly Ellipse _core;
    private readonly Ellipse _halo;
    private readonly ScaleTransform _bump = new(1, 1);
    private bool _active;
    private DateTimeOffset? _seenUpdate;

    public LiveDot()
    {
        Width = 10;
        Height = 10;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _bump;
        _halo = new Ellipse
        {
            Width = 6,
            Height = 6,
            Stroke = IslandColors.Brush(IslandColors.LiveTeal),
            StrokeThickness = 1,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Opacity = 0,
        };
        _core = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = IslandColors.Brush(IslandColors.LiveTeal, 0.9),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 3,
                Color = IslandColors.LiveTeal,
                Opacity = 0.55,
            },
        };
        Children.Add(_halo);
        Children.Add(_core);
        Usage.UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(MaybeBump);
        SetActive(false);
    }

    public void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (active)
        {
            _core.Fill = IslandColors.Brush(IslandColors.LiveTeal, 0.9);
            var scale = (ScaleTransform)_halo.RenderTransform;
            // ~2.4s breath: halo swells 1 -> 1.6 while fading out.
            var grow = new DoubleAnimation(1.0, 1.6, new Duration(TimeSpan.FromSeconds(1.2)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());
            var fade = new DoubleAnimation(0.55, 0.0, new Duration(TimeSpan.FromSeconds(1.2)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            _halo.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            var scale = (ScaleTransform)_halo.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _halo.BeginAnimation(OpacityProperty, null);
            _halo.Opacity = 0;
            _core.Fill = IslandColors.Brush(IslandColors.White(0.25));
        }
    }

    private void MaybeBump()
    {
        var updated = Usage.UsageStore.Shared.LastUpdated;
        if (updated == _seenUpdate) return;
        _seenUpdate = updated;
        var up = new DoubleAnimation(1.18, IslandAnimations.StrongEaseOutDuration)
        {
            EasingFunction = IslandAnimations.StrongEaseOut(),
        };
        up.Completed += (_, _) =>
        {
            var down = new DoubleAnimation(1.0, IslandAnimations.StrongEaseOutDuration)
            {
                EasingFunction = IslandAnimations.StrongEaseOut(),
            };
            _bump.BeginAnimation(ScaleTransform.ScaleXProperty, down);
            _bump.BeginAnimation(ScaleTransform.ScaleYProperty, down.Clone());
        };
        _bump.BeginAnimation(ScaleTransform.ScaleXProperty, up);
        _bump.BeginAnimation(ScaleTransform.ScaleYProperty, up.Clone());
    }
}

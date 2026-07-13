using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// The usage data row: one block per provider (5h + week tiles), separated
/// by a vertical hairline, with an inline Claude re-auth escape hatch when
/// the stored token can't satisfy the usage endpoint.
public sealed class UsagePage : Border
{
    private readonly ChartTile _claudeFiveHour = new(IslandColors.Claude, "5h", seed: 1);
    private readonly ChartTile _claudeWeekly = new(IslandColors.Claude, "week", seed: 2);
    private readonly ChartTile _codexFiveHour = new(IslandColors.Codex, "5h", seed: 3);
    private readonly ChartTile _codexWeekly = new(IslandColors.Codex, "week", seed: 4);
    private readonly Button _reauth;
    private readonly UIElement _claudeBlock;
    private readonly UIElement _codexBlock;
    private readonly Border _hairline;
    private readonly TextBlock _bothHidden;

    public UsagePage()
    {
        Padding = new Thickness(22, 12, 22, 6);
        var grid = new Grid();
        Child = grid;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _reauth = MakeReauthButton();
        _claudeBlock = MakeBlock(_claudeFiveHour, _claudeWeekly, _reauth);
        Grid.SetColumn(_claudeBlock, 0);
        grid.Children.Add(_claudeBlock);

        _hairline = new Border
        {
            Width = 1,
            Margin = new Thickness(0, 8, 0, 8),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(IslandColors.White(0.06), 0.5),
                    new GradientStop(Colors.Transparent, 1),
                },
                new Point(0, 0),
                new Point(0, 1)),
        };
        Grid.SetColumn(_hairline, 1);
        grid.Children.Add(_hairline);

        _codexBlock = MakeBlock(_codexFiveHour, _codexWeekly, extra: null);
        Grid.SetColumn(_codexBlock, 2);
        grid.Children.Add(_codexBlock);

        _bothHidden = new TextBlock
        {
            Text = Localization.L10n.Tr("Both providers are hidden. Enable one in Settings."),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.35)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 360,
            Visibility = Visibility.Collapsed,
        };
        Grid.SetColumnSpan(_bothHidden, 3);
        grid.Children.Add(_bothHidden);

        UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        StylePreferenceStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        Model.ProviderVisibilityStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Update);
        Update();
    }

    private static StackPanel MakeBlock(ChartTile fiveHour, ChartTile weekly, UIElement? extra)
    {
        var tiles = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(fiveHour, 0);
        Grid.SetColumn(weekly, 2);
        tiles.Children.Add(fiveHour);
        tiles.Children.Add(weekly);

        var block = new StackPanel { Orientation = Orientation.Vertical };
        block.Children.Add(tiles);
        if (extra is not null)
        {
            block.Children.Add(extra);
        }
        return block;
    }

    private Button MakeReauthButton()
    {
        var button = new Button
        {
            Content = Localization.L10n.Tr("Re-authenticate"),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.72)),
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Visibility = Visibility.Collapsed,
        };
        button.Click += (_, _) => ReauthFlow.Run(Core.TriggerTool.Claude);
        return button;
    }

    private static void ApplySingleWindow(ChartTile primary, ChartTile secondary, bool single)
    {
        secondary.Visibility = single ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(primary, single ? 3 : 1);
    }

    private void Update()
    {
        var store = UsageStore.Shared;
        var style = StylePreferenceStore.Shared.Style;
        var visibility = Model.ProviderVisibilityStore.Shared;

        // Hidden providers vacate their column; the hairline and both-hidden
        // placeholder track what's left, mirroring the macOS branches.
        _claudeBlock.Visibility = visibility.ClaudeVisible ? Visibility.Visible : Visibility.Collapsed;
        _codexBlock.Visibility = visibility.CodexVisible ? Visibility.Visible : Visibility.Collapsed;
        _hairline.Visibility = visibility.ClaudeVisible && visibility.CodexVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        _bothHidden.Visibility = !visibility.ClaudeVisible && !visibility.CodexVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        _claudeFiveHour.Update(store.Claude.FiveHour, style);
        _claudeWeekly.Update(store.Claude.Weekly, style);
        _codexFiveHour.Update(store.Codex.FiveHour, style);
        _codexWeekly.Update(store.Codex.Weekly, style);

        // A provider reporting one window gets one tile spanning the block —
        // Codex's July 2026 shape (single weekly quota) — instead of a
        // permanent "no data" ghost beside the real one.
        ApplySingleWindow(_claudeFiveHour, _claudeWeekly, store.Claude.SecondaryMissing);
        ApplySingleWindow(_codexFiveHour, _codexWeekly, store.Codex.SecondaryMissing);

        // Keep a manual Claude auth escape hatch available whenever the
        // Claude usage fetch is unhealthy — even when the CLI can't be
        // located, the button explains the manual path instead of stranding
        // the user with a bare caption.
        var claudeUnhealthy = store.Claude.FiveHour.Error is not null
            || store.Claude.Weekly.Error is not null;
        _reauth.Visibility = claudeUnhealthy ? Visibility.Visible : Visibility.Collapsed;
        _reauth.Content = store.ClaudeReauthInProgress
            ? Localization.L10n.Tr("waiting for login…")
            : Localization.L10n.Tr("Re-authenticate");
        _reauth.IsEnabled = !store.ClaudeReauthInProgress;
    }
}

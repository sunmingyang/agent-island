using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentIsland.Alarm;
using AgentIsland.Core;
using AgentIsland.Localization;
using AgentIsland.Model;
using AgentIsland.Trigger;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// The settings window: General / Display / Providers / Triggers / Status,
/// mirroring the macOS tab set in the island's dark chrome.
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    public static void Open()
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new SettingsWindow();
        _open = window;
        window.Closed += (_, _) => _open = null;
        window.Show();
    }

    private readonly StackPanel _rail = new() { Margin = new Thickness(10) };
    private readonly ContentControl _content = new() { Margin = new Thickness(4, 14, 18, 14) };
    private readonly List<(string Key, Button Button, Func<UIElement> Builder)> _tabs = new();

    private SettingsWindow()
    {
        Title = "Agent Island — " + L10n.Tr("Settings");
        Width = 640;
        Height = 480;
        Background = IslandColors.Brush(IslandColors.AlarmBackground);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var railHost = new Border
        {
            Background = IslandColors.Brush(IslandColors.White(0.03)),
            Child = _rail,
        };
        Grid.SetColumn(railHost, 0);
        root.Children.Add(railHost);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _content,
        };
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);
        Content = root;

        AddTab("General", BuildGeneral);
        AddTab("Display", BuildDisplay);
        AddTab("Providers", BuildProviders);
        AddTab("Triggers", BuildTriggers);
        AddTab("Status", BuildStatus);
        Select(0);
    }

    private void AddTab(string key, Func<UIElement> builder)
    {
        var button = new Button
        {
            Content = L10n.Tr(key),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.7)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        var index = _tabs.Count;
        button.Click += (_, _) => Select(index);
        _tabs.Add((key, button, builder));
        _rail.Children.Add(button);
    }

    private void Select(int index)
    {
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].Button.Background = i == index
                ? IslandColors.Brush(IslandColors.White(0.08))
                : Brushes.Transparent;
            _tabs[i].Button.Foreground = IslandColors.Brush(IslandColors.White(i == index ? 0.95 : 0.7));
        }
        _content.Content = _tabs[index].Builder();
    }

    // MARK: - Tabs

    private UIElement BuildGeneral()
    {
        var stack = Section();
        stack.Children.Add(Header(L10n.Tr("General")));

        var language = new ComboBox { Width = 160 };
        language.Items.Add(L10n.Tr("Auto (system)"));
        language.Items.Add("English");
        language.Items.Add("简体中文");
        language.SelectedIndex = AppLanguageStore.Load() switch
        {
            L10n.Language.English => 1,
            L10n.Language.SimplifiedChinese => 2,
            _ => 0,
        };
        language.SelectionChanged += (_, _) =>
        {
            AppLanguageStore.Save(language.SelectedIndex switch
            {
                1 => L10n.Language.English,
                2 => L10n.Language.SimplifiedChinese,
                _ => L10n.Language.Auto,
            });
            MessageBox.Show(this,
                L10n.Tr("Language saved. Restart Agent Island to apply everywhere."),
                "Agent Island");
        };
        stack.Children.Add(Row(L10n.Tr("Language"), language));

        var launch = new ToggleSwitch(LaunchAtLogin.IsEnabled) { VerticalAlignment = VerticalAlignment.Center };
        launch.Toggled += enabled => LaunchAtLogin.SetEnabled(enabled);
        stack.Children.Add(Row(L10n.Tr("Launch at login"), launch));

        stack.Children.Add(Header(L10n.Tr("Alerts")));
        var alerts = new ToggleSwitch(AlertThresholdStore.Shared.Enabled) { VerticalAlignment = VerticalAlignment.Center };
        alerts.Toggled += enabled => AlertThresholdStore.Shared.Enabled = enabled;
        stack.Children.Add(Row(L10n.Tr("Approaching-limit alerts"), alerts));
        stack.Children.Add(Row(
            L10n.TrFormat("Warning at {0}%", AlertThresholdStore.Shared.WarningPercent),
            Stepper(() => AlertThresholdStore.Shared.WarningPercent,
                value => AlertThresholdStore.Shared.WarningPercent = value,
                () => Select(0))));
        stack.Children.Add(Row(
            L10n.TrFormat("Critical at {0}%", AlertThresholdStore.Shared.CriticalPercent),
            Stepper(() => AlertThresholdStore.Shared.CriticalPercent,
                value => AlertThresholdStore.Shared.CriticalPercent = value,
                () => Select(0))));
        return stack;
    }

    private UIElement BuildDisplay()
    {
        var stack = Section();
        stack.Children.Add(Header(L10n.Tr("Chart style")));
        var style = new ComboBox { Width = 160 };
        foreach (var value in Enum.GetValues<ChartStyle>()) style.Items.Add(value.ToString());
        style.SelectedIndex = (int)StylePreferenceStore.Shared.Style;
        style.SelectionChanged += (_, _) =>
            StylePreferenceStore.Shared.Style = (ChartStyle)style.SelectedIndex;
        stack.Children.Add(Row(L10n.Tr("Usage charts"), style));

        var costStyle = new ComboBox { Width = 160 };
        costStyle.Items.Add("USD");
        costStyle.Items.Add("VALUE");
        costStyle.Items.Add("TOKENS");
        costStyle.Items.Add("TREND");
        costStyle.SelectedIndex = (int)CostStylePreferenceStore.Shared.Style;
        costStyle.SelectionChanged += (_, _) =>
            CostStylePreferenceStore.Shared.Style = (CostStyle)costStyle.SelectedIndex;
        stack.Children.Add(Row(L10n.Tr("Cost display"), costStyle));

        stack.Children.Add(Header(L10n.Tr("Top bar")));
        var width = new ComboBox { Width = 160 };
        width.Items.Add(L10n.Tr("Wide (notch style)"));
        width.Items.Add(L10n.Tr("Compact"));
        width.SelectedIndex = IslandModel.Shared.SpacingMode == IslandSpacingMode.NotchStyle ? 0 : 1;
        width.SelectionChanged += (_, _) =>
            IslandModel.Shared.SpacingMode = width.SelectedIndex == 0
                ? IslandSpacingMode.NotchStyle
                : IslandSpacingMode.Compact;
        stack.Children.Add(Row(L10n.Tr("Bar width"), width));

        var costPage = new ToggleSwitch(ScreenPref.Shared.ShowCostPage) { VerticalAlignment = VerticalAlignment.Center };
        costPage.Toggled += enabled => ScreenPref.Shared.ShowCostPage = enabled;
        stack.Children.Add(Row(L10n.Tr("Show cost page"), costPage));
        return stack;
    }

    private UIElement BuildProviders()
    {
        var stack = Section();
        stack.Children.Add(Header(L10n.Tr("Providers")));
        var claude = new ToggleSwitch(ProviderVisibilityStore.Shared.ClaudeVisible) { VerticalAlignment = VerticalAlignment.Center };
        claude.Toggled += enabled => ProviderVisibilityStore.Shared.ClaudeVisible = enabled;
        stack.Children.Add(Row("Claude", claude));
        var codex = new ToggleSwitch(ProviderVisibilityStore.Shared.CodexVisible) { VerticalAlignment = VerticalAlignment.Center };
        codex.Toggled += enabled => ProviderVisibilityStore.Shared.CodexVisible = enabled;
        stack.Children.Add(Row("Codex", codex));

        stack.Children.Add(Header(L10n.Tr("Refresh")));
        var interval = new ComboBox { Width = 160 };
        interval.Items.Add(L10n.Tr("Every 5 minutes"));
        interval.Items.Add(L10n.Tr("Every 15 minutes"));
        interval.Items.Add(L10n.Tr("Every 30 minutes"));
        interval.SelectedIndex = Array.IndexOf(RefreshIntervalStore.Presets, RefreshIntervalStore.Shared.Seconds);
        interval.SelectionChanged += (_, _) =>
            RefreshIntervalStore.Shared.Seconds = RefreshIntervalStore.Presets[Math.Max(0, interval.SelectedIndex)];
        stack.Children.Add(Row(L10n.Tr("Usage refresh interval"), interval));

        stack.Children.Add(Header(L10n.Tr("Accounts")));
        var claudeAuth = ActionButton(L10n.Tr("Re-authenticate Claude"), () => UsageStore.Shared.ReauthenticateClaude());
        claudeAuth.IsEnabled = ClaudeCredentials.CanPromptReauth();
        stack.Children.Add(Row("Claude", claudeAuth));
        var codexAuth = ActionButton(L10n.Tr("Re-authenticate Codex"), () => UsageStore.Shared.ReauthenticateCodex());
        codexAuth.IsEnabled = CodexCredentials.CanPromptReauth();
        stack.Children.Add(Row("Codex", codexAuth));
        return stack;
    }

    private UIElement BuildTriggers()
    {
        var stack = Section();
        stack.Children.Add(Header(L10n.Tr("Auto-resume")));
        var kill = new ToggleSwitch(TriggerSafetyStore.Shared.ExecutionEnabled) { VerticalAlignment = VerticalAlignment.Center };
        kill.Toggled += enabled => TriggerSafetyStore.Shared.ExecutionEnabled = enabled;
        stack.Children.Add(Row(L10n.Tr("Auto-resume enabled (kill switch)"), kill));
        stack.Children.Add(Row(
            L10n.Tr("Run records"),
            ActionButton(L10n.Tr("Open run records"), () => TriggerEngine.Shared.OpenLogsDirectory())));

        stack.Children.Add(Header(L10n.Tr("Trusted projects")));
        if (TriggerSafetyStore.Shared.AllowedRoots.Count == 0)
        {
            stack.Children.Add(Caption(L10n.Tr("No trusted projects yet.")));
        }
        foreach (var rootPath in TriggerSafetyStore.Shared.AllowedRoots.OrderBy(r => r))
        {
            var remove = ActionButton(L10n.Tr("Remove"), () =>
            {
                TriggerSafetyStore.Shared.SetAllowed(rootPath, false);
                Select(3);
            });
            stack.Children.Add(Row(rootPath, remove, monospaceLabel: true));
        }
        return stack;
    }

    private UIElement BuildStatus()
    {
        var stack = Section();
        stack.Children.Add(Header(L10n.Tr("Status legend")));
        stack.Children.Add(LegendRow(ActivityState.Idle, L10n.Tr("idle"), L10n.Tr("Nothing running — or the turn is over and it's yours.")));
        stack.Children.Add(LegendRow(ActivityState.Working, L10n.Tr("running"), L10n.Tr("A session is working; the logo spins.")));
        stack.Children.Add(LegendRow(ActivityState.NeedsYou, L10n.Tr("your turn"), L10n.Tr("A turn finished; the alarm calls you back.")));
        stack.Children.Add(LegendRow(ActivityState.RateLimited, L10n.Tr("needs attention"), L10n.Tr("Rate limit, login, network, or provider error.")));

        stack.Children.Add(Header(L10n.Tr("Turn alarm")));
        var enabled = new ToggleSwitch(AgentReminderStore.Shared.Enabled) { VerticalAlignment = VerticalAlignment.Center };
        enabled.Toggled += value => AgentReminderStore.Shared.Enabled = value;
        stack.Children.Add(Row(L10n.Tr("Turn alarms"), enabled));

        var sound = new ToggleSwitch(AgentReminderStore.Shared.SoundEnabled) { VerticalAlignment = VerticalAlignment.Center };
        sound.Toggled += value => AgentReminderStore.Shared.SoundEnabled = value;
        stack.Children.Add(Row(L10n.Tr("Alarm sound"), sound));

        var choice = new ComboBox { Width = 160 };
        foreach (var preset in AgentReminderStore.SoundPresets) choice.Items.Add(preset);
        var selected = Array.IndexOf(AgentReminderStore.SoundPresets, AgentReminderStore.Shared.SoundChoice);
        choice.SelectedIndex = selected >= 0 ? selected : 0;
        choice.SelectionChanged += (_, _) =>
        {
            AgentReminderStore.Shared.SoundChoice = AgentReminderStore.SoundPresets[Math.Max(0, choice.SelectedIndex)];
            // Preview on selection (click), never on hover.
            PreviewSound();
        };
        stack.Children.Add(Row(L10n.Tr("Sound"), choice));

        var volume = new Slider
        {
            Width = 160,
            Minimum = 0,
            Maximum = 1,
            Value = AgentReminderStore.Shared.Volume,
            VerticalAlignment = VerticalAlignment.Center,
        };
        volume.ValueChanged += (_, _) => AgentReminderStore.Shared.Volume = volume.Value;
        stack.Children.Add(Row(L10n.Tr("Volume"), volume));

        var details = new ToggleSwitch(AgentReminderStore.Shared.ShowSessionDetails) { VerticalAlignment = VerticalAlignment.Center };
        details.Toggled += value => AgentReminderStore.Shared.ShowSessionDetails = value;
        stack.Children.Add(Row(L10n.Tr("Show session details in alarms"), details));
        return stack;
    }

    private static void PreviewSound()
    {
        try
        {
            if (AgentReminderStore.Shared.ResolveSoundFile() is not { } file) return;
            var player = new MediaPlayer { Volume = AgentReminderStore.Shared.Volume };
            player.Open(new Uri(file));
            player.Play();
        }
        catch
        {
        }
    }

    // MARK: - Builders

    private static StackPanel Section() => new() { Orientation = Orientation.Vertical };

    private static TextBlock Header(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = IslandFonts.Ui,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = IslandColors.Brush(IslandColors.White(0.4)),
        Margin = new Thickness(2, 16, 0, 8),
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontFamily = IslandFonts.Ui,
        FontSize = 11,
        Foreground = IslandColors.Brush(IslandColors.White(0.35)),
        Margin = new Thickness(2, 0, 0, 6),
    };

    private static Border Row(string label, UIElement control, bool monospaceLabel = false)
    {
        var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            FontFamily = monospaceLabel ? IslandFonts.Mono : IslandFonts.Ui,
            FontSize = monospaceLabel ? 10 : 12,
            Foreground = IslandColors.Brush(IslandColors.White(0.85)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border
        {
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid,
        };
    }

    private static Button ActionButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.85)),
            Background = IslandColors.Brush(IslandColors.White(0.08)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 5, 10, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private StackPanel LegendRowHost() => new() { Orientation = Orientation.Horizontal };

    private Border LegendRow(ActivityState state, string name, string caption)
    {
        var host = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var logo = new ProviderLogo { Tool = TriggerTool.Claude, Width = 38, Height = 32 };
        logo.SetState(state);
        Grid.SetColumn(logo, 0);
        host.Children.Add(logo);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = name,
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        text.Children.Add(new TextBlock
        {
            Text = caption,
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.45)),
        });
        Grid.SetColumn(text, 1);
        host.Children.Add(text);
        return new Border
        {
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = host,
        };
    }

    private Button Stepper(Func<int> get, Action<int> set, Action refresh)
    {
        var button = ActionButton(get().ToString(), () => { });
        button.Click += (_, _) =>
        {
            // Click steps +5, wrapping via the store's own clamping.
            set(get() + 5);
            refresh();
        };
        button.MouseRightButtonUp += (_, args) =>
        {
            set(get() - 5);
            refresh();
            args.Handled = true;
        };
        button.ToolTip = L10n.Tr("Left-click +5, right-click -5");
        return button;
    }
}

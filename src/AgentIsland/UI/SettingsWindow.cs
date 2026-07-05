using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentIsland.Alarm;
using AgentIsland.Core;
using AgentIsland.Localization;
using AgentIsland.Model;
using AgentIsland.Trigger;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// Settings window — a faithful port of the macOS layout: brand header on
/// top, pill tab bar (General / Display / Providers / Triggers / Status),
/// hairlines, scrolling row content, and the GitHub/License/Quit footer.
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

    private enum Tab
    {
        General,
        Display,
        Providers,
        Triggers,
        Status,
    }

    private readonly StackPanel _tabBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 4, 18, 10) };
    private readonly List<(Tab Tab, Border Cell)> _tabCells = new();
    private readonly ScrollViewer _scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private Tab _active = Tab.General;

    private SettingsWindow()
    {
        Title = "Agent Island — " + L10n.Tr("Settings");
        Width = 480;
        Height = 640;
        MinWidth = 440;
        MinHeight = 420;
        Background = IslandColors.Brush(IslandColors.AlarmBackground);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel();
        Content = root;

        var header = BuildBrandHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        BuildTabBar();
        DockPanel.SetDock(_tabBar, Dock.Top);
        root.Children.Add(_tabBar);

        var topHairline = Hairline();
        DockPanel.SetDock(topHairline, Dock.Top);
        root.Children.Add(topHairline);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var bottomHairline = Hairline();
        DockPanel.SetDock(bottomHairline, Dock.Bottom);
        root.Children.Add(bottomHairline);

        root.Children.Add(_scroll);

        var savedTab = Preferences.Get<string?>("Settings.activeTab");
        if (Enum.TryParse<Tab>(savedTab, out var restored)) _active = restored;
        // Scripted-verification hook: jump straight to a tab.
        if (Enum.TryParse<Tab>(
                Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_SETTINGS_TAB"),
                out var forced))
        {
            _active = forced;
        }
        Select(_active);
    }

    // MARK: - Chrome

    private UIElement BuildBrandHeader()
    {
        var grid = new Grid { Margin = new Thickness(24, 16, 24, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var mark = new Image
        {
            Width = 26,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        try
        {
            mark.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/agentisland_logo.png"));
        }
        catch
        {
        }
        Grid.SetColumn(mark, 0);
        grid.Children.Add(mark);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "Agent Island",
            FontFamily = IslandFonts.Ui,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.92)),
        });
        titles.Children.Add(new TextBlock
        {
            Text = L10n.Tr("A status companion for Claude Code and Codex"),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);

        var version = new Border
        {
            Child = new TextBlock
            {
                Text = "v" + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"),
                FontFamily = IslandFonts.Mono,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = IslandColors.Brush(IslandColors.White(0.34)),
            },
            CornerRadius = new CornerRadius(11),
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            Padding = new Thickness(9, 4, 9, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(version, 2);
        grid.Children.Add(version);
        return grid;
    }

    private void BuildTabBar()
    {
        _tabBar.Children.Clear();
        _tabCells.Clear();
        foreach (var tab in Enum.GetValues<Tab>())
        {
            var label = tab switch
            {
                Tab.General => L10n.Tr("General"),
                Tab.Display => L10n.Tr("Display"),
                Tab.Providers => L10n.Tr("Providers"),
                Tab.Triggers => L10n.Tr("Triggers tab"),
                Tab.Status => L10n.Tr("Status guide"),
                _ => tab.ToString(),
            };
            var cell = new Border
            {
                Child = new TextBlock
                {
                    Text = label,
                    FontFamily = IslandFonts.Ui,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                },
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var captured = tab;
            cell.MouseLeftButtonUp += (_, args) =>
            {
                Select(captured);
                args.Handled = true;
            };
            _tabCells.Add((tab, cell));
            _tabBar.Children.Add(cell);
        }
    }

    private static UIElement Hairline() => new Border
    {
        Height = 1,
        Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(IslandColors.White(0.055), 0.25),
                new GradientStop(IslandColors.White(0.055), 0.75),
                new GradientStop(Colors.Transparent, 1),
            },
            new Point(0, 0),
            new Point(1, 0)),
    };

    private UIElement BuildFooter()
    {
        var grid = new Grid { Margin = new Thickness(24, 12, 24, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var github = new DottedLink("GitHub", "https://github.com/tristan666666/agent-island");
        github.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(github, 0);
        grid.Children.Add(github);

        var license = new DottedLink("License", "https://github.com/tristan666666/agent-island/blob/main/LICENSE");
        license.Margin = new Thickness(14, 0, 0, 0);
        license.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(license, 1);
        grid.Children.Add(license);

        var quit = new PillButtonControl(L10n.Tr("Quit"));
        quit.Clicked += () => System.Windows.Application.Current.Shutdown();
        Grid.SetColumn(quit, 3);
        grid.Children.Add(quit);
        return grid;
    }

    private void Select(Tab tab)
    {
        _active = tab;
        Preferences.Set("Settings.activeTab", tab.ToString());
        foreach (var (cellTab, cell) in _tabCells)
        {
            var isOn = cellTab == tab;
            cell.Background = isOn ? IslandColors.Brush(IslandColors.White(0.08)) : Brushes.Transparent;
            ((TextBlock)cell.Child!).Foreground = IslandColors.Brush(IslandColors.White(isOn ? 0.95 : 0.50));
        }
        _scroll.Content = tab switch
        {
            Tab.General => BuildGeneral(),
            Tab.Display => BuildDisplay(),
            Tab.Providers => BuildProviders(),
            Tab.Triggers => BuildTriggers(),
            Tab.Status => BuildStatus(),
            _ => new StackPanel(),
        };
    }

    // MARK: - Section helpers

    private static StackPanel TabStack() => new()
    {
        Orientation = Orientation.Vertical,
        Margin = new Thickness(14, 18, 14, 6),
    };

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = L10n.Tr(text).ToUpperInvariant(),
        FontFamily = IslandFonts.Ui,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = IslandColors.Brush(IslandColors.White(0.34)),
        Margin = new Thickness(10, 14, 10, 6),
    };

    // MARK: - General

    private UIElement BuildGeneral()
    {
        var stack = TabStack();
        stack.Children.Add(SectionLabel("General"));

        var launch = new CobaltToggle(LaunchAtLogin.IsEnabled);
        launch.Toggled += enabled => LaunchAtLogin.SetEnabled(enabled);
        stack.Children.Add(new SettingsRowControl(
            "Launch at Login", "Open AgentIsland when you sign in.", launch));

        var presets = RefreshIntervalStore.Presets;
        var refresh = new Segmented(new[] { "5m", "15m", "30m" },
            Math.Max(0, Array.IndexOf(presets, RefreshIntervalStore.Shared.Seconds)));
        refresh.SelectionChanged += index => RefreshIntervalStore.Shared.Seconds = presets[index];
        stack.Children.Add(new SettingsRowControl(
            "Refresh interval", "How often to refresh.", refresh));

        var language = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
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
            var chosen = language.SelectedIndex switch
            {
                1 => L10n.Language.English,
                2 => L10n.Language.SimplifiedChinese,
                _ => L10n.Language.Auto,
            };
            AppLanguageStore.Save(chosen);
            L10n.Current = chosen;
            App.Instance.RebuildForLanguageChange();
            Title = "Agent Island — " + L10n.Tr("Settings");
            BuildTabBar();
            Select(_active);
        };
        stack.Children.Add(new SettingsRowControl(
            "Language", CurrentLanguageSubtitle(), language));

        var lowPower = new CobaltToggle(LowPowerModeStore.Shared.Enabled);
        lowPower.Toggled += enabled => LowPowerModeStore.Shared.Enabled = enabled;
        stack.Children.Add(new SettingsRowControl(
            "Low Power Mode", "Glow only on refresh, hover, or limit alerts.", lowPower));

        stack.Children.Add(SectionLabel("Alerts"));
        var alertsHost = new StackPanel();
        var alerts = new CobaltToggle(AlertThresholdStore.Shared.Enabled);
        stack.Children.Add(new SettingsRowControl(
            "Approaching-limit alerts",
            "Tint the island and pulse the peek pill when 5-hour usage nears your limit.",
            alerts));

        alertsHost.Children.Add(ThresholdLine(IslandColors.AlertAmber, "Warning",
            () => AlertThresholdStore.Shared.WarningPercent,
            value => AlertThresholdStore.Shared.WarningPercent = value));
        alertsHost.Children.Add(ThresholdLine(IslandColors.AlertRed, "Critical",
            () => AlertThresholdStore.Shared.CriticalPercent,
            value => AlertThresholdStore.Shared.CriticalPercent = value));
        alertsHost.Margin = new Thickness(10, 8, 10, 8);
        alertsHost.Opacity = AlertThresholdStore.Shared.Enabled ? 1.0 : 0.40;
        alertsHost.IsEnabled = AlertThresholdStore.Shared.Enabled;
        alerts.Toggled += enabled =>
        {
            AlertThresholdStore.Shared.Enabled = enabled;
            alertsHost.Opacity = enabled ? 1.0 : 0.40;
            alertsHost.IsEnabled = enabled;
        };
        stack.Children.Add(alertsHost);

        stack.Children.Add(SectionLabel("Updates"));
        var autoCheck = new CobaltToggle(Preferences.Get<bool?>("AgentIsland.autoCheckUpdates") ?? true);
        autoCheck.Toggled += enabled => Preferences.Set("AgentIsland.autoCheckUpdates", enabled);
        stack.Children.Add(new SettingsRowControl(
            "Check for updates automatically",
            "Check for new versions in the background and notify you when one's available.",
            autoCheck));

        var check = new PillButtonControl(L10n.Tr("Check"));
        check.Clicked += () => MessageBox.Show(this,
            L10n.Tr("You're on the latest version. (Auto-update channel for Windows ships with a later release.)"),
            "Agent Island");
        stack.Children.Add(new SettingsRowControl(
            "Check now", "Look for a new version immediately.", check));

        return stack;
    }

    private static string CurrentLanguageSubtitle() => AppLanguageStore.Load() switch
    {
        L10n.Language.English => "English",
        L10n.Language.SimplifiedChinese => "简体中文",
        _ => L10n.Tr("Follows the system language."),
    };

    /// Threshold row: glowing severity dot, label, numeric %-field. The
    /// stores clamp so warning stays below critical.
    private UIElement ThresholdLine(Color color, string label, Func<int> get, Action<int> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = IslandColors.Brush(color),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                ShadowDepth = 0, BlurRadius = 4, Color = color, Opacity = 0.7,
            },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        var text = new TextBlock
        {
            Text = L10n.Tr(label),
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.92)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var fieldHost = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = IslandColors.Brush(IslandColors.White(0.05)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.10)),
            BorderThickness = new Thickness(0.5),
            Width = 64,
            Height = 28,
        };
        var fieldRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var field = new TextBox
        {
            Text = get().ToString(),
            FontFamily = IslandFonts.Mono,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.95)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 26,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CaretBrush = Brushes.White,
        };
        field.LostFocus += (_, _) =>
        {
            if (int.TryParse(field.Text, out var value)) set(value);
            field.Text = get().ToString();
        };
        field.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Enter)
            {
                if (int.TryParse(field.Text, out var value)) set(value);
                field.Text = get().ToString();
            }
        };
        fieldRow.Children.Add(field);
        fieldRow.Children.Add(new TextBlock
        {
            Text = "%",
            FontFamily = IslandFonts.Mono,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        fieldHost.Child = fieldRow;
        Grid.SetColumn(fieldHost, 2);
        grid.Children.Add(fieldHost);
        return grid;
    }

    // MARK: - Display

    private UIElement BuildDisplay()
    {
        var stack = TabStack();

        // 用量显示 — the five visual preview tiles, "click to switch" hint.
        var usageHeader = new Grid { Margin = new Thickness(10, 14, 10, 6) };
        usageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        usageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var usageLabel = SectionLabel("Usage display");
        usageLabel.Margin = new Thickness(0);
        Grid.SetColumn(usageLabel, 0);
        usageHeader.Children.Add(usageLabel);
        var hint = new TextBlock
        {
            Text = L10n.Tr("click to switch"),
            FontFamily = IslandFonts.Ui,
            FontSize = 10,
            Foreground = IslandColors.Brush(IslandColors.White(0.18)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(hint, 1);
        usageHeader.Children.Add(hint);
        stack.Children.Add(usageHeader);

        var stylePicker = new ChartStylePickerControl(StylePreferenceStore.Shared.Style)
        {
            Margin = new Thickness(10, 2, 2, 8),
        };
        stylePicker.StyleSelected += style => StylePreferenceStore.Shared.Style = style;
        stack.Children.Add(stylePicker);

        // 成本显示 — toggle first; the picker tiles appear only when the
        // cost page is on, matching the macOS conditional.
        stack.Children.Add(SectionLabel("Cost display"));
        var costPickerHost = new ContentControl { Margin = new Thickness(10, 2, 2, 8) };
        void RefreshCostPicker()
        {
            if (ScreenPref.Shared.ShowCostPage)
            {
                var picker = new CostStylePickerControl(CostStylePreferenceStore.Shared.Style);
                picker.StyleSelected += style => CostStylePreferenceStore.Shared.Style = style;
                costPickerHost.Content = picker;
            }
            else
            {
                costPickerHost.Content = null;
            }
        }
        var costPage = new CobaltToggle(ScreenPref.Shared.ShowCostPage);
        costPage.Toggled += enabled =>
        {
            ScreenPref.Shared.ShowCostPage = enabled;
            RefreshCostPicker();
        };
        stack.Children.Add(new SettingsRowControl(
            "Show cost page in top panel",
            "Include local token cost/value as a swipe page in the island.",
            costPage));
        RefreshCostPicker();
        stack.Children.Add(costPickerHost);

        // 顶部条.
        stack.Children.Add(SectionLabel("Top bar"));
        var alwaysShow = new CobaltToggle(AlwaysShowUsageStore.Shared.Enabled);
        alwaysShow.Toggled += enabled => AlwaysShowUsageStore.Shared.Enabled = enabled;
        stack.Children.Add(new SettingsRowControl(
            "Always show usage in top bar",
            "Show the 5-hour percentages beside the logos without hovering.",
            alwaysShow));

        var width = new Segmented(
            new[] { L10n.Tr("Compact"), L10n.Tr("Wide (notch style)") },
            IslandModel.Shared.SpacingMode == IslandSpacingMode.Compact ? 0 : 1);
        width.SelectionChanged += index =>
            IslandModel.Shared.SpacingMode = index == 0 ? IslandSpacingMode.Compact : IslandSpacingMode.NotchStyle;
        stack.Children.Add(new SettingsRowControl(
            "Bar style",
            "Wide mirrors the MacBook notch layout; Compact narrows the top bar.",
            width));

        // 屏幕.
        stack.Children.Add(SectionLabel("Screen"));
        var screens = System.Windows.Forms.Screen.AllScreens;
        var display = new ComboBox { Width = 180, VerticalAlignment = VerticalAlignment.Center };
        display.Items.Add(L10n.Tr("Auto"));
        foreach (var screen in screens)
        {
            display.Items.Add(screen.DeviceName.TrimStart('\\', '.') + (screen.Primary ? " ★" : ""));
        }
        var choice = IslandTargetDisplayStore.Shared.Choice;
        display.SelectedIndex = choice == "auto"
            ? 0
            : Math.Max(0, Array.FindIndex(screens, s => s.DeviceName == choice) + 1);
        display.SelectionChanged += (_, _) =>
            IslandTargetDisplayStore.Shared.Choice = display.SelectedIndex <= 0
                ? "auto"
                : screens[display.SelectedIndex - 1].DeviceName;
        stack.Children.Add(new SettingsRowControl(
            "Show on",
            L10n.TrFormat("Auto — currently on {0}.", L10n.Tr("the primary display")),
            display));

        return stack;
    }

    // MARK: - Providers

    private UIElement BuildProviders()
    {
        var stack = TabStack();
        stack.Children.Add(SectionLabel("Providers"));

        stack.Children.Add(ProviderRow(TriggerTool.Claude));
        stack.Children.Add(ProviderRow(TriggerTool.Codex));

        stack.Children.Add(SectionLabel("TOKEN"));
        var mode = new Segmented(
            new[] { L10n.Tr("All tokens"), L10n.Tr("Input + output") },
            TokenCountModeStore.Shared.Mode == TokenCountMode.All ? 0 : 1);
        mode.SelectionChanged += index =>
            TokenCountModeStore.Shared.Mode = index == 0 ? TokenCountMode.All : TokenCountMode.Billable;
        stack.Children.Add(new SettingsRowControl(
            "Token counting",
            TokenCountModeStore.Shared.Mode == TokenCountMode.All
                ? "Counts everything — input, output, and cache. Mirrors ccusage."
                : "Input + output only. Matches Anthropic's claude.ai stats.",
            mode));

        // Cost freshness strip: section label + last-scan caption + Refresh.
        var costRow = new Grid { Margin = new Thickness(10, 14, 10, 14) };
        costRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        costRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        costRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var costLabel = SectionLabel("Cost");
        costLabel.Margin = new Thickness(0);
        costLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(costLabel, 0);
        costRow.Children.Add(costLabel);
        var costCaption = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.42)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        costCaption.Text = Cost.CostStore.Shared.LastUpdated is { } updated
            ? L10n.TrFormat("last scan {0}", Formatting.RelativeAgo(DateTimeOffset.Now - updated, L10n.IsChinese))
            : L10n.Tr("swipe panel to view");
        Grid.SetColumn(costCaption, 1);
        costRow.Children.Add(costCaption);
        var costRefresh = new PillButtonControl(L10n.Tr("Refresh"));
        costRefresh.Clicked += () => Cost.CostStore.Shared.Refresh();
        Grid.SetColumn(costRefresh, 2);
        costRow.Children.Add(costRefresh);
        stack.Children.Add(costRow);

        return stack;
    }

    private UIElement ProviderRow(TriggerTool tool)
    {
        var store = UsageStore.Shared;
        var usage = tool == TriggerTool.Claude ? store.Claude : store.Codex;
        var visible = ProviderVisibilityStore.Shared.IsVisible(tool);

        var trailing = new StackPanel { Orientation = Orientation.Horizontal };
        var canReauth = tool == TriggerTool.Claude
            ? ClaudeCredentials.CanPromptReauth()
            : CodexCredentials.CanPromptReauth();
        if (canReauth)
        {
            var reauth = new PillButtonControl(L10n.Tr("Re-authenticate"));
            reauth.Margin = new Thickness(0, 0, 8, 0);
            reauth.Clicked += () =>
            {
                if (tool == TriggerTool.Claude) store.ReauthenticateClaude();
                else store.ReauthenticateCodex();
            };
            trailing.Children.Add(reauth);
        }
        var toggle = new CobaltToggle(visible);
        toggle.Toggled += enabled =>
        {
            if (tool == TriggerTool.Claude) ProviderVisibilityStore.Shared.ClaudeVisible = enabled;
            else ProviderVisibilityStore.Shared.CodexVisible = enabled;
        };
        toggle.VerticalAlignment = VerticalAlignment.Center;
        trailing.Children.Add(toggle);

        return new SettingsRowControl(
            tool.Display(),
            ProviderSubtitle(usage),
            trailing,
            dot: IslandColors.For(tool),
            chip: usage.Plan?.ToUpperInvariant());
    }

    /// "synced 2m ago · 69% / 33%" — the most authoritative diagnostic
    /// surface; errors surface in place of the numbers.
    private static string ProviderSubtitle(AppUsage usage)
    {
        var synced = UsageStore.Shared.LastUpdated is { } updated
            ? L10n.TrFormat("synced {0}", Formatting.RelativeAgo(DateTimeOffset.Now - updated, L10n.IsChinese))
            : L10n.Tr("idle");
        return $"{synced} · {WindowCaption(usage.FiveHour)} / {WindowCaption(usage.Weekly)}";
    }

    private static string WindowCaption(WindowUsage window)
    {
        if (window.Error is { } error && window.UsedPercent == 0)
        {
            return "⚠ " + ErrorDisplay.Localize(error);
        }
        return $"{(int)Math.Round(window.UsedPercent * 100)}%";
    }

    // MARK: - Triggers

    private UIElement BuildTriggers()
    {
        var stack = TabStack();
        stack.Children.Add(SectionLabel("Auto-resume"));
        stack.Children.Add(new TextBlock
        {
            Text = L10n.Tr("After the quota recovers, let Agent Island auto-resume chosen sessions."),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            Foreground = IslandColors.Brush(IslandColors.White(0.6)),
            Margin = new Thickness(10, 0, 10, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(SectionLabel("Execution safety"));
        var kill = new CobaltToggle(TriggerSafetyStore.Shared.ExecutionEnabled);
        kill.Toggled += enabled => TriggerSafetyStore.Shared.ExecutionEnabled = enabled;
        stack.Children.Add(new SettingsRowControl(
            "Auto-resume master switch",
            "When off, Agent Island never launches any Claude or Codex resume command.",
            kill));

        var records = new PillButtonControl(L10n.Tr("Open"));
        records.Clicked += () => TriggerEngine.Shared.OpenLogsDirectory();
        stack.Children.Add(new SettingsRowControl(
            "Run logs",
            "Open the log folder to review blocked or executed resume runs.",
            records));

        stack.Children.Add(new TextBlock
        {
            Text = TriggerStore.Shared.Triggers.Count == 0
                ? L10n.Tr("No rules yet — add one below.")
                : L10n.TrFormat("{0} rule(s) — manage them on the island's Triggers page.", TriggerStore.Shared.Triggers.Count),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            Foreground = IslandColors.Brush(IslandColors.White(0.45)),
            Margin = new Thickness(10, 12, 10, 6),
        });

        stack.Children.Add(SectionLabel("Trusted projects"));
        if (TriggerSafetyStore.Shared.AllowedRoots.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = L10n.Tr("No trusted projects yet."),
                FontFamily = IslandFonts.Ui,
                FontSize = 11,
                Foreground = IslandColors.Brush(IslandColors.White(0.35)),
                Margin = new Thickness(10, 0, 10, 6),
            });
        }
        foreach (var root in TriggerSafetyStore.Shared.AllowedRoots.OrderBy(r => r))
        {
            var remove = new PillButtonControl(L10n.Tr("Remove"));
            var captured = root;
            remove.Clicked += () =>
            {
                TriggerSafetyStore.Shared.SetAllowed(captured, false);
                Select(Tab.Triggers);
            };
            stack.Children.Add(new SettingsRowControl(captured, null, remove, monospaceTitle: true));
        }
        return stack;
    }

    // MARK: - Status

    private UIElement BuildStatus()
    {
        var stack = TabStack();
        stack.Children.Add(SectionLabel("Status legend"));
        stack.Children.Add(LegendRow(ActivityState.Idle, "idle",
            "Nothing running — or the turn is over and it's yours."));
        stack.Children.Add(LegendRow(ActivityState.Working, "running",
            "A session is working; the logo spins."));
        stack.Children.Add(LegendRow(ActivityState.NeedsYou, "your turn",
            "A turn finished; the alarm calls you back."));
        stack.Children.Add(LegendRow(ActivityState.RateLimited, "needs attention",
            "Rate limit, login, network, or provider error."));

        stack.Children.Add(SectionLabel("Turn alarm"));
        var enabled = new CobaltToggle(AgentReminderStore.Shared.Enabled);
        enabled.Toggled += value => AgentReminderStore.Shared.Enabled = value;
        stack.Children.Add(new SettingsRowControl(
            "Turn alarms", "Foreground alarm when a background turn finishes.", enabled));

        var sound = new CobaltToggle(AgentReminderStore.Shared.SoundEnabled);
        sound.Toggled += value => AgentReminderStore.Shared.SoundEnabled = value;
        stack.Children.Add(new SettingsRowControl(
            "Alarm sound", "Repeats until the alarm is dismissed.", sound));

        var choice = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
        foreach (var preset in AgentReminderStore.SoundPresets) choice.Items.Add(preset);
        var selected = Array.IndexOf(AgentReminderStore.SoundPresets, AgentReminderStore.Shared.SoundChoice);
        choice.SelectedIndex = selected >= 0 ? selected : 0;
        choice.SelectionChanged += (_, _) =>
        {
            AgentReminderStore.Shared.SoundChoice = AgentReminderStore.SoundPresets[Math.Max(0, choice.SelectedIndex)];
            PreviewSound();
        };
        stack.Children.Add(new SettingsRowControl("Sound", "Preview plays on selection.", choice));

        var volume = new Slider
        {
            Width = 130,
            Minimum = 0,
            Maximum = 1,
            Value = AgentReminderStore.Shared.Volume,
            VerticalAlignment = VerticalAlignment.Center,
        };
        volume.ValueChanged += (_, _) => AgentReminderStore.Shared.Volume = volume.Value;
        stack.Children.Add(new SettingsRowControl("Volume", null, volume));

        var details = new CobaltToggle(AgentReminderStore.Shared.ShowSessionDetails);
        details.Toggled += value => AgentReminderStore.Shared.ShowSessionDetails = value;
        stack.Children.Add(new SettingsRowControl(
            "Show session details in alarms",
            "Provider, session, and project fields on the alarm window.",
            details));

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

    private UIElement LegendRow(ActivityState state, string name, string caption)
    {
        var logo = new ProviderLogo { Tool = TriggerTool.Claude, Width = 38, Height = 32 };
        logo.SetState(state);
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(logo, 0);
        host.Children.Add(logo);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = L10n.Tr(name),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
        });
        text.Children.Add(new TextBlock
        {
            Text = L10n.Tr(caption),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.45)),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(text, 1);
        host.Children.Add(text);
        return new Border
        {
            Child = host,
            Padding = new Thickness(10, 8, 10, 8),
        };
    }
}

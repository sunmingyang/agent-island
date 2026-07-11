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

        // Scripted verification: render the active tab's full content (past the
        // viewport) to a PNG — immune to the window occlusion a screen grab hits.
        var png = Environment.GetEnvironmentVariable("AGENTISLAND_DEBUG_SETTINGS_PNG");
        if (!string.IsNullOrEmpty(png)) window.SaveSnapshot(png);
    }

    /// Renders the current tab's content column at full height onto the panel
    /// background, so a verification screenshot shows every row even when the
    /// window is behind something else.
    public void SaveSnapshot(string path)
    {
        var settle = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(650),
        };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try
            {
                if (_scroll.Content is not FrameworkElement content) return;
                var w = (int)Math.Ceiling(content.ActualWidth);
                var h = (int)Math.Ceiling(content.ActualHeight);
                if (w <= 0 || h <= 0) return;
                var visual = new System.Windows.Media.DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(
                        IslandColors.Brush(IslandColors.AlarmBackground), null, new Rect(0, 0, w, h));
                    dc.DrawRectangle(
                        new System.Windows.Media.VisualBrush(content), null, new Rect(0, 0, w, h));
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(path);
                encoder.Save(stream);
            }
            catch
            {
            }
        };
        settle.Start();
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
        // Pixel-snapped glyphs: the 10-13px settings copy is blurry in WPF's
        // default Ideal mode.
        System.Windows.Media.TextOptions.SetTextFormattingMode(
            this, System.Windows.Media.TextFormattingMode.Display);

        // No system title bar: the brand header doubles as the drag strip
        // and the caption buttons live inside the page (top-right), matching
        // the macOS integrated-titlebar look.
        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 58,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        var root = new DockPanel();
        var shell = new Grid();
        shell.Children.Add(root);
        shell.Children.Add(CaptionButtons.Build(this));
        Content = shell;

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
                Text = "v" + (typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"),
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
                Tab.Triggers => L10n.Tr("Auto-Trigger"),
                Tab.Status => L10n.Tr("Status"),
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
            "Refresh interval", null, refresh));


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
            // Recreate this window outright: patching just the title, tab
            // bar, and rows left the brand header and footer in whatever
            // language the window was BORN in (the "inverted slogan" bug).
            // Close() raises Closed synchronously, clearing the singleton,
            // so Open() builds a fresh window on the persisted tab.
            Close();
            Open();
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
            null,
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
        check.Clicked += () => _ = Update.UpdateChecker.Shared.CheckAsync(userInitiated: true);
        stack.Children.Add(new SettingsRowControl(
            "Check now", null, check));

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
            Text = L10n.Tr("click to cycle"),
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
            null,
            costPage));
        RefreshCostPicker();
        stack.Children.Add(costPickerHost);

        var quotaMode = Model.QuotaDisplayModeStore.Shared;
        var quotaSeg = new Segmented(
            new[] { Localization.L10n.Tr("Used"), Localization.L10n.Tr("Remaining") },
            quotaMode.ShowsRemaining ? 1 : 0);
        quotaSeg.SelectionChanged += index => quotaMode.ShowsRemaining = index == 1;
        stack.Children.Add(new SettingsRowControl(
            "Quota shows",
            null,
            quotaSeg));

        // 顶部条.
        stack.Children.Add(SectionLabel("Top bar"));
        var alwaysShow = new CobaltToggle(AlwaysShowUsageStore.Shared.Enabled);
        alwaysShow.Toggled += enabled => AlwaysShowUsageStore.Shared.Enabled = enabled;
        stack.Children.Add(new SettingsRowControl(
            "Always show usage in top bar",
            null,
            alwaysShow));

        // 屏幕. (The macOS bar-style choice — Compact vs Notched Mac — is
        // meaningless on Windows, where no display has a notch; the bar is
        // always the wide layout.)
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
            choice == "auto"
                ? L10n.TrFormat("Auto — showing on {0}.", L10n.Tr("the primary display"))
                : L10n.Tr("Pinned to a specific display. Falls back to Auto if unplugged."),
            display));

        // 位置 — no notch reserves the top-center on Windows, so placement is
        // a user choice: the Mac-style top bar or a draggable floating widget.
        stack.Children.Add(SectionLabel("Position"));
        var position = IslandPositionStore.Shared;

        var placements = new[] { IslandPlacement.TopBar, IslandPlacement.Floating };
        var placementBox = new ComboBox { Width = 180, VerticalAlignment = VerticalAlignment.Center };
        foreach (var mode in placements) placementBox.Items.Add(PlacementLabel(mode));
        placementBox.SelectedIndex = Math.Max(0, Array.IndexOf(placements, position.Placement));
        placementBox.SelectionChanged += (_, _) =>
        {
            if (placementBox.SelectedIndex >= 0)
            {
                position.Placement = placements[placementBox.SelectedIndex];
            }
        };
        stack.Children.Add(new SettingsRowControl(
            "Island position",
            "A bar at the top of the screen, or a floating widget you drag anywhere.",
            placementBox));

        var soloCenter = new CobaltToggle(SoloCenterStore.Shared.Enabled);
        soloCenter.Toggled += value => SoloCenterStore.Shared.Enabled = value;
        stack.Children.Add(new SettingsRowControl(
            "Center the island when only one provider is on",
            "Collapse the hidden side and pull the visible logo to the middle, instead of keeping the symmetric layout.",
            soloCenter));

        return stack;
    }

    private static string PlacementLabel(IslandPlacement mode) => mode switch
    {
        IslandPlacement.TopBar => L10n.Tr("Top bar"),
        IslandPlacement.Floating => L10n.Tr("Floating window"),
        _ => mode.ToString(),
    };

    // MARK: - Providers

    private UIElement BuildProviders()
    {
        var stack = TabStack();
        stack.Children.Add(SectionLabel("Providers"));

        stack.Children.Add(ProviderRow(TriggerTool.Claude));
        var claudeJump = Model.ClaudeJumpPreferenceStore.Shared;
        var claudeJumpSeg = new Segmented(
            new[] { L10n.Tr("Desktop app"), L10n.Tr("CLI resume") },
            claudeJump.PrefersCli ? 1 : 0);
        claudeJumpSeg.SelectionChanged += index => claudeJump.PrefersCli = index == 1;
        stack.Children.Add(new SettingsRowControl(
            "Open threads via",
            null,
            claudeJumpSeg));

        stack.Children.Add(ProviderRow(TriggerTool.Codex));
        var codexJump = Model.CodexJumpPreferenceStore.Shared;
        var codexJumpSeg = new Segmented(
            new[] { L10n.Tr("Desktop app"), L10n.Tr("CLI resume") },
            codexJump.PrefersCli ? 1 : 0);
        codexJumpSeg.SelectionChanged += index => codexJump.PrefersCli = index == 1;
        stack.Children.Add(new SettingsRowControl(
            "Open threads via",
            null,
            codexJumpSeg));

        stack.Children.Add(SectionLabel("TOKEN"));
        var mode = new Segmented(
            new[] { L10n.Tr("All tokens"), L10n.Tr("Input + output") },
            TokenCountModeStore.Shared.Mode == TokenCountMode.All ? 0 : 1);
        mode.SelectionChanged += index =>
            TokenCountModeStore.Shared.Mode = index == 0 ? TokenCountMode.All : TokenCountMode.Billable;
        stack.Children.Add(new SettingsRowControl(
            "Token counting",
            TokenCountModeStore.Shared.Mode == TokenCountMode.All
                ? "Input, output, and cache."
                : "Input and output only.",
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
        // Always offered: the click spawns the login terminal directly, and
        // the branded dialog (with Retry) covers a genuinely missing CLI —
        // hiding the button just strands people mid CLI update.
        var reauth = new PillButtonControl(L10n.Tr("Re-authenticate"));
        reauth.Margin = new Thickness(0, 0, 8, 0);
        reauth.Clicked += () => ReauthFlow.Run(tool);
        trailing.Children.Add(reauth);
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
        stack.Children.Add(SectionLabel("Auto-Trigger"));
        stack.Children.Add(new TextBlock
        {
            Text = L10n.Tr("When your AI limit resets, auto-send a message so a session keeps running."),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            Foreground = IslandColors.Brush(IslandColors.White(0.6)),
            Margin = new Thickness(10, 0, 10, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(SectionLabel("Safety"));
        var kill = new CobaltToggle(TriggerSafetyStore.Shared.ExecutionEnabled);
        kill.Toggled += enabled => TriggerSafetyStore.Shared.ExecutionEnabled = enabled;
        stack.Children.Add(new SettingsRowControl(
            "Auto-resume kill switch",
            "When off, Agent Island will never spawn Claude or Codex resume commands.",
            kill));

        var records = new PillButtonControl(L10n.Tr("Open"));
        records.Clicked += () => TriggerEngine.Shared.OpenLogsDirectory();
        stack.Children.Add(new SettingsRowControl(
            "Records",
            "Open the folder with blocked and executed auto-resume records.",
            records));

        stack.Children.Add(new TextBlock
        {
            Text = TriggerStore.Shared.Triggers.Count == 0
                ? L10n.Tr("No triggers yet — add one below.")
                : L10n.TrFormat("{0} rule(s) — manage them on the island's Triggers page.", TriggerStore.Shared.Triggers.Count),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            Foreground = IslandColors.Brush(IslandColors.White(0.45)),
            Margin = new Thickness(10, 12, 10, 6),
        });

        stack.Children.Add(SectionLabel("New trigger"));
        stack.Children.Add(BuildNewRuleForm());

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

    /// The inline rule-creation form from the macOS Triggers tab: provider
    /// segmented control, session picker with refresh, message field, timing
    /// segmented control, live reset caption, and the blue create button.
    private UIElement BuildNewRuleForm()
    {
        var tool = Core.TriggerTool.Claude;
        var sessions = new List<Core.ScannedSession>();

        var grid = new Grid { Margin = new Thickness(12, 10, 12, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Label(string key, int row)
        {
            var text = new TextBlock
            {
                Text = L10n.Tr(key),
                FontFamily = IslandFonts.Ui,
                FontSize = 12,
                Foreground = IslandColors.Brush(IslandColors.White(0.7)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 12),
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);
        }

        void Control(UIElement element, int row)
        {
            var host = new ContentControl
            {
                Content = element,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            };
            Grid.SetRow(host, row);
            Grid.SetColumn(host, 1);
            grid.Children.Add(host);
        }

        var sessionBox = new ComboBox { Width = 230, VerticalAlignment = VerticalAlignment.Center };
        var resetCaption = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.42)),
            Margin = new Thickness(0, 0, 0, 12),
        };

        void ReloadSessions()
        {
            // Scan tail-reads every transcript; off the UI thread so the tab
            // build and each provider toggle don't freeze the window. Snapshot
            // the requested tool so a stale scan can't clobber a newer one.
            var requestedTool = tool;
            sessionBox.Items.Clear();
            sessionBox.Items.Add(Localization.L10n.Tr("Loading…"));
            sessionBox.SelectedIndex = 0;
            System.Threading.Tasks.Task.Run(
                    () => Core.SessionScanner
                        .Scan(DateTimeOffset.UtcNow, new Dictionary<string, DateTimeOffset>())
                        .Where(s => s.Tool == requestedTool)
                        .Take(20)
                        .ToList())
                .ContinueWith(task =>
                {
                    var scanned = task.Result;
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (tool != requestedTool) return;
                        sessions = scanned;
                        sessionBox.Items.Clear();
                        foreach (var session in sessions) sessionBox.Items.Add(session.Label);
                        if (sessionBox.Items.Count > 0) sessionBox.SelectedIndex = 0;
                    });
                }, System.Threading.Tasks.TaskScheduler.Default);
        }

        void UpdateResetCaption()
        {
            var resetAt = tool == Core.TriggerTool.Claude
                ? UsageStore.Shared.Claude.FiveHour.ResetAt
                : UsageStore.Shared.Codex.FiveHour.ResetAt;
            resetCaption.Text = resetAt is { } reset && reset > DateTimeOffset.Now
                ? L10n.TrFormat("{0} resets {1}.", tool.Display(),
                    Core.Formatting.LongCountdown(reset - DateTimeOffset.Now, L10n.IsChinese))
                : L10n.TrFormat("{0} reset time unknown.", tool.Display());
        }

        Label("Tool", 0);
        var service = new Segmented(new[] { "Claude", "Codex" }, 0);
        service.SelectionChanged += index =>
        {
            tool = index == 0 ? Core.TriggerTool.Claude : Core.TriggerTool.Codex;
            ReloadSessions();
            UpdateResetCaption();
        };
        Control(service, 0);

        Label("Thread", 1);
        var sessionRow = new StackPanel { Orientation = Orientation.Horizontal };
        sessionRow.Children.Add(sessionBox);
        var refresh = new PillButtonControl("↻") { Margin = new Thickness(8, 0, 0, 0) };
        refresh.Clicked += ReloadSessions;
        sessionRow.Children.Add(refresh);
        Control(sessionRow, 1);

        Label("Message", 2);
        var message = new TextBox
        {
            Width = 230,
            Text = L10n.Tr("Continue"),
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = IslandColors.Brush(IslandColors.White(0.05)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.10)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(8, 5, 8, 5),
            CaretBrush = Brushes.White,
        };
        Control(message, 2);

        Label("When", 3);
        var timingRow = new StackPanel { Orientation = Orientation.Horizontal };
        var mode = Trigger.TriggerMode.AfterReset;
        var hoursBox = new TextBox
        {
            Width = 44,
            Text = "5",
            FontFamily = IslandFonts.Mono,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = IslandColors.Brush(IslandColors.White(0.05)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.10)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(6, 5, 6, 5),
            TextAlignment = TextAlignment.Center,
            CaretBrush = Brushes.White,
            Margin = new Thickness(8, 0, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        var timing = new Segmented(
            new[] { L10n.Tr("After reset"), L10n.Tr("Every Nh") }, 0);
        timing.SelectionChanged += index =>
        {
            mode = index == 0 ? Trigger.TriggerMode.AfterReset : Trigger.TriggerMode.EveryHours;
            hoursBox.Visibility = mode == Trigger.TriggerMode.EveryHours
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
        timingRow.Children.Add(timing);
        timingRow.Children.Add(hoursBox);
        Control(timingRow, 3);

        Grid.SetRow(resetCaption, 4);
        Grid.SetColumn(resetCaption, 1);
        grid.Children.Add(resetCaption);

        var create = new Border
        {
            Child = new TextBlock
            {
                Text = L10n.Tr("Add a trigger"),
                FontFamily = IslandFonts.Ui,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            },
            Background = IslandColors.Brush(System.Windows.Media.Color.FromRgb(0x2E, 0x7C, 0xF6)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        create.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            if (sessionBox.SelectedIndex < 0 || sessionBox.SelectedIndex >= sessions.Count) return;
            var chosen = sessions[sessionBox.SelectedIndex];
            var hours = int.TryParse(hoursBox.Text, out var parsed) ? Math.Max(1, parsed) : 5;
            TriggerStore.Shared.Add(new Trigger.Trigger
            {
                Tool = chosen.Tool,
                SessionId = chosen.SessionId,
                Label = chosen.Label,
                Cwd = chosen.Cwd,
                Message = message.Text.Length > 0 ? message.Text : L10n.Tr("Continue"),
                Mode = mode,
                EveryHours = hours,
            });
            // Creating a rule here is an explicit act — trust its project so
            // the rule can actually fire; the Trusted projects list keeps it
            // revocable.
            if (chosen.Cwd.Length > 0) TriggerSafetyStore.Shared.SetAllowed(chosen.Cwd, true);
            Select(Tab.Triggers);
        };
        Grid.SetRow(create, 5);
        Grid.SetColumn(create, 1);
        grid.Children.Add(create);

        ReloadSessions();
        UpdateResetCaption();

        return new Border
        {
            Child = grid,
            CornerRadius = new CornerRadius(10),
            Background = IslandColors.Brush(IslandColors.White(0.03)),
            Margin = new Thickness(0, 0, 0, 8),
        };
    }

    // MARK: - Status

    private UIElement BuildStatus()
    {
        var stack = TabStack();
        stack.Children.Add(new TextBlock
        {
            Text = L10n.Tr("What the island's two logos are telling you."),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.5)),
            Margin = new Thickness(10, 0, 10, 12),
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(SectionLabel("Logo states"));
        stack.Children.Add(LegendRow(ActivityState.Working, "Running",
            "The logo rotates while a session is running."));
        stack.Children.Add(BellLegendRow("Your turn",
            "A thread finished — Agent Island opens an alarm window so you can reply."));
        stack.Children.Add(LegendRow(ActivityState.AuthRequired, "Needs attention",
            "Limits, login, network, or provider errors make the logo pulse red."));

        stack.Children.Add(SectionLabel("Reminders"));
        var enabled = new CobaltToggle(AgentReminderStore.Shared.Enabled);
        enabled.Toggled += value => AgentReminderStore.Shared.Enabled = value;
        stack.Children.Add(new SettingsRowControl(
            "Turn alarm",
            "Pop up a foreground alarm and system notification when a background run needs you.",
            enabled));

        var details = new CobaltToggle(AgentReminderStore.Shared.ShowSessionDetails);
        details.Toggled += value => AgentReminderStore.Shared.ShowSessionDetails = value;
        stack.Children.Add(new SettingsRowControl(
            "Show thread details",
            "Show session and project names in alarms and notifications.",
            details));

        var subagents = new CobaltToggle(SubagentAlarmStore.Shared.Enabled);
        subagents.Toggled += value => SubagentAlarmStore.Shared.Enabled = value;
        stack.Children.Add(new SettingsRowControl(
            "Subagent alarms",
            "Also alarm when orchestrated subagents finish. Off: only your own threads alarm.",
            subagents));

        // The exhaustion-alarm opt-out: some people only want auto-resume and
        // treat the "out of quota" popup as noise. Subtitle nil, matching mac.
        var quotaAlarm = new CobaltToggle(Model.QuotaAlarmStore.Shared.Enabled);
        quotaAlarm.Toggled += value => Model.QuotaAlarmStore.Shared.Enabled = value;
        stack.Children.Add(new SettingsRowControl(
            "Out-of-quota alarm",
            null,
            quotaAlarm));

        var soundHost = new StackPanel();
        var sound = new CobaltToggle(AgentReminderStore.Shared.SoundEnabled);
        stack.Children.Add(new SettingsRowControl(
            "Alarm sound",
            "Choose a built-in sound or use your own file.",
            sound));

        BuildSoundControls(soundHost);
        soundHost.Visibility = AgentReminderStore.Shared.SoundEnabled ? Visibility.Visible : Visibility.Collapsed;
        sound.Toggled += value =>
        {
            AgentReminderStore.Shared.SoundEnabled = value;
            soundHost.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        };
        stack.Children.Add(soundHost);

        // Demo buttons — force a state on the island. Visible in demo/debug
        // launches, exactly like macOS.
        if (AppEnvironment.IsDemo || AppEnvironment.IsDebug)
        {
            stack.Children.Add(SectionLabel("Demo — force a state on the island"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 10, 8) };
            row.Children.Add(DemoButton("Working", ActivityState.Working));
            row.Children.Add(DemoButton("Your turn", ActivityState.NeedsYou));
            row.Children.Add(DemoButton("Auth", ActivityState.AuthRequired));
            row.Children.Add(DemoButton("Rate", ActivityState.RateLimited));
            row.Children.Add(DemoButton("Live", null));
            stack.Children.Add(row);
        }

        return stack;
    }

    private static UIElement DemoButton(string label, ActivityState? state)
    {
        var button = new PillButtonControl(L10n.Tr(label)) { Margin = new Thickness(0, 0, 8, 0) };
        button.Clicked += () => ActivityMonitor.Shared.Demo(state);
        return button;
    }

    /// Expandable sound list with the selected checkmark and the custom-file
    /// row, plus the volume slider — the macOS SoundPicker.
    private void BuildSoundControls(StackPanel host)
    {
        var list = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(10, 0, 10, 8) };

        var headerLabel = new TextBlock
        {
            Text = CurrentSoundLabel(),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.58)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chevron = new TextBlock
        {
            Text = "⌄",
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.42)),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var soundTitle = new TextBlock
        {
            Text = L10n.Tr("Sound"),
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.92)),
        };
        Grid.SetColumn(soundTitle, 0);
        headerRow.Children.Add(soundTitle);
        var headerRight = new StackPanel { Orientation = Orientation.Horizontal };
        headerRight.Children.Add(headerLabel);
        headerRight.Children.Add(chevron);
        Grid.SetColumn(headerRight, 1);
        headerRow.Children.Add(headerRight);
        var header = new Border
        {
            Child = headerRow,
            CornerRadius = new CornerRadius(7),
            Background = IslandColors.Brush(IslandColors.White(0.015)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(10, 0, 10, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        header.MouseLeftButtonUp += (_, args) =>
        {
            list.Visibility = list.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            chevron.Text = list.Visibility == Visibility.Visible ? "⌃" : "⌄";
            args.Handled = true;
        };
        host.Children.Add(header);

        void RebuildList()
        {
            list.Children.Clear();
            foreach (var preset in AgentReminderStore.SoundPresets)
            {
                list.Children.Add(SoundChoiceRow(preset, isCustom: false, RebuildList, headerLabel));
            }
            list.Children.Add(SoundChoiceRow(
                AgentReminderStore.CustomSoundChoice, isCustom: true, RebuildList, headerLabel));
        }
        RebuildList();
        host.Children.Add(list);

        var volume = new Slider
        {
            Width = 120,
            Minimum = 0,
            Maximum = 1,
            Value = AgentReminderStore.Shared.Volume,
            VerticalAlignment = VerticalAlignment.Center,
        };
        volume.ValueChanged += (_, _) => AgentReminderStore.Shared.Volume = volume.Value;
        host.Children.Add(new SettingsRowControl(
            "Volume", null, volume));
    }

    /// The "your reply is up" legend uses the bell mark, matching the macOS
    /// StatePreviewLogo for needsYou.
    private UIElement BellLegendRow(string name, string caption)
    {
        var bellHost = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = IslandColors.Brush(IslandColors.White(0.05)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "",   // Segoe Fluent Ringer bell
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = IslandColors.Brush(IslandColors.Claude),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(bellHost, 0);
        host.Children.Add(bellHost);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
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
        return new Border { Child = host, Padding = new Thickness(10, 8, 10, 8) };
    }

    private static string CurrentSoundLabel()
    {
        var store = AgentReminderStore.Shared;
        if (store.SoundChoice == AgentReminderStore.CustomSoundChoice)
        {
            return store.CustomSoundPath.Length > 0
                ? System.IO.Path.GetFileName(store.CustomSoundPath)
                : L10n.Tr("Custom file");
        }
        return AgentReminderStore.PresetLabel(store.SoundChoice);
    }

    private UIElement SoundChoiceRow(string choice, bool isCustom, Action rebuild, TextBlock headerLabel)
    {
        var store = AgentReminderStore.Shared;
        var isSelected = store.SoundChoice == choice;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = isCustom ? L10n.Tr("Custom file") : AgentReminderStore.PresetLabel(choice),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(isSelected ? 0.95 : 0.72)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);
        if (isCustom)
        {
            var action = new TextBlock
            {
                Text = store.CustomSoundPath.Length == 0 ? L10n.Tr("Choose") : L10n.Tr("Change"),
                FontFamily = IslandFonts.Ui,
                FontSize = 10,
                Foreground = IslandColors.Brush(IslandColors.White(0.55)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(action, 1);
            row.Children.Add(action);
        }
        if (isSelected)
        {
            var check = new TextBlock
            {
                Text = "✓",
                FontFamily = IslandFonts.Ui,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = IslandColors.Brush(IslandColors.Cobalt),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(check, 2);
            row.Children.Add(check);
        }

        var host = new Border
        {
            Child = row,
            Height = 34,
            Padding = new Thickness(12, 0, 12, 0),
            Background = isSelected ? IslandColors.Brush(IslandColors.White(0.055)) : Brushes.Transparent,
            BorderBrush = IslandColors.Brush(IslandColors.White(0.045)),
            BorderThickness = new Thickness(0, 0, 0, 0.5),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        host.MouseLeftButtonUp += (_, args) =>
        {
            if (isCustom)
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Audio|*.wav;*.mp3;*.wma;*.m4a|All files|*.*",
                };
                if (dialog.ShowDialog(this) == true)
                {
                    store.CustomSoundPath = dialog.FileName;
                    store.SoundChoice = AgentReminderStore.CustomSoundChoice;
                }
            }
            else
            {
                store.SoundChoice = choice;
                PreviewSound();
            }
            headerLabel.Text = CurrentSoundLabel();
            rebuild();
            args.Handled = true;
        };
        return host;
    }

    // Held in a field: a local MediaPlayer can be collected mid-playback,
    // making the preview intermittently silent or clipped.
    private static MediaPlayer? _previewPlayer;

    private static void PreviewSound()
    {
        try
        {
            if (AgentReminderStore.Shared.ResolveSoundFile() is not { } file) return;
            _previewPlayer ??= new MediaPlayer();
            _previewPlayer.Stop();
            _previewPlayer.Volume = AgentReminderStore.Shared.Volume;
            _previewPlayer.Open(new Uri(file));
            _previewPlayer.Play();
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

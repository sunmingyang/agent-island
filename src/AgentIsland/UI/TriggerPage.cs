using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentIsland.Core;
using AgentIsland.Trigger;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;
using AgentIsland.Usage;

namespace AgentIsland.UI;

/// The auto-resume page: reset countdowns up top, the rules list below.
/// Each rule shows its message + mode, a run-now action, an enable toggle,
/// and a trust badge when its project isn't on the allow list yet.
public sealed class TriggerPage : Border
{
    private readonly TextBlock _countdowns;
    private readonly StackPanel _list;
    private readonly TextBlock _empty;

    public TriggerPage()
    {
        Padding = new Thickness(22, 10, 22, 4);
        var root = new Grid();
        Child = root;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _countdowns = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_countdowns, 0);
        header.Children.Add(_countdowns);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(SmallButton("+ " + Localization.L10n.Tr("Add rule"), OpenPicker));
        var manage = SmallButton(Localization.L10n.Tr("Manage"), null);
        manage.Click += (_, _) => OpenManageMenu(manage);
        manage.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(manage);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _list = new StackPanel { Orientation = Orientation.Vertical };
        scroll.Content = _list;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        _empty = new TextBlock
        {
            Text = Localization.L10n.Tr("No rules yet. Add one to auto-resume a session after the quota resets."),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.35)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 420,
        };
        Grid.SetRow(_empty, 1);
        root.Children.Add(_empty);

        TriggerStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Rebuild);
        TriggerSafetyStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(Rebuild);
        UsageStore.Shared.PropertyChanged += (_, _) => Dispatcher.BeginInvoke(UpdateCountdowns);
        Rebuild();
        UpdateCountdowns();
    }

    private void UpdateCountdowns()
    {
        _countdowns.Inlines.Clear();
        AppendCountdown(IslandColors.Claude, "Claude", UsageStore.Shared.Claude.FiveHour.ResetAt);
        _countdowns.Inlines.Add(new System.Windows.Documents.Run("    "));
        AppendCountdown(IslandColors.Codex, "Codex", UsageStore.Shared.Codex.FiveHour.ResetAt);
    }

    private void AppendCountdown(Color tint, string name, DateTimeOffset? resetAt)
    {
        _countdowns.Inlines.Add(new System.Windows.Documents.Run("●  ")
        {
            Foreground = IslandColors.Brush(tint),
            FontSize = 9,
        });
        _countdowns.Inlines.Add(new System.Windows.Documents.Run(name + "  ")
        {
            Foreground = Brushes.White,
        });
        var caption = resetAt is { } reset && reset > DateTimeOffset.Now
            ? Localization.L10n.TrFormat("resets in {0}", Core.Formatting.CompactDuration(reset - DateTimeOffset.Now))
            : "—";
        _countdowns.Inlines.Add(new System.Windows.Documents.Run(caption)
        {
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
        });
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var triggers = TriggerStore.Shared.Triggers;
        _empty.Visibility = triggers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var trigger in triggers)
        {
            _list.Children.Add(MakeRow(trigger));
        }
    }

    private Border MakeRow(Trigger.Trigger trigger)
    {
        var row = new Grid { Margin = new Thickness(10, 7, 10, 7) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = IslandColors.Brush(IslandColors.For(trigger.Tool)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(dot, 0);
        row.Children.Add(dot);

        var text = new StackPanel();
        var title = new TextBlock
        {
            Text = trigger.Label,
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        text.Children.Add(title);
        var modeCaption = trigger.Mode == TriggerMode.AfterReset
            ? Localization.L10n.Tr("after quota reset")
            : Localization.L10n.TrFormat("every {0}h", trigger.EveryHours);
        var subtitle = new TextBlock
        {
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.45)),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        subtitle.Inlines.Add(new System.Windows.Documents.Run($"「{trigger.Message}」 · {modeCaption}"));
        if (!TriggerSafetyStore.Shared.IsAllowed(trigger.Cwd))
        {
            var trust = new System.Windows.Documents.Run("  " + Localization.L10n.Tr("untrusted — click to trust"))
            {
                Foreground = IslandColors.Brush(IslandColors.AlertAmber),
            };
            subtitle.Inlines.Add(trust);
            subtitle.Cursor = System.Windows.Input.Cursors.Hand;
            subtitle.MouseLeftButtonUp += (_, args) =>
            {
                TriggerSafetyStore.Shared.SetAllowed(trigger.Cwd, true);
                args.Handled = true;
            };
        }
        text.Children.Add(subtitle);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var run = SmallButton("▶", null);
        run.ToolTip = TriggerEngine.Shared.Preview(trigger);
        run.Click += (_, _) => TriggerEngine.Shared.Fire(trigger);
        controls.Children.Add(run);
        var toggle = new ToggleSwitch(trigger.Enabled)
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += enabled => TriggerStore.Shared.SetEnabled(trigger.Id, enabled);
        controls.Children.Add(toggle);
        var remove = SmallButton("✕", null);
        remove.Margin = new Thickness(10, 0, 0, 0);
        remove.Click += (_, _) => TriggerStore.Shared.Remove(trigger.Id);
        controls.Children.Add(remove);
        Grid.SetColumn(controls, 2);
        row.Children.Add(controls);

        return new Border
        {
            Background = IslandColors.Brush(IslandColors.White(0.04)),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 0, 6),
            Child = row,
        };
    }

    private static Button SmallButton(string text, Action? onClick)
    {
        var button = new Button
        {
            Content = text,
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.8)),
            Background = IslandColors.Brush(IslandColors.White(0.06)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(9, 4, 9, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        if (onClick is not null) button.Click += (_, _) => onClick();
        return button;
    }

    private void OpenManageMenu(Button anchor)
    {
        var menu = new ContextMenu();
        var kill = new MenuItem
        {
            Header = Localization.L10n.Tr("Auto-resume enabled (kill switch)"),
            IsCheckable = true,
            IsChecked = TriggerSafetyStore.Shared.ExecutionEnabled,
        };
        kill.Click += (_, _) => TriggerSafetyStore.Shared.ExecutionEnabled = kill.IsChecked;
        menu.Items.Add(kill);
        var logs = new MenuItem { Header = Localization.L10n.Tr("Open run records") };
        logs.Click += (_, _) => TriggerEngine.Shared.OpenLogsDirectory();
        menu.Items.Add(logs);
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    private void OpenPicker()
    {
        var picker = new SessionPickerWindow();
        picker.Owner = Window.GetWindow(this);
        picker.ShowDialog();
    }
}

/// Minimal on/off pill matching the island's dark chrome.
public sealed class ToggleSwitch : Border
{
    private readonly Ellipse _knob;
    private bool _on;

    public event Action<bool>? Toggled;

    public ToggleSwitch(bool on)
    {
        _on = on;
        Width = 34;
        Height = 18;
        CornerRadius = new CornerRadius(9);
        Cursor = System.Windows.Input.Cursors.Hand;
        _knob = new Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _knob;
        MouseLeftButtonUp += (_, args) =>
        {
            _on = !_on;
            Render();
            Toggled?.Invoke(_on);
            args.Handled = true;
        };
        Render();
    }

    private void Render()
    {
        Background = _on
            ? IslandColors.Brush(Color.FromRgb(0x2E, 0x7C, 0xF6))
            : IslandColors.Brush(IslandColors.White(0.15));
        _knob.Margin = _on ? new Thickness(18, 0, 2, 0) : new Thickness(2, 0, 18, 0);
    }
}

/// Session picker for new rules: recent resumable sessions from both
/// providers, newest first. Selecting one creates an after-reset rule with
/// the default message and (optionally) trusts its project.
public sealed class SessionPickerWindow : Window
{
    public SessionPickerWindow()
    {
        Title = Localization.L10n.Tr("Add rule");
        Width = 460;
        Height = 400;
        WindowStyle = WindowStyle.ToolWindow;
        Background = IslandColors.Brush(IslandColors.AlarmBackground);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var trust = new CheckBox
        {
            Content = new TextBlock
            {
                Text = Localization.L10n.Tr("Trust this project for auto-resume"),
                Foreground = IslandColors.Brush(IslandColors.White(0.75)),
                FontFamily = IslandFonts.Ui,
                FontSize = 11,
            },
            IsChecked = true,
            Margin = new Thickness(2, 8, 0, 0),
        };
        DockPanel.SetDock(trust, Dock.Bottom);
        root.Children.Add(trust);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var list = new StackPanel();
        scroll.Content = list;
        root.Children.Add(scroll);

        var sessions = SessionScanner.Scan(DateTimeOffset.UtcNow, new Dictionary<string, DateTimeOffset>());
        foreach (var session in sessions.Take(24))
        {
            var row = new Grid { Margin = new Thickness(8, 7, 8, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = IslandColors.Brush(IslandColors.For(session.Tool)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(dot, 0);
            row.Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = session.Label,
                Foreground = Brushes.White,
                FontFamily = IslandFonts.Ui,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = session.Cwd,
                Foreground = IslandColors.Brush(IslandColors.White(0.4)),
                FontFamily = IslandFonts.Mono,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var host = new Border
            {
                Background = IslandColors.Brush(IslandColors.White(0.04)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = row,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var captured = session;
            host.MouseLeftButtonUp += (_, args) =>
            {
                TriggerStore.Shared.Add(new Trigger.Trigger
                {
                    Tool = captured.Tool,
                    SessionId = captured.SessionId,
                    Label = captured.Label,
                    Cwd = captured.Cwd,
                });
                if (trust.IsChecked == true && captured.Cwd.Length > 0)
                {
                    TriggerSafetyStore.Shared.SetAllowed(captured.Cwd, true);
                }
                Close();
                args.Handled = true;
            };
            list.Children.Add(host);
        }
    }
}

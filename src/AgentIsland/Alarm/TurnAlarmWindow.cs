using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using AgentIsland.Core;
using AgentIsland.UI;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.Alarm;

/// The foreground "It's your turn" alarm. Dark 520x520 panel, glow-pulsing
/// provider mark, headline, session line, metadata row, and the two
/// actions. Topmost so it surfaces over full-screen work; Esc dismisses.
public sealed class TurnAlarmWindow : Window
{
    public TriggerTool Provider { get; }
    public ActivityMonitor.ActiveThread? Thread { get; }
    public string DeliveryKey { get; }

    private readonly TurnAlarmSoundLooper _sound = new();
    public event Action<TurnAlarmWindow>? Dismissed;

    public TurnAlarmWindow(TriggerTool provider, ActivityMonitor.ActiveThread? thread, string deliveryKey)
    {
        Provider = provider;
        Thread = thread;
        DeliveryKey = deliveryKey;

        Width = 520;
        Height = 520;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = Localization.L10n.Tr("It's your turn");

        var tint = IslandColors.For(provider);
        Content = BuildContent(tint);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Acknowledge();
        };
        MouseLeftButtonDown += (_, _) => DragMoveSafe();
        Loaded += (_, _) => _sound.Start();
        Closed += (_, _) => _sound.Stop();
    }

    private void DragMoveSafe()
    {
        try { DragMove(); } catch { }
    }

    private UIElement BuildContent(Color tint)
    {
        var root = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = IslandColors.Brush(IslandColors.AlarmBackground),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.07)),
            BorderThickness = new Thickness(1),
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40, 24, 40, 24),
        };
        root.Child = stack;

        // Glow-pulsing provider mark inside a faint ring.
        var mark = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("F1 " + (Provider == TriggerTool.Claude
                ? BrandGeometry.ClaudePath
                : BrandGeometry.OpenAiPath)),
            Fill = IslandColors.Brush(tint),
            Width = 96,
            Height = 96,
            Stretch = Stretch.Uniform,
            Effect = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 24,
                Color = tint,
                Opacity = 0.6,
            },
        };
        var ring = new Border
        {
            Width = 168,
            Height = 168,
            CornerRadius = new CornerRadius(84),
            BorderBrush = IslandColors.Brush(tint, 0.35),
            BorderThickness = new Thickness(1),
            Child = mark,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28),
        };
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(ring);
        PulseGlow((DropShadowEffect)mark.Effect);

        stack.Children.Add(new TextBlock
        {
            Text = Localization.L10n.Tr("It's your turn"),
            FontFamily = IslandFonts.Ui,
            FontSize = 36,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var sessionLabel = Thread?.Label is { Length: > 0 } label ? label : Provider.Display();
        stack.Children.Add(new TextBlock
        {
            Text = Localization.L10n.TrFormat("{0} is waiting", sessionLabel),
            FontFamily = IslandFonts.Ui,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(tint),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420,
        });

        stack.Children.Add(new TextBlock
        {
            Text = Localization.L10n.Tr("The thread finished. Come back and reply."),
            FontFamily = IslandFonts.Ui,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.7)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });

        if (AgentReminderStore.Shared.ShowSessionDetails && Thread is { } thread)
        {
            stack.Children.Add(BuildMetadata(thread, tint));
        }

        var open = MakeButton(
            Localization.L10n.Tr("Open thread"),
            foreground: Colors.White,
            background: tint,
            bold: true);
        open.Margin = new Thickness(0, 28, 0, 0);
        open.Click += (_, _) =>
        {
            if (Thread is { } target) TurnAlarmNavigator.Open(Provider, target);
            Acknowledge();
        };
        stack.Children.Add(open);

        var gotIt = MakeButton(
            Localization.L10n.Tr("I know"),
            foreground: IslandColors.White(0.85),
            background: IslandColors.White(0.06),
            bold: false);
        gotIt.Margin = new Thickness(0, 10, 0, 0);
        gotIt.Click += (_, _) => Acknowledge();
        stack.Children.Add(gotIt);

        return root;
    }

    private Grid BuildMetadata(ActivityMonitor.ActiveThread thread, Color tint)
    {
        var grid = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddMeta(grid, 0, Localization.L10n.Tr("Alarm provider"), Provider.Display(), tint);
        AddMeta(grid, 1, Localization.L10n.Tr("Alarm thread"), thread.Label, null);
        var project = string.IsNullOrEmpty(thread.Cwd)
            ? "—"
            : System.IO.Path.GetFileName(thread.Cwd.TrimEnd('\\', '/'));
        AddMeta(grid, 2, Localization.L10n.Tr("Alarm project"), project, null);
        return grid;
    }

    private static void AddMeta(Grid grid, int column, string caption, string value, Color? dotColor)
    {
        var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        cell.Children.Add(new TextBlock
        {
            Text = caption,
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.4)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var valueRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        if (dotColor is { } dot)
        {
            valueRow.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = IslandColors.Brush(dot),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
        }
        valueRow.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = IslandFonts.Ui,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            MaxWidth = 140,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        cell.Children.Add(valueRow);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Button MakeButton(string text, Color foreground, Color background, bool bold)
    {
        var button = new Button
        {
            Content = text,
            FontFamily = IslandFonts.Ui,
            FontSize = 16,
            FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = IslandColors.Brush(foreground),
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
        };
        // Rounded template so the buttons match the macOS pill shape.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetValue(Border.BackgroundProperty, IslandColors.Brush(background));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        return button;
    }

    private static void PulseGlow(DropShadowEffect glow)
    {
        var pulse = new DoubleAnimation(16, 32, new Duration(TimeSpan.FromSeconds(1.1)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, pulse);
    }

    private void Acknowledge()
    {
        AgentReminderCenter.Shared.Acknowledge(Provider, Thread);
        DismissSilently();
    }

    /// Close without acknowledging — used when the turn already left
    /// needsYou (the user replied in the thread).
    public void DismissSilently()
    {
        Dismissed?.Invoke(this);
        Close();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// App-styled dialog in the turn-alarm design language — dark rounded card,
/// ringed glowing brand mark, headline, message, optional caption/value meta
/// rows (thread, project, …), and stacked buttons. Replaces every raw
/// Win32 MessageBox in the app.
public sealed class IslandDialog : Window
{
    private IslandDialog(
        string title,
        string message,
        Color tint,
        Geometry mark,
        IReadOnlyList<(string Caption, string Value)>? meta,
        string primaryLabel,
        Action? primaryAction,
        string? secondaryLabel)
    {
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = title;
        System.Windows.Media.TextOptions.SetTextFormattingMode(
            this, System.Windows.Media.TextFormattingMode.Display);

        var root = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = IslandColors.Brush(IslandColors.AlarmBackground),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.07)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(12),
            Effect = new DropShadowEffect
            {
                ShadowDepth = 4,
                Direction = 270,
                BlurRadius = 18,
                Color = Colors.Black,
                Opacity = 0.55,
            },
        };
        Content = root;

        var stack = new StackPanel { Margin = new Thickness(32, 26, 32, 24) };
        root.Child = stack;

        var glyph = new System.Windows.Shapes.Path
        {
            Data = mark,
            Fill = IslandColors.Brush(tint),
            Width = 40,
            Height = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 16,
                Color = tint,
                Opacity = 0.55,
            },
        };
        stack.Children.Add(new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(36),
            BorderBrush = IslandColors.Brush(tint, 0.35),
            BorderThickness = new Thickness(1),
            Child = glyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = IslandFonts.Ui,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontFamily = IslandFonts.Ui,
            FontSize = 12.5,
            Foreground = IslandColors.Brush(IslandColors.White(0.7)),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            LineHeight = 19,
        });

        if (meta is { Count: > 0 })
        {
            var grid = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            for (var i = 0; i < meta.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (var i = 0; i < meta.Count; i++)
            {
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                cell.Children.Add(new TextBlock
                {
                    Text = meta[i].Caption.ToUpperInvariant(),
                    FontFamily = IslandFonts.Ui,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = IslandColors.Brush(IslandColors.White(0.4)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                cell.Children.Add(new TextBlock
                {
                    Text = meta[i].Value,
                    FontFamily = IslandFonts.Ui,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium,
                    Foreground = IslandColors.Brush(IslandColors.White(0.85)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 110,
                });
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            stack.Children.Add(grid);
        }

        var primary = MakeButton(primaryLabel, Colors.White, tint, bold: true);
        primary.Margin = new Thickness(0, 22, 0, 0);
        primary.Click += (_, _) =>
        {
            Close();
            primaryAction?.Invoke();
        };
        stack.Children.Add(primary);

        if (secondaryLabel is not null)
        {
            var secondary = MakeButton(
                secondaryLabel, IslandColors.White(0.85), IslandColors.White(0.06), bold: false);
            secondary.Margin = new Thickness(0, 10, 0, 0);
            secondary.Click += (_, _) => Close();
            stack.Children.Add(secondary);
        }

        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close();
        };
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch { }
        };
    }

    /// Provider-tinted dialog (spark for Claude, knot for Codex).
    public static void Show(
        Core.TriggerTool tool,
        string title,
        string message,
        IReadOnlyList<(string Caption, string Value)>? meta = null,
        string? primaryLabel = null,
        Action? primaryAction = null,
        string? secondaryLabel = null)
    {
        var mark = Geometry.Parse("F1 " + (tool == Core.TriggerTool.Claude
            ? BrandGeometry.ClaudePath
            : BrandGeometry.OpenAiPath));
        Present(new IslandDialog(
            title, message, IslandColors.For(tool), mark, meta,
            primaryLabel ?? Localization.L10n.Tr("I know"), primaryAction, secondaryLabel));
    }

    /// App-branded dialog (Claude spark in cobalt) for provider-neutral
    /// messages like update checks.
    public static void ShowApp(
        string title,
        string message,
        string? primaryLabel = null,
        Action? primaryAction = null,
        string? secondaryLabel = null)
    {
        Present(new IslandDialog(
            title, message, IslandColors.Cobalt,
            Geometry.Parse("F1 " + BrandGeometry.ClaudePath), null,
            primaryLabel ?? Localization.L10n.Tr("I know"), primaryAction, secondaryLabel));
    }

    private static void Present(IslandDialog dialog)
    {
        dialog.Show();
        dialog.Activate();
    }

    private static Button MakeButton(string label, Color foreground, Color background, bool bold)
    {
        var button = new Button
        {
            Content = label,
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Medium,
            Foreground = IslandColors.Brush(foreground),
            Background = IslandColors.Brush(background),
            BorderThickness = new Thickness(0),
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        // Rounded pill template so the default WPF chrome (square, light)
        // never shows through.
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        factory.SetValue(Border.BackgroundProperty, IslandColors.Brush(background));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        return button;
    }
}

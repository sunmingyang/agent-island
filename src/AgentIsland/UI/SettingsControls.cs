using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// The on/off switch shared by every Settings row — cobalt-glow track when
/// on, dim white-on-dark when off, 30x17 with a 13pt dot, matching the
/// macOS SettingsToggle exactly.
public sealed class CobaltToggle : Border
{
    private readonly Ellipse _dot;
    private bool _isOn;

    public event Action<bool>? Toggled;

    public CobaltToggle(bool isOn)
    {
        _isOn = isOn;
        Width = 30;
        Height = 17;
        CornerRadius = new CornerRadius(8.5);
        BorderThickness = new Thickness(1);
        Cursor = System.Windows.Input.Cursors.Hand;
        _dot = new Ellipse
        {
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Child = _dot;
        MouseLeftButtonUp += (_, args) =>
        {
            _isOn = !_isOn;
            Render();
            Toggled?.Invoke(_isOn);
            args.Handled = true;
        };
        MouseEnter += (_, _) => BorderBrush = IslandColors.Brush(IslandColors.White(0.20));
        MouseLeave += (_, _) => BorderBrush = IslandColors.Brush(IslandColors.White(0.13));
        BorderBrush = IslandColors.Brush(IslandColors.White(0.13));
        Render();
    }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            _isOn = value;
            Render();
        }
    }

    private void Render()
    {
        Background = _isOn
            ? IslandColors.Brush(IslandColors.Cobalt, 0.32)
            : IslandColors.Brush(IslandColors.White(0.07));
        _dot.Fill = _isOn ? IslandColors.Brush(IslandColors.Cobalt) : IslandColors.Brush(IslandColors.White(0.5));
        _dot.Effect = _isOn
            ? new DropShadowEffect { ShadowDepth = 0, BlurRadius = 5, Color = IslandColors.Cobalt, Opacity = 0.85 }
            : null;
        _dot.HorizontalAlignment = _isOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _dot.Margin = new Thickness(2, 0, 2, 0);
    }
}

/// Pill-shaped segmented control (Refresh interval, Token counting, Mac
/// type pickers).
public sealed class Segmented : Border
{
    private readonly StackPanel _items = new() { Orientation = Orientation.Horizontal };
    private readonly List<Border> _cells = new();
    private int _selected;

    public event Action<int>? SelectionChanged;

    public Segmented(IReadOnlyList<string> labels, int selected)
    {
        _selected = selected;
        CornerRadius = new CornerRadius(7);
        Background = IslandColors.Brush(IslandColors.White(0.04));
        Padding = new Thickness(2);
        Child = _items;
        for (var i = 0; i < labels.Count; i++)
        {
            var index = i;
            var text = new TextBlock
            {
                Text = labels[i],
                FontFamily = IslandFonts.Mono,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
            };
            var cell = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 5, 10, 5),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            cell.MouseLeftButtonUp += (_, args) =>
            {
                Select(index);
                SelectionChanged?.Invoke(index);
                args.Handled = true;
            };
            _cells.Add(cell);
            _items.Children.Add(cell);
        }
        Select(selected);
    }

    public void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _cells.Count - 1);
        for (var i = 0; i < _cells.Count; i++)
        {
            var isOn = i == _selected;
            _cells[i].Background = isOn ? IslandColors.Brush(IslandColors.White(0.10)) : Brushes.Transparent;
            ((TextBlock)_cells[i].Child!).Foreground =
                IslandColors.Brush(IslandColors.White(isOn ? 0.95 : 0.55));
        }
    }
}

/// Plain pill action button ("Refresh", "Check").
public sealed class PillButtonControl : Border
{
    private readonly TextBlock _label;

    public event Action? Clicked;

    public PillButtonControl(string label)
    {
        _label = new TextBlock
        {
            Text = label,
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.9)),
        };
        Child = _label;
        CornerRadius = new CornerRadius(6);
        Background = IslandColors.Brush(IslandColors.White(0.10));
        BorderBrush = IslandColors.Brush(IslandColors.White(0.08));
        BorderThickness = new Thickness(0.5);
        Padding = new Thickness(12, 5, 12, 5);
        Cursor = System.Windows.Input.Cursors.Hand;
        VerticalAlignment = VerticalAlignment.Center;
        MouseLeftButtonUp += (_, args) =>
        {
            Clicked?.Invoke();
            args.Handled = true;
        };
    }

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }
}

/// Dotted-underline external link ("GitHub ↗").
public sealed class DottedLink : StackPanel
{
    public DottedLink(string title, string url)
    {
        Orientation = Orientation.Horizontal;
        Cursor = System.Windows.Input.Cursors.Hand;
        var text = new TextBlock
        {
            Text = Localization.L10n.Tr(title),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.55)),
            TextDecorations = null,
        };
        var arrow = new TextBlock
        {
            Text = " ↗",
            FontFamily = IslandFonts.Ui,
            FontSize = 10,
            Foreground = IslandColors.Brush(IslandColors.White(0.3)),
        };
        Children.Add(text);
        Children.Add(arrow);
        MouseEnter += (_, _) =>
        {
            text.Foreground = IslandColors.Brush(IslandColors.White(0.92));
            arrow.Foreground = IslandColors.Brush(IslandColors.White(0.6));
        };
        MouseLeave += (_, _) =>
        {
            text.Foreground = IslandColors.Brush(IslandColors.White(0.55));
            arrow.Foreground = IslandColors.Brush(IslandColors.White(0.3));
        };
        MouseLeftButtonUp += (_, args) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
            args.Handled = true;
        };
    }
}

/// A single Settings list row: title (+ optional brand dot and plan chip),
/// subtitle, trailing control. Hover lifts the background to a faint wash.
public sealed class SettingsRowControl : Border
{
    public SettingsRowControl(
        string title,
        string? subtitle,
        UIElement trailing,
        Color? dot = null,
        string? chip = null,
        bool monospaceTitle = false)
    {
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(10, 11, 10, 11);
        Background = Brushes.Transparent;
        MouseEnter += (_, _) => Background = IslandColors.Brush(IslandColors.White(0.030));
        MouseLeave += (_, _) => Background = Brushes.Transparent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Child = grid;

        var text = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot is { } dotColor)
        {
            titleRow.Children.Add(new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = IslandColors.Brush(dotColor),
                Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 4, Color = dotColor, Opacity = 0.7 },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
        }
        titleRow.Children.Add(new TextBlock
        {
            Text = monospaceTitle ? title : Localization.L10n.Tr(title),
            FontFamily = monospaceTitle ? IslandFonts.Mono : IslandFonts.Ui,
            FontSize = monospaceTitle ? 10 : 13,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.92)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 300,
        });
        if (!string.IsNullOrEmpty(chip))
        {
            titleRow.Children.Add(new Border
            {
                Child = new TextBlock
                {
                    Text = chip,
                    FontFamily = IslandFonts.Ui,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = IslandColors.Brush(IslandColors.White(0.6)),
                },
                CornerRadius = new CornerRadius(3),
                Background = IslandColors.Brush(IslandColors.White(0.06)),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        text.Children.Add(titleRow);
        if (!string.IsNullOrEmpty(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = Localization.L10n.Tr(subtitle!),
                FontFamily = IslandFonts.Ui,
                FontSize = 11,
                Foreground = IslandColors.Brush(IslandColors.White(0.55)),
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 380,
            });
        }
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var trailingHost = new ContentControl
        {
            Content = trailing,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        Grid.SetColumn(trailingHost, 1);
        grid.Children.Add(trailingHost);
    }
}

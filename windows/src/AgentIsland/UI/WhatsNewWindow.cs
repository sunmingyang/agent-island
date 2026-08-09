using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentIsland.Core;
using AgentIsland.Localization;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// The release-notes card — the Windows port of the macOS 2.1.2 What's New
/// spread: poster hero, one page per theme, page dots, and a Get-started
/// close. Fires once per version.
public static class WhatsNewGate
{
    private const string SeenKey = "AgentIsland.whatsNewSeenVersion";

    public static string CurrentVersion =>
        typeof(WhatsNewGate).Assembly.GetName().Version?.ToString(3) ?? "0";

    /// Always fires once per version — deliberately NOT user-configurable
    /// (owner call, 1.7.2: every user walks through the release card once).
    public static void MaybeShow()
    {
        if (AppEnvironment.IsDemo) return;
        if (Environment.GetEnvironmentVariable("AGENTISLAND_REPORT_SNAPSHOT") is not null) return;
        if (Environment.GetEnvironmentVariable("AGENTISLAND_MONTHLY_SNAPSHOT") is not null) return;
        if (Preferences.Get<string?>(SeenKey) == CurrentVersion) return;
        WhatsNewWindow.Open();
    }

    public static void MarkSeen() => Preferences.Set(SeenKey, CurrentVersion);
}

public sealed class WhatsNewWindow : Window
{
    private sealed record Page(string? ImageName, string Title, string Body, bool IsClosing = false);

    /// 2.1.2 pages — the macOS set with the two platform-specific pages
    /// speaking Windows: the terminal page describes the live-window jump
    /// this port does, and the tones page names the Windows alarm library.
    private static Page[] Pages => new[]
    {
        new Page("whatsnew-overview", "At a glance",
            "The fifth seat changes hands: Antigravity replaces Gemini — Google's gradient, a real weekly quota, resume to the exact conversation — and every alarm now lands back in the terminal you actually use"),
        new Page("whatsnew-antigravity", "Antigravity arrives",
            "Google retired Gemini Code Assist for individuals, so Antigravity takes the slot: live session state, weekly quota read from its own local service, and one click back to the exact conversation"),
        new Page("whatsnew-terminal", "Back to your terminal",
            "An alarm click lands in the session's live window — Windows Terminal, a console, or an IDE pane. A fresh terminal opens only when nothing is running"),
        new Page("whatsnew-tones", "Windows alarm tones",
            "Chimes, Xylophone, Chords — the alarm rings with Windows' own tones, played straight from the system's alarm library. Chimes is the new default"),
        new Page("whatsnew-cost", "Cost across all five",
            "Grok reports its own dollars, Cursor counts its tokens — cost and reports now cover every agent, read locally, and say so honestly where a provider publishes less"),
        new Page("whatsnew-start", "Get started",
            "Welcome back to Agent Island", IsClosing: true),
    };

    private static WhatsNewWindow? _open;

    private readonly Grid _pageHost = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal };
    private readonly Button _back;
    private readonly Button _next;
    private int _page;

    public static void Open()
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }
        _open = new WhatsNewWindow();
        _open.Closed += (_, _) =>
        {
            _open = null;
            WhatsNewGate.MarkSeen();
        };
        _open.Show();
        _open.Activate();
    }

    private WhatsNewWindow()
    {
        Title = "Agent Island";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _back = Pill(L10n.Tr("Back"), primary: false, (_, _) => Flip(_page - 1));
        _next = Pill(L10n.Tr("Next"), primary: true, (_, _) =>
        {
            if (_page >= Pages.Length - 1) Close();
            else Flip(_page + 1);
        });

        var column = new StackPanel { Width = 470 - 48 };
        column.Children.Add(Header());
        column.Children.Add(_pageHost);
        column.Children.Add(FooterRow());

        var card = new Border
        {
            CornerRadius = new CornerRadius(26),
            Background = IslandColors.Brush(Color.FromRgb(0x0E, 0x0F, 0x13)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.06)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24),
            Child = column,
        };
        Content = new Border { Padding = new Thickness(16), Child = card };
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 24, ShadowDepth = 8, Opacity = 0.35, Color = Colors.Black,
        };

        MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close();
        };

        Flip(0);
    }

    private UIElement Header()
    {
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 14) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        try
        {
            var mark = new Image
            {
                Source = new BitmapImage(new Uri(
                    "pack://application:,,,/Assets/agentisland_logo_small.png")),
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);
            brand.Children.Add(mark);
        }
        catch
        {
        }
        brand.Children.Add(new TextBlock
        {
            Text = string.Join(' ', "AGENT ISLAND".ToCharArray()),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.ExtraBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.88)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(brand, Dock.Left);
        row.Children.Add(brand);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = IslandColors.Brush(IslandColors.White(0.10)),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "v" + WhatsNewGate.CurrentVersion,
                FontFamily = IslandFonts.Ui,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = IslandColors.Brush(Colors.White),
            },
        };
        DockPanel.SetDock(chip, Dock.Right);
        row.Children.Add(chip);
        return row;
    }

    private void Flip(int target)
    {
        _page = Math.Clamp(target, 0, Pages.Length - 1);
        var page = Pages[_page];

        _pageHost.Children.Clear();
        var stack = new StackPanel();
        if (Poster(page.ImageName) is { } poster) stack.Children.Add(poster);
        stack.Children.Add(new TextBlock
        {
            Text = L10n.Tr(page.Title),
            FontFamily = IslandFonts.Ui,
            FontSize = 20,
            FontWeight = FontWeights.Black,
            Foreground = IslandColors.Brush(Colors.White),
            Margin = new Thickness(0, 16, 0, 8),
        });
        stack.Children.Add(new TextBlock
        {
            Text = L10n.Tr(page.Body),
            FontFamily = IslandFonts.Ui,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = IslandColors.Brush(IslandColors.White(0.62)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            MinHeight = 76,
        });
        _pageHost.Children.Add(stack);

        _dots.Children.Clear();
        for (var i = 0; i < Pages.Length; i++)
        {
            var active = i == _page;
            var index = i;
            var dot = new Border
            {
                Width = active ? 16 : 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = IslandColors.Brush(active ? Colors.White : IslandColors.White(0.16)),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
            };
            dot.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                Flip(index);
            };
            _dots.Children.Add(dot);
        }

        _back.Visibility = _page > 0 ? Visibility.Visible : Visibility.Collapsed;
        ((TextBlock)_next.Content).Text = Pages[_page].IsClosing
            ? L10n.Tr("Get started")
            : L10n.Tr("Next");
    }

    private static UIElement? Poster(string? imageName)
    {
        if (imageName is null) return null;
        try
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(
                    $"pack://application:,,,/Assets/{imageName}.png")),
                Stretch = Stretch.UniformToFill,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var frame = new Border
            {
                CornerRadius = new CornerRadius(14),
                Height = 226,
                Background = IslandColors.Brush(IslandColors.White(0.03)),
                BorderBrush = IslandColors.Brush(IslandColors.White(0.07)),
                BorderThickness = new Thickness(1),
                Child = image,
                ClipToBounds = true,
            };
            // Round-clip the bitmap to the frame.
            frame.Loaded += (_, _) => frame.Clip = new RectangleGeometry(
                new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 14, 14);
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private UIElement FooterRow()
    {
        var row = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 16, 0, 0) };
        _dots.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_dots, Dock.Left);
        row.Children.Add(_dots);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _back.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_back);
        buttons.Children.Add(_next);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        return row;
    }

    private static Button Pill(string label, bool primary, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                FontFamily = IslandFonts.Ui,
                FontSize = 12.5,
                FontWeight = primary ? FontWeights.ExtraBold : FontWeights.Bold,
                Foreground = primary
                    ? IslandColors.Brush(Color.FromRgb(0x17, 0x17, 0x17))
                    : IslandColors.Brush(IslandColors.White(0.55)),
            },
            Background = primary
                ? IslandColors.Brush(Colors.White)
                : IslandColors.Brush(IslandColors.White(0.05)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 7, 16, 7),
            Cursor = Cursors.Hand,
        };
        button.Resources.Add(typeof(Border), PillBorderStyle());
        button.Click += onClick;
        return button;
    }

    private static Style PillBorderStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(10)));
        return style;
    }
}

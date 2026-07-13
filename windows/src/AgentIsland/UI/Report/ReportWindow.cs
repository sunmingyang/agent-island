using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI.Report;

/// Hosts a share card (weekly or monthly) in a borderless window: the card
/// paints its own shadow, Esc closes, drag anywhere moves. Two actions —
/// Copy image and Save PNG — both served from a warm 3x render so neither
/// ever feels broken. Sharing is always the USER posting an image; nothing
/// leaves the machine on its own.
public sealed class ReportWindow : Window
{
    public enum Kind
    {
        Weekly,
        Monthly,
    }

    private static readonly Dictionary<Kind, ReportWindow> Open = new();

    private readonly Kind _kind;
    private readonly TextBlock _coach;
    private readonly Button _copy;
    private BitmapSource? _rendered;
    private DispatcherTimer? _coachTimer;

    public static void Show(Kind kind)
    {
        if (Open.TryGetValue(kind, out var existing))
        {
            existing.Activate();
            return;
        }
        var window = new ReportWindow(kind);
        Open[kind] = window;
        window.Closed += (_, _) => Open.Remove(kind);
        window.Show();
        window.Activate();
    }

    private ReportWindow(Kind kind)
    {
        _kind = kind;
        Title = kind == Kind.Weekly
            ? Localization.L10n.Tr("Weekly report")
            : Localization.L10n.Tr("Share monthly report");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        System.Windows.Media.TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // Fresh data per open — a cached tree kept serving stale numbers and
        // the pre-switch language on macOS; build-on-show avoids both.
        var card = BuildCard(rounded: true);
        card.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 4,
            Direction = 270,
            BlurRadius = 30,
            Color = Colors.Black,
            Opacity = 0.30,
        };

        _copy = ActionButton(Localization.L10n.Tr("Copy image"), prominent: true);
        _copy.Click += (_, _) =>
        {
            if (!CopyImage()) return;
            _copy.Content = Localization.L10n.Tr("Copied");
            ShowCoach(Localization.L10n.Tr("Copied! Post it and bring a friend to the island 🏝️ Thanks for spreading the word"));
            var reset = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            reset.Tick += (_, _) =>
            {
                reset.Stop();
                _copy.Content = Localization.L10n.Tr("Copy image");
            };
            reset.Start();
        };
        var save = ActionButton(Localization.L10n.Tr("Save PNG"), prominent: false);
        save.Click += (_, _) => SavePng();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(_copy);
        save.Margin = new Thickness(10, 0, 0, 0);
        buttons.Children.Add(save);

        // Fixed one-line slot so the window never reflows.
        _coach = new TextBlock
        {
            Text = " ",
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = IslandColors.Brush(Color.FromRgb(0x8C, 0xD9, 0x9E)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Opacity = 0,
        };

        // The close control rides ON the card (top-right, dark disc, hover
        // red) — parked on the window's transparent margin it was invisible
        // against a light desktop.
        var cardHost = new Grid();
        cardHost.Children.Add(card);
        cardHost.Children.Add(CloseDisc());

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 12) };
        stack.Children.Add(cardHost);
        stack.Children.Add(buttons);
        stack.Children.Add(_coach);
        Content = stack;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
        MouseLeftButtonDown += (_, _) =>
        {
            try { DragMove(); } catch { }
        };

        // Warm the 3x export render off the click path — it costs a beat,
        // and doing it lazily made the first Copy feel broken.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _ = ExportRender());
    }

    private FrameworkElement BuildCard(bool rounded) => _kind == Kind.Weekly
        ? ReportCards.Weekly(WeeklyReportData.Current(), rounded)
        : ReportCards.Monthly(MonthlyReportData.Current(), rounded);

    /// The card's own close control: a quiet dark disc with an ✕, top-right
    /// corner, red on hover — always visible against the card's ink.
    private UIElement CloseDisc()
    {
        var glyph = new TextBlock
        {
            Text = "", // Segoe Fluent ChromeClose
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 8.5,
            Foreground = IslandColors.Brush(IslandColors.White(0.65)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var disc = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            Background = IslandColors.Brush(IslandColors.White(0.10)),
            BorderBrush = IslandColors.Brush(IslandColors.White(0.12)),
            BorderThickness = new Thickness(0.5),
            Child = glyph,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            Cursor = Cursors.Hand,
        };
        disc.MouseEnter += (_, _) =>
        {
            disc.Background = IslandColors.Brush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            glyph.Foreground = Brushes.White;
        };
        disc.MouseLeave += (_, _) =>
        {
            disc.Background = IslandColors.Brush(IslandColors.White(0.10));
            glyph.Foreground = IslandColors.Brush(IslandColors.White(0.65));
        };
        disc.MouseLeftButtonDown += (_, e) => e.Handled = true; // not a drag
        disc.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Close();
        };
        return disc;
    }

    private void ShowCoach(string text)
    {
        _coach.Text = text;
        _coach.Opacity = 1;
        _coachTimer?.Stop();
        _coachTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _coachTimer.Tick += (_, _) =>
        {
            _coachTimer?.Stop();
            _coach.Opacity = 0;
        };
        _coachTimer.Start();
    }

    // MARK: - Export

    /// The EXPORT version is the card itself, full-bleed with SQUARE outer
    /// corners on an opaque background, rendered at 3x for crispness.
    private BitmapSource ExportRender()
    {
        if (_rendered is not null) return _rendered;
        _rendered = RenderCard(_kind);
        return _rendered;
    }

    public static BitmapSource RenderCard(Kind kind)
    {
        var card = kind == Kind.Weekly
            ? ReportCards.Weekly(WeeklyReportData.Current(), rounded: false)
            : ReportCards.Monthly(MonthlyReportData.Current(), rounded: false);
        const double scale = 3;
        card.Measure(new Size(ReportCards.CardWidth, ReportCards.CardHeight));
        card.Arrange(new Rect(0, 0, ReportCards.CardWidth, ReportCards.CardHeight));
        card.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)(ReportCards.CardWidth * scale), (int)(ReportCards.CardHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        return bitmap;
    }

    private bool CopyImage()
    {
        try
        {
            Clipboard.SetImage(ExportRender());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SavePng()
    {
        var tag = _kind == Kind.Weekly ? "weekly" : "monthly";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"agent-island-{tag}-{DateTime.Today:yyyy-MM-dd}.png",
            Filter = "PNG|*.png",
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var stream = File.Create(dialog.FileName);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ExportRender()));
            encoder.Save(stream);
            ShowCoach(Localization.L10n.Tr("Copied! Post it and bring a friend to the island 🏝️ Thanks for spreading the word"));
        }
        catch
        {
        }
    }

    /// Headless snapshot for tooling/screenshots:
    /// AGENTISLAND_REPORT_SNAPSHOT / AGENTISLAND_MONTHLY_SNAPSHOT = path.png.
    public static void WritePng(Kind kind, string path)
    {
        try
        {
            using var stream = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderCard(kind)));
            encoder.Save(stream);
        }
        catch
        {
        }
    }

    /// Weekly report moment: once per ISO week, surface the card shortly
    /// after launch (the cost scan needs a beat). Sharing needs a moment put
    /// in front of people, not a buried menu item.
    public static void ArmWeeklyMoment()
    {
        var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            var now = DateTime.Now;
            var weekKey = $"{ISOWeek.GetYear(now)}-W{ISOWeek.GetWeekOfYear(now)}";
            const string shownKey = "AgentIsland.weeklyReportShownForWeek";
            if (Core.Preferences.Get<string?>(shownKey) == weekKey) return;
            Core.Preferences.Set(shownKey, weekKey);
            Show(Kind.Weekly);
        };
        delay.Start();
    }

    private static Button ActionButton(string title, bool prominent)
    {
        var button = new Button
        {
            Content = title,
            FontFamily = IslandFonts.Ui,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = prominent ? Brushes.Black : IslandColors.Brush(IslandColors.White(0.85)),
            Height = 30,
            Padding = new Thickness(16, 0, 16, 0),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        var face = prominent ? Brushes.White : IslandColors.Brush(IslandColors.White(0.12));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        factory.SetValue(Border.BackgroundProperty, face);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(MarginProperty, new Thickness(16, 0, 16, 0));
        factory.AppendChild(presenter);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        IslandMotion.AttachPressFeedback(button);
        return button;
    }
}

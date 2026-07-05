using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AgentIsland.UI.Charts;
using AgentIsland.UI.Theme;

namespace AgentIsland.UI;

/// Horizontal page carousel. Pages sit side by side on a canvas track that
/// slides with the pageSwipe curve; edge-clamped, no wrap-around. Mouse
/// wheel (and the footer dots) drive paging on Windows, standing in for the
/// macOS trackpad swipe.
public sealed class PagedContent : Grid
{
    private readonly Canvas _track = new();
    private readonly TranslateTransform _slide = new();
    private readonly List<(IslandScreen Screen, FrameworkElement View)> _pages = new();
    private int _wheelAccumulator;

    public PagedContent()
    {
        ClipToBounds = true;
        _track.RenderTransform = _slide;
        Children.Add(_track);
        BuildPages();
        SizeChanged += (_, _) => Relayout(animate: false);
        ScreenPref.Shared.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScreenPref.Screen))
            {
                Dispatcher.BeginInvoke(() => Relayout(animate: true));
            }
            else if (args.PropertyName == nameof(ScreenPref.VisibleScreens))
            {
                Dispatcher.BeginInvoke(() =>
                {
                    BuildPages();
                    Relayout(animate: false);
                });
            }
        };
        MouseWheel += OnWheel;
    }

    private void BuildPages()
    {
        _track.Children.Clear();
        _pages.Clear();
        foreach (var screen in ScreenPref.Shared.VisibleScreens)
        {
            FrameworkElement view = screen switch
            {
                IslandScreen.Usage => new UsagePage(),
                IslandScreen.Cost => new CostPage(),
                IslandScreen.Overview => new OverviewPage(),
                IslandScreen.Triggers => new TriggerPage(),
                _ => new PlaceholderPage(screen),
            };
            _pages.Add((screen, view));
            _track.Children.Add(view);
        }
    }

    private void Relayout(bool animate)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        for (var i = 0; i < _pages.Count; i++)
        {
            var view = _pages[i].View;
            // Margin on a Canvas child offsets it without shrinking an
            // explicit Width — the page would shift right and clip. Pages
            // must own their insets as Padding; enforce margin-free roots.
            view.Margin = new Thickness(0);
            view.Width = width;
            view.Height = height;
            // Pages sit edge to edge; clip each one so nothing bleeds
            // through the seam while a neighbor is showing.
            view.ClipToBounds = true;
            Canvas.SetLeft(view, i * width);
            Canvas.SetTop(view, 0);
        }
        var index = _pages.FindIndex(p => p.Screen == ScreenPref.Shared.Screen);
        if (index < 0) index = 0;
        var target = -index * width;
        if (!animate)
        {
            _slide.BeginAnimation(TranslateTransform.XProperty, null);
            _slide.X = target;
            return;
        }
        var slide = new DoubleAnimation(target, IslandAnimations.PageSwipeDuration)
        {
            EasingFunction = IslandAnimations.PageSwipe(),
        };
        _slide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void OnWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        _wheelAccumulator += e.Delta;
        if (Math.Abs(_wheelAccumulator) < 120) return;
        var direction = _wheelAccumulator < 0 ? 1 : -1;
        _wheelAccumulator = 0;
        ScreenPref.Shared.ShowNext(direction);
        e.Handled = true;
    }
}

/// Stand-in for pages whose milestones haven't landed yet.
public sealed class PlaceholderPage : Grid
{
    public PlaceholderPage(IslandScreen screen)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new TextBlock
        {
            Text = Localization.L10n.Tr(screen.ToString()),
            FontFamily = IslandFonts.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = IslandColors.Brush(IslandColors.White(0.75)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = Localization.L10n.Tr("coming soon"),
            FontFamily = IslandFonts.Ui,
            FontSize = 11,
            Foreground = IslandColors.Brush(IslandColors.White(0.35)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        Children.Add(stack);
    }
}

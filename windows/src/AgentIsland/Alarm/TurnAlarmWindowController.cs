using AgentIsland.Core;

namespace AgentIsland.Alarm;

/// Owns the single visible alarm panel. Additional finished turns queue in
/// FIFO order; dismissing one recalls the next, so nothing gets swallowed
/// and nothing stacks.
public sealed class TurnAlarmWindowController
{
    public static TurnAlarmWindowController Shared { get; } = new();
    private TurnAlarmWindowController() { }

    private TurnAlarmWindow? _current;
    private readonly List<(TriggerTool Provider, ActivityMonitor.ActiveThread? Thread, string DeliveryKey)> _queue = new();

    public void Show(TriggerTool provider, ActivityMonitor.ActiveThread? thread, string deliveryKey)
    {
        if (_current is { } current)
        {
            if (current.DeliveryKey == deliveryKey) return;
            if (_queue.Any(item => item.DeliveryKey == deliveryKey)) return;
            _queue.Add((provider, thread, deliveryKey));
            return;
        }
        Present(provider, thread, deliveryKey);
    }

    /// The turn left needsYou (user replied, or it aged out): a visible
    /// panel for it is pure noise. Closes without acknowledging.
    public void AutoDismiss(TriggerTool provider, string deliveryKey)
    {
        _queue.RemoveAll(item => item.DeliveryKey == deliveryKey);
        if (_current is { } current && current.DeliveryKey == deliveryKey)
        {
            current.DismissSilently();
        }
    }

    private void Present(TriggerTool provider, ActivityMonitor.ActiveThread? thread, string deliveryKey)
    {
        var window = new TurnAlarmWindow(provider, thread, deliveryKey);
        window.Dismissed += OnDismissed;
        _current = window;
        window.Show();
        window.Activate();
    }

    private void OnDismissed(TurnAlarmWindow window)
    {
        window.Dismissed -= OnDismissed;
        if (ReferenceEquals(_current, window)) _current = null;
        if (_queue.Count == 0) return;
        var next = _queue[0];
        _queue.RemoveAt(0);
        // Let the close unwind before the next panel takes the stage. A
        // Show() can race in during that gap (a fresh turn finishing), so
        // re-check _current at fire time — if one is already up, put this
        // back at the head instead of stacking a second window (the macOS
        // controller's guard).
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_current is not null)
            {
                _queue.Insert(0, next);
                return;
            }
            Present(next.Provider, next.Thread, next.DeliveryKey);
        });
    }
}

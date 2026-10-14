using System.Collections.Concurrent;
using System.Diagnostics;

namespace BleHid.Core;

/// <summary>
/// Opt-in IME-control metadata and report state, never ordinary key usages or text.
/// Bounded buffering keeps console/file I/O off the hook thread even during an event loop.
/// </summary>
internal sealed class JisCaptureDiagnostic
{
    private const int Capacity = 256;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly object _gate = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _pendingCount;
    private long _rawControls, _reports, _notifyReturns, _dropped;
    private string _queuedControls = "", _notifiedControls = "";

    public JisKeyDiagnostic Keys { get; } = new("ime-control");

    public JisCaptureDiagnostic() => Keys.Metadata += line =>
    {
        Interlocked.Increment(ref _rawControls);
        Enqueue(line);
    };

    private void Enqueue(string line)
    {
        if (Interlocked.Increment(ref _pendingCount) > Capacity)
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _dropped);
            return;
        }
        _pending.Enqueue($"t={Stopwatch.GetElapsedTime(_started).TotalMilliseconds:F0}ms {line}");
    }

    private static bool IsControl(byte usage) => usage is 0x35 or 0x39 or 0x88 or 0x8A or 0x8B or 0x90 or 0x91;

    public void Report(KeyModifiers modifiers, byte[] usages, bool notifyReturned)
    {
        var controls = string.Join(" ", usages.Where(IsControl).Distinct().Order().Select(u => u.ToString("X2")));
        lock (_gate)
        {
            var previous = notifyReturned ? _notifiedControls : _queuedControls;
            if (notifyReturned)
            {
                _notifyReturns++;
                _notifiedControls = controls;
            }
            else
            {
                _reports++;
                _queuedControls = controls;
            }
            if (controls.Length != 0 || previous.Length != 0)
                Enqueue($"[jis-report] stage={(notifyReturned ? "notify-returned" : "queued")} " +
                    $"mod=0x{(byte)modifiers:X2} ime-usages=[{controls}]");
        }
    }

    public void Drain(Action<string> log)
    {
        while (_pending.TryDequeue(out var line))
        {
            Interlocked.Decrement(ref _pendingCount);
            log(line);
        }
    }

    public string Summary(int keyboardEvents)
    {
        lock (_gate)
            return $"[jis-status] keyboard-events={keyboardEvents} ime-events={Interlocked.Read(ref _rawControls)} " +
                $"queued={_reports} notify-returned={_notifyReturns} " +
                $"queued-ime=[{_queuedControls}] notified-ime=[{_notifiedControls}] dropped={Interlocked.Read(ref _dropped)}";
    }
}

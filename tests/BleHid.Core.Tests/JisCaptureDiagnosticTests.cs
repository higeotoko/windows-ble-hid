using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class JisCaptureDiagnosticTests
{
    [Fact]
    public void Control_group_records_ime_flags_but_not_letters_or_oem_symbol_keys()
    {
        var diagnostic = new JisCaptureDiagnostic();
        var lines = new List<string>();
        diagnostic.Keys.Observe(0x41, 0x1E, false, true, 0, KeyModifiers.None);
        diagnostic.Keys.Observe(0xC0, 0x1A, false, true, 0, KeyModifiers.None);
        diagnostic.Keys.Observe(0x16, 0, false, true, 0x10, KeyModifiers.None);
        diagnostic.Drain(lines.Add);
        var line = Assert.Single(lines);
        Assert.Contains("vk=0x16", line);
        Assert.Contains("flags=0x10", line);
        Assert.Contains("injected=True", line);
    }

    [Fact]
    public void Reports_record_only_control_usages_and_their_release()
    {
        var diagnostic = new JisCaptureDiagnostic();
        var lines = new List<string>();
        diagnostic.Report(KeyModifiers.None, [0x04, 0x05], false);
        diagnostic.Report(KeyModifiers.LeftShift, [0x04, 0x35, 0x90], false);
        diagnostic.Report(KeyModifiers.None, [0x04, 0x05], false);
        diagnostic.Drain(lines.Add);
        Assert.Equal(2, lines.Count);
        Assert.Contains("ime-usages=[35 90]", lines[0]);
        Assert.Contains("ime-usages=[]", lines[1]);
        Assert.DoesNotContain(lines, line => line.Contains("04") || line.Contains("05"));
    }

    [Fact]
    public void Idle_summary_distinguishes_queued_and_notify_returned_state()
    {
        var diagnostic = new JisCaptureDiagnostic();
        diagnostic.Report(KeyModifiers.None, [0x35], false);
        Assert.Contains("queued-ime=[35] notified-ime=[]", diagnostic.Summary(1));
        diagnostic.Report(KeyModifiers.None, [0x35], true);
        Assert.Contains("queued-ime=[35] notified-ime=[35]", diagnostic.Summary(1));
        diagnostic.Report(KeyModifiers.None, [], false);
        diagnostic.Report(KeyModifiers.None, [], true);
        Assert.Contains("queued-ime=[] notified-ime=[]", diagnostic.Summary(2));
    }

    [Fact]
    public void Runaway_injected_events_cannot_create_an_unbounded_log_queue()
    {
        var diagnostic = new JisCaptureDiagnostic();
        for (var i = 0; i < 1000; i++)
            diagnostic.Keys.Observe(0x1A, 0, false, true, 0x10, KeyModifiers.None);
        var lines = new List<string>();
        diagnostic.Drain(lines.Add);
        Assert.Equal(256, lines.Count);
        Assert.Contains("ime-events=1000", diagnostic.Summary(1000));
        Assert.Contains("dropped=744", diagnostic.Summary(1000));
    }

    [Fact]
    public void Capture_diagnostic_can_expose_an_unmatched_ime_release_without_logging_other_keys()
    {
        var diagnostic = new JisCaptureDiagnostic();
        using var capture = new InputCapture { JisDiagnostic = diagnostic.Keys };
        capture.KeyboardReport += (modifiers, usages) => diagnostic.Report(modifiers, usages, false);
        capture.HandleKeyboardEvent(0x16, true, 0, false, 0x10);
        capture.HandleKeyboardEvent(0x1A, false, 0, false, 0x90);
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        Assert.Contains("queued-ime=[90]", diagnostic.Summary(3));
        var lines = new List<string>();
        diagnostic.Drain(lines.Add);
        Assert.DoesNotContain(lines, line => line.Contains("vk=0x41"));
    }
}

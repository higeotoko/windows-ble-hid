using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class JisDbeToggleTests
{
    [Theory]
    [InlineData(0xF3, 0xF4)] [InlineData(0xF4, 0xF3)]
    public void Dbe_state_transition_is_a_toggle_pulse_not_a_held_key(int oldVk, int newVk)
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        // G14 trace: old SBCS state Up, new DBCS state Down, then no further events.
        capture.HandleKeyboardEvent(oldVk, false, 0x29, false, 0x80);
        capture.HandleKeyboardEvent(newVk, true, 0x29);
        Assert.Empty(reports[^1]);
        Assert.Equal(1, reports.Count(keys => keys.Contains((byte)0x35)));
        Assert.Equal(new byte[] { 0x35 }, reports[^2]);
    }

    [Fact]
    public void Repeated_observed_transition_pairs_never_leave_a_toggle_held()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        for (var i = 0; i < 20; i++)
        {
            capture.HandleKeyboardEvent(0xF3, false, 0x29);
            capture.HandleKeyboardEvent(0xF4, true, 0x29);
            Assert.Empty(reports[^1]);
        }
        Assert.Equal(20, reports.Count(keys => keys.Contains((byte)0x35)));
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        Assert.Equal(new byte[] { 0x04 }, reports[^1]);
    }

    [Theory]
    [InlineData(0xA0, KeyModifiers.LeftShift)]
    [InlineData(0xA2, KeyModifiers.LeftControl)]
    [InlineData(0xA4, KeyModifiers.LeftAlt)]
    public void Pulse_release_preserves_other_held_keys_and_modifiers(int modifierVk, KeyModifiers expected)
    {
        using var capture = new InputCapture();
        var reports = new List<(KeyModifiers Modifiers, byte[] Usages)>();
        capture.KeyboardReport += (modifiers, usages) => reports.Add((modifiers, usages));
        capture.HandleKeyboardEvent(modifierVk, true);
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        reports.Clear();
        capture.HandleKeyboardEvent(0xF4, true, 0x29);
        Assert.Equal(2, reports.Count);
        Assert.Equal(expected, reports[0].Modifiers);
        Assert.Equal(expected, reports[1].Modifiers);
        Assert.Equal(new byte[] { 0x04, 0x35 }, reports[0].Usages);
        Assert.Equal(new byte[] { 0x04 }, reports[1].Usages);
    }

    [Fact]
    public void Dbe_up_alone_does_not_generate_another_toggle()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.HandleKeyboardEvent(0xF3, false, 0x29);
        Assert.DoesNotContain(reports, keys => keys.Contains((byte)0x35));
    }

    [Fact]
    public void Us_grave_key_retains_normal_down_repeat_up_semantics()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.HandleKeyboardEvent(0xC0, true, 0x29);
        capture.HandleKeyboardEvent(0xC0, true, 0x29);
        capture.HandleKeyboardEvent(0xC0, false, 0x29);
        Assert.Equal(new byte[] { 0x35 }, reports[0]);
        Assert.Equal(new byte[] { 0x35 }, reports[1]);
        Assert.Empty(reports[2]);
    }

    [Fact]
    public void Other_scan_or_extended_flag_does_not_trigger_the_dbe_pulse()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.HandleKeyboardEvent(0xF4, true, 0x29, true);
        Assert.Empty(Assert.Single(reports));
        reports.Clear();
        capture.HandleKeyboardEvent(0xF4, true, 0x1A);
        Assert.Equal(new byte[] { 0x2F }, Assert.Single(reports));
    }

    [Fact]
    public void Local_pass_through_keeps_dbe_events_local()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        capture.KeyboardReport += (_, _) => Assert.Fail("local DBE state must not be forwarded");
        Assert.False(capture.HandleKeyboardEvent(0xF3, false, 0x29));
        Assert.False(capture.HandleKeyboardEvent(0xF4, true, 0x29));
    }

    [Fact]
    public void Capture_diagnostic_reports_no_held_control_after_observed_dbe_pair()
    {
        var diagnostic = new JisCaptureDiagnostic();
        using var capture = new InputCapture { JisDiagnostic = diagnostic.Keys };
        capture.KeyboardReport += (modifiers, usages) => diagnostic.Report(modifiers, usages, false);
        capture.HandleKeyboardEvent(0xF3, false, 0x29, false, 0x80);
        capture.HandleKeyboardEvent(0xF4, true, 0x29);
        Assert.Contains("queued-ime=[]", diagnostic.Summary(2));
    }
}

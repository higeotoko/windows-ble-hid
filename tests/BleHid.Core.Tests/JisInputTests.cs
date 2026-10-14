using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class JisInputTests
{
    [Theory]
    [InlineData(0x19, 0x29, 0x35)] [InlineData(0x1D, 0x7B, 0x8B)]
    [InlineData(0x1C, 0x79, 0x8A)] [InlineData(0x15, 0x70, 0x88)]
    [InlineData(0xDC, 0x7D, 0x89)] [InlineData(0xE2, 0x73, 0x87)]
    [InlineData(0xF0, 0x3A, 0x39)]
    [InlineData(0x16, 0x72, 0x90)] [InlineData(0x1A, 0x71, 0x91)]
    public void Jis_down_repeat_up_produces_a_key_then_an_empty_report(int vk, uint scan, byte usage)
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.HandleKeyboardEvent(vk, true, scan);
        capture.HandleKeyboardEvent(vk, true, scan);
        capture.HandleKeyboardEvent(vk, false, scan);
        Assert.Equal(new byte[] { usage }, reports[0]);
        Assert.Equal(new byte[] { usage }, reports[1]);
        Assert.Empty(reports[2]);
    }

    [Fact]
    public void Up_uses_down_identity_when_vk_changes_even_for_unmapped_scans()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        capture.HandleKeyboardEvent(0x42, true, 0x1E); // repeat after a layout change
        capture.HandleKeyboardEvent(0x42, false, 0x1E);
        Assert.Equal(new byte[] { 0x04 }, reports[0]);
        Assert.Equal(new byte[] { 0x04 }, reports[1]);
        Assert.Empty(reports[2]);
    }

    [Fact]
    public void Yen_and_ro_can_be_held_together_with_shift()
    {
        using var capture = new InputCapture();
        var reports = new List<(KeyModifiers Modifiers, byte[] Usages)>();
        capture.KeyboardReport += (mod, usages) => reports.Add((mod, usages));
        capture.HandleKeyboardEvent(0xA0, true, 0x2A);
        capture.HandleKeyboardEvent(0xDC, true, 0x7D);
        capture.HandleKeyboardEvent(0xDC, true, 0x73);
        Assert.Equal(KeyModifiers.LeftShift, reports[^1].Modifiers);
        Assert.Equal(new byte[] { 0x89, 0x87 }, reports[^1].Usages);
        capture.HandleKeyboardEvent(0xE2, false, 0x73);
        Assert.Equal(new byte[] { 0x89 }, reports[^1].Usages);
        capture.HandleKeyboardEvent(0xDC, false, 0x7D);
        capture.HandleKeyboardEvent(0xA0, false, 0x2A);
        Assert.Equal(KeyModifiers.None, reports[^1].Modifiers);
        Assert.Empty(reports[^1].Usages);
    }

    [Fact]
    public void Duplicate_usages_are_held_until_both_physical_keys_are_released()
    {
        using var capture = new InputCapture();
        byte[] last = [];
        capture.KeyboardReport += (_, usages) => last = usages;
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        capture.HandleKeyboardEvent(0x41, true, 0x7F);
        capture.HandleKeyboardEvent(0x41, false, 0x1E);
        Assert.Equal(new byte[] { 0x04 }, last);
        capture.HandleKeyboardEvent(0x41, false, 0x7F);
        Assert.Empty(last);
    }

    [Fact]
    public void Locally_started_jis_key_is_released_locally_despite_changed_vk()
    {
        using var capture = new InputCapture();
        capture.SetPassThrough(true);
        Assert.False(capture.HandleKeyboardEvent(0xDC, true, 0x73));
        capture.SetPassThrough(false);
        Assert.False(capture.HandleKeyboardEvent(0xE2, false, 0x73));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Held_keys_do_not_reappear_after_target_selection(bool useCapsShortcut)
    {
        using var capture = new InputCapture();
        var queue = new CaptureSession.ReportQueue();
        capture.KeyboardReport += queue.EnqueueKeyboard;
        capture.SwitchHostRequested += () => queue.EnqueueTargetChange(CaptureSession.Queued.SwitchHost);
        capture.HandleKeyboardEvent(0xDC, true, 0x7D);
        capture.HandleKeyboardEvent(0xA0, true, 0x2A);
        capture.HandleKeyboardEvent(0xA2, true, 0x1D);
        capture.HandleKeyboardEvent(0xA4, true, 0x38);
        while (queue.TryDequeue(out _)) { }
        var selected = false;
        if (useCapsShortcut) capture.HandleKeyboardEvent(0xF0, true, 0x3A);
        else capture.RequestTargetChange(() => queue.EnqueueTargetChange(CaptureSession.Queued.SelectTarget,
            () => selected = true));
        Assert.True(queue.TryDequeue(out var release));
        Assert.Equal(CaptureSession.Queued.Report, release.Kind);
        Assert.Equal(KeyModifiers.None, release.Modifiers);
        Assert.Empty(release.Usages!);
        Assert.True(queue.TryDequeue(out var change));
        Assert.Equal(useCapsShortcut ? CaptureSession.Queued.SwitchHost : CaptureSession.Queued.SelectTarget, change.Kind);
        if (!useCapsShortcut)
        {
            Assert.False(selected);
            change.Selection!();
            Assert.True(selected);
        }
        capture.HandleKeyboardEvent(0xDC, true, 0x7D); // held-key repeat after selection
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        Assert.True(queue.TryDequeue(out var repeat));
        Assert.Empty(repeat.Usages!);
        Assert.Equal(KeyModifiers.None, repeat.Modifiers);
        Assert.True(queue.TryDequeue(out var a));
        Assert.Equal(new byte[] { 0x04 }, a.Usages);
        Assert.Equal(KeyModifiers.None, a.Modifiers);
        capture.HandleKeyboardEvent(0xDC, false, 0x7D);
        capture.HandleKeyboardEvent(0xDC, true, 0x7D);
        Assert.True(queue.TryDequeue(out _));
        Assert.True(queue.TryDequeue(out var fresh));
        Assert.Contains((byte)0x89, fresh.Usages!);
    }

    [Fact]
    public void Local_release_during_ui_pass_through_cannot_restore_old_usage()
    {
        using var capture = new InputCapture();
        byte[] last = [];
        capture.KeyboardReport += (_, usages) => last = usages;
        capture.HandleKeyboardEvent(0xDC, true, 0x7D);
        capture.SetPassThrough(true);
        Assert.Empty(last);
        capture.HandleKeyboardEvent(0xDC, false, 0x7D);
        capture.SetPassThrough(false);
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        Assert.Equal(new byte[] { 0x04 }, last);
    }

    [Fact]
    public void Ctrl_alt_q_releases_jis_and_modifiers_before_stop()
    {
        using var capture = new InputCapture();
        capture.HandleKeyboardEvent(0x1C, true, 0x79);
        capture.HandleKeyboardEvent(0xA2, true, 0x1D);
        capture.HandleKeyboardEvent(0xA4, true, 0x38);
        var events = new List<string>();
        capture.KeyboardReport += (mod, usages) =>
        {
            Assert.Equal(KeyModifiers.None, mod);
            Assert.Empty(usages);
            events.Add("release");
        };
        capture.StopRequested += () => events.Add("stop");
        capture.HandleKeyboardEvent(0x51, true, 0x10);
        Assert.Equal(new[] { "release", "stop" }, events);
    }

    [Fact]
    public void Release_only_ime_event_does_not_leave_a_key_pressed()
    {
        // This deliberately does not synthesize an unobserved IME pulse. Hardware validation
        // is required before supporting a firmware path that only emits Up.
        using var capture = new InputCapture();
        byte[] last = [];
        capture.KeyboardReport += (_, usages) => last = usages;
        capture.HandleKeyboardEvent(0x16, false, 0xF2);
        Assert.Empty(last);
    }
}

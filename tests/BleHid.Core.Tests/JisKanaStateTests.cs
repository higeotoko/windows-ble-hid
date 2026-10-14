using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class JisKanaStateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Observed_kana_state_events_end_with_release(bool oldStateUp)
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        for (var i = 0; i < 4; i++)
        {
            if (oldStateUp) capture.HandleKeyboardEvent(0xF0, false, 0x70);
            capture.HandleKeyboardEvent(0xF2, true, 0x70);
            Assert.Empty(reports[^1]);
        }
        Assert.Equal(4, reports.Count(keys => keys.Contains((byte)0x88)));
    }

    [Fact]
    public void Down_only_after_switch_back_is_not_suppressed()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, usages) => reports.Add(usages);
        capture.HandleKeyboardEvent(0xF2, true, 0x70);
        capture.SetPassThrough(true);
        capture.HandleKeyboardEvent(0xF2, true, 0x70);
        capture.SetPassThrough(false);
        reports.Clear();
        capture.HandleKeyboardEvent(0xF2, true, 0x70);
        Assert.Equal(new byte[] { 0x88 }, reports[0]);
        Assert.Empty(reports[1]);
    }

    [Fact]
    public void Kana_pulse_preserves_other_keys_and_shift()
    {
        using var capture = new InputCapture();
        var reports = new List<(KeyModifiers Mod, byte[] Keys)>();
        capture.KeyboardReport += (mod, keys) => reports.Add((mod, keys));
        capture.HandleKeyboardEvent(0xA0, true);
        capture.HandleKeyboardEvent(0x41, true, 0x1E);
        capture.HandleKeyboardEvent(0xF2, true, 0x70);
        Assert.Equal(new byte[] { 0x04, 0x88 }, reports[^2].Keys);
        Assert.Equal(new byte[] { 0x04 }, reports[^1].Keys);
        Assert.Equal(KeyModifiers.LeftShift, reports[^1].Mod);
    }

    [Fact]
    public void Local_state_events_and_up_alone_do_not_send_kana()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, keys) => reports.Add(keys);
        capture.HandleKeyboardEvent(0xF0, false, 0x70);
        capture.SetPassThrough(true);
        reports.Clear();
        Assert.False(capture.HandleKeyboardEvent(0xF2, true, 0x70));
        Assert.Empty(reports);
    }

    [Fact]
    public void Ordinary_kana_vk_keeps_physical_down_up_behavior()
    {
        using var capture = new InputCapture();
        var reports = new List<byte[]>();
        capture.KeyboardReport += (_, keys) => reports.Add(keys);
        capture.HandleKeyboardEvent(0x15, true, 0x70);
        Assert.Equal(new byte[] { 0x88 }, Assert.Single(reports));
        capture.HandleKeyboardEvent(0x15, false, 0x70);
        Assert.Empty(reports[^1]);
    }
}

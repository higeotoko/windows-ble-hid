using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class JisDiagnosticTests
{
    [Fact]
    public void Diagnostic_emits_selected_metadata_but_no_other_key_events_or_held_usages()
    {
        var diagnostic = new JisKeyDiagnostic("yen");
        var lines = new List<string>();
        diagnostic.Metadata += lines.Add;
        using var capture = new InputCapture { JisDiagnostic = diagnostic };
        capture.SetPassThrough(true);
        for (var vk = 0x30; vk <= 0x5A; vk++) capture.HandleKeyboardEvent(vk, true, 0x1E);
        capture.HandleKeyboardEvent(0xDC, true, 0x2B); // same VK, different key
        capture.HandleKeyboardEvent(0xDC, true, 0x7D, false, 0x10);
        capture.HandleKeyboardEvent(0xDC, false, 0x7D);
        Assert.Equal(2, lines.Count);
        Assert.Contains("scan=0x7D", lines[0]);
        Assert.Contains("injected=True", lines[0]);
        Assert.Contains("usage=0x89", lines[0]);
        Assert.Contains(" up ", lines[1]);
        Assert.DoesNotContain(lines, line => line.Contains("usages=["));
    }

    [Fact]
    public void Unknown_target_is_rejected() => Assert.Throws<ArgumentException>(() => new JisKeyDiagnostic("all"));

    [Fact]
    public void Unexpected_ime_scan_is_observable_without_assigning_lang5()
    {
        var diagnostic = new JisKeyDiagnostic("hankaku");
        string? line = null;
        diagnostic.Metadata += value => line = value;
        diagnostic.Observe(0x19, 0x76, false, true, 0, KeyModifiers.None);
        Assert.Contains("scan=0x76", line);
        Assert.Contains("usage=unmapped", line);
    }

    [Fact]
    public void Passive_diagnostic_does_not_swallow_capture_shortcuts()
    {
        using var capture = new InputCapture { HotkeysEnabled = false, JisDiagnostic = new("caps") };
        capture.SetPassThrough(true);
        capture.SwitchHostRequested += () => Assert.Fail("diagnostics must not switch hosts");
        capture.StopRequested += () => Assert.Fail("diagnostics must not capture shortcuts");
        foreach (var vk in new[] { 0xA2, 0xA4, 0x14, 0x51 })
            Assert.False(capture.HandleKeyboardEvent(vk, true));
    }
}

using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

/// <summary>
/// Capture translates Windows virtual-key codes into HID usages, so an error here sends the
/// wrong character to the host with no local symptom at all.
/// </summary>
public class VirtualKeyMapTests
{
    [Theory]
    [InlineData(0x41, 0x04)] // A
    [InlineData(0x5A, 0x1D)] // Z
    [InlineData(0x31, 0x1E)] // 1
    [InlineData(0x39, 0x26)] // 9
    [InlineData(0x30, 0x27)] // 0
    [InlineData(0x0D, 0x28)] // Enter
    [InlineData(0x1B, 0x29)] // Escape
    [InlineData(0x08, 0x2A)] // Backspace
    [InlineData(0x09, 0x2B)] // Tab
    [InlineData(0x20, 0x2C)] // Space
    [InlineData(0x70, 0x3A)] // F1
    [InlineData(0x7B, 0x45)] // F12
    public void Maps_known_virtual_keys_to_usage_ids(int virtualKey, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(virtualKey, out var usage));
        Assert.Equal(expected, usage);
    }

    [Fact]
    public void Function_keys_are_contiguous_from_f1_to_f12()
    {
        for (var i = 0; i < 12; i++)
        {
            Assert.True(VirtualKeyMap.TryGetUsage(0x70 + i, out var usage));
            Assert.Equal(0x3A + i, usage);
        }
    }

    [Fact]
    public void Digits_one_through_nine_are_contiguous()
    {
        for (var i = 0; i < 9; i++)
        {
            Assert.True(VirtualKeyMap.TryGetUsage(0x31 + i, out var usage));
            Assert.Equal(0x1E + i, usage);
        }
    }

    [Fact]
    public void Zero_follows_nine_rather_than_preceding_one()
    {
        VirtualKeyMap.TryGetUsage(0x39, out var nine);
        VirtualKeyMap.TryGetUsage(0x30, out var zero);

        Assert.Equal(nine + 1, zero);
    }

    [Fact]
    public void Distinct_virtual_keys_never_share_a_usage()
    {
        var seen = new Dictionary<byte, int>();

        for (var vk = 0; vk <= 0xFF; vk++)
        {
            if (!VirtualKeyMap.TryGetUsage(vk, out var usage) || usage == 0) continue;
            Assert.False(seen.ContainsKey(usage),
                $"usage 0x{usage:X2} is produced by both VK 0x{seen.GetValueOrDefault(usage):X2} and VK 0x{vk:X2}");
            seen[usage] = vk;
        }
    }

    [Fact]
    public void Unmapped_virtual_keys_are_rejected()
    {
        Assert.False(VirtualKeyMap.TryGetUsage(0x07, out _));
    }

    /// <summary>Anything the map emits must fit the declared key array.</summary>
    [Fact]
    public void Every_mapped_usage_is_within_the_declared_logical_range()
    {
        for (var vk = 0; vk <= 0xFF; vk++)
            if (VirtualKeyMap.TryGetUsage(vk, out var usage))
                Assert.InRange(usage, (byte)0, HidDescriptors.KeyboardUsageMaximum);
    }

    [Theory]
    [InlineData(0xE2, 0x73, 0x87)]
    [InlineData(0x15, 0x70, 0x88)]
    [InlineData(0xDC, 0x7D, 0x89)]
    [InlineData(0x1C, 0x79, 0x8A)]
    [InlineData(0x1D, 0x7B, 0x8B)]
    [InlineData(0x19, 0x29, 0x35)]
    [InlineData(0xF0, 0x3A, 0x39)]
    // Physical positions, even when the local JIS OEM VK differs from the US VK.
    [InlineData(0xDE, 0x0D, 0x2E)]
    [InlineData(0xC0, 0x1A, 0x2F)]
    [InlineData(0xDB, 0x1B, 0x30)]
    [InlineData(0xDD, 0x2B, 0x31)]
    [InlineData(0xBB, 0x27, 0x33)]
    [InlineData(0xBA, 0x28, 0x34)]
    public void Physical_jis_keys_preserve_their_hid_identity(int vk, uint scan, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(vk, scan, false, out var usage));
        Assert.Equal(expected, usage);
        Assert.InRange(usage, (byte)0, HidDescriptors.KeyboardUsageMaximum);
    }

    [Theory]
    [InlineData(0x73)] [InlineData(0x70)] [InlineData(0x7D)]
    [InlineData(0x79)] [InlineData(0x7B)]
    public void Extended_scans_are_not_treated_as_jis_keys(uint scan) =>
        Assert.False(VirtualKeyMap.TryGetUsage(0x07, scan, true, out _));

    [Theory]
    [InlineData(0x16, 0x90)] [InlineData(0x1A, 0x91)]
    public void Dedicated_ime_virtual_keys_use_microsoft_assignments(int vk, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(vk, out var usage));
        Assert.Equal(expected, usage);
    }

    [Theory]
    [InlineData(0x15)] [InlineData(0x19)] [InlineData(0x1C)] [InlineData(0x1D)]
    [InlineData(0xE2)]
    public void Ambiguous_language_keys_require_physical_evidence(int vk) =>
        Assert.False(VirtualKeyMap.TryGetUsage(vk, 0, false, out _));

    [Fact]
    public void F24_scan_is_not_assumed_to_be_lang5() =>
        Assert.False(VirtualKeyMap.TryGetUsage(0x87, 0x76, false, out _));

    [Theory]
    [InlineData(0xBB, 0x0D, 0x2E)] [InlineData(0xDB, 0x1A, 0x2F)]
    [InlineData(0xDD, 0x1B, 0x30)] [InlineData(0xDC, 0x2B, 0x31)]
    [InlineData(0xBA, 0x27, 0x33)] [InlineData(0xDE, 0x28, 0x34)]
    [InlineData(0xC0, 0x29, 0x35)]
    public void Us_symbols_keep_existing_physical_and_vk_results(int vk, uint scan, byte expected)
    {
        Assert.True(VirtualKeyMap.TryGetUsage(vk, scan, false, out var physical));
        Assert.True(VirtualKeyMap.TryGetUsage(vk, 0, false, out var fallback));
        Assert.Equal(expected, physical);
        Assert.Equal(expected, fallback);
    }
}

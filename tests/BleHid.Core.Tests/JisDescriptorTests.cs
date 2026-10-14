using BleHid.Core;
using Xunit;

namespace BleHid.Core.Tests;

public sealed class JisDescriptorTests
{
    private sealed record Field(int ReportId, int Type, int Page, int Size, int Count,
        int LogicalMin, int LogicalMax, int UsageMin, int UsageMax, int Flags);

    // Read HID short items rather than search bytes: values can themselves equal item tags.
    private static List<Field> Fields(byte[] map)
    {
        var fields = new List<Field>();
        int page = 0, report = 0, size = 0, count = 0, min = 0, max = 0, usageMin = 0, usageMax = 0;
        for (var offset = 0; offset < map.Length;)
        {
            var prefix = map[offset++];
            Assert.NotEqual(0xFE, prefix);
            var bytes = (prefix & 3) == 3 ? 4 : prefix & 3;
            Assert.True(offset + bytes <= map.Length);
            var value = 0;
            for (var i = 0; i < bytes; i++) value |= map[offset + i] << (8 * i);
            var signed = bytes switch { 1 => (sbyte)value, 2 => (short)value, _ => value };
            offset += bytes;
            var type = (prefix >> 2) & 3;
            var tag = prefix >> 4;
            if (type == 1)
            {
                switch (tag)
                {
                    case 0: page = value; break;
                    case 1: min = signed; break;
                    case 2: max = min < 0 ? signed : value; break;
                    case 7: size = value; break;
                    case 8: report = value; break;
                    case 9: count = value; break;
                }
            }
            else if (type == 2)
            {
                if (tag == 1) usageMin = value;
                if (tag == 2) usageMax = value;
            }
            else if (type == 0)
            {
                if (tag is 8 or 9)
                    fields.Add(new(report, tag, page, size, count, min, max, usageMin, usageMax, value));
                usageMin = usageMax = 0; // local items reset after every Main item
            }
        }
        return fields;
    }

    [Fact]
    public void Extended_range_and_report_sizes_are_declared_in_the_actual_map()
    {
        var fields = Fields(HidDescriptors.ReportMapValue);
        var keyboard = fields.Where(f => f.ReportId == 1 && f.Type == 8).ToList();
        var keys = Assert.Single(keyboard, f => f.Flags == 0);
        Assert.Equal(7, keys.Page);
        Assert.Equal(8, keys.Size);
        Assert.Equal(6, keys.Count);
        Assert.Equal(0, keys.LogicalMin);
        Assert.Equal(0x91, keys.LogicalMax);
        Assert.Equal(0, keys.UsageMin);
        Assert.Equal(0x91, keys.UsageMax);
        Assert.Equal(64, keyboard.Sum(f => f.Size * f.Count));
        Assert.Equal(8, fields.Where(f => f.ReportId == 1 && f.Type == 9).Sum(f => f.Size * f.Count));
        Assert.Equal(48, fields.Where(f => f.ReportId == 2 && f.Type == 8).Sum(f => f.Size * f.Count));
        var leds = Assert.Single(fields, f => f.ReportId == 1 && f.Type == 9 && f.Flags == 2);
        Assert.Equal(8, leds.Page);
        Assert.Equal((0, 1, 1, 5), (leds.LogicalMin, leds.LogicalMax, leds.Size, leds.Count));
        var modifiers = Assert.Single(keyboard, f => f.Flags == 2);
        Assert.Equal((0xE0, 0xE7, 1, 8), (modifiers.UsageMin, modifiers.UsageMax, modifiers.Size, modifiers.Count));
    }

    [Fact]
    public void Jis_usage_bytes_fit_the_unchanged_eight_byte_payload()
    {
        var report = HidReports.Keyboard(KeyModifiers.LeftShift, 0x87, 0x88, 0x89, 0x8A, 0x90, 0x91);
        Assert.Equal(new byte[] { 2, 0, 0x87, 0x88, 0x89, 0x8A, 0x90, 0x91 }, report);
        Assert.Equal(new byte[8], HidReports.KeyboardRelease());
    }

    [Fact]
    public void Mouse_descriptor_bytes_are_identical_to_v060()
    {
        byte[] original = [
            0x05,0x01,0x09,0x02,0xA1,0x01,0x85,0x02,0x09,0x01,0xA1,0x00,
            0x05,0x09,0x19,0x01,0x29,0x03,0x15,0x00,0x25,0x01,0x95,0x03,
            0x75,0x01,0x81,0x02,0x95,0x01,0x75,0x05,0x81,0x01,0x05,0x01,
            0x09,0x30,0x09,0x31,0x16,0x01,0x80,0x26,0xFF,0x7F,0x75,0x10,
            0x95,0x02,0x81,0x06,0x09,0x38,0x15,0x81,0x25,0x7F,0x75,0x08,
            0x95,0x01,0x81,0x06,0xC0,0xC0];
        Assert.Equal(original, HidDescriptors.ReportMapValue[^original.Length..]);
    }
}

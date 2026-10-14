namespace BleHid.Core;

/// <summary>
/// Metadata for one explicitly selected physical key, never text or the complete key array.
/// The caller must queue Metadata callbacks rather than do I/O on the low-level hook thread.
/// </summary>
public sealed class JisKeyDiagnostic
{
    public static readonly string[] Targets =
        ["hankaku", "muhenkan", "henkan", "kana", "yen", "ro", "caps", "ime-on", "ime-off"];

    private readonly string _target;
    private readonly uint _scan;
    private readonly int _virtualKey;

    public JisKeyDiagnostic(string target)
    {
        _target = target.ToLowerInvariant();
        (_scan, _virtualKey) = _target switch
        {
            "hankaku" => (0x29u, 0x19),
            "muhenkan" => (0x7Bu, 0x1D),
            "henkan" => (0x79u, 0x1C),
            "kana" => (0x70u, 0x15),
            "yen" => (0x7Du, 0),
            "ro" => (0x73u, 0),
            "caps" => (0x3Au, 0x14),
            "ime-on" => (0u, 0x16),
            "ime-off" => (0u, 0x1A),
            _ => throw new ArgumentException("Unknown JIS diagnostic target.", nameof(target))
        };
    }

    public event Action<string>? Metadata;

    internal void Observe(int vk, uint scan, bool extended, bool down, uint flags, KeyModifiers modifiers)
    {
        // A selected IME VK is useful when firmware emits an unusual scan code. Do not use
        // a broad OEM-VK filter: those VKs can also represent unrelated printable keys.
        if (!(scan != 0 && scan == _scan && !extended) && !(_virtualKey != 0 && vk == _virtualKey))
            return;
        var mapped = VirtualKeyMap.TryGetUsage(vk, scan, extended, out var usage)
            ? $"0x{usage:X2}" : "unmapped";
        Metadata?.Invoke($"[jis:{_target}] vk=0x{vk:X2} scan=0x{scan:X2} extended={extended} " +
            $"{(down ? "down" : "up")} injected={(flags & 0x10) != 0} " +
            $"mod=0x{(byte)modifiers:X2} usage={mapped}");
    }
}

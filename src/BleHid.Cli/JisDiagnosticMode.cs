using System.Collections.Concurrent;
using BleHid.Core;

namespace BleHid.Cli;

internal static class JisDiagnosticMode
{
    public static async Task<int> RunAsync(string[] args)
    {
        var index = Array.FindIndex(args, arg => arg.Equals("--diagnose-jis", StringComparison.OrdinalIgnoreCase));
        if (index + 1 >= args.Length || !JisKeyDiagnostic.Targets.Contains(args[index + 1], StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: BleHid.Cli --diagnose-jis <" + string.Join('|', JisKeyDiagnostic.Targets) + ">");
            return 1;
        }

        var diagnostic = new JisKeyDiagnostic(args[index + 1]);
        var pending = new ConcurrentQueue<string>();
        diagnostic.Metadata += pending.Enqueue;
        using var capture = new InputCapture { JisDiagnostic = diagnostic, HotkeysEnabled = false };
        capture.Log += pending.Enqueue; // hook installation errors, never verbose key reports
        capture.SetPassThrough(true);
        Console.WriteLine("Local-only JIS diagnostic; no BLE service or HID reports are sent.");
        Console.WriteLine("Only the selected key's VK/scan/flags/modifiers are shown. No text or other key usages are recorded.");
        Console.WriteLine("Press the selected key alone, with Shift/Alt, and hold it briefly. Press Enter to finish.");
        var finish = Task.Run(() => Console.ReadLine());
        capture.Start();
        while (!finish.IsCompleted)
        {
            while (pending.TryDequeue(out var line)) Console.WriteLine(line);
            await Task.Delay(100);
        }
        capture.Stop();
        while (pending.TryDequeue(out var line)) Console.WriteLine(line);
        return 0;
    }
}

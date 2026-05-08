// Мост к Python-хелперу через subprocess.
// .NET не может корректно эмулировать через unicorn DLL напрямую (uc_emu_start крашит процесс
// независимо от binding/threading/W^X настроек), а Python работает идеально. Поэтому делегируем.
//
// Протокол: JSON-сообщения по строке, через stdin/stdout python-процесса.
// См. py_helper/warden_emu_helper.py.

using System.Diagnostics;
using System.Text.Json;

namespace WowBot.WardenEmulator;

public sealed class PythonEmulatorBridge : IDisposable
{
    private readonly Process _proc;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;

    public PythonEmulatorBridge(string? helperScriptPath = null, string pythonExe = "python")
    {
        helperScriptPath ??= LocateHelperScript();
        if (!File.Exists(helperScriptPath))
            throw new FileNotFoundException($"Python helper not found: {helperScriptPath}");

        var psi = new ProcessStartInfo(pythonExe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(helperScriptPath);

        _proc = Process.Start(psi) ?? throw new Exception("failed to start python");
        _stdin = _proc.StandardInput;
        _stdout = _proc.StandardOutput;

        // helper печатает baner в stderr — читаем в фоне чтобы pipe не залип
        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await _proc.StandardError.ReadLineAsync()) != null)
                    Console.Error.WriteLine($"[py-helper] {line}");
            }
            catch { }
        });
    }

    private static string LocateHelperScript()
    {
        // Ищем рядом с собой py_helper/warden_emu_helper.py
        var here = AppContext.BaseDirectory;
        // bin/Debug/net9.0 → ../../../py_helper/warden_emu_helper.py
        var candidates = new[]
        {
            Path.Combine(here, "py_helper", "warden_emu_helper.py"),
            Path.Combine(here, "..", "..", "..", "py_helper", "warden_emu_helper.py"),
            Path.Combine(here, "..", "..", "..", "..", "py_helper", "warden_emu_helper.py"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full)) return full;
        }
        return Path.GetFullPath(candidates[1]);
    }

    private JsonElement Send(object msg)
    {
        var json = JsonSerializer.Serialize(msg);
        _stdin.WriteLine(json);
        _stdin.Flush();
        var responseLine = _stdout.ReadLine();
        if (responseLine == null) throw new Exception("python helper closed pipe");
        var doc = JsonDocument.Parse(responseLine);
        return doc.RootElement.Clone();
    }

    private void EnsureOk(JsonElement res)
    {
        if (!res.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var err = res.TryGetProperty("error", out var e) ? e.GetString() : "unknown error";
            throw new Exception($"python helper error: {err}");
        }
    }

    // ----- Public API -----

    public string Ping()
    {
        var r = Send(new { op = "ping" });
        EnsureOk(r);
        return r.GetProperty("version").GetString() ?? "?";
    }

    public uint Smoke()
    {
        var r = Send(new { op = "smoke" });
        EnsureOk(r);
        var s = r.GetProperty("eax").GetString()!;
        return Convert.ToUInt32(s, 16);
    }

    public int Open(string arch = "x86", int mode = 32)
    {
        var r = Send(new { op = "open", arch, mode });
        EnsureOk(r);
        return r.GetProperty("uc_id").GetInt32();
    }

    public void Close(int ucId) => EnsureOk(Send(new { op = "close", uc_id = ucId }));

    public void Map(int ucId, ulong addr, ulong size, uint perms = 7)
        => EnsureOk(Send(new { op = "map", uc_id = ucId, addr = $"0x{addr:X}", size = $"0x{size:X}", perms = $"0x{perms:X}" }));

    public void Write(int ucId, ulong addr, byte[] data)
        => EnsureOk(Send(new { op = "write", uc_id = ucId, addr = $"0x{addr:X}", data_b64 = Convert.ToBase64String(data) }));

    public byte[] Read(int ucId, ulong addr, int size)
    {
        var r = Send(new { op = "read", uc_id = ucId, addr = $"0x{addr:X}", size });
        EnsureOk(r);
        return Convert.FromBase64String(r.GetProperty("data_b64").GetString()!);
    }

    public void Emu(int ucId, ulong begin, ulong until, int timeout = 0, int count = 0)
        => EnsureOk(Send(new { op = "emu", uc_id = ucId, begin = $"0x{begin:X}", until = $"0x{until:X}", timeout, count }));

    public uint RegRead(int ucId, string reg)
    {
        var r = Send(new { op = "reg_read", uc_id = ucId, reg });
        EnsureOk(r);
        return Convert.ToUInt32(r.GetProperty("value").GetString()!, 16);
    }

    public void RegWrite(int ucId, string reg, uint value)
        => EnsureOk(Send(new { op = "reg_write", uc_id = ucId, reg, value = $"0x{value:X}" }));

    public void Dispose()
    {
        try { _stdin.Close(); _proc.WaitForExit(2000); if (!_proc.HasExited) _proc.Kill(); }
        catch { }
        _proc.Dispose();
    }
}

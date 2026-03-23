using System.Runtime.InteropServices;
using JIE剪切板.Native;

namespace JIE剪切板.Services;

public class HotkeyService : IDisposable
{
    public const int HOTKEY_WAKE = 1;
    private IntPtr _windowHandle;
    private readonly Dictionary<int, Action> _callbacks = new();
    private bool _disposed;

    public void Initialize(IntPtr windowHandle) => _windowHandle = windowHandle;

    public bool RegisterHotkey(int id, int modifiers, int key, Action callback)
    {
        if (_windowHandle == IntPtr.Zero) return false;

        try
        {
            UnregisterHotkey(id);
            bool result = Win32Api.RegisterHotKey(_windowHandle, id, (uint)modifiers, (uint)key);
            if (result) _callbacks[id] = callback;
            else LogService.Log($"Hotkey registration failed: ID={id}, Error={Marshal.GetLastWin32Error()}");
            return result;
        }
        catch (Exception ex)
        {
            LogService.Log("Hotkey registration exception", ex);
            return false;
        }
    }

    public void UnregisterHotkey(int id)
    {
        try
        {
            Win32Api.UnregisterHotKey(_windowHandle, id);
            _callbacks.Remove(id);
        }
        catch { }
    }

    public bool ProcessHotkeyMessage(Message m)
    {
        if (m.Msg != Win32Api.WM_HOTKEY) return false;
        int id = m.WParam.ToInt32();
        if (_callbacks.TryGetValue(id, out var callback))
        {
            try { callback.Invoke(); }
            catch (Exception ex) { LogService.Log("Hotkey callback failed", ex); }
            return true;
        }
        return false;
    }

    public static string GetHotkeyDisplayText(int modifiers, int key)
    {
        var parts = new List<string>();
        if ((modifiers & Win32Api.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Win32Api.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Win32Api.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Win32Api.MOD_WIN) != 0) parts.Add("Win");

        string keyName = key switch
        {
            >= 0x30 and <= 0x39 => ((char)key).ToString(),
            >= 0x41 and <= 0x5A => ((char)key).ToString(),
            >= 0x70 and <= 0x7B => $"F{key - 0x70 + 1}",
            _ => $"0x{key:X2}"
        };
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _callbacks.Keys.ToList())
        {
            try { Win32Api.UnregisterHotKey(_windowHandle, id); } catch { }
        }
        _callbacks.Clear();
    }
}

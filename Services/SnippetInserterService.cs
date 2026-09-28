using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Inserts snippet text into the currently focused application by putting it
/// on the clipboard and synthesizing Ctrl+V.
/// </summary>
public class SnippetInserterService
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    public async Task InsertAsync(string content)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(content);

            // The user is still holding the hotkey's modifiers; pasting while they
            // are down would send e.g. Ctrl+Alt+V instead of Ctrl+V.
            if (!await WaitForModifierReleaseAsync())
            {
                // Still held after the timeout - don't paste a wrong combination.
                // The snippet stays on the clipboard for a manual Ctrl+V.
                return;
            }

            SendCtrlV();
        }
        catch (Exception ex)
        {
            // Called fire-and-forget from the hotkey event; without this the failure would be silent.
            System.Diagnostics.Debug.WriteLine($"Failed to insert snippet: {ex}");
        }
    }

    /// <returns>True when all modifiers were released; false on timeout.</returns>
    private static async Task<bool> WaitForModifierReleaseAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (KeyboardState.IsAnyModifierDown())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(15);
        }

        return true;
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyInput(VK_CONTROL, keyUp: false),
            KeyInput(VK_V, keyUp: false),
            KeyInput(VK_V, keyUp: true),
            KeyInput(VK_CONTROL, keyUp: true),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT KeyInput(ushort vk, bool keyUp) => new()
    {
        Type = INPUT_KEYBOARD,
        Data = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                Vk = vk,
                Flags = keyUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };
}

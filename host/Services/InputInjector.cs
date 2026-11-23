using System.Runtime.InteropServices;
using BayControl.Host.Models;

namespace BayControl.Host.Services;

public sealed class InputInjector
{
    private readonly object _gate = new();

    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventScancode = 0x0008;
    private const uint MouseFlagMove = 0x0001;
    private const uint MouseFlagLeftDown = 0x0002;
    private const uint MouseFlagLeftUp = 0x0004;
    private const uint MouseFlagRightDown = 0x0008;
    private const uint MouseFlagRightUp = 0x0010;
    private const uint MouseFlagWheel = 0x0800;

    public void SendAction(ActionMapping mapping)
    {
        if (mapping.Keys.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var modifiers = new List<ushort>();
            ushort? main = null;

            foreach (var raw in mapping.Keys)
            {
                var key = raw.Trim().ToLowerInvariant();
                if (key is "ctrl" or "control")
                {
                    modifiers.Add(VirtualKeys.Control);
                }
                else if (key is "shift")
                {
                    modifiers.Add(VirtualKeys.Shift);
                }
                else if (key is "alt" or "menu")
                {
                    modifiers.Add(VirtualKeys.Menu);
                }
                else
                {
                    main = MapKey(key);
                }
            }

            if (main is null)
            {
                return;
            }

            var inputs = new List<INPUT>();
            foreach (var mod in modifiers)
            {
                inputs.Add(KeyEvent(mod, down: true));
            }

            inputs.Add(KeyEvent(main.Value, down: true));
            inputs.Add(KeyEvent(main.Value, down: false));

            for (var i = modifiers.Count - 1; i >= 0; i--)
            {
                inputs.Add(KeyEvent(modifiers[i], down: false));
            }

            Dispatch(inputs);
        }
    }

    public void MouseMove(int dx, int dy)
    {
        lock (_gate)
        {
            Dispatch(
            [
                new INPUT
                {
                    type = InputMouse,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            dx = dx,
                            dy = dy,
                            dwFlags = MouseFlagMove
                        }
                    }
                }
            ]);
        }
    }

    public void MouseClick(bool right = false)
    {
        lock (_gate)
        {
            var down = right ? MouseFlagRightDown : MouseFlagLeftDown;
            var up = right ? MouseFlagRightUp : MouseFlagLeftUp;
            Dispatch(
            [
                new INPUT
                {
                    type = InputMouse,
                    U = new InputUnion { mi = new MOUSEINPUT { dwFlags = down } }
                },
                new INPUT
                {
                    type = InputMouse,
                    U = new InputUnion { mi = new MOUSEINPUT { dwFlags = up } }
                }
            ]);
        }
    }

    public void MouseScroll(int delta)
    {
        lock (_gate)
        {
            Dispatch(
            [
                new INPUT
                {
                    type = InputMouse,
                    U = new InputUnion
                    {
                        mi = new MOUSEINPUT
                        {
                            mouseData = unchecked((uint)delta),
                            dwFlags = MouseFlagWheel
                        }
                    }
                }
            ]);
        }
    }

    public void TypeText(string text)
    {
        lock (_gate)
        {
            foreach (var ch in text)
            {
                if (ch is '\n' or '\r')
                {
                    SendAction(new ActionMapping { Keys = ["enter"] });
                    continue;
                }

                Dispatch(
                [
                    new INPUT
                    {
                        type = InputKeyboard,
                        U = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wScan = ch,
                                dwFlags = KeyEventUnicode
                            }
                        }
                    },
                    new INPUT
                    {
                        type = InputKeyboard,
                        U = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wScan = ch,
                                dwFlags = KeyEventUnicode | KeyEventKeyUp
                            }
                        }
                    }
                ]);
            }
        }
    }

    private static void Dispatch(IReadOnlyList<INPUT> inputs)
    {
        var arr = inputs.ToArray();
        var sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
        if (sent != arr.Length)
        {
            // Best-effort; callers log at a higher layer when needed.
        }
    }

    private static INPUT KeyEvent(ushort vk, bool down)
    {
        var scan = (ushort)MapVirtualKey(vk, 0);
        uint flags = 0;
        if (!down)
        {
            flags |= KeyEventKeyUp;
        }

        if (IsExtended(vk))
        {
            flags |= KeyEventExtendedKey;
        }

        // Prefer scancodes where available — more reliable for games / RDP.
        if (scan != 0)
        {
            flags |= KeyEventScancode;
            return new INPUT
            {
                type = InputKeyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scan,
                        dwFlags = flags
                    }
                }
            };
        }

        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags & ~KeyEventScancode
                }
            }
        };
    }

    private static bool IsExtended(ushort vk) =>
        vk is VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down
            or VirtualKeys.Control or VirtualKeys.Menu
            or 0x21 or 0x22 or 0x23 or 0x24 or 0x2D or 0x2E; // prior/next/end/home/ins/del

    public static ushort MapKey(string key) => key.ToLowerInvariant() switch
    {
        "esc" or "escape" => VirtualKeys.Escape,
        "tab" => VirtualKeys.Tab,
        "space" => VirtualKeys.Space,
        "enter" or "return" => VirtualKeys.Return,
        "backspace" => VirtualKeys.Back,
        "left" => VirtualKeys.Left,
        "right" => VirtualKeys.Right,
        "up" => VirtualKeys.Up,
        "down" => VirtualKeys.Down,
        "f1" => VirtualKeys.F1,
        "f2" => VirtualKeys.F2,
        "f3" => VirtualKeys.F3,
        "f4" => VirtualKeys.F4,
        "f5" => VirtualKeys.F5,
        "f6" => VirtualKeys.F6,
        "f7" => VirtualKeys.F7,
        "f8" => VirtualKeys.F8,
        "f9" => VirtualKeys.F9,
        "f10" => VirtualKeys.F10,
        "f11" => VirtualKeys.F11,
        "f12" => VirtualKeys.F12,
        "oem_minus" or "-" or "minus" => VirtualKeys.OemMinus,
        "oem_plus" or "=" or "plus" => VirtualKeys.OemPlus,
        "oem_4" or "[" => VirtualKeys.Oem4,
        "oem_6" or "]" => VirtualKeys.Oem6,
        "oem_1" or ";" => VirtualKeys.Oem1,
        "oem_7" or "'" => VirtualKeys.Oem7,
        "oem_comma" or "," => VirtualKeys.OemComma,
        "oem_period" or "." => VirtualKeys.OemPeriod,
        var s when s.Length == 1 && char.IsLetter(s[0]) => (ushort)char.ToUpperInvariant(s[0]),
        var s when s.Length == 1 && char.IsDigit(s[0]) => (ushort)s[0],
        _ => throw new ArgumentException($"Unsupported key '{key}'")
    };

    private static class VirtualKeys
    {
        public const ushort Back = 0x08;
        public const ushort Tab = 0x09;
        public const ushort Return = 0x0D;
        public const ushort Shift = 0x10;
        public const ushort Control = 0x11;
        public const ushort Menu = 0x12;
        public const ushort Escape = 0x1B;
        public const ushort Space = 0x20;
        public const ushort Left = 0x25;
        public const ushort Up = 0x26;
        public const ushort Right = 0x27;
        public const ushort Down = 0x28;
        public const ushort F1 = 0x70;
        public const ushort F2 = 0x71;
        public const ushort F3 = 0x72;
        public const ushort F4 = 0x73;
        public const ushort F5 = 0x74;
        public const ushort F6 = 0x75;
        public const ushort F7 = 0x76;
        public const ushort F8 = 0x77;
        public const ushort F9 = 0x78;
        public const ushort F10 = 0x79;
        public const ushort F11 = 0x7A;
        public const ushort F12 = 0x7B;
        public const ushort Oem1 = 0xBA;
        public const ushort OemPlus = 0xBB;
        public const ushort OemComma = 0xBC;
        public const ushort OemMinus = 0xBD;
        public const ushort OemPeriod = 0xBE;
        public const ushort Oem4 = 0xDB;
        public const ushort Oem6 = 0xDD;
        public const ushort Oem7 = 0xDE;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // Explicit size matches Win32 INPUT union on x64 (MOUSEINPUT is largest).
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}

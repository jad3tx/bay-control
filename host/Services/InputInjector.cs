using System.Runtime.InteropServices;
using BayControl.Host.Models;

namespace BayControl.Host.Services;

public sealed class InputInjector
{
    private readonly object _gate = new();

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
                inputs.Add(KeyDown(mod));
            }

            inputs.Add(KeyDown(main.Value));
            inputs.Add(KeyUp(main.Value));

            for (var i = modifiers.Count - 1; i >= 0; i--)
            {
                inputs.Add(KeyUp(modifiers[i]));
            }

            var arr = inputs.ToArray();
            _ = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());

            if (mapping.HoldMs > 0)
            {
                Thread.Sleep(mapping.HoldMs);
            }
        }
    }

    public void MouseMove(int dx, int dy)
    {
        lock (_gate)
        {
            var input = new INPUT
            {
                type = InputType.Mouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        dwFlags = MouseEventFlags.Move
                    }
                }
            };
            _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        }
    }

    public void MouseClick(bool right = false)
    {
        lock (_gate)
        {
            var downFlag = right ? MouseEventFlags.RightDown : MouseEventFlags.LeftDown;
            var upFlag = right ? MouseEventFlags.RightUp : MouseEventFlags.LeftUp;
            INPUT[] inputs =
            [
                new()
                {
                    type = InputType.Mouse,
                    U = new InputUnion { mi = new MOUSEINPUT { dwFlags = downFlag } }
                },
                new()
                {
                    type = InputType.Mouse,
                    U = new InputUnion { mi = new MOUSEINPUT { dwFlags = upFlag } }
                }
            ];
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    public void MouseScroll(int delta)
    {
        lock (_gate)
        {
            var input = new INPUT
            {
                type = InputType.Mouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        mouseData = (uint)delta,
                        dwFlags = MouseEventFlags.Wheel
                    }
                }
            };
            _ = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        }
    }

    public void TypeText(string text)
    {
        lock (_gate)
        {
            foreach (var ch in text)
            {
                if (ch == '\n' || ch == '\r')
                {
                    SendAction(new ActionMapping { Keys = ["enter"] });
                    continue;
                }

                var down = new INPUT
                {
                    type = InputType.Keyboard,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wScan = ch,
                            dwFlags = KeyEventFlags.Unicode
                        }
                    }
                };
                var up = new INPUT
                {
                    type = InputType.Keyboard,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wScan = ch,
                            dwFlags = KeyEventFlags.Unicode | KeyEventFlags.KeyUp
                        }
                    }
                };
                _ = SendInput(2, [down, up], Marshal.SizeOf<INPUT>());
            }
        }
    }

    private static INPUT KeyDown(ushort vk) => new()
    {
        type = InputType.Keyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 }
        }
    };

    private static INPUT KeyUp(ushort vk) => new()
    {
        type = InputType.Keyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT { wVk = vk, dwFlags = KeyEventFlags.KeyUp }
        }
    };

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

    private enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1
    }

    [Flags]
    private enum KeyEventFlags : uint
    {
        KeyUp = 0x0002,
        Unicode = 0x0004
    }

    [Flags]
    private enum MouseEventFlags : uint
    {
        Move = 0x0001,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010,
        Wheel = 0x0800
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public InputType type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
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
        public MouseEventFlags dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public KeyEventFlags dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

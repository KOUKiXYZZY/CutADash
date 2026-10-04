using System;
using System.Collections.Generic;

namespace UiLibrary
{
    /// <summary>
    /// ショートカットキーの修飾キー。値はWin32のRegisterHotKeyのMOD_*と同じなので、
    /// ホットキーを登録する側へそのまま渡せる。
    /// </summary>
    [Flags]
    public enum ShortcutModifiers : uint
    {
        None = 0,
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Windows = 0x0008
    }

    /// <summary>修飾キーの組み合わせと、仮想キーコード(Win32のVK_*)1つで表す、ショートカットキー。</summary>
    public sealed class ShortcutKey : IEquatable<ShortcutKey>
    {
        public ShortcutModifiers Modifiers { get; }

        /// <summary>仮想キーコード(Win32のVK_*、<see cref="VirtualKey"/>と同じ値)。</summary>
        public uint VirtualKey { get; }

        public ShortcutKey(ShortcutModifiers modifiers, uint virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        /// <summary>"Ctrl + Shift + A"のような表示用の文字列。</summary>
        public override string ToString()
        {
            var parts = new List<string>();

            if (Modifiers.HasFlag(ShortcutModifiers.Control))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ShortcutModifiers.Shift))
                parts.Add("Shift");
            if (Modifiers.HasFlag(ShortcutModifiers.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ShortcutModifiers.Windows))
                parts.Add("Win");

            parts.Add(GetKeyName(VirtualKey));

            return string.Join(" + ", parts);
        }

        /// <summary>修飾キーだけを並べた表示用の文字列(キー入力を待っている間の途中経過に使う)。</summary>
        public static string FormatModifiers(ShortcutModifiers modifiers)
        {
            var parts = new List<string>();

            if (modifiers.HasFlag(ShortcutModifiers.Control))
                parts.Add("Ctrl");
            if (modifiers.HasFlag(ShortcutModifiers.Shift))
                parts.Add("Shift");
            if (modifiers.HasFlag(ShortcutModifiers.Alt))
                parts.Add("Alt");
            if (modifiers.HasFlag(ShortcutModifiers.Windows))
                parts.Add("Win");

            return string.Join(" + ", parts);
        }

        // VirtualKey列挙体の名前("Number1"、"Back"等)をそのまま出すと分かりにくいものだけ読み替える
        private static string GetKeyName(uint vk)
        {
            if (vk >= 0x30 && vk <= 0x39)
                return ((char)vk).ToString();

            if (vk >= 0x41 && vk <= 0x5A)
                return ((char)vk).ToString();

            if (vk >= 0x60 && vk <= 0x69)
                return $"Num{vk - 0x60}";

            if (vk >= 0x70 && vk <= 0x87)
                return $"F{vk - 0x6F}";

            return vk switch
            {
                0x08 => "Backspace",
                0x09 => "Tab",
                0x0D => "Enter",
                0x1B => "Esc",
                0x20 => "Space",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x23 => "End",
                0x24 => "Home",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0x2D => "Insert",
                0x2E => "Delete",
                0xBA => ";",
                0xBB => "=",
                0xBC => ",",
                0xBD => "-",
                0xBE => ".",
                0xBF => "/",
                0xC0 => "`",
                0xDB => "[",
                0xDC => "\\",
                0xDD => "]",
                0xDE => "'",
                _ => ((Windows.System.VirtualKey)vk).ToString(),
            };
        }

        public bool Equals(ShortcutKey? other)
            => other is not null && other.Modifiers == Modifiers && other.VirtualKey == VirtualKey;

        public override bool Equals(object? obj) => Equals(obj as ShortcutKey);

        public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);
    }
}

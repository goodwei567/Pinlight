using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class KeyBindings
    {
        internal static readonly string[] Labels = { "截图", "贴图", "隐藏 / 显示贴图", "鼠标穿透" };
        internal static Keys[] Get(Settings settings) { return new Keys[] { (Keys)settings.CaptureKey, (Keys)settings.PinKey, (Keys)settings.VisibilityKey, (Keys)settings.PassThroughKey }; }
        internal static void Set(Settings settings, Keys[] keys)
        {
            settings.CaptureKey = (int)keys[0]; settings.PinKey = (int)keys[1];
            settings.VisibilityKey = (int)keys[2]; settings.PassThroughKey = (int)keys[3];
        }
        internal static bool IsModifier(Keys key) { return key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin; }
        internal static string Validate(Keys[] keys)
        {
            if (keys == null || keys.Length != 4) return "需要设置 4 项快捷键。";
            HashSet<Keys> seen = new HashSet<Keys>();
            foreach (Keys combination in keys)
            {
                Keys key = combination & Keys.KeyCode;
                if (key == Keys.None || IsModifier(key) || key == Keys.Escape || key == Keys.Enter || key == Keys.Tab || key == Keys.Back || key == Keys.Delete) return "请选择功能键，或 Ctrl / Alt / Shift 与字母的组合。";
                if ((combination & Keys.Modifiers) == 0 && (key < Keys.F1 || key > Keys.F24)) return "单个按键请使用 F1–F24。";
                if ((combination & Keys.Control) != 0 && (key == Keys.C || key == Keys.V || key == Keys.X || key == Keys.S || key == Keys.Z || key == Keys.Y || key == Keys.A)) return "该组合用于常见编辑操作，请选择其他快捷键。";
                if (combination == (Keys.Alt | Keys.F4) || key == Keys.F12) return "该快捷键由系统保留。";
                if (!seen.Add(combination)) return "快捷键不能重复。";
            }
            return null;
        }
        internal static string Format(Keys key)
        {
            string text = "";
            if ((key & Keys.Control) != 0) text += "Ctrl+";
            if ((key & Keys.Alt) != 0) text += "Alt+";
            if ((key & Keys.Shift) != 0) text += "Shift+";
            return text + (key & Keys.KeyCode).ToString();
        }
    }
    internal sealed class HotkeyInput : TextBox
    {
        internal Keys Combination;
        internal HotkeyInput(Keys initial)
        {
            Combination = initial; Text = KeyBindings.Format(initial); ReadOnly = true;
            BackColor = Theme.Surface; ForeColor = Theme.Ink; BorderStyle = BorderStyle.FixedSingle;
            Font = Theme.Font(14); ShortcutsEnabled = false; Cursor = Cursors.Hand;
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab)) return base.ProcessCmdKey(ref message, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { Text = KeyBindings.Format(Combination); Parent.Focus(); return true; }
            if (KeyBindings.IsModifier(key)) return true;
            Combination = keyData; Text = KeyBindings.Format(keyData); SelectionLength = 0; return true;
        }
        protected override void OnEnter(EventArgs e) { BackColor = Theme.Mint; base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { BackColor = Theme.Surface; base.OnLeave(e); }
    }
}

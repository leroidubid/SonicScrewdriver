using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SonicScrewdriver.Input;

namespace SonicScrewdriver.UI;

/// <summary>
/// Read-only box that records the next key press: a chord ("Ctrl+Alt+F9") or, when <see cref="Chord"/> is false,
/// a single remappable key ("F11"). Tab and Escape keep their normal meaning so the window stays usable.
/// </summary>
public sealed class KeyCaptureBox : TextBox
{
    private string? _value;

    public bool Chord { get; set; }

    /// <summary>Key or chord name as stored in the settings; null when empty.</summary>
    public string? Value
    {
        get => _value;
        set { _value = value; ShowValue(); }
    }

    /// <summary>Extra check on a captured value: returns an error message, or null to accept it.</summary>
    public Func<string, string?>? Validator { get; set; }

    /// <summary>A pressed key was refused (the argument says why).</summary>
    public event Action<string>? Rejected;

    public KeyCaptureBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        VerticalContentAlignment = VerticalAlignment.Center;
        GotKeyboardFocus += (_, _) => Text = "Press a key…";
        LostKeyboardFocus += (_, _) => ShowValue();
        ContextMenu = null;
    }

    private void ShowValue() => Text = _value ?? "None";

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (key is Key.Tab or Key.Escape && (mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed or Key.ImeProcessed)
            return; // wait for the real key
        var vk = KeyInterop.VirtualKeyFromKey(key);
        var name = KeyNames.NameOf(vk);
        if (Chord)
        {
            var m = Modifiers.None;
            if (mods.HasFlag(ModifierKeys.Control)) m |= Modifiers.Ctrl;
            if (mods.HasFlag(ModifierKeys.Alt)) m |= Modifiers.Alt;
            if (mods.HasFlag(ModifierKeys.Shift)) m |= Modifiers.Shift;
            if (mods.HasFlag(ModifierKeys.Windows)) m |= Modifiers.Win;
            if (name is null) Rejected?.Invoke("This key cannot be used in a shortcut.");
            else if (m == Modifiers.None) Rejected?.Invoke("Hold at least one of Ctrl, Alt, Shift or Win with the key.");
            else
            {
                var chord = new Hotkey(m, vk).ToString();
                if (Validator?.Invoke(chord) is { } error) Rejected?.Invoke(error);
                else Accept(chord);
            }
        }
        else if (name is null || !KeyNames.IsRemappable(vk))
            Rejected?.Invoke("Use a function key (F1–F24), Pause, Scroll Lock, Insert, Home, End, Page Up/Down or a numpad key: typing keys are not allowed.");
        else if (Validator?.Invoke(name) is { } error) Rejected?.Invoke(error);
        else Accept(name);
    }

    private void Accept(string value)
    {
        _value = value;
        Rejected?.Invoke(""); // clears a previous message
        Keyboard.ClearFocus();
        ShowValue();
    }
}

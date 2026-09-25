using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Screendrop.Core.Settings;

namespace Screendrop.App.Settings;

/// <summary>
/// Key-capture box for one hotkey (mac HotkeyShortcutRecorder parity).
/// Focus it, press a combo — the box records it. Backspace resets to the
/// default; Escape cancels recording. Only combos with a modifier are
/// accepted (a bare key would hijack typing system-wide).
/// </summary>
internal sealed class HotkeyRecorderBox : TextBox
{
    private bool _recording;
    private string _textBeforeRecording = string.Empty;

    public HotkeyRecorderBox(string defaultCombo)
    {
        DefaultCombo = defaultCombo;
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
        Text = defaultCombo;
        MinWidth = 140;
        // The combo is a code, so it reads in the same tabular mono face as every
        // other value field in the app.
        FontFamily = (FontFamily)Application.Current.Resources["Sd.MonoFont"];
        FontWeight = FontWeights.SemiBold;
        VerticalContentAlignment = VerticalAlignment.Center;
        TextAlignment = TextAlignment.Center;
    }

    public string DefaultCombo { get; }

    /// <summary>Raised when the user records a new valid combo.</summary>
    public event EventHandler<string>? ComboChanged;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _recording = true;
        _textBeforeRecording = Text;
        // The shared field template already paints the accent focus ring.
        Text = "Press a shortcut…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Escape:
                StopRecording();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;

            case Key.Back:
            case Key.Delete:
                SetCombo(DefaultCombo);
                StopRecording();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                return;
        }

        // Modifier-only presses: keep waiting for the actual key.
        if (key is Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LWin or Key.RWin)
        {
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        var combo = new HotkeyCombo(
            Alt: Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
            Shift: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            Control: Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Windows: Keyboard.Modifiers.HasFlag(ModifierKeys.Windows),
            VirtualKey: vk);

        if (!combo.HasModifier)
        {
            Text = "Needs a modifier (Alt/Ctrl/…)";
            return;
        }

        SetCombo(combo.Format());
        StopRecording();
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void SetCombo(string combo)
    {
        if (combo == Text)
        {
            return;
        }

        Text = combo;
        ComboChanged?.Invoke(this, combo);
    }

    private void StopRecording()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;

        // Focus left without a valid combo (e.g. the user clicked Save
        // mid-recording): restore the previous value instead of leaving the
        // placeholder text in the field.
        if (Text is "Press a shortcut…" or "Needs a modifier (Alt/Ctrl/…)")
        {
            Text = _textBeforeRecording;
        }
    }
}

namespace OpenAIDictation.Core;

[Flags]
public enum ShortcutAction { None = 0, Consume = 1, Toggle = 2, Cancel = 4, MaskWindowsRelease = 8 }

public readonly record struct ShortcutEvent(int VirtualKey, bool IsDown, bool IsInjected,
    bool WindowsDown, bool ControlDown, bool AltDown, bool ShiftDown, bool SessionActive);

/// <summary>Pure decision logic for the low-level hook. Never records or retains typed text.</summary>
public sealed class WindowsShortcutPolicy
{
    private bool _hHeld, _escapeHeld, _maskWindowsRelease;

    public ShortcutAction Process(ShortcutEvent input)
    {
        if (input.IsInjected) return ShortcutAction.None;
        if (input.VirtualKey == 0x48) // H
        {
            if (input.IsDown)
            {
                if (_hHeld) return ShortcutAction.Consume;
                if (input.WindowsDown && !input.ControlDown && !input.AltDown && !input.ShiftDown)
                {
                    _hHeld = true; _maskWindowsRelease = true;
                    return ShortcutAction.Consume | ShortcutAction.Toggle;
                }
            }
            else if (_hHeld) { _hHeld = false; return ShortcutAction.Consume; }
        }
        if (!input.IsDown && input.VirtualKey is 0x5B or 0x5C && _maskWindowsRelease)
        {
            _maskWindowsRelease = false;
            return ShortcutAction.Consume | ShortcutAction.MaskWindowsRelease;
        }
        if (input.VirtualKey == 0x1B) // Escape: keep its matching release out of the editor after cancellation.
        {
            if (_escapeHeld)
            {
                if (!input.IsDown) _escapeHeld = false;
                return ShortcutAction.Consume;
            }
            if (input.IsDown && input.SessionActive)
            {
                _escapeHeld = true;
                return ShortcutAction.Consume | ShortcutAction.Cancel;
            }
        }
        return ShortcutAction.None;
    }
}

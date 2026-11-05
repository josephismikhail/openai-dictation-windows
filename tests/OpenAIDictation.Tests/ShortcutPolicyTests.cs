using OpenAIDictation.Core;

namespace OpenAIDictation.Tests;

public class ShortcutPolicyTests
{
    private static ShortcutEvent Key(int key = 0x48, bool down = true, bool windows = true,
        bool control = false, bool alt = false, bool shift = false, bool active = false, bool injected = false)
        => new(key, down, injected, windows, control, alt, shift, active);

    [Fact] public void PlainWindowsHTogglesOnceUntilHRelease()
    {
        var policy = new WindowsShortcutPolicy();
        Assert.Equal(ShortcutAction.Consume | ShortcutAction.Toggle, policy.Process(Key()));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key()));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key(down:false, windows:false)));
        Assert.Equal(ShortcutAction.Consume | ShortcutAction.Toggle, policy.Process(Key()));
    }
    [Theory] [InlineData(true,false,false)] [InlineData(false,true,false)] [InlineData(false,false,true)]
    public void ExtraModifiersKeepOriginalShortcut(bool control, bool alt, bool shift)
    {
        var policy = new WindowsShortcutPolicy();
        Assert.Equal(ShortcutAction.None, policy.Process(Key(control:control, alt:alt, shift:shift)));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(down:false)));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(0x5B,down:false)));
    }
    [Fact] public void OrdinaryHAndUnrelatedKeysPassThrough()
    {
        var policy = new WindowsShortcutPolicy();
        Assert.Equal(ShortcutAction.None, policy.Process(Key(windows:false)));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(down:false,windows:false)));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(0x41)));
    }
    [Fact] public void InjectedEventsDoNotAlterHeldKeyState()
    {
        var policy = new WindowsShortcutPolicy();
        Assert.Equal(ShortcutAction.None, policy.Process(Key(injected:true)));
        Assert.Equal(ShortcutAction.Consume | ShortcutAction.Toggle, policy.Process(Key()));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(down:false,injected:true)));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key()));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key(down:false)));
    }
    [Theory] [InlineData(0x5B)] [InlineData(0x5C)]
    public void WindowsReleaseRequestsOneMenuMaskAndOneRealRelease(int windowsKey)
    {
        var policy = new WindowsShortcutPolicy(); policy.Process(Key());
        Assert.Equal(ShortcutAction.Consume | ShortcutAction.MaskWindowsRelease, policy.Process(Key(windowsKey,down:false)));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(windowsKey,down:false)));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key(down:false,windows:false)));
    }
    [Fact] public void EscapeCancelsOnlyActiveSessionsAndKeepsRepeatReleasePaired()
    {
        var policy = new WindowsShortcutPolicy();
        Assert.Equal(ShortcutAction.None, policy.Process(Key(0x1B,windows:false)));
        Assert.Equal(ShortcutAction.Consume | ShortcutAction.Cancel, policy.Process(Key(0x1B,active:true)));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key(0x1B,active:false)));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key(0x1B,down:false,active:false)));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(0x1B,down:false,active:false)));
    }
    [Fact] public void ReleasingWindowsBeforeHDoesNotRetriggerRepeatedH()
    {
        var policy = new WindowsShortcutPolicy(); policy.Process(Key());
        policy.Process(Key(0x5B,down:false));
        Assert.Equal(ShortcutAction.Consume, policy.Process(Key(windows:false)));
        policy.Process(Key(down:false,windows:false));
        Assert.Equal(ShortcutAction.None, policy.Process(Key(windows:false)));
    }
}

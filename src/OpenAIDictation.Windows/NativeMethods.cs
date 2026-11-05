using System.Runtime.InteropServices;
using OpenAIDictation.Core;

namespace OpenAIDictation.Windows;

internal static class NativeMethods
{
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint window,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    [DllImport("user32.dll", SetLastError=true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32.dll", SetLastError=true)] internal static extern nint SetWindowsHookEx(int id, HookCallback callback, nint module,uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook,int code,nint wp,nint lp);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] internal static extern nint GetModuleHandle(string? name);
    internal delegate nint HookCallback(int code,nint wp,nint lp);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo {public uint Size,Flags;public nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rect CaretRect;}
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public uint Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { public uint Key,Scan,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)] public KeyInput Key; [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort VirtualKey,Scan; public uint Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X,Y; public uint Data,Flags,Time; public nuint Extra; }
    internal static Input Key(ushort key,bool up=false)=>new(){Type=1,Data=new(){Key=new(){VirtualKey=key,Flags=up?2u:0}}};
    internal static bool Down(int key)=>(GetAsyncKeyState(key)&0x8000)!=0;
}

internal sealed class GlobalShortcut : IDisposable
{
    private readonly NativeMethods.HookCallback callback;
    private nint hook;
    private readonly WindowsShortcutPolicy policy=new();
    public Func<bool>? Active { get;set; }
    public event Action? Toggle;
    public event Action? Cancel;
    public GlobalShortcut()
    {
        callback=Handle;
        hook=NativeMethods.SetWindowsHookEx(13,callback,NativeMethods.GetModuleHandle(null),0);
        if(hook==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not register Win+H.");
    }
    private nint Handle(int code,nint wp,nint lp)
    {
        if(code<0)return NativeMethods.CallNextHookEx(hook,code,wp,lp);
        var data=Marshal.PtrToStructure<NativeMethods.KeyboardData>(lp);
        bool down=wp==0x100 || wp==0x104,up=wp==0x101 || wp==0x105;
        if(!down && !up)return NativeMethods.CallNextHookEx(hook,code,wp,lp);
        var action=policy.Process(new((int)data.Key,down,(data.Flags&0x10)!=0,
            NativeMethods.Down(0x5B)||NativeMethods.Down(0x5C),NativeMethods.Down(0x11),NativeMethods.Down(0x12),NativeMethods.Down(0x10),Active?.Invoke()==true));
        if(action.HasFlag(ShortcutAction.Toggle))Toggle?.Invoke();
        if(action.HasFlag(ShortcutAction.Cancel))Cancel?.Invoke();
        if(action.HasFlag(ShortcutAction.MaskWindowsRelease))
        {
            // Unassigned VK_E8 masks the Start menu without changing a physically
            // held modifier; replay Win-up so Windows cannot leave it stuck down.
            NativeMethods.Input[] mask=[NativeMethods.Key(0xE8),NativeMethods.Key(0xE8,true),NativeMethods.Key((ushort)data.Key,true)];
            if(NativeMethods.SendInput((uint)mask.Length,mask,Marshal.SizeOf<NativeMethods.Input>())==mask.Length)return 1;
            return NativeMethods.CallNextHookEx(hook,code,wp,lp);
        }
        if(action.HasFlag(ShortcutAction.Consume))return 1;
        return NativeMethods.CallNextHookEx(hook,code,wp,lp);
    }
    public void Dispose(){if(hook!=0){NativeMethods.UnhookWindowsHookEx(hook);hook=0;}}
}

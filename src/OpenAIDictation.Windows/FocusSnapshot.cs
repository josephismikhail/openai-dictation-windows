using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace OpenAIDictation.Windows;

/// <summary>Local-only identity of the destination. No text or UI labels are read.</summary>
internal sealed record FocusSnapshot(nint Window,nint Control,uint Process,int[]? RuntimeId)
{
    public static FocusSnapshot Capture()
    {
        nint window=NativeMethods.GetForegroundWindow();
        uint thread=NativeMethods.GetWindowThreadProcessId(window,out uint process);
        var info=new NativeMethods.GuiThreadInfo{Size=(uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>()};
        NativeMethods.GetGUIThreadInfo(thread,ref info);
        int[]? id=null;
        try
        {
            var element=AutomationElement.FocusedElement;
            if(element!=null && element.Current.ProcessId==process)
            {
                if(element.Current.IsPassword)throw new InvalidOperationException("Dictation is not available in password fields.");
                id=element.GetRuntimeId();
            }
        }
        catch(ElementNotAvailableException){}catch(UnauthorizedAccessException){}catch(COMException){}
        return new(window,info.Focus,process,id);
    }
    public bool IsCurrent()
    {
        try
        {
            var now=Capture();
            return Window!=0 && Window==now.Window && Process==now.Process && Control==now.Control
                && (RuntimeId==null || now.RuntimeId!=null && RuntimeId.SequenceEqual(now.RuntimeId));
        }
        catch{return false;}
    }
}

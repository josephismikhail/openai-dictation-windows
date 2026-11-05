using System.Runtime.InteropServices;

namespace OpenAIDictation.Windows;

/// <summary>Read-only check before omitting Windows' empty enterprise clipboard marker.</summary>
internal static class ClipboardProtection
{
    internal static bool IsUnprotected()
    {
        nint id=0;
        try{return EdpGetEnterpriseIdForClipboard(out id)>=0 && (id==0 || Marshal.ReadInt16(id)==0);}
        catch{return false;}
        finally{if(id!=0)HeapFree(GetProcessHeap(),0,id);}
    }
    [DllImport("edputil.dll")]private static extern int EdpGetEnterpriseIdForClipboard(out nint enterpriseId);
    [DllImport("kernel32.dll")]private static extern nint GetProcessHeap();
    [DllImport("kernel32.dll")]private static extern bool HeapFree(nint heap,uint flags,nint memory);
}

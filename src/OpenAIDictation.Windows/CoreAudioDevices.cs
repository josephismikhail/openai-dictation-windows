using System.Runtime.InteropServices;

namespace OpenAIDictation.Windows;

/// <summary>One COM coclass declaration shared by capture muting and feedback rendering.</summary>
internal static class CoreAudioDevices
{
    internal static IDeviceEnumerator CreateEnumerator()=>(IDeviceEnumerator)(object)new DeviceEnumerator();

    // Declaring the same CLSID as two different managed coclass types causes an
    // activation-cache InvalidCastException after both paths run in the same process.
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class DeviceEnumerator {}

    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceEnumerator
    {
        [PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IDeviceCollection devices);
        [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
    }
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDeviceCollection
    {
        [PreserveSig]int GetCount(out uint count);
        [PreserveSig]int Item(uint index,out IDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDevice
    {
        [PreserveSig]int Activate(ref Guid id,uint context,nint parameters,[MarshalAs(UnmanagedType.IUnknown)]out object result);
        [PreserveSig]int OpenPropertyStore(int access,out nint properties);
        [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetState(out uint state);
    }
}

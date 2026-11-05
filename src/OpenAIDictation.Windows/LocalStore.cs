using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenAIDictation.Core;

namespace OpenAIDictation.Windows;

internal sealed class LocalStore
{
    public static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAIDictation");
    public static string KeyPath=>Path.Combine(Folder,"credential.dpapi");
    private static string SettingsPath=>Path.Combine(Folder,"settings.json");
    public static bool HasKey=>File.Exists(KeyPath);
    public static void SaveKey(string key)
    {
        key=key.Trim();
        if(!key.StartsWith("sk-",StringComparison.Ordinal) || key.Length<20)throw new ArgumentException("Enter a valid OpenAI API key.");
        byte[] bytes=Encoding.UTF8.GetBytes(key);
        try{WriteAtomic(KeyPath,Protect(bytes,false));}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static string LoadKey()
    {
        if(!HasKey)throw new InvalidOperationException("Open Settings and save your OpenAI API key first.");
        byte[] bytes=Protect(File.ReadAllBytes(KeyPath),true);
        try{return Encoding.UTF8.GetString(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static DictationOptions LoadOptions()
    {
        try{return JsonSerializer.Deserialize<DictationOptions>(File.ReadAllText(SettingsPath))??new();}
        catch{return new();}
    }
    public static void SaveOptions(DictationOptions options)=>WriteAtomic(SettingsPath,JsonSerializer.SerializeToUtf8Bytes(options));
    private static void WriteAtomic(string path,byte[] bytes)
    {
        Directory.CreateDirectory(Folder);string temp=path+".tmp";
        File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);
    }
    private static byte[] Protect(byte[] data,bool decrypt)
    {
        nint memory=Marshal.AllocHGlobal(data.Length);
        var input=new Blob{Length=data.Length,Data=memory};Blob output=default;
        try
        {
            Marshal.Copy(data,0,memory,data.Length);
            bool ok=decrypt?CryptUnprotectData(ref input,0,0,0,0,1,out output):CryptProtectData(ref input,"OpenAI Dictation",0,0,0,1,out output);
            if(!ok)throw new InvalidOperationException("Windows could not access the encrypted credential for this account.");
            byte[] result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            for(int i=0;i<data.Length;i++)Marshal.WriteByte(memory,i,0);
            Marshal.FreeHGlobal(memory);
            if(output.Data!=0){if(decrypt)for(int i=0;i<output.Length;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob{public int Length;public nint Data;}
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,string description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,nint description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}

internal static class TextInserter
{
    public static async Task InsertAsync(FocusSnapshot target,string text,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(text))return;
        ct.ThrowIfCancellationRequested();
        for(int i=0;i<40 && ModifiersDown();i++)await Task.Delay(50,ct);
        if(ModifiersDown())throw new InvalidOperationException("Release the shortcut keys before inserting text.");
        if(!target.IsCurrent())throw new InvalidOperationException("The text field changed. Dictation was not inserted.");
        System.Windows.IDataObject? old=null;
        uint ownSequence=0;
        bool changed=false;
        try
        {
            for(int attempt=0;;attempt++)
            {
                try
                {
                    uint before=NativeMethods.GetClipboardSequenceNumber();
                    old=ClipboardSnapshot.Capture(System.Windows.Clipboard.GetDataObject());
                    if(before!=NativeMethods.GetClipboardSequenceNumber())
                        throw new COMException("Clipboard changed while copying.");
                    ct.ThrowIfCancellationRequested();
                    if(!target.IsCurrent())throw new InvalidOperationException("The text field changed. Dictation was not inserted.");
                    System.Windows.Clipboard.SetText(text);changed=true;ownSequence=NativeMethods.GetClipboardSequenceNumber();break;
                }
                catch(System.Runtime.InteropServices.COMException) when(attempt<4){await Task.Delay(50,ct);}
            }
            ct.ThrowIfCancellationRequested();
            if(!target.IsCurrent())throw new InvalidOperationException("The text field changed. Dictation was not inserted.");
            NativeMethods.Input[] keys=[NativeMethods.Key(0x11),NativeMethods.Key(0x56),NativeMethods.Key(0x56,true),NativeMethods.Key(0x11,true)];
            if(NativeMethods.SendInput((uint)keys.Length,keys,Marshal.SizeOf<NativeMethods.Input>())!=keys.Length)
                throw new InvalidOperationException("Windows blocked text insertion. Try a normal, non-administrator text editor.");
            await Task.Delay(700);
        }
        finally
        {
            if(changed && NativeMethods.GetClipboardSequenceNumber()==ownSequence)
            {
                try{if(old!=null)System.Windows.Clipboard.SetDataObject(old,true);else System.Windows.Clipboard.Clear();}catch(COMException){}
            }
        }
    }
    private static bool ModifiersDown()=>NativeMethods.Down(0x5B)||NativeMethods.Down(0x5C)||NativeMethods.Down(0x11)||NativeMethods.Down(0x12)||NativeMethods.Down(0x10);
}

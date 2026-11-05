using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace OpenAIDictation.Windows;

/// <summary>Materializes clipboard data before replacing its OLE owner.</summary>
internal static class ClipboardSnapshot
{
    private const long MaximumFormatBytes=64L*1024*1024;
    internal static DataObject? Capture(IDataObject? source)=>Capture(source,ClipboardProtection.IsUnprotected);
    internal static DataObject? Capture(IDataObject? source,Func<bool> isUnprotected)
    {
        if(source==null)return null;
        var snapshot=new DataObject();
        foreach(string format in source.GetFormats(false))
        {
            object? value;
            try{value=source.GetData(format,false);}
            catch(COMException error) when(error.HResult==unchecked((int)0x800401D3)
                && format.Equals("EnterpriseDataProtectionId",StringComparison.OrdinalIgnoreCase) && isUnprotected())
            {continue;}
            if(value==null && format.Equals("EnterpriseDataProtectionId",StringComparison.OrdinalIgnoreCase) && isUnprotected())continue;
            if(value==null)throw new InvalidOperationException("The current clipboard could not be preserved. Copy plain text or clear the clipboard, then try again.");
            snapshot.SetData(format,Clone(value),false);
        }
        return snapshot;
    }
    internal static object Clone(object value)=>value switch
    {
        string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or char or DateTime or Guid=>value,
        byte[] bytes when bytes.LongLength<=MaximumFormatBytes=>bytes.ToArray(),
        string[] strings=>strings.ToArray(),
        StringCollection strings=>CloneStrings(strings),
        BitmapSource bitmap=>CloneBitmap(bitmap),
        System.Drawing.Image bitmap=>bitmap.Clone(),
        Stream stream=>CloneStream(stream),
        _=>throw new InvalidOperationException("This clipboard format cannot be safely preserved. Copy plain text or clear the clipboard, then try again.")
    };
    private static StringCollection CloneStrings(StringCollection source)
    {
        var result=new StringCollection();foreach(string? value in source)if(value!=null)result.Add(value);return result;
    }
    private static BitmapSource CloneBitmap(BitmapSource source)
    {
        int stride=checked((source.PixelWidth*source.Format.BitsPerPixel+7)/8);
        long count=(long)stride*source.PixelHeight;
        if(stride<=0 || count>MaximumFormatBytes)throw new InvalidOperationException("The clipboard image cannot be copied safely.");
        byte[] pixels=new byte[(int)count];source.CopyPixels(pixels,stride,0);
        BitmapSource result=BitmapSource.Create(source.PixelWidth,source.PixelHeight,source.DpiX,source.DpiY,source.Format,source.Palette,pixels,stride);
        result.Freeze();return result;
    }
    private static MemoryStream CloneStream(Stream source)
    {
        if(!source.CanRead || !source.CanSeek || source.Length>MaximumFormatBytes)
            throw new InvalidOperationException("The current clipboard is too large or cannot be copied safely.");
        long position=source.Position;
        try
        {
            source.Position=0;var result=new MemoryStream();source.CopyTo(result);result.Position=0;return result;
        }
        finally{source.Position=position;}
    }
}

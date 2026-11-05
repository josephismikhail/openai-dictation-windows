using System.Runtime.InteropServices;
using System.Windows;
using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class ClipboardMetadataTests
{
    private sealed class Source(string format="EnterpriseDataProtectionId",int error=unchecked((int)0x800401D3),bool nullMarker=false):IDataObject
    {
        public object? GetData(string name,bool autoConvert)=>name==DataFormats.UnicodeText?"sample text":nullMarker?null:throw new COMException("Unreadable metadata",error);
        public object? GetData(string name)=>GetData(name,false);
        public object? GetData(Type type)=>GetData(type.FullName!,false);
        public bool GetDataPresent(string name,bool autoConvert)=>GetFormats().Contains(name);
        public bool GetDataPresent(string name)=>GetDataPresent(name,false);
        public bool GetDataPresent(Type type)=>GetDataPresent(type.FullName!,false);
        public string[] GetFormats(bool autoConvert)=>[DataFormats.UnicodeText,format];
        public string[] GetFormats()=>GetFormats(false);
        public void SetData(object data)=>throw new NotSupportedException();
        public void SetData(string name,object data)=>throw new NotSupportedException();
        public void SetData(Type type,object data)=>throw new NotSupportedException();
        public void SetData(string name,object data,bool autoConvert)=>throw new NotSupportedException();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void EmptyEnterpriseMarkerDoesNotBlockOtherwiseReadableText(bool nullMarker)
    {
        var snapshot=ClipboardSnapshot.Capture(new Source(nullMarker:nullMarker),()=>true)!;
        Assert.Equal("sample text",snapshot.GetData(DataFormats.UnicodeText,false));
        Assert.DoesNotContain("EnterpriseDataProtectionId",snapshot.GetFormats(false));
    }
    [Fact] public void ProtectedClipboardMarkerIsNeverDiscarded()=>
        Assert.Throws<COMException>(()=>ClipboardSnapshot.Capture(new Source(),()=>false));
    [Fact] public void UnreadableOrdinaryFormatsRemainAnError()=>
        Assert.Throws<COMException>(()=>ClipboardSnapshot.Capture(new Source("CustomDocument"),()=>true));
    [Fact] public void AccessErrorsAreNeverTreatedAsEmptyMetadata()=>
        Assert.Throws<COMException>(()=>ClipboardSnapshot.Capture(new Source(error:unchecked((int)0x80070005)),()=>true));
}

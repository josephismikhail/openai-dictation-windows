using System.IO;
using System.Windows;
using OpenAIDictation.Core;
using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class LocalSafetyTests
{
    private static void Sta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact] public void ClipboardTextHtmlAndFileListAreCopiedIndependently()=>Sta(()=>
    {
        var source=new DataObject();string[] files=["first.txt","second.txt"];
        source.SetData(DataFormats.UnicodeText,"sample",false);source.SetData(DataFormats.Html,"<b>sample</b>",false);source.SetData(DataFormats.FileDrop,files,false);
        var snapshot=ClipboardSnapshot.Capture(source)!;files[0]="changed.txt";source.SetData(DataFormats.UnicodeText,"changed",false);
        Assert.Equal("sample",snapshot.GetData(DataFormats.UnicodeText,false));Assert.Equal("<b>sample</b>",snapshot.GetData(DataFormats.Html,false));
        Assert.Equal("first.txt",Assert.IsType<string[]>(snapshot.GetData(DataFormats.FileDrop,false))[0]);
    });
    [Fact] public void ClipboardStreamSurvivesOriginalDisposalAndRestoresItsPosition()=>Sta(()=>
    {
        var source=new DataObject();var bytes=new byte[]{1,2,3,4};var original=new MemoryStream(bytes){Position=2};
        source.SetData("IndependentStream",original,false);var snapshot=ClipboardSnapshot.Capture(source)!;
        Assert.Equal(2,original.Position);original.Dispose();bytes[0]=99;
        var copy=Assert.IsType<MemoryStream>(snapshot.GetData("IndependentStream",false));Assert.Equal(new byte[]{1,2,3,4},copy.ToArray());Assert.Equal(0,copy.Position);
    });
    [Fact] public void ClipboardByteArraysAreNotShared()=>Sta(()=>
    {
        byte[] original=[1,2,3];var copy=Assert.IsType<byte[]>(ClipboardSnapshot.Clone(original));original[0]=99;Assert.Equal(1,copy[0]);
    });
    [Fact] public void ClipboardBitmapPixelsAreMaterialized()=>Sta(()=>
    {
        var source=new System.Windows.Media.Imaging.WriteableBitmap(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null);
        source.WritePixels(new Int32Rect(0,0,1,1),new byte[]{10,20,30,255},4,0);
        var copy=Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(ClipboardSnapshot.Clone(source));
        source.WritePixels(new Int32Rect(0,0,1,1),new byte[]{0,0,0,0},4,0);
        byte[] pixels=new byte[4];copy.CopyPixels(pixels,4,0);Assert.Equal(new byte[]{10,20,30,255},pixels);
    });
    [Fact] public void UnsupportedClipboardObjectsFailBeforeAnyReplacement()=>Sta(()=>
    {
        var source=new DataObject();source.SetData("UnsupportedOpaque",new object(),false);
        Assert.Throws<InvalidOperationException>(()=>ClipboardSnapshot.Capture(source));Assert.NotNull(source.GetData("UnsupportedOpaque",false));
    });
    [Fact] public void MissingClipboardIsPreservedAsEmpty()=>Sta(()=>Assert.Null(ClipboardSnapshot.Capture(null)));
    [Fact] public void PcmLimitTrimsTheFinalChunkWithoutExceedingTenMinutes()
    {
        Assert.Equal(1920,PcmLimit.AcceptedBytes(0,1920));
        Assert.Equal(20,PcmLimit.AcceptedBytes(WaveFile.MaxPcmBytes-20,1920));
        Assert.Equal(0,PcmLimit.AcceptedBytes(WaveFile.MaxPcmBytes,1920));
    }
    [Fact] public void PcmLimitOnlyAcceptsCompleteSamples()
    {
        Assert.Equal(2,PcmLimit.AcceptedBytes(0,3));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PcmLimit.AcceptedBytes(-2,4));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PcmLimit.AcceptedBytes(1,4));
        Assert.Throws<ArgumentOutOfRangeException>(()=>PcmLimit.AcceptedBytes(WaveFile.MaxPcmBytes+2,4));
    }
    [Fact] public void DelayedUiStopCannotGrowTheRecordingPastTheCap()
    {
        long accepted=0;for(int i=0;i<WaveFile.MaxPcmBytes/1920+200;i++)accepted+=PcmLimit.AcceptedBytes(accepted,1920);
        Assert.Equal(WaveFile.MaxPcmBytes,accepted);
    }
}

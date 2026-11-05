using System.Buffers.Binary;
using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class PixelAudioLevelTests
{
    private static byte[] Samples(short amplitude)
    {
        byte[] data=new byte[3200];
        for(int i=0;i<data.Length;i+=2)BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i,2),amplitude);
        return data;
    }
    [Fact] public void SilenceAndQuietNoiseHaveNoSignal()
    {
        Assert.Equal(0,PixelAudioLevel.Measure([]));Assert.Equal(0,PixelAudioLevel.Measure(new byte[3200]));
        Assert.Equal(0,PixelAudioLevel.Measure(Samples(10)));
    }
    [Fact] public void PhoneDecibelMappingIsPreserved()
    {
        const short amplitude=1000;
        double expected=(20*Math.Log10(amplitude/32768d)+55)/43;
        Assert.Equal(expected,PixelAudioLevel.Measure(Samples(amplitude)),6);
        Assert.Equal(PixelAudioLevel.Measure(Samples(amplitude)),PixelAudioLevel.Measure(Samples(-amplitude)));
        Assert.Equal(1,PixelAudioLevel.Measure(Samples(short.MinValue)));
    }
    [Fact] public void PartialSampleIsIgnoredAndInputIsUnchanged()
    {
        byte[] data=[0,0,127];var copy=data.ToArray();
        Assert.Equal(0,PixelAudioLevel.Measure(data));Assert.Equal(copy,data);
    }
}

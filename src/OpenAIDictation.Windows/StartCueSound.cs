using System.IO;
using System.Text;

namespace OpenAIDictation.Windows;

/// <summary>Handy's MIT-licensed default start sound, at its original gain.</summary>
internal static class StartCueSound
{
    private static readonly Lazy<float[]> decoded=new(()=>
    {
        using var source=typeof(StartCueSound).Assembly.GetManifestResourceStream("OpenAIDictation.HandyStart.wav")
            ??throw new InvalidOperationException("The start sound is unavailable.");
        return Decode(source);
    });
    internal static float[] Samples()=>(float[])decoded.Value.Clone();
    internal static float[] Decode(Stream source)
    {
        using var reader=new BinaryReader(source,Encoding.ASCII,true);
        string FourCc()=>Encoding.ASCII.GetString(reader.ReadBytes(4));
        if(FourCc()!="RIFF")throw new InvalidDataException("Invalid start sound.");
        reader.ReadUInt32();if(FourCc()!="WAVE")throw new InvalidDataException("Invalid start sound.");
        int channels=0,rate=0,bits=0,format=0;byte[]? data=null;
        while(source.Position+8<=source.Length)
        {
            string kind=FourCc();uint length=reader.ReadUInt32();
            if(length>2_000_000 || length>source.Length-source.Position)throw new InvalidDataException("Invalid sound chunk.");
            long next=source.Position+length+(length&1);
            if(kind=="fmt ")
            {
                if(length<16)throw new InvalidDataException("Invalid sound format.");
                format=reader.ReadUInt16();channels=reader.ReadUInt16();rate=reader.ReadInt32();
                reader.ReadUInt32();reader.ReadUInt16();bits=reader.ReadUInt16();
            }
            else if(kind=="data")data=reader.ReadBytes((int)length);
            source.Position=next;
        }
        if(format!=1 || channels!=2 || rate!=44100 || bits!=16 || data==null || data.Length==0 || data.Length%4!=0)
            throw new InvalidDataException("Unsupported start sound format.");
        int sourceFrames=data.Length/4,frames=(int)Math.Round(sourceFrames*48000d/rate);
        var result=new float[frames*2];
        for(int frame=0;frame<frames;frame++)
        {
            double position=frame*rate/48000d;int left=Math.Min((int)position,sourceFrames-1),right=Math.Min(left+1,sourceFrames-1);
            float fraction=(float)(position-left);
            for(int channel=0;channel<2;channel++)
            {
                float a=BitConverter.ToInt16(data,left*4+channel*2)/32768f,b=BitConverter.ToInt16(data,right*4+channel*2)/32768f;
                result[frame*2+channel]=a+(b-a)*fraction;
            }
        }
        return result;
    }
}

using System.Security.Cryptography;
using System.IO;
using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class StartCueSoundTests
{
    [Fact] public void EmbeddedAssetMatchesTheAttributedUnmodifiedHandyFile()
    {
        using var source=typeof(StartCueSound).Assembly.GetManifestResourceStream("OpenAIDictation.HandyStart.wav");
        Assert.NotNull(source);
        Assert.Equal("EC58FB613B5102ECA54689A6B42C389F1AA46154DB80C27FEA756879DFC6F39A",Convert.ToHexString(SHA256.HashData(source)));
    }
    [Theory]
    [InlineData(new byte[]{})]
    [InlineData(new byte[]{82,73,70,70,0,0,0,0,87,65,86,69})]
    public void MissingOrIncompleteWaveDataFailsClosed(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes);
        Assert.ThrowsAny<Exception>(()=>StartCueSound.Decode(stream));
    }
}

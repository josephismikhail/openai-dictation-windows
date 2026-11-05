using System.Reflection;
using System.Runtime.InteropServices;
using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class CoreAudioInteropTests
{
    [Fact] public void ImportedComCoclassesHaveUniqueClsidsAcrossAllAudioPaths()
    {
        // Two managed coclass declarations for one CLSID can pass standalone
        // probes, then fail activation after the other path populates CLR's cache.
        var classes=typeof(CoreAudioDevices).Assembly.GetTypes().Where(t=>t.IsClass && t.IsImport).ToArray();
        Assert.NotEmpty(classes);
        foreach(var group in classes.GroupBy(t=>t.GUID))Assert.Single(group);
        Assert.Single(classes,t=>t.GUID==new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
    }
    [Fact] public void DeviceEnumeratorSharesBothEnumerationAndDefaultSelectionAbi()
    {
        Type enumerator=typeof(CoreAudioDevices.IDeviceEnumerator);
        Assert.True(enumerator.IsImport);
        Assert.Equal(new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),enumerator.GUID);
        var methods=enumerator.GetMethods().OrderBy(m=>m.MetadataToken).ToArray();
        Assert.Equal(new[]{"EnumAudioEndpoints","GetDefaultAudioEndpoint"},methods.Select(m=>m.Name));
        Assert.Equal(typeof(CoreAudioDevices.IDeviceCollection).MakeByRefType(),methods[0].GetParameters()[2].ParameterType);
        Assert.Equal(typeof(CoreAudioDevices.IDevice).MakeByRefType(),methods[1].GetParameters()[2].ParameterType);
    }
}

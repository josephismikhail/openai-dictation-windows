using System.Runtime.InteropServices;
using System.Text.Json;
using OpenAIDictation.Core;
using OpenAIDictation.Windows;

namespace OpenAIDictation.Windows.Tests;

public sealed class DictationFailureTests
{
    [Fact] public void ClipboardFailureDoesNotBlameApiSettings()
    {
        string text=DictationFailure.Describe(new COMException("private clipboard data"),DictationStage.Insertion);
        Assert.Contains("clipboard",text);Assert.DoesNotContain("API",text);Assert.DoesNotContain("private",text);
    }
    [Theory]
    [InlineData("Recording","Microphone")]
    [InlineData("Transcription","Transcription")]
    [InlineData("Cleanup","cleanup")]
    [InlineData("Insertion","insert")]
    public void UnexpectedFailuresIdentifyTheFailingStage(string stageName,string expected)
    {
        var stage=Enum.Parse<DictationStage>(stageName);
        string text=DictationFailure.Describe(new Exception("secret provider body"),stage);
        Assert.Contains(expected,text);Assert.DoesNotContain("secret",text);Assert.DoesNotContain("API settings",text);
    }
    [Fact] public void DiagnosticsExcludeExceptionMessagesAndStackTraces()
    {
        var error=new InvalidOperationException("private transcript",new Exception("sk-private-credential"));
        string json=JsonSerializer.Serialize(DictationFailure.Create(DictationStage.Transcription,"Transcription failed.",error));
        Assert.DoesNotContain("private transcript",json);Assert.DoesNotContain("sk-private",json);
        Assert.DoesNotContain("StackTrace",json);Assert.Contains("InvalidOperationException",json);Assert.Contains("Transcription",json);
    }
    [Fact] public void UnexpectedInvalidOperationMessageCannotEnterTheSavedReport()
    {
        var error=new InvalidOperationException("private transcript / sk-private-credential");
        var message=DictationFailure.Describe(error,DictationStage.Transcription);
        string json=JsonSerializer.Serialize(DictationFailure.Create(DictationStage.Transcription,message,error));
        Assert.DoesNotContain("private transcript",json);Assert.DoesNotContain("sk-private",json);
        Assert.Contains("Transcription failed",message);
    }
    [Fact] public void TrustedLocalMessagesRemainActionable()
    {
        const string expected="The text field changed. Dictation was not inserted.";
        Assert.Equal(expected,DictationFailure.Describe(new InvalidOperationException(expected),DictationStage.Insertion));
    }
    [Fact] public void KnownProviderMessagesRemainActionable()
    {
        const string expected="OpenAI rate or credit limit reached. Check your API account.";
        Assert.Equal(expected,DictationFailure.Describe(new DictationException(expected),DictationStage.Cleanup));
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAIDictation.Core;

namespace OpenAIDictation.Windows;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum DictationStage { Initialization, Recording, Stopping, Transcription, Cleanup, Insertion }

/// <summary>Last safe failure metadata only. Never records audio, text, keys, or provider bodies.</summary>
internal sealed record DictationFailure(DateTimeOffset Time,DictationStage Stage,string Message,string? ErrorType,int? HResult,string? CauseType,int? CauseHResult)
{
    private static readonly HashSet<string> LocalMessages=new(StringComparer.Ordinal)
    {
        "Open Settings and save your OpenAI API key first.",
        "Windows could not access the encrypted credential for this account.",
        "Windows could not mute audio for recording. Please try again.",
        "Microphone capture failed. Check Windows microphone permissions and input device.",
        "The microphone did not finish stopping. Restart the app before recording again.",
        "The microphone driver did not release its audio buffers.",
        "The final microphone audio could not be read.",
        "Release the shortcut keys before inserting text.",
        "The text field changed. Dictation was not inserted.",
        "Windows blocked text insertion. Try a normal, non-administrator text editor.",
        "The current clipboard could not be preserved. Copy plain text or clear the clipboard, then try again.",
        "This clipboard format cannot be safely preserved. Copy plain text or clear the clipboard, then try again.",
        "The clipboard image cannot be copied safely.",
        "The current clipboard is too large or cannot be copied safely.",
        "The display could not be located."
    };
    internal static string Describe(Exception error,DictationStage stage)
    {
        if(error is DictationException)return error.Message;
        if(error is ObjectDisposedException)return "Dictation was interrupted before it finished. Please try again.";
        if(stage==DictationStage.Insertion && error is ExternalException)
            return "Windows could not access the clipboard to paste your dictation. Please try again.";
        if(error is InvalidOperationException && LocalMessages.Contains(error.Message))return error.Message;
        return stage switch
        {
            DictationStage.Initialization=>"Dictation could not start. Check the microphone and dictation settings.",
            DictationStage.Recording or DictationStage.Stopping=>"Microphone capture failed. Please try again.",
            DictationStage.Transcription=>"Transcription failed. Check your connection and try again.",
            DictationStage.Cleanup=>"Text cleanup failed. Your original transcript is available from the tray menu.",
            _=>"Windows could not insert the dictation. Your transcript is available from the tray menu."
        };
    }
    internal static DictationFailure Create(DictationStage stage,string safeMessage,Exception? error)=>
        new(DateTimeOffset.UtcNow,stage,safeMessage,error?.GetType().Name,error?.HResult,error?.InnerException?.GetType().Name,error?.InnerException?.HResult);
    internal static void Save(DictationStage stage,string safeMessage,Exception? error=null)
    {
        try
        {
            string path=Path.Combine(LocalStore.Folder,"last-error.json");
            Directory.CreateDirectory(LocalStore.Folder);
            File.WriteAllText(path,JsonSerializer.Serialize(Create(stage,safeMessage,error),new JsonSerializerOptions{WriteIndented=true}));
        }
        catch { /* Diagnostic storage must not prevent capture cleanup or error display. */ }
    }
}

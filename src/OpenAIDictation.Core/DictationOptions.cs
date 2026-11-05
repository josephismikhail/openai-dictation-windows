using System.Text.RegularExpressions;

namespace OpenAIDictation.Core;

public sealed record DictationOptions(
    string Vocabulary = "", string Instructions = "",
    string TranscriptionModel = "gpt-live-transcribe",
    string CleanupModel = "gpt-6.1-sol", string ServiceTier = "ultrafast")
{
    internal void Validate()
    {
        if (Vocabulary is null || Instructions is null || TranscriptionModel is null || CleanupModel is null ||
            !Regex.IsMatch(TranscriptionModel, "^[a-zA-Z0-9._-]{1,100}$") ||
            !Regex.IsMatch(CleanupModel, "^[a-zA-Z0-9._-]{1,100}$") ||
            ServiceTier is not ("ultrafast" or "default" or "auto" or "priority") ||
            Vocabulary.Length > 16_000 || Instructions.Length > 16_000)
            throw new ArgumentException("Invalid dictation settings.");
    }

    internal string[] Keywords => Vocabulary.Split(['\r', '\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(v => v.Length <= 128 && !v.Contains('<') && !v.Contains('>')).Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToArray();
}

public sealed record CleanupResult(string Text, int InputTokens, int OutputTokens, string Model);

/// <summary>A safe, user-facing failure. Provider bodies and credentials are never included.</summary>
public sealed class DictationException(string message) : Exception(message);

using System.Buffers;
using System.Globalization;
using System.Text;

namespace OpenAIDictation.Core;

/// <summary>Applies configured spelling/case to whole terms without changing surrounding text.</summary>
internal static class PreferredSpelling
{
    internal static string Apply(string text,IEnumerable<string> preferredTerms)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(preferredTerms);
        var terms=preferredTerms.Where(term=>!string.IsNullOrWhiteSpace(term))
            .Select(term=>term.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(term=>term.Length).ToArray();
        if(text.Length==0 || terms.Length==0)return text;
        var output=new StringBuilder(text.Length);
        for(int position=0;position<text.Length;)
        {
            string? match=null;
            if(!IsWordBefore(text,position) && !char.IsLowSurrogate(text[position]))
            {
                foreach(string term in terms)
                    if(text.AsSpan(position).StartsWith(term.AsSpan(),StringComparison.OrdinalIgnoreCase)
                        && !IsWordAt(text,position+term.Length))
                    {match=term;break;}
            }
            if(match is null)output.Append(text[position++]);
            else {output.Append(match);position+=match.Length;}
        }
        return output.ToString();
    }

    private static bool IsWordBefore(string text,int position)=>position>0
        && Rune.DecodeLastFromUtf16(text.AsSpan(0,position),out var rune,out _)==OperationStatus.Done && IsWord(rune);
    private static bool IsWordAt(string text,int position)=>position<text.Length
        && Rune.DecodeFromUtf16(text.AsSpan(position),out var rune,out _)==OperationStatus.Done && IsWord(rune);
    private static bool IsWord(Rune rune)=>rune.Value is 0x200C or 0x200D || Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or
        UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.DecimalDigitNumber or
        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.ConnectorPunctuation;
}

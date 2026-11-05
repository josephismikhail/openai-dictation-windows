using System.Net;
using System.Text;
using OpenAIDictation.Core;

namespace OpenAIDictation.Tests;

public sealed class PreferredSpellingTests
{
    [Fact] public void AppliesConfiguredCaseAndKeepsPunctuation()
    {
        Assert.Equal("Acme, Acme. Acme's (Acme)! CloudBox?",
            PreferredSpelling.Apply("acme, ACME. acme's (Acme)! cloudbox?",["Acme","CloudBox"]));
    }
    [Theory]
    [InlineData("AcmeCloud")]
    [InlineData("acme_cloud")]
    [InlineData("acme2")]
    [InlineData("2acme")]
    [InlineData("éacme")]
    [InlineData("acme\u0301")]
    [InlineData("\U00010400acme")]
    [InlineData("acme\U00010400")]
    [InlineData("acme\u200DCloud")]
    public void DoesNotReplaceInsideUnicodeWords(string text)=>Assert.Equal(text,PreferredSpelling.Apply(text,["ACME"]));

    [Fact] public void ChoosesLongestPhraseAndDoesNotReprocessReplacements()
    {
        Assert.Equal("CloudBox Pro and CloudBox professional.",
            PreferredSpelling.Apply("cloudbox pro and cloudbox professional.",["CloudBox","CloudBox Pro","PRO"]));
    }
    [Fact] public void TreatsPatternCharactersAsLiteralText()
    {
        Assert.Equal("A+B and A[B], but axb.",PreferredSpelling.Apply("a+b and a[b], but axb.",["A+B","A[B]"]));
    }
    [Fact] public void MatchesUnicodeCaseWithoutCurrentCulture()
    {
        var previous=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=new("tr-TR");
            Assert.Equal("CloudBox Облако",PreferredSpelling.Apply("cloudbox ОБЛАКО",["CloudBox","Облако"]));
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=previous;}
    }
    [Fact] public void EmptyAndDuplicateEntriesDoNotChangeUnrelatedText()
    {
        Assert.Equal("Acme; untouched",PreferredSpelling.Apply("ACME; untouched",[" ","Acme","ACME"]));
        Assert.Equal("unchanged",PreferredSpelling.Apply("unchanged",[]));
    }
    [Fact] public async Task CleanupAppliesOnlyConfiguredPreferredTerms()
    {
        using var client=new OpenAiClient(new HttpClient(new ResponseHandler()));
        var result=await client.CleanupAsync("acme and cloudbox", "test", new("Acme"));
        Assert.Equal("Acme and cloudbox.",result.Text);
    }
    private sealed class ResponseHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {Content=new StringContent("""{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"acme and cloudbox."}]}]}""",Encoding.UTF8,"application/json")});
    }
}

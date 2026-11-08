using PersonaOS.Application.Common;

namespace PersonaOS.Tests.Common;

public class WordStemTests
{
    [Theory]
    // Owner's chat 4nzrb5af (2026-11-07): "commuting" did not find "Commute".
    [InlineData("commuting", "Commute")]
    [InlineData("commuted", "commute")]
    [InlineData("commutes", "Commute")]
    [InlineData("running", "run")]
    [InlineData("shopping", "Shop")]
    [InlineData("workouts", "workout")]
    [InlineData("meetings", "Meeting")]
    [InlineData("dancing", "Dance")]
    [InlineData("Meditation's", "meditation")]
    [InlineData("transitions", "transition")]
    [InlineData("lunches", "Lunch")]
    [InlineData("classes", "class")]
    [InlineData("parties", "party")]
    public void Endings_of_one_word_meet(string a, string b)
    {
        Assert.Equal(WordStem.Of(a), WordStem.Of(b));
    }

    [Theory]
    [InlineData("ring", "rung")]
    [InlineData("bed", "be")]
    [InlineData("fill", "fit")]
    [InlineData("dinner", "dine")]
    [InlineData("walk", "work")]
    public void Different_words_stay_apart(string a, string b)
    {
        Assert.NotEqual(WordStem.Of(a), WordStem.Of(b));
    }

    [Theory]
    [InlineData("ring", "ring")]
    [InlineData("bed", "bed")]
    [InlineData("need", "need")]
    [InlineData("pass", "pass")]
    [InlineData("calling", "call")]
    public void Short_and_whole_words_are_kept(string word, string stem)
    {
        Assert.Equal(stem, WordStem.Of(word));
    }
}

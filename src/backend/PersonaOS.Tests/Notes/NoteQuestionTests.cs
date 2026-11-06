using PersonaOS.Application.Notes;

namespace PersonaOS.Tests.Notes;

/// <summary>Training run 2026-11-06b, bench-6: "None of the notes mention a wedding", with "Buy stamps for the wedding cards" saved.</summary>
public class NoteQuestionTests
{
    [Theory]
    [InlineData("Is there a note about the wedding?", "wedding")]
    [InlineData("Do I have any notes on the boiler", "boiler")]
    [InlineData("did I note anything about Sanjay's birthday?", "sanjay birthday")]
    [InlineData("Did I write a note about the locker combination?", "locker combination")]
    [InlineData("What did I note about the electrician?", "electrician")]
    public void A_note_question_names_its_topic(string message, string topic)
    {
        Assert.Equal(topic.Split(' '), NoteQuestion.TopicOf(message));
    }

    [Theory]
    [InlineData("Add a note about the wedding")]
    [InlineData("Show me my notes")]
    [InlineData("Is there a reminder about the wedding?")]
    public void Other_messages_are_not_note_questions(string message)
    {
        Assert.Empty(NoteQuestion.TopicOf(message));
    }

    [Theory]
    [InlineData("Buy stamps for the wedding cards", true)]
    [InlineData("Weddings to attend in May", true)]
    [InlineData("Book the venue", false)]
    public void A_note_matches_the_topic(string text, bool matches)
    {
        Assert.Equal(matches, NoteQuestion.Matches(text, ["wedding"]));
    }

    [Fact]
    public void The_answer_quotes_the_notes_found_or_says_none()
    {
        Assert.Equal("One note about “wedding”: “Buy stamps for the wedding cards”.",
            NoteQuestion.Answer(["wedding"], ["Buy stamps for the wedding cards"]));
        Assert.Equal("I couldn't find a note about “wedding”.", NoteQuestion.Answer(["wedding"], []));
    }
}

using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

/// <summary>
/// The detector decides whether a reply gets a corrective model round or a "nothing was saved"
/// warning, so both mistakes matter: a missed claim leaves the user misled, and a false alarm
/// costs a slow extra round or a wrong warning on an honest reply.
/// </summary>
public class ActionClaimDetectorTests
{
    [Theory]
    // The real reply that started this: qwen2.5, 2026-09-11, no tool called.
    [InlineData("I've set a reminder to remind you to submit the report at 19:00 today.")]
    [InlineData("I have created a new goal called Learn Rust for this month.")]
    [InlineData("Done! I added \"Buy milk\" to your planner for tomorrow.")]
    [InlineData("I went ahead and scheduled a reminder for 7pm.")]
    [InlineData("I just marked the task as done.")]
    [InlineData("I'll create a sub-goal for that under Master AI.")]
    [InlineData("Your reminder has been set for 9:00 p.m.")]
    [InlineData("The goal is now created and active.")]
    [InlineData("I've updated your progress on Learn Rust to 40%.")]
    [InlineData("I cancelled the reminder for the dentist.")]
    [InlineData("Sure. I've moved the planner item to Friday.")]
    public void Catches_a_claimed_change(string reply)
    {
        Assert.NotNull(ActionClaimDetector.FindClaim(reply));
    }

    [Theory]
    // Offers and questions.
    [InlineData("I can set a reminder for 7pm if you like.")]
    [InlineData("Shall I create that goal for you?")]
    [InlineData("Would you like me to add it to your planner?")]
    [InlineData("Do you want me to schedule a reminder?")]
    // Proposals under the confirmation gate — these are honest.
    [InlineData("I propose creating a goal called Learn Rust. Please confirm.")]
    [InlineData("I've prepared a reminder for 7pm — it will be saved once you confirm.")]
    // Honest refusals and negatives.
    [InlineData("I haven't created the reminder yet.")]
    [InlineData("I didn't add anything to your planner.")]
    [InlineData("I can't set reminders because the reminders module is turned off.")]
    // Reading existing data back, not changing it.
    [InlineData("You have 3 goals: Learn Rust, Read Books Daily and Master AI.")]
    [InlineData("Your next reminder is at 7pm: submit the report.")]
    [InlineData("Here's a quick review of your current goals.")]
    // First person, but nothing the assistant can change.
    [InlineData("I've updated my understanding of what you need.")]
    [InlineData("I set out the options below.")]
    // Reported from the phone (alpha.6): an intention, ending in a request to go ahead. The
    // amber "nothing was saved" note on this was a false alarm.
    [InlineData("Sure, I'll add a new goal to create a tutorial. Here's the goal: Create Tutorial, "
        + "monthly, starting today. Would you like to proceed with these details?")]
    [InlineData("I'll set a reminder for 7pm. Shall I go ahead?")]
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_honest_replies_alone(string? reply)
    {
        Assert.Null(ActionClaimDetector.FindClaim(reply));
    }

    [Fact]
    public void Returns_the_claiming_sentence_not_the_whole_reply()
    {
        var claim = ActionClaimDetector.FindClaim(
            "Great question. I've set a reminder for 7pm. Anything else?");

        Assert.Equal("I've set a reminder for 7pm.", claim);
    }

    [Fact]
    public void A_question_elsewhere_in_the_reply_does_not_hide_a_claim()
    {
        Assert.NotNull(ActionClaimDetector.FindClaim(
            "Would you like a summary later? I've added the task to your planner."));
    }

    [Fact]
    public void An_intention_with_nothing_asked_is_still_a_claim()
    {
        // The reason "I'll" is in the detector at all: models promise and never call the tool.
        Assert.NotNull(ActionClaimDetector.FindClaim("I'll create a sub-goal for that under Master AI."));
    }

    [Fact]
    public void Asking_to_proceed_does_not_excuse_saying_it_is_already_done()
    {
        Assert.NotNull(ActionClaimDetector.FindClaim(
            "I've set the reminder for 7pm. Would you like to proceed with anything else?"));
    }
}

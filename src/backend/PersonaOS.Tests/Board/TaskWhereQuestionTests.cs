using PersonaOS.Application.Board;
using PersonaOS.Domain.Entities;

namespace PersonaOS.Tests.Board;

/// <summary>Training run 2026-11-06b, bench-5: "TASK-17 is in the backlog. It was added mid-sprint to TASK-16, which is in the running sprint".</summary>
public class TaskWhereQuestionTests
{
    [Theory]
    [InlineData("Is TASK-17 in the running sprint or in the backlog?", 17)]
    [InlineData("is TASK-4 in the backlog", 4)]
    [InlineData("Where is TASK-9?", 9)]
    [InlineData("Which sprint is TASK-2 in?", 2)]
    public void A_where_question_names_its_task(string message, int number)
    {
        Assert.Equal(number, TaskWhereQuestion.Parse(message));
    }

    [Theory]
    [InlineData("Move TASK-17 to the backlog")]
    [InlineData("Is TASK-17 done?")]
    [InlineData("What is TASK-17 about?")]
    public void Other_messages_are_not_where_questions(string message)
    {
        Assert.Null(TaskWhereQuestion.Parse(message));
    }

    [Fact]
    public void A_backlog_task_is_said_to_be_in_the_backlog()
    {
        Assert.Equal("TASK-17 “Sand the deck” is in the backlog, not in a sprint.",
            TaskWhereQuestion.Answer(17, new BoardTask { Number = 17, Title = "Sand the deck", AddedMidSprint = true }));
    }

    [Fact]
    public void A_sprint_task_names_the_sprint_and_its_column()
    {
        var sprint = new Sprint { Number = 2, Name = "Paperwork week", Status = SprintStatuses.Active };
        Assert.Equal("TASK-3 “Read chapter 4” is in the running sprint, SPRINT-2 “Paperwork week”, and is in progress.",
            TaskWhereQuestion.Answer(3, new BoardTask { Number = 3, Title = "Read chapter 4", Sprint = sprint, Status = BoardTaskStatuses.InProgress }));
    }

    [Theory]
    [InlineData("How many value points does TASK-17 have?", 17)]
    [InlineData("how many points is TASK-4", 4)]
    [InlineData("What are TASK-9's points?", 9)]
    [InlineData("What are the story points for TASK-2?", 2)]
    public void A_points_question_names_its_task(string message, int number)
    {
        Assert.Equal(number, TaskWhereQuestion.ParsePoints(message));
    }

    [Theory]
    [InlineData("Give TASK-17 5 points")]
    [InlineData("How many points are in the sprint?")]
    [InlineData("Where is TASK-17?")]
    public void Other_messages_are_not_points_questions(string message)
    {
        Assert.Null(TaskWhereQuestion.ParsePoints(message));
    }

    [Fact]
    public void A_points_answer_says_the_points_or_unestimated()
    {
        Assert.Equal("TASK-17 “Sand the deck” has 5 value points.",
            TaskWhereQuestion.PointsAnswer(17, new BoardTask { Number = 17, Title = "Sand the deck", Points = 5 }));
        Assert.Equal("TASK-4 “Plan trip” has 1 value point.",
            TaskWhereQuestion.PointsAnswer(4, new BoardTask { Number = 4, Title = "Plan trip", Points = 1 }));
        Assert.Equal("TASK-6 “Tidy” has no value points yet (unestimated).",
            TaskWhereQuestion.PointsAnswer(6, new BoardTask { Number = 6, Title = "Tidy" }));
        Assert.Null(TaskWhereQuestion.PointsAnswer(99, null));
    }

    [Fact]
    public void A_task_that_does_not_exist_is_left_to_the_model()
    {
        Assert.Null(TaskWhereQuestion.Answer(99, null));
    }
}

using PersonaOS.Domain.Services;

namespace PersonaOS.Tests.Domain;

public class ItemKeysTests
{
    [Fact]
    public void A_new_item_takes_the_lowest_free_number()
    {
        Assert.Equal(1, KeyNumberAllocator.LowestFree([]));
        Assert.Equal(4, KeyNumberAllocator.LowestFree([1, 2, 3]));
        // TASK-2 was deleted, so its number comes back.
        Assert.Equal(2, KeyNumberAllocator.LowestFree([1, 3, 4]));
    }

    [Theory]
    [InlineData("TASK-12", 12)]
    [InlineData("task-12", 12)]
    [InlineData("task 12", 12)]
    [InlineData("#12", 12)]
    [InlineData("12", 12)]
    public void Reads_the_ways_a_task_key_gets_written(string raw, int expected)
    {
        Assert.Equal(expected, ItemKeys.Parse(raw, ItemKeys.TaskPrefix));
    }

    [Theory]
    [InlineData("GOAL-12")] // the wrong kind of key must not quietly hit a task
    [InlineData("TASK-")]
    [InlineData("TASK-0")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_keys_that_do_not_name_a_task(string? raw)
    {
        Assert.Null(ItemKeys.Parse(raw, ItemKeys.TaskPrefix));
    }

    [Fact]
    public void Formats_keys()
    {
        Assert.Equal("GOAL-3", ItemKeys.Goal(3));
        Assert.Equal("TASK-7", ItemKeys.Task(7));
    }
}

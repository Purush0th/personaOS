using System.Text.Json;
using PersonaOS.Application.Ai;

namespace PersonaOS.Tests.Ai;

public class ToolLabelTests
{
    [Theory]
    [InlineData("get_goals", "Read goals", "Reading goals…", "Read goals")]
    [InlineData("create_goal", "Create goal", "Creating goal…", "Created goal")]
    [InlineData("update_goal_status", "Update goal status", "Updating goal status…", "Updated goal status")]
    [InlineData("cancel_reminder", "Cancel reminder", "Cancelling reminder…", "Cancelled reminder")]
    [InlineData("list_documents", "List documents", "Listing documents…", "Listed documents")]
    [InlineData("remember_about_user", "Remember about you", "Remembering about you…", "Remembered about you")]
    public void Says_what_a_tool_does_in_the_users_words(string tool, string action, string running, string done)
    {
        Assert.Equal(action, ToolLabel.Action(tool));
        Assert.Equal(running, ToolLabel.Running(tool));
        Assert.Equal(done, ToolLabel.Done(tool));
    }

    [Fact]
    public void Reads_a_tool_with_an_unknown_verb_as_plain_words()
    {
        Assert.Equal("Archive goal", ToolLabel.Done("archive_goal"));
    }

    [Fact]
    public void A_receipt_carries_its_label_to_the_client_even_when_stored_before_labels_existed()
    {
        var stored = """[{"Tool":"create_goal","Ok":true,"Summary":"Learn Rust"}]""";

        var receipt = JsonSerializer.Deserialize<List<ToolReceipt>>(stored)![0];
        var sent = JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("Created goal", receipt.Label);
        Assert.Contains("\"label\":\"Created goal\"", sent);
    }
}

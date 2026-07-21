namespace PersonaOS.Application.Goals;

/// <summary>A goal with its computed rollup progress and nested children.</summary>
public record GoalNode(
    int Id,
    string Title,
    string? Description,
    int? ParentGoalId,
    string PeriodType,
    DateOnly PeriodStart,
    string Status,
    int Progress,
    int EffectiveProgress,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<GoalNode> Children);

public record CreateGoalRequest(
    string Title,
    string? Description,
    int? ParentGoalId,
    string PeriodType,
    DateOnly PeriodStart,
    int Progress = 0);

/// <summary>Partial update; null fields are left unchanged.</summary>
public record UpdateGoalRequest(
    string? Title = null,
    string? Description = null,
    string? PeriodType = null,
    DateOnly? PeriodStart = null,
    int? Progress = null);

/// <summary>Invalid input to a goal operation; message is user/model-presentable.</summary>
public class GoalValidationException(string message) : Exception(message);

public interface IGoalService
{
    /// <summary>Root goals with nested children and rollup progress.</summary>
    Task<IReadOnlyList<GoalNode>> GetTreeAsync(bool includeDropped = false, CancellationToken ct = default);

    /// <summary>One goal's subtree, or null when it does not exist.</summary>
    Task<GoalNode?> GetAsync(int id, CancellationToken ct = default);

    Task<GoalNode> CreateAsync(CreateGoalRequest request, CancellationToken ct = default);

    Task<GoalNode?> UpdateAsync(int id, UpdateGoalRequest request, CancellationToken ct = default);

    /// <summary>Sets the status; completing a goal also sets its own progress to 100.</summary>
    Task<GoalNode?> UpdateStatusAsync(int id, string status, CancellationToken ct = default);

    /// <summary>Re-parents a goal (null makes it a root). Rejects cycles.</summary>
    Task<GoalNode?> LinkAsync(int id, int? parentGoalId, CancellationToken ct = default);

    /// <summary>Deletes a goal and its whole subtree. False when it does not exist.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

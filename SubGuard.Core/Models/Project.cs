namespace SubGuard.Core.Models;

public enum ProjectStatus
{
    Active,
    Closed
}

public sealed record Project
{
    public int Id { get; init; }
    public required string ProjectNumber { get; init; }
    public required string Name { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public ProjectStatus Status { get; init; } = ProjectStatus.Active;
}

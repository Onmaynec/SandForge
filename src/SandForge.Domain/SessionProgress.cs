namespace SandForge.Domain;

/// <summary>
/// Host-side progress signal for CLI and TUI presentation. The status is stable and language-neutral;
/// presentation layers choose localized text without changing domain error codes.
/// </summary>
public sealed record SessionProgress(SessionStatus Status, int CompletedCollectors = 0, int TotalCollectors = 0);

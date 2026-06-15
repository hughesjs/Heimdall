namespace Heimdall.Core.Models;

/// <summary>A snapshot of the GitHub API rate limit, read from the last response.</summary>
public record RateLimitInfo(int Remaining, int Limit, DateTimeOffset ResetsAt);

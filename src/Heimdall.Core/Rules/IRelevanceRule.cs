using Heimdall.Core.Models;

namespace Heimdall.Core.Rules;

/// <summary>
/// A single, pure relevance check over an already-enriched <see cref="RunRecord"/>. All I/O and
/// caching (e.g. resolving PR authors) happen in the GitHub gateway before rules run, so a rule is
/// just a field comparison. A run is relevant if <em>any</em> enabled rule matches.
/// </summary>
public interface IRelevanceRule
{
    /// <summary>Stable identifier used as the settings toggle key.</summary>
    string Id { get; }

    /// <summary>Whether this rule is enabled out of the box.</summary>
    bool DefaultEnabled { get; }

    bool IsRelevant(RunRecord run, Identity me, RepoConfig repo);
}

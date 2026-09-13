using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Planning;

internal sealed class IndustryPlanComputationContext(IndustryPlan plan, long marketLocationId, string? marketAccountKey)
{
    public IndustryPlan Plan { get; } = plan;

    public long MarketLocationId { get; } = marketLocationId;

    public string? MarketAccountKey { get; } = marketAccountKey;

    public List<string> HardConflicts { get; } = [];

    public List<string> SoftWarnings { get; } = [];

    public List<string> UncertainFacts { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> CostTraceExplanations { get; } = [];

    public HashSet<string> BlockingNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> NodeHardConflicts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> NodeSoftWarnings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RecipeBackedNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> RecipeSemanticClassByNodeId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> InheritedBlueprintQualityNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ProbabilityAdjustedNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CharacterAdjustedTimeNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CharacterAdjustedProbabilityNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> CharacterAdjustedYieldNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, IReadOnlyList<CharacterPoolEntry>> NodeCandidateCharacters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CharacterPoolEntry> SuggestedCharactersByNodeId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> SuggestedCharacterRationalesByNodeId { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CharacterPoolEntry> PlanningCharactersById { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, OperatorPoolEntry> OperatorsById { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool UsedPlaceholderMarketFacts { get; set; }

    public void AddHardConflict(string message, string? nodeId = null)
    {
        HardConflicts.Add(message);
        Warnings.Add(message);

        if (nodeId is not null)
        {
            BlockingNodeIds.Add(nodeId);
            AddNodeMessage(NodeHardConflicts, nodeId, message);
        }
    }

    public void AddSoftWarning(string message, string? nodeId = null)
    {
        SoftWarnings.Add(message);
        Warnings.Add(message);

        if (nodeId is not null && HardConflicts.Count > 0)
        {
            BlockingNodeIds.Add(nodeId);
        }

        if (nodeId is not null)
        {
            AddNodeMessage(NodeSoftWarnings, nodeId, message);
        }
    }

    public void AddUncertainFact(string message)
    {
        UncertainFacts.Add(message);
        Warnings.Add(message);
    }

    public void AddWarnings(IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
        {
            Warnings.Add(warning);
        }
    }

    public void AddCostTrace(string message)
    {
        CostTraceExplanations.Add(message);
    }

    public void RecordRecipeSemanticClass(string nodeId, string plannerSemanticClass)
    {
        RecipeSemanticClassByNodeId[nodeId] = plannerSemanticClass;
    }

    private static void AddNodeMessage(
        IDictionary<string, List<string>> target,
        string nodeId,
        string message)
    {
        if (!target.TryGetValue(nodeId, out var messages))
        {
            messages = [];
            target[nodeId] = messages;
        }

        messages.Add(message);
    }
}

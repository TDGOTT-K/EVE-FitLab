using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.CharacterProgression;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.CharacterProgression;

public sealed class InMemoryCharacterProgressionService : ICharacterProgressionService
{
    private const int MaxSkillLevel = 5;
    private readonly ICharacterPoolService? characterPoolService;
    private readonly TimeProvider timeProvider;
    private readonly ICharacterProgressionSkillCatalogProvider skillCatalogProvider;

    private static readonly long[] LevelThresholds =
    [
        0L,
        250L,
        1415L,
        8000L,
        45255L,
        256000L
    ];

    internal InMemoryCharacterProgressionService(
        ICharacterPoolService? characterPoolService,
        TimeProvider timeProvider,
        ICharacterProgressionSkillCatalogProvider skillCatalogProvider)
    {
        this.characterPoolService = characterPoolService;
        this.timeProvider = timeProvider;
        this.skillCatalogProvider = skillCatalogProvider;
    }

    public InMemoryCharacterProgressionService(ICharacterPoolService? characterPoolService, TimeProvider timeProvider)
        : this(characterPoolService, timeProvider, new EmbeddedCharacterProgressionSkillCatalogProvider())
    {
    }

    public InMemoryCharacterProgressionService(ICharacterPoolService? characterPoolService, TimeProvider timeProvider, string sdeRootPath)
        : this(characterPoolService, timeProvider, new CharacterProgressionSkillCatalogProvider(sdeRootPath))
    {
    }

    public UseCaseResult<CharacterProgressionSkillCatalogView> GetSkillCatalog(GetCharacterProgressionSkillCatalogRequest request)
    {
        var traceId = CreateTraceId("character_progression.get_skill_catalog");
        return UseCaseResult<CharacterProgressionSkillCatalogView>.Success(
            new CharacterProgressionSkillCatalogView
            {
                Skills = skillCatalogProvider.List()
            },
            "Loaded character progression skill catalog.",
            traceId);
    }

    public UseCaseResult<CharacterProgressionSimulationView> Simulate(SimulateCharacterProgressionRequest request)
    {
        var traceId = CreateTraceId("character_progression.simulate");
        if (request.Targets.Count == 0)
        {
            return UseCaseResult<CharacterProgressionSimulationView>.Failure(
                UseCaseStatus.InvalidInput,
                "Character progression simulation requires at least one target skill.",
                traceId,
                ["Targets cannot be empty."]);
        }

        var characterResolution = ResolveCharacterContext(request, traceId);
        if (!characterResolution.IsSuccess)
        {
            return UseCaseResult<CharacterProgressionSimulationView>.Failure(
                characterResolution.Status,
                characterResolution.Summary,
                traceId,
                characterResolution.Errors,
                characterResolution.Warnings);
        }

        var currentSkillMap = BuildCurrentSkillMap(characterResolution.Data!.SkillLevels);
        var expansionResult = ExpandTargets(request.Targets, currentSkillMap, traceId);
        if (!expansionResult.IsSuccess)
        {
            return UseCaseResult<CharacterProgressionSimulationView>.Failure(
                expansionResult.Status,
                expansionResult.Summary,
                traceId,
                expansionResult.Errors,
                expansionResult.Warnings);
        }

        var orderedSkillKeys = TopologicalSort(expansionResult.Data!.RequiredTargetLevels, traceId);
        if (!orderedSkillKeys.IsSuccess)
        {
            return UseCaseResult<CharacterProgressionSimulationView>.Failure(
                orderedSkillKeys.Status,
                orderedSkillKeys.Summary,
                traceId,
                orderedSkillKeys.Errors,
                orderedSkillKeys.Warnings);
        }

        var effectiveTotalSp = ResolveStartingTotalSkillPoints(
            request.CurrentTotalSkillPoints,
            characterResolution.Data.KnownSkillPoints,
            request.SupplementStrategy.UnallocatedSkillPoints);

        var supplementState = new SupplementState(
            request.SupplementStrategy.UnallocatedSkillPoints,
            effectiveTotalSp,
            request.SupplementStrategy.MaxInjectors);

        var reverseDependencies = BuildReverseDependencies(expansionResult.Data.RequiredTargetLevels.Keys);
        var plannedSkills = new List<CharacterProgressionPlannedSkill>();
        decimal totalPassiveHours = 0m;
        long totalRequiredSp = 0L;
        long totalSupplementedSp = 0L;
        long totalPassiveSp = 0L;

        foreach (var skillKey in orderedSkillKeys.Data!)
        {
            var definition = skillCatalogProvider.GetRequired(skillKey);
            var currentLevel = currentSkillMap.TryGetValue(skillKey, out var currentSkill)
                ? currentSkill.Level
                : 0;
            var currentSkillSp = currentSkillMap.TryGetValue(skillKey, out currentSkill)
                ? currentSkill.SkillPoints
                : 0L;
            var targetLevel = expansionResult.Data.RequiredTargetLevels[skillKey];
            var targetSkillSp = GetSkillPointsForLevel(definition.Rank, targetLevel);
            var remainingSp = Math.Max(0L, targetSkillSp - currentSkillSp);
            var supplementedSp = 0L;

            if (remainingSp > 0)
            {
                supplementedSp = ApplySupplementStrategy(
                    request.SupplementStrategy.Kind,
                    supplementState,
                    remainingSp);
            }

            var passiveSp = remainingSp - supplementedSp;
            var trainingRatePerHour = GetTrainingRatePerHour(request.Attributes, definition);
            var passiveHours = passiveSp <= 0
                ? 0m
                : Math.Round(passiveSp / trainingRatePerHour, 6, MidpointRounding.AwayFromZero);

            totalRequiredSp += remainingSp;
            totalSupplementedSp += supplementedSp;
            totalPassiveSp += passiveSp;
            totalPassiveHours += passiveHours;

            plannedSkills.Add(new CharacterProgressionPlannedSkill
            {
                SkillKey = definition.SkillKey,
                DisplayName = definition.DisplayName,
                IsExplicitTarget = expansionResult.Data.ExplicitTargets.Contains(skillKey),
                SkillRank = definition.Rank,
                CurrentLevel = currentLevel,
                TargetLevel = targetLevel,
                CurrentSkillPoints = currentSkillSp,
                TargetSkillPoints = targetSkillSp,
                RemainingSkillPoints = remainingSp,
                SupplementedSkillPoints = supplementedSp,
                PassiveTrainingSkillPoints = passiveSp,
                TrainingRatePerHour = trainingRatePerHour,
                PassiveTrainingHours = passiveHours,
                PrimaryAttribute = definition.PrimaryAttribute,
                SecondaryAttribute = definition.SecondaryAttribute,
                DirectPrerequisites = definition.Prerequisites,
                RequiredBySkillKeys = reverseDependencies.TryGetValue(skillKey, out var requiredBy)
                    ? requiredBy.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()
                    : Array.Empty<string>()
            });

            currentSkillMap[skillKey] = new CharacterSkillState(targetLevel, targetSkillSp);
        }

        var projectedCompletionAtUtc = timeProvider.GetUtcNow().AddHours((double)totalPassiveHours);
        return UseCaseResult<CharacterProgressionSimulationView>.Success(
            new CharacterProgressionSimulationView
            {
                WorkspaceId = request.WorkspaceId,
                CharacterId = characterResolution.Data.CharacterId,
                CharacterName = characterResolution.Data.CharacterName,
                Attributes = request.Attributes,
                RequestedTargets = request.Targets
                    .Select(target => new CharacterProgressionTargetSkillRequest
                    {
                        SkillKey = target.SkillKey.Trim(),
                        TargetLevel = target.TargetLevel
                    })
                    .ToArray(),
                PrerequisiteGraph = expansionResult.Data.GraphEdges
                    .OrderBy(edge => edge.RequiredSkillKey, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(edge => edge.DependentSkillKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                PlannedSkills = plannedSkills,
                StartingKnownSkillPoints = characterResolution.Data.KnownSkillPoints,
                StartingEffectiveTotalSkillPoints = effectiveTotalSp,
                TotalRequiredSkillPoints = totalRequiredSp,
                SupplementedSkillPoints = totalSupplementedSp,
                PassiveTrainingSkillPoints = totalPassiveSp,
                PassiveTrainingHours = Math.Round(totalPassiveHours, 6, MidpointRounding.AwayFromZero),
                ProjectedCompletionAtUtc = projectedCompletionAtUtc,
                RemainingUnallocatedSkillPoints = supplementState.RemainingUnallocatedSkillPoints,
                InjectorsConsumed = supplementState.InjectorsConsumed,
                SupplementStrategy = request.SupplementStrategy,
                SupplementUsages = supplementState.Usages
            },
            "Simulated character progression training path.",
            traceId,
            characterResolution.Warnings);
    }

    private UseCaseResult<IReadOnlyList<string>> TopologicalSort(
        IReadOnlyDictionary<string, int> requiredTargetLevels,
        string traceId)
    {
        var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        bool Visit(string skillKey, List<string> stack)
        {
            if (visited.TryGetValue(skillKey, out var state))
            {
                if (state == 2)
                {
                    return true;
                }

                if (state == 1)
                {
                    return false;
                }
            }

            visited[skillKey] = 1;
            stack.Add(skillKey);

            var definition = skillCatalogProvider.GetRequired(skillKey);
            foreach (var prerequisite in definition.Prerequisites)
            {
                if (!requiredTargetLevels.ContainsKey(prerequisite.SkillKey))
                {
                    continue;
                }

                if (!Visit(prerequisite.SkillKey, stack))
                {
                    return false;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            visited[skillKey] = 2;
            ordered.Add(skillKey);
            return true;
        }

        foreach (var skillKey in requiredTargetLevels.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            if (visited.ContainsKey(skillKey))
            {
                continue;
            }

            if (!Visit(skillKey, []))
            {
                return UseCaseResult<IReadOnlyList<string>>.Failure(
                    UseCaseStatus.Error,
                    "Detected a cycle in the character progression prerequisite graph.",
                    traceId,
                    ["Skill prerequisite graph contains a cycle."]);
            }
        }

        return UseCaseResult<IReadOnlyList<string>>.Success(
            ordered,
            "Ordered skills by prerequisite dependency.",
            traceId);
    }

    private Dictionary<string, HashSet<string>> BuildReverseDependencies(IEnumerable<string> skillKeys)
    {
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var skillKey in skillKeys)
        {
            var definition = skillCatalogProvider.GetRequired(skillKey);
            foreach (var prerequisite in definition.Prerequisites)
            {
                if (!reverse.TryGetValue(prerequisite.SkillKey, out var dependents))
                {
                    dependents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    reverse[prerequisite.SkillKey] = dependents;
                }

                dependents.Add(skillKey);
            }
        }

        return reverse;
    }

    private static long ResolveStartingTotalSkillPoints(long? requestedTotalSp, long knownSkillPoints, long unallocatedSkillPoints)
    {
        var baseline = Math.Max(knownSkillPoints, knownSkillPoints + Math.Max(0L, unallocatedSkillPoints));
        if (!requestedTotalSp.HasValue)
        {
            return baseline;
        }

        return Math.Max(requestedTotalSp.Value, baseline);
    }

    private static long ApplySupplementStrategy(
        CharacterProgressionSupplementStrategyKind strategyKind,
        SupplementState state,
        long remainingSp)
    {
        if (remainingSp <= 0)
        {
            return 0L;
        }

        var supplemented = 0L;

        if (strategyKind is CharacterProgressionSupplementStrategyKind.UseUnallocatedFirst
            or CharacterProgressionSupplementStrategyKind.UseUnallocatedAndInjectors)
        {
            var fromUnallocated = Math.Min(remainingSp, state.RemainingUnallocatedSkillPoints);
            if (fromUnallocated > 0)
            {
                state.RemainingUnallocatedSkillPoints -= fromUnallocated;
                state.EffectiveTotalSkillPoints += fromUnallocated;
                supplemented += fromUnallocated;
                state.Usages.Add(new CharacterProgressionSupplementUsage
                {
                    Sequence = state.Usages.Count + 1,
                    Kind = CharacterProgressionSupplementKind.UnallocatedSkillPoints,
                    GrantedSkillPoints = fromUnallocated,
                    EffectiveTotalSkillPointsAfterUse = state.EffectiveTotalSkillPoints,
                    RemainingUnallocatedSkillPointsAfterUse = state.RemainingUnallocatedSkillPoints,
                    Description = "Applied unallocated skill points before passive training."
                });
            }
        }

        if (strategyKind != CharacterProgressionSupplementStrategyKind.UseUnallocatedAndInjectors)
        {
            return supplemented;
        }

        var residual = remainingSp - supplemented;
        while (residual > 0 && state.InjectorsConsumed < state.MaxInjectors)
        {
            var injectorYield = GetInjectorYield(state.EffectiveTotalSkillPoints);
            if (injectorYield <= 0)
            {
                break;
            }

            var granted = Math.Min(residual, injectorYield);
            state.InjectorsConsumed += 1;
            state.EffectiveTotalSkillPoints += granted;
            supplemented += granted;
            residual -= granted;
            state.Usages.Add(new CharacterProgressionSupplementUsage
            {
                Sequence = state.Usages.Count + 1,
                Kind = CharacterProgressionSupplementKind.SkillInjector,
                GrantedSkillPoints = granted,
                EffectiveTotalSkillPointsAfterUse = state.EffectiveTotalSkillPoints,
                RemainingUnallocatedSkillPointsAfterUse = state.RemainingUnallocatedSkillPoints,
                Description = $"Applied injector #{state.InjectorsConsumed} using current diminishing-return bracket."
            });
        }

        return supplemented;
    }

    private static int GetAttributeValue(CharacterProgressionAttributeProfile profile, CharacterAttributeKind kind)
    {
        return kind switch
        {
            CharacterAttributeKind.Intelligence => profile.Intelligence,
            CharacterAttributeKind.Memory => profile.Memory,
            CharacterAttributeKind.Perception => profile.Perception,
            CharacterAttributeKind.Willpower => profile.Willpower,
            CharacterAttributeKind.Charisma => profile.Charisma,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported attribute kind.")
        };
    }

    private static decimal GetTrainingRatePerHour(
        CharacterProgressionAttributeProfile attributes,
        CharacterProgressionSkillDefinition definition)
    {
        var primary = Math.Max(1, GetAttributeValue(attributes, definition.PrimaryAttribute));
        var secondary = Math.Max(1, GetAttributeValue(attributes, definition.SecondaryAttribute));
        return primary * 60m + secondary * 30m;
    }

    private static long GetInjectorYield(long totalSkillPoints)
    {
        if (totalSkillPoints < 5_000_000L)
        {
            return 500_000L;
        }

        if (totalSkillPoints < 50_000_000L)
        {
            return 400_000L;
        }

        if (totalSkillPoints < 80_000_000L)
        {
            return 300_000L;
        }

        return 150_000L;
    }

    private Dictionary<string, CharacterSkillState> BuildCurrentSkillMap(IReadOnlyList<CharacterIndustrySkillLevel> skillLevels)
    {
        var map = new Dictionary<string, CharacterSkillState>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skillLevels)
        {
            if (string.IsNullOrWhiteSpace(skill.SkillKey))
            {
                continue;
            }

            var normalizedKey = skill.SkillKey.Trim();
            if (!skillCatalogProvider.TryGet(normalizedKey, out var definition))
            {
                continue;
            }

            var level = Math.Clamp(skill.Level, 0, MaxSkillLevel);
            map[normalizedKey] = new CharacterSkillState(level, GetSkillPointsForLevel(definition.Rank, level));
        }

        return map;
    }

    private static long GetSkillPointsForLevel(int rank, int level)
    {
        if (rank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "Skill rank must be greater than zero.");
        }

        var normalizedLevel = Math.Clamp(level, 0, MaxSkillLevel);
        return LevelThresholds[normalizedLevel] * rank;
    }

    private static string CreateTraceId(string useCaseName) => $"{useCaseName}:{Guid.NewGuid():N}";

    private UseCaseResult<TargetExpansionResult> ExpandTargets(
        IReadOnlyList<CharacterProgressionTargetSkillRequest> targets,
        IReadOnlyDictionary<string, CharacterSkillState> currentSkills,
        string traceId)
    {
        var targetLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var explicitTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var graphEdges = new List<CharacterProgressionPrerequisiteEdge>();

        UseCaseResult<TargetExpansionResult>? failure = null;

        void Visit(string skillKey, int targetLevel, string? dependentSkillKey)
        {
            if (failure is not null)
            {
                return;
            }

            if (!skillCatalogProvider.TryGet(skillKey, out var definition))
            {
                failure = UseCaseResult<TargetExpansionResult>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Character progression simulation references an unsupported skill key.",
                    traceId,
                    [$"Skill '{skillKey}' is not in the supported progression catalog."]);
                return;
            }

            var normalizedLevel = Math.Clamp(targetLevel, 0, MaxSkillLevel);
            if (normalizedLevel <= 0)
            {
                return;
            }

            if (dependentSkillKey is not null)
            {
                graphEdges.Add(new CharacterProgressionPrerequisiteEdge
                {
                    RequiredSkillKey = skillKey,
                    DependentSkillKey = dependentSkillKey,
                    RequiredLevel = normalizedLevel
                });
            }

            if (currentSkills.TryGetValue(skillKey, out var current) && current.Level >= normalizedLevel)
            {
                if (!targetLevels.ContainsKey(skillKey))
                {
                    targetLevels[skillKey] = current.Level;
                }

                return;
            }

            if (!targetLevels.TryGetValue(skillKey, out var existingLevel) || normalizedLevel > existingLevel)
            {
                targetLevels[skillKey] = normalizedLevel;
            }

            foreach (var prerequisite in definition.Prerequisites)
            {
                Visit(prerequisite.SkillKey, prerequisite.RequiredLevel, definition.SkillKey);
            }
        }

        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.SkillKey))
            {
                return UseCaseResult<TargetExpansionResult>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Character progression target skill key is required.",
                    traceId,
                    ["Targets[].SkillKey cannot be empty."]);
            }

            if (target.TargetLevel is < 1 or > MaxSkillLevel)
            {
                return UseCaseResult<TargetExpansionResult>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Character progression target level must stay between 1 and 5.",
                    traceId,
                    [$"Target level for '{target.SkillKey}' must stay between 1 and 5."]);
            }

            var normalizedKey = target.SkillKey.Trim();
            explicitTargets.Add(normalizedKey);
            Visit(normalizedKey, target.TargetLevel, dependentSkillKey: null);

            if (failure is not null)
            {
                return failure;
            }
        }

        return UseCaseResult<TargetExpansionResult>.Success(
            new TargetExpansionResult(targetLevels, explicitTargets, graphEdges),
            "Expanded requested targets through prerequisite graph.",
            traceId);
    }

    private UseCaseResult<CharacterResolution> ResolveCharacterContext(
        SimulateCharacterProgressionRequest request,
        string traceId)
    {
        if (!string.IsNullOrWhiteSpace(request.CharacterId))
        {
            if (string.IsNullOrWhiteSpace(request.WorkspaceId))
            {
                return UseCaseResult<CharacterResolution>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Workspace id is required when character id is supplied.",
                    traceId,
                    ["WorkspaceId is required when CharacterId is provided."]);
            }

            if (characterPoolService is null)
            {
                return UseCaseResult<CharacterResolution>.Failure(
                    UseCaseStatus.DependencyUnavailable,
                    "Character progression service cannot resolve character pool state.",
                    traceId,
                    ["Character pool service is not configured."]);
            }

            var listResult = characterPoolService.List(new ListCharacterPoolRequest
            {
                WorkspaceId = request.WorkspaceId.Trim()
            });
            if (!listResult.IsSuccess || listResult.Data is null)
            {
                return UseCaseResult<CharacterResolution>.Failure(
                    listResult.Status,
                    listResult.Summary,
                    traceId,
                    listResult.Errors,
                    listResult.Warnings);
            }

            var character = listResult.Data.Characters.FirstOrDefault(entry =>
                string.Equals(entry.CharacterId, request.CharacterId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (character is null)
            {
                return UseCaseResult<CharacterResolution>.Failure(
                    UseCaseStatus.NotFound,
                    "Character progression simulation could not resolve the requested character.",
                    traceId,
                    [$"Character '{request.CharacterId}' was not found in workspace '{request.WorkspaceId}'."]);
            }

            var skillLevels = character.CapabilityProfile.Industry.SkillLevels;
            var knownSkillPoints = skillLevels
                .Where(skill => skillCatalogProvider.TryGet(skill.SkillKey, out _))
                .Sum(skill =>
                {
                    var definition = skillCatalogProvider.GetRequired(skill.SkillKey);
                    return GetSkillPointsForLevel(definition.Rank, skill.Level);
                });

            return UseCaseResult<CharacterResolution>.Success(
                new CharacterResolution(
                    character.CharacterId,
                    character.DisplayName,
                    skillLevels,
                    knownSkillPoints),
                "Resolved current skills from character pool entry.",
                traceId);
        }

        var directSkillLevels = request.CurrentSkillLevels;
        var directKnownSp = directSkillLevels
            .Where(skill => skillCatalogProvider.TryGet(skill.SkillKey, out _))
            .Sum(skill =>
            {
                var definition = skillCatalogProvider.GetRequired(skill.SkillKey);
                return GetSkillPointsForLevel(definition.Rank, skill.Level);
            });

        return UseCaseResult<CharacterResolution>.Success(
            new CharacterResolution(
                request.CharacterId,
                null,
                directSkillLevels,
                directKnownSp),
            "Resolved progression simulation from direct skill payload.",
            traceId);
    }

    private sealed record CharacterResolution(
        string? CharacterId,
        string? CharacterName,
        IReadOnlyList<CharacterIndustrySkillLevel> SkillLevels,
        long KnownSkillPoints);

    private sealed record CharacterSkillState(int Level, long SkillPoints);

    private sealed record TargetExpansionResult(
        IReadOnlyDictionary<string, int> RequiredTargetLevels,
        IReadOnlySet<string> ExplicitTargets,
        IReadOnlyList<CharacterProgressionPrerequisiteEdge> GraphEdges);

    private sealed class SupplementState(
        long remainingUnallocatedSkillPoints,
        long effectiveTotalSkillPoints,
        int maxInjectors)
    {
        public long RemainingUnallocatedSkillPoints { get; set; } = Math.Max(0L, remainingUnallocatedSkillPoints);

        public long EffectiveTotalSkillPoints { get; set; } = Math.Max(0L, effectiveTotalSkillPoints);

        public int MaxInjectors { get; } = Math.Max(0, maxInjectors);

        public int InjectorsConsumed { get; set; }

        public List<CharacterProgressionSupplementUsage> Usages { get; } = [];
    }
}

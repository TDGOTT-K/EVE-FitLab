using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.Planning;

namespace EdenOS.Application.Planning;

internal static class IndustryPlanTimeEstimator
{
    public static PlanTimeEstimate BuildTimeModel(
        IndustryPlanComputationContext context,
        IndustrialRecipeResolver recipeResolver)
    {
        var durations = context.Plan.Nodes.ToDictionary(
            node => node.NodeId,
            node => ResolveDurationHours(node, context, recipeResolver),
            StringComparer.OrdinalIgnoreCase);

        var incoming = context.Plan.Nodes.ToDictionary(
            node => node.NodeId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var outgoing = context.Plan.Nodes.ToDictionary(
            node => node.NodeId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var link in context.Plan.Links)
        {
            if (!incoming.ContainsKey(link.ToNodeId) || !outgoing.ContainsKey(link.FromNodeId))
            {
                continue;
            }

            incoming[link.ToNodeId].Add(link.FromNodeId);
            outgoing[link.FromNodeId].Add(link.ToNodeId);
        }

        var indegree = incoming.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(context.Plan.Nodes
            .Select(node => node.NodeId)
            .Where(nodeId => indegree[nodeId] == 0));
        var ordered = new List<string>(context.Plan.Nodes.Count);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            ordered.Add(nodeId);

            foreach (var next in outgoing[nodeId])
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (ordered.Count != context.Plan.Nodes.Count)
        {
            context.AddHardConflict("Plan graph contains a cycle, so time estimates fall back to aggregate duration.");

            var aggregateSegments = context.Plan.Nodes
                .Select(node => new PlanTimeSegment
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    DurationHours = durations[node.NodeId],
                    ScheduledStartHours = 0m,
                    ScheduledFinishHours = durations[node.NodeId],
                    OnCriticalPath = true,
                    EstimatedManualLoadUnits = ResolveOperatorManualLoadUnits(node),
                    DependsOnNodeIds = incoming[node.NodeId].ToArray()
                })
                .ToArray();

            var aggregateDuration = Math.Round(aggregateSegments.Sum(segment => segment.DurationHours), 2);
            return new PlanTimeEstimate
            {
                TotalDurationHours = aggregateDuration,
                CriticalPathHours = aggregateDuration,
                Segments = aggregateSegments,
                CharacterSchedules = Array.Empty<PlanCharacterSchedule>(),
                OperatorSchedules = Array.Empty<PlanOperatorSchedule>(),
                CriticalNodeIds = aggregateSegments.Select(segment => segment.NodeId).ToArray(),
                Explanations = ["Cycle detected, so durations are aggregated instead of scheduled via DAG traversal."]
            };
        }

        var nodesById = context.Plan.Nodes.ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);
        var startTimes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var finishTimes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var predecessor = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var assignmentsByNodeId = new Dictionary<string, ScheduledNodeAssignment>(StringComparer.OrdinalIgnoreCase);
        var characterStates = new Dictionary<string, CharacterScheduleState>(StringComparer.OrdinalIgnoreCase);
        var operatorStates = new Dictionary<string, OperatorScheduleState>(StringComparer.OrdinalIgnoreCase);

        foreach (var nodeId in ordered)
        {
            var node = nodesById[nodeId];
            var parents = incoming[nodeId];
            var dependencyPredecessorId = parents
                .OrderByDescending(parentId => finishTimes[parentId])
                .FirstOrDefault();
            var dependencyReadyAt = dependencyPredecessorId is null ? 0m : finishTimes[dependencyPredecessorId];

            var assignment = TryScheduleAcrossCharacters(
                context,
                node,
                durations[nodeId],
                dependencyReadyAt,
                characterStates,
                operatorStates);

            if (assignment is null)
            {
                startTimes[nodeId] = dependencyReadyAt;
                finishTimes[nodeId] = Math.Round(dependencyReadyAt + durations[nodeId], 2);
                predecessor[nodeId] = dependencyPredecessorId;
                continue;
            }

            assignmentsByNodeId[nodeId] = assignment;
            startTimes[nodeId] = assignment.StartHours;
            finishTimes[nodeId] = assignment.FinishHours;
            predecessor[nodeId] = ResolveDominantPredecessor(
                dependencyPredecessorId,
                assignment.BlockingCharacterNodeId,
                dependencyReadyAt,
                assignment.CharacterReadyAt);
        }

        var terminalNodeId = finishTimes
            .OrderByDescending(pair => pair.Value)
            .FirstOrDefault()
            .Key;
        var criticalNodeIds = new List<string>();

        while (terminalNodeId is not null)
        {
            criticalNodeIds.Add(terminalNodeId);
            terminalNodeId = predecessor[terminalNodeId];
        }

        criticalNodeIds.Reverse();
        var criticalPath = new HashSet<string>(criticalNodeIds, StringComparer.OrdinalIgnoreCase);

        var segments = context.Plan.Nodes
            .Select(node =>
            {
                assignmentsByNodeId.TryGetValue(node.NodeId, out var assignment);
                var primaryAllocation = assignment?.CharacterAllocations
                    .OrderByDescending(allocation => allocation.ConsumedSlots)
                    .ThenBy(allocation => allocation.CharacterName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                var operatorAllocations = assignment is null
                    ? Array.Empty<ResolvedOperatorAllocation>()
                    : ResolveOperatorAllocations(context, assignment);
                var primaryOperatorAllocation = operatorAllocations
                    .OrderByDescending(allocation => allocation.ConsumedCharacterCount)
                    .ThenBy(allocation => allocation.OperatorName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                var manualLoadUnits = ResolveOperatorManualLoadUnits(node);

                return new PlanTimeSegment
                {
                    NodeId = node.NodeId,
                    Title = node.Title,
                    DurationHours = durations[node.NodeId],
                    ScheduledStartHours = startTimes[node.NodeId],
                    ScheduledFinishHours = finishTimes[node.NodeId],
                    OnCriticalPath = criticalPath.Contains(node.NodeId),
                    AssignedCharacterId = primaryAllocation?.CharacterId,
                    AssignedCharacterName = primaryAllocation?.CharacterName,
                    AssignedOperatorId = primaryOperatorAllocation?.OperatorId,
                    AssignedOperatorName = primaryOperatorAllocation?.OperatorName,
                    AssignedCharacterIds = assignment?.CharacterAllocations.Select(allocation => allocation.CharacterId).ToArray()
                        ?? Array.Empty<string>(),
                    AssignedCharacterNames = assignment?.CharacterAllocations.Select(allocation => allocation.CharacterName).ToArray()
                        ?? Array.Empty<string>(),
                    AssignedOperatorIds = operatorAllocations.Select(allocation => allocation.OperatorId).ToArray(),
                    AssignedOperatorNames = operatorAllocations.Select(allocation => allocation.OperatorName).ToArray(),
                    ParallelCharacterCount = assignment?.CharacterAllocations.Count ?? 0,
                    ConsumedCharacterSlots = assignment?.ConsumedSlots ?? 0,
                    EstimatedManualLoadUnits = manualLoadUnits,
                    CharacterAllocations = assignment?.CharacterAllocations
                        .Select(allocation => new PlanTimeCharacterAllocation
                        {
                            CharacterId = allocation.CharacterId,
                            CharacterName = allocation.CharacterName,
                            ConsumedSlots = allocation.ConsumedSlots,
                            ScheduledStartHours = assignment.StartHours,
                            ScheduledFinishHours = assignment.FinishHours
                        })
                        .ToArray()
                        ?? Array.Empty<PlanTimeCharacterAllocation>(),
                    DependsOnNodeIds = incoming[node.NodeId].ToArray(),
                    AssignmentRationale = BuildAssignmentRationale(context, node, assignment)
                };
            })
            .ToArray();

        var criticalPathHours = finishTimes.Count == 0 ? 0m : finishTimes.Values.Max();
        var characterSchedules = characterStates.Values
            .Where(state => state.AssignedNodeIds.Count > 0)
            .OrderByDescending(state => state.AssignedNodeIds.Count)
            .ThenBy(state => state.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Select(state => new PlanCharacterSchedule
            {
                CharacterId = state.CharacterId,
                CharacterName = state.CharacterName,
                AssignedNodeCount = state.AssignedNodeIds.Count,
                ScheduledHours = Math.Round(state.TotalAssignedDurationHours, 2),
                PeakConsumedSlots = state.PeakConsumedSlots,
                AssignedNodeIds = state.AssignedNodeIds.ToArray()
            })
            .ToArray();
        var operatorSchedules = BuildOperatorSchedules(context, segments);
        AnalyzeOperatorCapacity(context, operatorSchedules);

        var explanations = new List<string>
        {
            "Time estimate follows the current plan graph as a dependency DAG.",
            context.RecipeBackedNodeIds.Count > 0
                ? "Industrial node durations use explicit metadata when present, then real SDE activity times when available, then conservative defaults."
                : "Node durations come from metadata when present, otherwise from conservative defaults per node kind."
        };

        if (characterSchedules.Length > 0)
        {
            explanations.Add($"Character-aware scheduling assigned {assignmentsByNodeId.Count} node(s) across {characterSchedules.Length} character(s).");
        }

        if (operatorSchedules.Count > 0)
        {
            explanations.Add($"Scheduled work currently spans {operatorSchedules.Count} operator(s).");
        }

        if (context.OperatorsById.Values.Any(operatorEntry => operatorEntry.MaxDailyManualOperations.HasValue))
        {
            explanations.Add("Operator manual-operation budgets were checked against the scheduled weighted manual-load distribution.");
        }

        if (context.Plan.Preferences.PreferLowerCharacterLoad)
        {
            explanations.Add("Scheduling favored lower-loaded qualified characters when availability was otherwise comparable.");
        }

        if (context.OperatorsById.Values.Any(operatorEntry => operatorEntry.MaxDailyManualOperations.HasValue))
        {
            explanations.Add("Scheduling also tried to avoid assigning new work to operators already near their weighted manual-operation budget.");
        }

        if (assignmentsByNodeId.Values.Any(assignment => assignment.CharacterAllocations.Count > 1))
        {
            explanations.Add("Some nodes were split across multiple characters so one logical production step could consume several pilots in parallel.");
        }

        if (assignmentsByNodeId.Values.Any(assignment => assignment.SlotShortageFactor > 1m))
        {
            explanations.Add("Some nodes were scheduled with fewer slots than they requested, so their durations were stretched to reflect serialized work.");
        }

        if (context.CharacterAdjustedTimeNodeIds.Count > 0)
        {
            explanations.Add($"Suggested character time bonuses adjusted {context.CharacterAdjustedTimeNodeIds.Count} node(s).");
        }

        if (context.CharacterAdjustedProbabilityNodeIds.Count > 0)
        {
            explanations.Add($"Suggested character invention bonuses reduced expected attempt counts for {context.CharacterAdjustedProbabilityNodeIds.Count} node(s).");
        }

        if (context.CharacterAdjustedYieldNodeIds.Count > 0)
        {
            explanations.Add($"Suggested character reprocessing yield bonuses adjusted effective output per run for {context.CharacterAdjustedYieldNodeIds.Count} node(s).");
        }

        return new PlanTimeEstimate
        {
            TotalDurationHours = criticalPathHours,
            CriticalPathHours = criticalPathHours,
            Segments = segments,
            CharacterSchedules = characterSchedules,
            OperatorSchedules = operatorSchedules,
            CriticalNodeIds = criticalNodeIds,
            Explanations = explanations
        };
    }

    private static void AnalyzeOperatorCapacity(
        IndustryPlanComputationContext context,
        IReadOnlyList<PlanOperatorSchedule> operatorSchedules)
    {
        foreach (var schedule in operatorSchedules)
        {
            if (!context.OperatorsById.TryGetValue(schedule.OperatorId, out var operatorEntry))
            {
                continue;
            }

            if (!operatorEntry.MaxDailyManualOperations.HasValue)
            {
                continue;
            }

            var budget = operatorEntry.MaxDailyManualOperations.Value;
            if (schedule.TotalManualLoadUnits <= budget)
            {
                continue;
            }

            var overBy = Math.Round(schedule.TotalManualLoadUnits - budget, 2);
            var message =
                $"Operator '{schedule.OperatorName}' is assigned {schedule.AssignedNodeCount} scheduled node(s) with weighted manual load {schedule.TotalManualLoadUnits:N2}, exceeding its manual-operation budget {budget:N2} by {overBy:N2}.";

            context.AddHardConflict(message);
            foreach (var nodeId in schedule.AssignedNodeIds)
            {
                var nodeTitle = context.Plan.Nodes
                    .FirstOrDefault(node => node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                    ?.Title;
                context.AddHardConflict(
                    nodeTitle is null
                        ? $"Assigned operator '{schedule.OperatorName}' is overloaded beyond manual-operation budget."
                        : $"Node '{nodeTitle}' is assigned to overloaded operator '{schedule.OperatorName}', whose weighted manual load exceeds its manual-operation budget.",
                    nodeId);
            }
        }
    }

    private static IReadOnlyList<ResolvedOperatorAllocation> ResolveOperatorAllocations(
        IndustryPlanComputationContext context,
        ScheduledNodeAssignment assignment)
    {
        return assignment.CharacterAllocations
            .Select(allocation =>
            {
                var operatorId = context.PlanningCharactersById.GetValueOrDefault(allocation.CharacterId)?.OperatorId;
                if (operatorId is null || !context.OperatorsById.TryGetValue(operatorId, out var operatorEntry))
                {
                    return null;
                }

                return new ResolvedOperatorAllocation(
                    operatorEntry.OperatorId,
                    operatorEntry.DisplayName,
                    allocation.ConsumedSlots);
            })
            .Where(allocation => allocation is not null)
            .GroupBy(allocation => allocation!.OperatorId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ResolvedOperatorAllocation(
                group.Key,
                group.First()!.OperatorName,
                group.Sum(item => item!.ConsumedCharacterCount)))
            .OrderByDescending(allocation => allocation.ConsumedCharacterCount)
            .ThenBy(allocation => allocation.OperatorName, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static IReadOnlyList<PlanOperatorSchedule> BuildOperatorSchedules(
        IndustryPlanComputationContext context,
        IReadOnlyList<PlanTimeSegment> segments)
    {
        return segments
            .Where(segment => segment.AssignedOperatorIds.Count > 0)
            .SelectMany(segment =>
            {
                var totalConsumedSlots = Math.Max(1, segment.CharacterAllocations.Sum(allocation => allocation.ConsumedSlots));
                return segment.CharacterAllocations.Select(allocation => new
                {
                    allocation.CharacterId,
                    allocation.CharacterName,
                    CharacterOperatorId = context.PlanningCharactersById.GetValueOrDefault(allocation.CharacterId)?.OperatorId,
                    Segment = segment,
                    ManualLoadUnits = ResolveAllocatedManualLoadUnits(
                        segment.EstimatedManualLoadUnits,
                        allocation.ConsumedSlots,
                        totalConsumedSlots)
                });
            })
            .Where(item => item.CharacterOperatorId is not null)
            .Select(item => new
            {
                OperatorId = item.CharacterOperatorId!,
                OperatorName = context.OperatorsById.TryGetValue(item.CharacterOperatorId!, out var operatorEntry)
                    ? operatorEntry.DisplayName
                    : item.CharacterOperatorId!,
                item.CharacterId,
                item.CharacterName,
                item.Segment,
                item.ManualLoadUnits
            })
            .GroupBy(item => item.OperatorId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var characterIds = group
                    .Select(item => item.CharacterId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var characterNames = group
                    .Select(item => item.CharacterName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new PlanOperatorSchedule
                {
                    OperatorId = group.Key,
                    OperatorName = group.First().OperatorName,
                    AssignedNodeCount = group.Select(item => item.Segment.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    AssignedCharacterCount = characterIds.Length,
                    ScheduledHours = Math.Round(group
                        .GroupBy(item => item.Segment.NodeId, StringComparer.OrdinalIgnoreCase)
                        .Sum(item => item.First().Segment.ScheduledFinishHours - item.First().Segment.ScheduledStartHours), 2),
                    TotalManualLoadUnits = Math.Round(group.Sum(item => item.ManualLoadUnits), 2),
                    MaxManualLoadUnits = context.OperatorsById.TryGetValue(group.Key, out var operatorEntry)
                        ? operatorEntry.MaxDailyManualOperations
                        : null,
                    IsOverloaded = context.OperatorsById.TryGetValue(group.Key, out operatorEntry) &&
                                   operatorEntry.MaxDailyManualOperations.HasValue &&
                                   group.Sum(item => item.ManualLoadUnits) > operatorEntry.MaxDailyManualOperations.Value,
                    AssignedNodeIds = group.Select(item => item.Segment.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    AssignedCharacterIds = characterIds,
                    AssignedCharacterNames = characterNames
                };
            })
            .OrderByDescending(schedule => schedule.TotalManualLoadUnits)
            .ThenByDescending(schedule => schedule.AssignedNodeCount)
            .ThenBy(schedule => schedule.OperatorName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? BuildAssignmentRationale(
        IndustryPlanComputationContext context,
        PlanNode node,
        ScheduledNodeAssignment? assignment)
    {
        if (assignment is null)
        {
            return null;
        }

        if (assignment.CharacterAllocations.Count > 1)
        {
            var names = string.Join(", ", assignment.CharacterAllocations.Select(allocation => allocation.CharacterName));
            return $"Scheduled across {assignment.CharacterAllocations.Count} qualified characters ({names}) to cover {assignment.ConsumedSlots} parallel slot(s).";
        }

        var primaryCharacter = assignment.CharacterAllocations[0];
        var suggested = IndustryPlanCharacterConstraintAnalyzer.ResolveSuggestedCharacter(context, node);
        context.SuggestedCharacterRationalesByNodeId.TryGetValue(node.NodeId, out var suggestedRationale);

        if (suggested is not null &&
            primaryCharacter.CharacterId.Equals(suggested.CharacterId, StringComparison.OrdinalIgnoreCase))
        {
            return suggestedRationale is null
                ? $"Assigned to '{primaryCharacter.CharacterName}' as the best-fit qualified character."
                : $"{suggestedRationale} It was also the earliest available qualified option when the node became ready.";
        }

        if (suggested is not null)
        {
            return $"Assigned to '{primaryCharacter.CharacterName}' because it was the earliest available qualified option when dependencies cleared; static fit still preferred '{suggested.DisplayName}'.";
        }

        return $"Assigned to '{primaryCharacter.CharacterName}' because it was the earliest available qualified option when dependencies cleared.";
    }

    private static decimal ResolveDurationHours(
        PlanNode node,
        IndustryPlanComputationContext context,
        IndustrialRecipeResolver recipeResolver)
    {
        var metadataDuration = PlanningMetadataReader.ReadDecimal(node.Metadata, "estimated_duration_hours");
        if (metadataDuration.HasValue && metadataDuration.Value > 0m)
        {
            return metadataDuration.Value;
        }

        var recipe = recipeResolver.ResolveRecipeActivity(node);
        if (recipe?.TimeSeconds is > 0)
        {
            context.RecipeBackedNodeIds.Add(node.NodeId);
            context.RecordRecipeSemanticClass(node.NodeId, recipe.PlannerSemanticClass);
            var adjustedSeconds = recipeResolver.ApplyTimeEfficiency(
                context,
                node,
                recipe.TimeSeconds.Value * recipeResolver.ResolvePlannedRuns(context, node, recipe));
            var durationHours = Math.Round(adjustedSeconds / 3600m, 2);
            if (durationHours > 0m)
            {
                return durationHours;
            }
        }

        return node.Kind switch
        {
            PlanNodeKind.Production => 18m,
            PlanNodeKind.Reaction => 14m,
            PlanNodeKind.CopyOrInvention => 9m,
            PlanNodeKind.Transport => 3m,
            PlanNodeKind.Trade => 1.5m,
            PlanNodeKind.InventoryPool => 0.25m,
            _ => 1m
        };
    }

    private static ScheduledNodeAssignment? TryScheduleAcrossCharacters(
        IndustryPlanComputationContext context,
        PlanNode node,
        decimal baseDurationHours,
        decimal dependencyReadyAt,
        IDictionary<string, CharacterScheduleState> characterStates,
        IDictionary<string, OperatorScheduleState> operatorStates)
    {
        if (!IndustryPlanCharacterConstraintAnalyzer.RequiresCharacterScheduling(node))
        {
            return null;
        }

        var candidates = IndustryPlanCharacterConstraintAnalyzer.ResolveCandidateCharacters(context, node);
        if (candidates.Count == 0)
        {
            return null;
        }

        var requestedSlots = IndustryPlanCharacterConstraintAnalyzer.ResolveRequestedActivitySlots(node);
        var activityKey = IndustryPlanCharacterConstraintAnalyzer.ResolveActivityKey(node);
        var nodeManualLoadUnits = ResolveOperatorManualLoadUnits(node);
        if (requestedSlots <= 0 || activityKey is null)
        {
            return null;
        }

        var laneOptions = candidates
            .SelectMany((candidate, candidateRank) =>
            {
                if (!characterStates.TryGetValue(candidate.CharacterId, out var state))
                {
                    state = new CharacterScheduleState(candidate.CharacterId, candidate.DisplayName);
                    characterStates[candidate.CharacterId] = state;
                }

                var operatorId = candidate.OperatorId;
                OperatorScheduleState? operatorState = null;
                if (operatorId is not null)
                {
                    if (!operatorStates.TryGetValue(operatorId, out operatorState))
                    {
                        var operatorName = context.OperatorsById.TryGetValue(operatorId, out var operatorEntry)
                            ? operatorEntry.DisplayName
                            : operatorId;
                        var maxDailyManualOperations = context.OperatorsById.TryGetValue(operatorId, out operatorEntry)
                            ? operatorEntry.MaxDailyManualOperations
                            : null;
                        operatorState = new OperatorScheduleState(operatorId, operatorName, maxDailyManualOperations);
                        operatorStates[operatorId] = operatorState;
                    }
                }

                var capacity = Math.Max(1, IndustryPlanCharacterConstraintAnalyzer.ResolveCharacterCapacity(node, candidate));
                return state.GetLaneHandles(activityKey, capacity, candidateRank, operatorState, nodeManualLoadUnits);
            })
            .OrderBy(handle => handle.AvailableAtHours)
            .ThenBy(handle => handle.OperatorOverBudget ? 1 : 0)
            .ThenBy(handle => handle.OperatorBudgetPressure)
            .ThenBy(handle => handle.OperatorCurrentManualLoadUnits)
            .ThenBy(handle => handle.OperatorAssignedNodeCount)
            .ThenBy(handle => context.Plan.Preferences.PreferLowerCharacterLoad ? handle.CurrentAssignedHours : 0m)
            .ThenBy(handle => handle.CandidateRank)
            .ThenBy(handle => context.Plan.Preferences.PreferLowerCharacterLoad ? 0m : handle.CurrentAssignedHours)
            .ThenBy(handle => handle.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(handle => handle.LaneIndex)
            .ToArray();

        var maxSelectableSlots = Math.Min(requestedSlots, laneOptions.Length);
        if (maxSelectableSlots <= 0)
        {
            return null;
        }

        ScheduledNodeAssignment? bestAssignment = null;

        for (var selectedSlotCount = 1; selectedSlotCount <= maxSelectableSlots; selectedSlotCount++)
        {
            var selectedLanes = laneOptions
                .Take(selectedSlotCount)
                .ToArray();
            var characterReadyAt = selectedLanes.Max(handle => handle.AvailableAtHours);
            var startHours = Math.Round(Math.Max(dependencyReadyAt, characterReadyAt), 2);
            var shortageFactor = Math.Max(1m, requestedSlots / (decimal)selectedSlotCount);
            var finishHours = Math.Round(startHours + (baseDurationHours * shortageFactor), 2);
            var blockingNodeId = selectedLanes
                .OrderByDescending(handle => handle.AvailableAtHours)
                .ThenBy(handle => handle.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(handle => handle.LaneIndex)
                .Select(handle => handle.LastNodeId)
                .FirstOrDefault(nodeId => nodeId is not null);
            var allocations = selectedLanes
                .GroupBy(handle => new { handle.CharacterId, handle.CharacterName })
                .Select(group => new ScheduledCharacterAllocation(
                    group.Key.CharacterId,
                    group.Key.CharacterName,
                    group.Count()))
                .OrderByDescending(allocation => allocation.ConsumedSlots)
                .ThenBy(allocation => allocation.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var assignment = new ScheduledNodeAssignment(
                activityKey,
                startHours,
                finishHours,
                selectedSlotCount,
                shortageFactor,
                blockingNodeId,
                characterReadyAt,
                selectedLanes,
                allocations);

            if (bestAssignment is null ||
                assignment.FinishHours < bestAssignment.FinishHours ||
                (assignment.FinishHours == bestAssignment.FinishHours &&
                 assignment.StartHours < bestAssignment.StartHours) ||
                (assignment.FinishHours == bestAssignment.FinishHours &&
                 assignment.StartHours == bestAssignment.StartHours &&
                 assignment.CharacterAllocations.Count > bestAssignment.CharacterAllocations.Count))
            {
                bestAssignment = assignment;
            }
        }

        if (bestAssignment is null)
        {
            return null;
        }

        foreach (var allocationGroup in bestAssignment.SelectedLanes.GroupBy(handle => handle.CharacterId, StringComparer.OrdinalIgnoreCase))
        {
            characterStates[allocationGroup.Key].Commit(
                bestAssignment.ActivityKey,
                allocationGroup.Select(handle => handle.LaneIndex).ToArray(),
                bestAssignment.FinishHours,
                node.NodeId,
                allocationGroup.Count(),
                bestAssignment.FinishHours - bestAssignment.StartHours);
        }

        foreach (var operatorGroup in bestAssignment.SelectedLanes
                     .Where(handle => handle.OperatorId is not null)
                     .GroupBy(handle => handle.OperatorId!, StringComparer.OrdinalIgnoreCase))
        {
            if (operatorStates.TryGetValue(operatorGroup.Key, out var operatorState))
            {
                operatorState.Commit(
                    node.NodeId,
                    ResolveAllocatedManualLoadUnits(
                        nodeManualLoadUnits,
                        operatorGroup.Count(),
                        bestAssignment.ConsumedSlots));
            }
        }

        return bestAssignment;
    }

    private static string? ResolveDominantPredecessor(
        string? dependencyPredecessorId,
        string? characterPredecessorId,
        decimal dependencyReadyAt,
        decimal characterReadyAt)
    {
        if (characterPredecessorId is null)
        {
            return dependencyPredecessorId;
        }

        if (dependencyPredecessorId is null)
        {
            return characterPredecessorId;
        }

        return characterReadyAt > dependencyReadyAt
            ? characterPredecessorId
            : dependencyPredecessorId;
    }

    private sealed class CharacterScheduleState(string characterId, string characterName)
    {
        private readonly Dictionary<string, List<CharacterLaneState>> _lanesByActivity = new(StringComparer.OrdinalIgnoreCase);

        public string CharacterId { get; } = characterId;

        public string CharacterName { get; } = characterName;

        public List<string> AssignedNodeIds { get; } = [];

        public decimal TotalAssignedDurationHours { get; private set; }

        public int PeakConsumedSlots { get; private set; }

        public IReadOnlyList<CharacterLaneHandle> GetLaneHandles(
            string activityKey,
            int capacity,
            int candidateRank,
            OperatorScheduleState? operatorState,
            decimal nodeManualLoadUnits)
        {
            var lanes = GetOrCreateLanes(activityKey, capacity);
            return lanes
                .Select((lane, index) => new CharacterLaneHandle(
                    CharacterId,
                    CharacterName,
                    activityKey,
                    operatorState?.OperatorId,
                    operatorState?.OperatorName,
                    operatorState?.AssignedNodeCount ?? 0,
                    operatorState?.TotalManualLoadUnits ?? 0m,
                    operatorState?.ResolveBudgetPressure(nodeManualLoadUnits) ?? 0m,
                    operatorState?.WouldExceedBudget(nodeManualLoadUnits) ?? false,
                    candidateRank,
                    index,
                    lane.AvailableAtHours,
                    TotalAssignedDurationHours,
                    lane.LastNodeId))
                .ToArray();
        }

        public void Commit(
            string activityKey,
            IReadOnlyList<int> laneIndices,
            decimal finishHours,
            string nodeId,
            int consumedSlots,
            decimal durationHours)
        {
            var lanes = _lanesByActivity[activityKey];
            foreach (var laneIndex in laneIndices)
            {
                lanes[laneIndex] = lanes[laneIndex] with
                {
                    AvailableAtHours = finishHours,
                    LastNodeId = nodeId
                };
            }

            AssignedNodeIds.Add(nodeId);
            TotalAssignedDurationHours += durationHours;
            PeakConsumedSlots = Math.Max(PeakConsumedSlots, consumedSlots);
        }

        private List<CharacterLaneState> GetOrCreateLanes(string activityKey, int capacity)
        {
            if (!_lanesByActivity.TryGetValue(activityKey, out var lanes))
            {
                lanes = [];
                _lanesByActivity[activityKey] = lanes;
            }

            while (lanes.Count < capacity)
            {
                lanes.Add(new CharacterLaneState(0m, LastNodeId: null));
            }

            return lanes;
        }
    }

    private sealed record CharacterLaneState(
        decimal AvailableAtHours,
        string? LastNodeId);

    private sealed record CharacterLaneHandle(
        string CharacterId,
        string CharacterName,
        string ActivityKey,
        string? OperatorId,
        string? OperatorName,
        int OperatorAssignedNodeCount,
        decimal OperatorCurrentManualLoadUnits,
        decimal OperatorBudgetPressure,
        bool OperatorOverBudget,
        int CandidateRank,
        int LaneIndex,
        decimal AvailableAtHours,
        decimal CurrentAssignedHours,
        string? LastNodeId);

    private sealed class OperatorScheduleState(string operatorId, string operatorName, int? maxDailyManualOperations)
    {
        private readonly HashSet<string> _assignedNodeIds = new(StringComparer.OrdinalIgnoreCase);

        public string OperatorId { get; } = operatorId;

        public string OperatorName { get; } = operatorName;

        public int? MaxDailyManualOperations { get; } = maxDailyManualOperations;

        public int AssignedNodeCount => _assignedNodeIds.Count;

        public decimal TotalManualLoadUnits { get; private set; }

        public void Commit(string nodeId, decimal manualLoadUnits)
        {
            _assignedNodeIds.Add(nodeId);
            TotalManualLoadUnits = Math.Round(TotalManualLoadUnits + manualLoadUnits, 2);
        }

        public decimal ResolveBudgetPressure(decimal projectedAdditionalLoadUnits)
        {
            if (!MaxDailyManualOperations.HasValue || MaxDailyManualOperations.Value <= 0)
            {
                return 0m;
            }

            return Math.Round(
                (TotalManualLoadUnits + projectedAdditionalLoadUnits) / MaxDailyManualOperations.Value,
                4);
        }

        public bool WouldExceedBudget(decimal projectedAdditionalLoadUnits)
        {
            if (!MaxDailyManualOperations.HasValue || MaxDailyManualOperations.Value <= 0)
            {
                return false;
            }

            return TotalManualLoadUnits + projectedAdditionalLoadUnits > MaxDailyManualOperations.Value;
        }
    }

    private static decimal ResolveOperatorManualLoadUnits(PlanNode node)
    {
        var explicitUnits = PlanningMetadataReader.ReadDecimal(node.Metadata, "operator_manual_load_units")
            ?? PlanningMetadataReader.ReadDecimal(node.Metadata, "manual_load_units");
        if (explicitUnits.HasValue && explicitUnits.Value > 0m)
        {
            return Math.Round(explicitUnits.Value, 2);
        }

        var defaults = node.Kind switch
        {
            PlanNodeKind.InventoryPool => 0.1m,
            PlanNodeKind.Production => 1.0m,
            PlanNodeKind.Reaction => 1.2m,
            PlanNodeKind.CopyOrInvention => 1.4m,
            PlanNodeKind.Trade => 1.8m,
            PlanNodeKind.Transport => 2.0m,
            _ => 1.0m
        };

        return Math.Round(defaults, 2);
    }

    private static decimal ResolveAllocatedManualLoadUnits(
        decimal totalManualLoadUnits,
        int consumedSlots,
        int totalConsumedSlots)
    {
        if (totalManualLoadUnits <= 0m)
        {
            return 0m;
        }

        var denominator = Math.Max(1, totalConsumedSlots);
        var numerator = Math.Max(0, consumedSlots);
        return Math.Round(totalManualLoadUnits * numerator / denominator, 2);
    }

    private sealed record ScheduledCharacterAllocation(
        string CharacterId,
        string CharacterName,
        int ConsumedSlots);

    private sealed record ResolvedOperatorAllocation(
        string OperatorId,
        string OperatorName,
        int ConsumedCharacterCount);

    private sealed record ScheduledNodeAssignment(
        string ActivityKey,
        decimal StartHours,
        decimal FinishHours,
        int ConsumedSlots,
        decimal SlotShortageFactor,
        string? BlockingCharacterNodeId,
        decimal CharacterReadyAt,
        IReadOnlyList<CharacterLaneHandle> SelectedLanes,
        IReadOnlyList<ScheduledCharacterAllocation> CharacterAllocations);
}

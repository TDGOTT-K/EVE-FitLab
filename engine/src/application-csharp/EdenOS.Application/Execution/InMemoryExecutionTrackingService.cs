using System.Collections.Concurrent;
using EdenOS.Application.RuntimeState;
using EdenOS.Application.Tasks;
using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.Execution;

public sealed class InMemoryExecutionTrackingService : IExecutionTrackingService
{
    private const int PersistenceRetryCount = 3;

    private readonly IIndustryPlanService planService;
    private readonly IIndustryPlanComputationService computationService;
    private readonly ITaskPoolService taskPoolService;
    private readonly ITaskPoolExecutionCoordinator? taskPoolExecutionCoordinator;
    private readonly IWorkspaceService workspaceService;
    private readonly TimeProvider timeProvider;
    private readonly LocalJsonStateStore<ExecutionRuntimeState>? stateStore;
    private readonly ExecutionEvaluator executionEvaluator = new();
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<string, ExecutionStateRecord> stateRecords = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryExecutionTrackingService(
        IIndustryPlanService planService,
        IIndustryPlanComputationService computationService,
        ITaskPoolService taskPoolService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider)
        : this(planService, computationService, taskPoolService, workspaceService, timeProvider, stateStore: null)
    {
    }

    internal InMemoryExecutionTrackingService(
        IIndustryPlanService planService,
        IIndustryPlanComputationService computationService,
        ITaskPoolService taskPoolService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider,
        LocalJsonStateStore<ExecutionRuntimeState>? stateStore)
    {
        this.planService = planService;
        this.computationService = computationService;
        this.taskPoolService = taskPoolService;
        taskPoolExecutionCoordinator = taskPoolService as ITaskPoolExecutionCoordinator;
        this.workspaceService = workspaceService;
        this.timeProvider = timeProvider;
        this.stateStore = stateStore;
        ReloadPersistedState();
    }

    public UseCaseResult<ExecutionNextActionsView> GetNextActions(GetNextActionsRequest request)
    {
        var traceId = CreateTraceId("execution.get_next_actions");
        if (request.ActionLimit <= 0 || request.ActionLimit > 10)
        {
            return Failure<ExecutionNextActionsView>(
                UseCaseStatus.InvalidInput,
                "Action limit must be between 1 and 10.",
                traceId,
                "ActionLimit must be between 1 and 10.");
        }

        var contextResult = ResolveExecutionContext(
            request.WorkspaceId,
            request.PlanId,
            request.MarketLocationId,
            request.MarketAccountKey,
            traceId);
        if (!contextResult.IsSuccess || contextResult.Data is null)
        {
            return Failure<ExecutionNextActionsView>(
                contextResult.Status,
                contextResult.Summary,
                traceId,
                contextResult.Errors.ToArray());
        }

        var context = contextResult.Data;
        var evaluation = EvaluateExecution(context, request.ActionLimit);
        return UseCaseResult<ExecutionNextActionsView>.Success(
            ExecutionViewBuilder.BuildNextActionsView(context, evaluation, timeProvider.GetUtcNow()),
            $"Generated next actions for plan '{context.Plan.Name}'.",
            traceId,
            context.ComputationWarnings);
    }

    public UseCaseResult<ExecutionState> MarkStepDone(MarkStepDoneRequest request)
    {
        var traceId = CreateTraceId("execution.mark_step_done");
        return ExecutePersistedMutation(traceId, "execution.mark_step_done", () =>
        {
            var resolution = ResolvePlanNode(request.WorkspaceId, request.PlanId, request.NodeId, traceId, "execution.mark_step_done");
            if (!resolution.IsSuccess || resolution.Data is null)
            {
                return Failure<ExecutionState>(resolution.Status, resolution.Summary, traceId, resolution.Errors.ToArray());
            }

            var nodeResolution = resolution.Data;
            var node = nodeResolution.Node!;
            var record = GetOrCreateRecord(nodeResolution.WorkspaceId, nodeResolution.Plan.PlanId);
            if (!record.CompletedNodeIds.Add(node.NodeId))
            {
                return UseCaseResult<ExecutionState>.Success(
                    BuildState(nodeResolution.WorkspaceId, nodeResolution.Plan.PlanId, record),
                    $"Node '{node.Title}' was already marked done.",
                    traceId,
                    ["No new execution event was appended because the step was already complete."]);
            }

            AppendEvent(
                record,
                nodeResolution.WorkspaceId,
                nodeResolution.Plan.PlanId,
                new StepDoneEventDetails
                {
                    NodeId = node.NodeId,
                    NodeTitle = node.Title
                },
                $"Marked step '{node.Title}' done.",
                request.Notes,
                request.CompletedAtUtc,
                metadata: null);

            return UseCaseResult<ExecutionState>.Success(
                BuildState(nodeResolution.WorkspaceId, nodeResolution.Plan.PlanId, record),
                $"Marked plan node '{node.Title}' done.",
                traceId);
        });
    }

    public UseCaseResult<ExecutionState> RecordMaterialArrival(RecordMaterialArrivalRequest request)
    {
        var traceId = CreateTraceId("execution.record_material_arrival");
        return ExecutePersistedMutation(traceId, "execution.record_material_arrival", () =>
            RecordEventState(
                request.WorkspaceId,
                request.PlanId,
                request.NodeId,
                traceId,
                "execution.record_material_arrival",
                node => new MaterialArrivalEventDetails
                {
                    NodeId = node?.NodeId,
                    NodeTitle = node?.Title,
                    ResourceTypeId = request.ResourceTypeId.Trim(),
                    ResourceName = request.ResourceName.Trim(),
                    Quantity = request.Quantity,
                    LocationLabel = NormalizeOptional(request.LocationLabel) ?? NormalizeOptional(node?.LocationLabel)
                },
                $"Recorded material arrival for '{request.ResourceName}'.",
                request.Notes,
                request.OccurredAtUtc));
    }

    public UseCaseResult<ExecutionState> RecordMaterialLoss(RecordMaterialLossRequest request)
    {
        var traceId = CreateTraceId("execution.record_material_loss");
        return ExecutePersistedMutation(traceId, "execution.record_material_loss", () =>
            RecordEventState(
                request.WorkspaceId,
                request.PlanId,
                request.NodeId,
                traceId,
                "execution.record_material_loss",
                node => new MaterialLossEventDetails
                {
                    NodeId = node?.NodeId,
                    NodeTitle = node?.Title,
                    ResourceTypeId = request.ResourceTypeId.Trim(),
                    ResourceName = request.ResourceName.Trim(),
                    Quantity = request.Quantity,
                    LocationLabel = NormalizeOptional(request.LocationLabel) ?? NormalizeOptional(node?.LocationLabel)
                },
                $"Recorded material loss for '{request.ResourceName}'.",
                request.Notes,
                request.OccurredAtUtc));
    }

    public UseCaseResult<ExecutionState> RecordMarketChange(RecordMarketChangeRequest request)
    {
        var traceId = CreateTraceId("execution.record_market_change");
        return ExecutePersistedMutation(traceId, "execution.record_market_change", () =>
            RecordEventState(
                request.WorkspaceId,
                request.PlanId,
                nodeId: null,
                traceId,
                "execution.record_market_change",
                _ => new MarketChangeEventDetails
                {
                    ResourceTypeId = NormalizeOptional(request.ResourceTypeId),
                    ResourceName = NormalizeOptional(request.ResourceName),
                    MarketLocationId = request.MarketLocationId,
                    ChangeSummary = request.ChangeSummary.Trim(),
                    ImpactHint = NormalizeOptional(request.ImpactHint)
                },
                request.ChangeSummary.Trim(),
                request.Notes,
                request.OccurredAtUtc));
    }

    public UseCaseResult<ExecutionState> RecordIndustryCostChange(RecordIndustryCostChangeRequest request)
    {
        var traceId = CreateTraceId("execution.record_industry_cost_change");
        return ExecutePersistedMutation(traceId, "execution.record_industry_cost_change", () =>
            RecordEventState(
                request.WorkspaceId,
                request.PlanId,
                request.NodeId,
                traceId,
                "execution.record_industry_cost_change",
                node => new IndustryCostChangeEventDetails
                {
                    NodeId = node?.NodeId,
                    NodeTitle = node?.Title,
                    LocationLabel = NormalizeOptional(request.LocationLabel) ?? NormalizeOptional(node?.LocationLabel),
                    PreviousSystemCostIndex = request.PreviousSystemCostIndex,
                    CurrentSystemCostIndex = request.CurrentSystemCostIndex,
                    ChangeSummary = request.ChangeSummary.Trim(),
                    ImpactHint = NormalizeOptional(request.ImpactHint)
                },
                request.ChangeSummary.Trim(),
                request.Notes,
                request.OccurredAtUtc));
    }

    public UseCaseResult<ExecutionState> RecordLocationChange(RecordLocationChangeRequest request)
    {
        var traceId = CreateTraceId("execution.record_location_change");
        return ExecutePersistedMutation(traceId, "execution.record_location_change", () =>
            RecordEventState(
                request.WorkspaceId,
                request.PlanId,
                request.NodeId,
                traceId,
                "execution.record_location_change",
                node => new LocationChangeEventDetails
                {
                    NodeId = node?.NodeId,
                    NodeTitle = node?.Title,
                    ChangeSummary = request.ChangeSummary.Trim(),
                    CurrentLocationLabel = request.CurrentLocationLabel.Trim(),
                    IsRecovered = request.IsRecovered
                },
                request.ChangeSummary.Trim(),
                request.Notes,
                request.OccurredAtUtc));
    }

    public UseCaseResult<ExecutionState> RecordManualOverride(RecordManualOverrideRequest request)
    {
        var traceId = CreateTraceId("execution.record_manual_override");
        return ExecutePersistedMutation(traceId, "execution.record_manual_override", () =>
        {
            var validation = ValidateWorkspaceAndPlan(request.WorkspaceId, request.PlanId, traceId, "execution.record_manual_override");
            if (!validation.IsSuccess || validation.Data is null)
            {
                return Failure<ExecutionState>(validation.Status, validation.Summary, traceId, validation.Errors.ToArray());
            }

            var planResolution = validation.Data;
            var record = GetOrCreateRecord(planResolution.WorkspaceId, planResolution.Plan.PlanId);
            AppendEvent(
                record,
                planResolution.WorkspaceId,
                planResolution.Plan.PlanId,
                new ManualOverrideEventDetails
                {
                    OverrideSummary = request.OverrideSummary.Trim(),
                    Reason = NormalizeOptional(request.Reason),
                    AffectedNodeIds = request.AffectedNodeIds
                        .Select(NormalizeOptional)
                        .Where(nodeId => nodeId is not null)
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                },
                request.OverrideSummary.Trim(),
                request.Notes,
                request.OccurredAtUtc,
                metadata: null);

            return UseCaseResult<ExecutionState>.Success(
                BuildState(planResolution.WorkspaceId, planResolution.Plan.PlanId, record),
                "Recorded manual override event.",
                traceId);
        });
    }

    public UseCaseResult<ExecutionState> ResolveManualOverride(ResolveManualOverrideRequest request)
    {
        var traceId = CreateTraceId("execution.resolve_manual_override");
        return ExecutePersistedMutation(traceId, "execution.resolve_manual_override", () =>
        {
            var validation = ValidateWorkspaceAndPlan(request.WorkspaceId, request.PlanId, traceId, "execution.resolve_manual_override");
            if (!validation.IsSuccess || validation.Data is null)
            {
                return Failure<ExecutionState>(validation.Status, validation.Summary, traceId, validation.Errors.ToArray());
            }

            var planResolution = validation.Data;
            var record = GetOrCreateRecord(planResolution.WorkspaceId, planResolution.Plan.PlanId);
            AppendEvent(
                record,
                planResolution.WorkspaceId,
                planResolution.Plan.PlanId,
                new ManualOverrideResolvedEventDetails
                {
                    ResolutionSummary = request.ResolutionSummary.Trim(),
                    Reason = NormalizeOptional(request.Reason),
                    AffectedNodeIds = request.AffectedNodeIds
                        .Select(NormalizeOptional)
                        .Where(nodeId => nodeId is not null)
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                },
                request.ResolutionSummary.Trim(),
                request.Notes,
                request.OccurredAtUtc,
                metadata: null);

            return UseCaseResult<ExecutionState>.Success(
                BuildState(planResolution.WorkspaceId, planResolution.Plan.PlanId, record),
                "Resolved manual override event.",
                traceId);
        });
    }

    public UseCaseResult<ExecutionBlockerView> GetBlockers(GetBlockersRequest request)
    {
        var traceId = CreateTraceId("execution.get_blockers");
        var contextResult = ResolveExecutionContext(
            request.WorkspaceId,
            request.PlanId,
            request.MarketLocationId,
            request.MarketAccountKey,
            traceId);
        if (!contextResult.IsSuccess || contextResult.Data is null)
        {
            return Failure<ExecutionBlockerView>(
                contextResult.Status,
                contextResult.Summary,
                traceId,
                contextResult.Errors.ToArray());
        }

        var context = contextResult.Data;
        var evaluation = EvaluateExecution(context, actionLimit: null);
        return UseCaseResult<ExecutionBlockerView>.Success(
            ExecutionViewBuilder.BuildBlockerView(context, evaluation, timeProvider.GetUtcNow()),
            $"Collected blockers for plan '{context.Plan.Name}'.",
            traceId,
            context.ComputationWarnings);
    }

    public UseCaseResult<ExecutionReplanResult> Replan(ExecutionReplanRequest request)
    {
        var traceId = CreateTraceId("execution.replan");
        if (request.ActionLimit <= 0 || request.ActionLimit > 10)
        {
            return Failure<ExecutionReplanResult>(
                UseCaseStatus.InvalidInput,
                "Action limit must be between 1 and 10.",
                traceId,
                "ActionLimit must be between 1 and 10.");
        }

        var validation = ValidateWorkspaceAndPlan(request.WorkspaceId, request.PlanId, traceId, "execution.replan");
        if (!validation.IsSuccess || validation.Data is null)
        {
            return Failure<ExecutionReplanResult>(validation.Status, validation.Summary, traceId, validation.Errors.ToArray());
        }

        var planResolution = validation.Data;
        var state = BuildState(planResolution.WorkspaceId, planResolution.Plan.PlanId, GetOrCreateRecord(planResolution.WorkspaceId, planResolution.Plan.PlanId));
        var replanReason = ExecutionNarrativeBuilder.BuildReplanReason(state.Events, request.Reason);
        var recompute = computationService.Recompute(new PlanRecomputeRequest
        {
            WorkspaceId = planResolution.WorkspaceId,
            PlanId = planResolution.Plan.PlanId,
            MarketLocationId = request.MarketLocationId,
            MarketAccountKey = request.MarketAccountKey,
            Reason = replanReason,
            AlternativePathLimit = request.AlternativePathLimit
        });

        if (!recompute.IsSuccess || recompute.Data is null)
        {
            return Failure<ExecutionReplanResult>(recompute.Status, recompute.Summary, traceId, recompute.Errors.ToArray());
        }

        var adjustedComputation = ExecutionReplanComputationAdjuster.ApplyExecutionEventBiases(planResolution.Plan, recompute.Data, state.Events);

        var context = new ExecutionContextData(
            planResolution.WorkspaceId,
            planResolution.Plan,
            state,
            adjustedComputation,
            recompute.Warnings,
            BuildPendingTaskPoolView(planResolution.WorkspaceId, planResolution.Plan.PlanId));

        var evaluation = EvaluateExecution(context, request.ActionLimit);
        if (ReconcileReadyTasksForBlockedNodes(planResolution.WorkspaceId, planResolution.Plan.PlanId, evaluation, replanReason))
        {
            state = BuildState(
                planResolution.WorkspaceId,
                planResolution.Plan.PlanId,
                GetOrCreateRecord(planResolution.WorkspaceId, planResolution.Plan.PlanId));
            context = new ExecutionContextData(
                planResolution.WorkspaceId,
                planResolution.Plan,
                state,
                adjustedComputation,
                recompute.Warnings,
                BuildPendingTaskPoolView(planResolution.WorkspaceId, planResolution.Plan.PlanId));
            evaluation = EvaluateExecution(context, request.ActionLimit);
        }

        return UseCaseResult<ExecutionReplanResult>.Success(
            ExecutionViewBuilder.BuildReplanResult(context, evaluation, replanReason, timeProvider.GetUtcNow()),
            $"Replanned execution for plan '{planResolution.Plan.Name}'.",
            traceId,
            recompute.Warnings);
    }

    private ExecutionEvaluation EvaluateExecution(ExecutionContextData context, int? actionLimit)
    {
        return executionEvaluator.Evaluate(
            context.Plan,
            context.State,
            context.Computation,
            context.TaskPool,
            actionLimit);
    }

    private bool ReconcileReadyTasksForBlockedNodes(
        string workspaceId,
        string planId,
        ExecutionEvaluation evaluation,
        string reason)
    {
        if (taskPoolExecutionCoordinator is null)
        {
            return false;
        }

        var blockedNodeIds = evaluation.NodeStatuses
            .Where(status => status.StatusKind is
                ExecutionNodeStatusKind.BlockedByConstraint or
                ExecutionNodeStatusKind.BlockedByMaterial or
                ExecutionNodeStatusKind.BlockedByOverride or
                ExecutionNodeStatusKind.BlockedByResource)
            .Select(status => status.NodeId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (blockedNodeIds.Length == 0)
        {
            return false;
        }

        var reverted = taskPoolExecutionCoordinator.ReturnReadyTasksToPending(
            workspaceId,
            planId,
            blockedNodeIds,
            reason);

        return reverted.Count > 0;
    }

    private UseCaseResult<ExecutionState> RecordEventState(
        string workspaceId,
        string planId,
        string? nodeId,
        string traceId,
        string useCaseName,
        Func<PlanNode?, ExecutionEventDetails> detailsFactory,
        string summary,
        string? notes,
        DateTimeOffset? occurredAtUtc)
    {
        var resolution = nodeId is null
            ? ValidateWorkspaceAndPlan(workspaceId, planId, traceId, useCaseName)
            : ResolvePlanNode(workspaceId, planId, nodeId, traceId, useCaseName);

        if (!resolution.IsSuccess || resolution.Data is null)
        {
            return Failure<ExecutionState>(resolution.Status, resolution.Summary, traceId, resolution.Errors.ToArray());
        }

        var planResolution = resolution.Data;
        var details = detailsFactory(planResolution.Node);
        var validationErrors = ValidateEventDetails(details);
        if (validationErrors.Count > 0)
        {
            return Failure<ExecutionState>(UseCaseStatus.InvalidInput, "Execution event payload is invalid.", traceId, validationErrors.ToArray());
        }

        var record = GetOrCreateRecord(planResolution.WorkspaceId, planResolution.Plan.PlanId);
        AppendEvent(
            record,
            planResolution.WorkspaceId,
            planResolution.Plan.PlanId,
            details,
            summary,
            notes,
            occurredAtUtc,
            metadata: null);

        return UseCaseResult<ExecutionState>.Success(
            BuildState(planResolution.WorkspaceId, planResolution.Plan.PlanId, record),
            summary,
            traceId);
    }

    private UseCaseResult<PlanResolution> ValidateWorkspaceAndPlan(string workspaceId, string planId, string traceId, string useCaseName)
    {
        var normalizedWorkspaceId = NormalizeOptional(workspaceId);
        var normalizedPlanId = NormalizeOptional(planId);
        if (normalizedWorkspaceId is null || normalizedPlanId is null)
        {
            return Failure<PlanResolution>(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires workspace and plan identifiers.",
                traceId,
                "WorkspaceId and PlanId must not be empty.");
        }

        var plan = planService.Get(new GetPlanRequest
        {
            WorkspaceId = normalizedWorkspaceId,
            PlanId = normalizedPlanId
        });
        if (!plan.IsSuccess || plan.Data is null)
        {
            return Failure<PlanResolution>(plan.Status, plan.Summary, traceId, plan.Errors.ToArray());
        }

        return UseCaseResult<PlanResolution>.Success(
            new PlanResolution(normalizedWorkspaceId, plan.Data, null),
            "Resolved workspace and plan context.",
            traceId);
    }

    private UseCaseResult<PlanResolution> ResolvePlanNode(string workspaceId, string planId, string nodeId, string traceId, string useCaseName)
    {
        var planResolution = ValidateWorkspaceAndPlan(workspaceId, planId, traceId, useCaseName);
        if (!planResolution.IsSuccess || planResolution.Data is null)
        {
            return planResolution;
        }

        var normalizedNodeId = NormalizeOptional(nodeId);
        if (normalizedNodeId is null)
        {
            return Failure<PlanResolution>(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires a node id.",
                traceId,
                "NodeId must not be empty.");
        }

        var node = planResolution.Data.Plan.Nodes.FirstOrDefault(candidate => candidate.NodeId.Equals(normalizedNodeId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return Failure<PlanResolution>(
                UseCaseStatus.NotFound,
                $"Node '{normalizedNodeId}' was not found in the requested plan.",
                traceId,
                "Unknown node id.");
        }

        return UseCaseResult<PlanResolution>.Success(
            planResolution.Data with { Node = node },
            "Resolved workspace, plan, and node context.",
            traceId);
    }

    private UseCaseResult<ExecutionContextData> ResolveExecutionContext(
        string workspaceId,
        string planId,
        long? marketLocationId,
        string? marketAccountKey,
        string traceId)
    {
        var resolution = ValidateWorkspaceAndPlan(workspaceId, planId, traceId, "execution.context");
        if (!resolution.IsSuccess || resolution.Data is null)
        {
            return Failure<ExecutionContextData>(resolution.Status, resolution.Summary, traceId, resolution.Errors.ToArray());
        }

        var planResolution = resolution.Data;
        var computation = computationService.Compute(new PlanComputeRequest
        {
            WorkspaceId = planResolution.WorkspaceId,
            PlanId = planResolution.Plan.PlanId,
            MarketLocationId = marketLocationId,
            MarketAccountKey = marketAccountKey
        });
        if (!computation.IsSuccess || computation.Data is null)
        {
            return Failure<ExecutionContextData>(computation.Status, computation.Summary, traceId, computation.Errors.ToArray());
        }

        return UseCaseResult<ExecutionContextData>.Success(
            new ExecutionContextData(
                planResolution.WorkspaceId,
                planResolution.Plan,
                BuildState(planResolution.WorkspaceId, planResolution.Plan.PlanId, GetOrCreateRecord(planResolution.WorkspaceId, planResolution.Plan.PlanId)),
                computation.Data,
                computation.Warnings,
                BuildPendingTaskPoolView(planResolution.WorkspaceId, planResolution.Plan.PlanId)),
            "Resolved execution context.",
            traceId);
    }

    private ExecutionState BuildState(string workspaceId, string planId, ExecutionStateRecord record)
    {
        var taskView = BuildPendingTaskPoolView(workspaceId, planId);

        return new ExecutionState
        {
            WorkspaceId = workspaceId,
            PlanId = planId,
            CompletedNodeIds = record.CompletedNodeIds.OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase).ToArray(),
            Events = record.Events.OrderBy(evt => evt.OccurredAtUtc).ToArray(),
            PendingTaskCount = taskView.PendingCount,
            ReadyForPublishTaskCount = taskView.ReadyForPublishCount,
            UpdatedAtUtc = record.UpdatedAtUtc
        };
    }

    private PendingTaskPoolView BuildPendingTaskPoolView(string workspaceId, string planId)
    {
        var taskView = taskPoolService.ListPending(new ListPendingTasksRequest
        {
            WorkspaceId = workspaceId,
            PlanId = planId
        });

        if (taskView.IsSuccess && taskView.Data is not null)
        {
            return taskView.Data;
        }

        return new PendingTaskPoolView
        {
            WorkspaceId = workspaceId,
            PlanId = planId,
            Tasks = Array.Empty<PendingTask>()
        };
    }

    private ExecutionStateRecord GetOrCreateRecord(string workspaceId, string planId)
    {
        return stateRecords.GetOrAdd(CreateStateKey(workspaceId, planId), _ => new ExecutionStateRecord
        {
            UpdatedAtUtc = timeProvider.GetUtcNow()
        });
    }

    private void AppendEvent(
        ExecutionStateRecord record,
        string workspaceId,
        string planId,
        ExecutionEventDetails details,
        string summary,
        string? notes,
        DateTimeOffset? occurredAtUtc,
        IReadOnlyDictionary<string, string>? metadata)
    {
        var eventRecord = new ExecutionEventRecord
        {
            EventId = $"execution-event-{Guid.NewGuid():N}",
            WorkspaceId = workspaceId,
            PlanId = planId,
            Summary = summary,
            Notes = NormalizeOptional(notes),
            Details = details,
            Metadata = metadata ?? new Dictionary<string, string>(),
            OccurredAtUtc = occurredAtUtc ?? timeProvider.GetUtcNow(),
            RecordedAtUtc = timeProvider.GetUtcNow()
        };

        record.Events.Add(eventRecord);
        record.UpdatedAtUtc = eventRecord.RecordedAtUtc;
        PersistState();
    }

    private static IReadOnlyList<string> ValidateEventDetails(ExecutionEventDetails details)
    {
        var errors = new List<string>();

        switch (details)
        {
            case MaterialArrivalEventDetails arrival:
                if (string.IsNullOrWhiteSpace(arrival.ResourceTypeId))
                {
                    errors.Add("Material arrival requires resource_type_id.");
                }

                if (string.IsNullOrWhiteSpace(arrival.ResourceName))
                {
                    errors.Add("Material arrival requires resource_name.");
                }

                if (arrival.Quantity <= 0)
                {
                    errors.Add("Material arrival quantity must be greater than zero.");
                }
                break;

            case MaterialLossEventDetails loss:
                if (string.IsNullOrWhiteSpace(loss.ResourceTypeId))
                {
                    errors.Add("Material loss requires resource_type_id.");
                }

                if (string.IsNullOrWhiteSpace(loss.ResourceName))
                {
                    errors.Add("Material loss requires resource_name.");
                }

                if (loss.Quantity <= 0)
                {
                    errors.Add("Material loss quantity must be greater than zero.");
                }
                break;

            case MarketChangeEventDetails marketChange:
                if (string.IsNullOrWhiteSpace(marketChange.ChangeSummary))
                {
                    errors.Add("Market change requires change_summary.");
                }
                break;

            case IndustryCostChangeEventDetails industryCostChange:
                if (string.IsNullOrWhiteSpace(industryCostChange.ChangeSummary))
                {
                    errors.Add("Industry cost change requires change_summary.");
                }

                if (industryCostChange.CurrentSystemCostIndex < 0m)
                {
                    errors.Add("Industry cost change current_system_cost_index cannot be negative.");
                }
                break;

            case LocationChangeEventDetails locationChange:
                if (string.IsNullOrWhiteSpace(locationChange.ChangeSummary))
                {
                    errors.Add("Location change requires change_summary.");
                }

                if (string.IsNullOrWhiteSpace(locationChange.CurrentLocationLabel))
                {
                    errors.Add("Location change requires current_location_label.");
                }
                break;

            case ManualOverrideEventDetails manualOverride:
                if (string.IsNullOrWhiteSpace(manualOverride.OverrideSummary))
                {
                    errors.Add("Manual override requires override_summary.");
                }
                break;

            case ManualOverrideResolvedEventDetails manualOverrideResolved:
                if (string.IsNullOrWhiteSpace(manualOverrideResolved.ResolutionSummary))
                {
                    errors.Add("Manual override resolution requires resolution_summary.");
                }
                break;
        }

        return errors;
    }

    private static string CreateStateKey(string workspaceId, string planId) => $"{workspaceId}:{planId}";

    private static string CreateTraceId(string useCaseName) => $"{useCaseName}:{Guid.NewGuid():N}";

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static UseCaseResult<T> Failure<T>(
        UseCaseStatus status,
        string summary,
        string traceId,
        params string[] errors) =>
        UseCaseResult<T>.Failure(status, summary, traceId, errors);

    private UseCaseResult<ExecutionState> ExecutePersistedMutation(
        string traceId,
        string useCaseName,
        Func<UseCaseResult<ExecutionState>> operation)
    {
        lock (syncRoot)
        {
            for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
            {
                if (stateStore is not null)
                {
                    ReloadPersistedState();
                }

                try
                {
                    return operation();
                }
                catch (RuntimeStateConcurrencyException) when (attempt < PersistenceRetryCount - 1)
                {
                }
            }
        }

        return Failure<ExecutionState>(
            UseCaseStatus.Conflict,
            $"{useCaseName} detected a concurrent runtime state update.",
            traceId,
            "Shared runtime state changed repeatedly while this request was executing. Retry the request.");
    }

    private void ReloadPersistedState()
    {
        if (stateStore is null)
        {
            return;
        }

        stateRecords.Clear();

        foreach (var state in stateStore.Load().States)
        {
            var record = new ExecutionStateRecord
            {
                UpdatedAtUtc = state.UpdatedAtUtc
            };

            foreach (var completedNodeId in state.CompletedNodeIds)
            {
                record.CompletedNodeIds.Add(completedNodeId);
            }

            record.Events.AddRange(state.Events);
            stateRecords[CreateStateKey(state.WorkspaceId, state.PlanId)] = record;
        }
    }

    private void PersistState()
    {
        stateStore?.Save(new ExecutionRuntimeState
        {
            States = stateRecords
                .Select(pair =>
                {
                    var keyParts = pair.Key.Split(':', 2);
                    return new PersistedExecutionStateRecord
                    {
                        WorkspaceId = keyParts[0],
                        PlanId = keyParts[1],
                        CompletedNodeIds = pair.Value.CompletedNodeIds
                            .OrderBy(nodeId => nodeId, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        Events = pair.Value.Events
                            .OrderBy(evt => evt.OccurredAtUtc)
                            .ThenBy(evt => evt.RecordedAtUtc)
                            .ToArray(),
                        UpdatedAtUtc = pair.Value.UpdatedAtUtc
                    };
                })
                .OrderBy(state => state.WorkspaceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(state => state.PlanId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        });
    }

    private sealed class ExecutionStateRecord
    {
        public HashSet<string> CompletedNodeIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ExecutionEventRecord> Events { get; } = [];

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private sealed record PlanResolution(string WorkspaceId, IndustryPlan Plan, PlanNode? Node);

}

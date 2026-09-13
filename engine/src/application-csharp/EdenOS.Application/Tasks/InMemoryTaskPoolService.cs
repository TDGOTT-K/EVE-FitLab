using System.Collections.Concurrent;
using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.Tasks;

public sealed class InMemoryTaskPoolService : ITaskPoolService, ITaskPoolExecutionCoordinator
{
    private const int PersistenceRetryCount = 3;

    private readonly IIndustryPlanService planService;
    private readonly IWorkspaceService workspaceService;
    private readonly TimeProvider timeProvider;
    private readonly LocalJsonStateStore<PendingTaskRuntimeState>? stateStore;
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<string, PendingTask> tasks = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryTaskPoolService(
        IIndustryPlanService planService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider)
        : this(planService, workspaceService, timeProvider, stateStore: null)
    {
    }

    internal InMemoryTaskPoolService(
        IIndustryPlanService planService,
        IWorkspaceService workspaceService,
        TimeProvider timeProvider,
        LocalJsonStateStore<PendingTaskRuntimeState>? stateStore)
    {
        this.planService = planService;
        this.workspaceService = workspaceService;
        this.timeProvider = timeProvider;
        this.stateStore = stateStore;
        ReloadPersistedState();
    }

    public UseCaseResult<PendingTaskPoolView> ListPending(ListPendingTasksRequest request)
    {
        var traceId = CreateTraceId("task_pool.list_pending");
        var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "task_pool.list_pending");
        if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
        {
            return Failure<PendingTaskPoolView>(
                workspaceResult.Status,
                workspaceResult.Summary,
                traceId,
                workspaceResult.Errors.ToArray());
        }

        var workspaceId = workspaceResult.Data.WorkspaceId;
        var planId = NormalizeOptional(request.PlanId);
        if (planId is not null)
        {
            var planResult = planService.Get(new GetPlanRequest
            {
                WorkspaceId = workspaceId,
                PlanId = planId
            });

            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return Failure<PendingTaskPoolView>(
                    planResult.Status,
                    planResult.Summary,
                    traceId,
                    planResult.Errors.ToArray());
            }
        }

        PendingTask[] items;
        lock (syncRoot)
        {
            ReloadPersistedStateIfNeeded();
            items = tasks.Values
                .Where(task =>
                    task.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase) &&
                    (planId is null || task.Origin.PlanId.Equals(planId, StringComparison.OrdinalIgnoreCase)) &&
                    (!request.Status.HasValue || task.Status == request.Status.Value))
                .OrderBy(task => task.Status)
                .ThenByDescending(task => task.UpdatedAtUtc)
                .ToArray();
        }

        var view = new PendingTaskPoolView
        {
            WorkspaceId = workspaceId,
            PlanId = planId,
            Tasks = items,
            PendingCount = items.Count(task => task.Status == PendingTaskStatus.Pending),
            ReadyForPublishCount = items.Count(task => task.Status == PendingTaskStatus.ReadyForPublish)
        };

        return UseCaseResult<PendingTaskPoolView>.Success(
            view,
            $"Listed {items.Length} pending task(s).",
            traceId);
    }

    public UseCaseResult<PendingTask> CreateFromTransportNode(CreateTaskFromTransportNodeRequest request)
    {
        var traceId = CreateTraceId("task_pool.create_from_transport_node");
        return ExecutePersistedMutation(traceId, "task_pool.create_from_transport_node", () =>
        {
            var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "task_pool.create_from_transport_node");
            if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
            {
                return Failure<PendingTask>(
                    workspaceResult.Status,
                    workspaceResult.Summary,
                    traceId,
                    workspaceResult.Errors.ToArray());
            }

            var nodeResult = ResolveNode(workspaceResult.Data.WorkspaceId, request.PlanId, request.NodeId, traceId, "task_pool.create_from_transport_node");
            if (!nodeResult.IsSuccess || nodeResult.Data is null)
            {
                return Failure<PendingTask>(
                    nodeResult.Status,
                    nodeResult.Summary,
                    traceId,
                    nodeResult.Errors.ToArray());
            }

            var resolution = nodeResult.Data;
            if (resolution.Node.Details is not TransportNodeDetails transport)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "The selected node is not a transport node.",
                    traceId,
                    "Use task_pool.create_from_transport_node only with PlanNodeKind.Transport.");
            }

            var details = new TransportPendingTaskDetails
            {
                SourceLocation = transport.SourceLocation.Trim(),
                DestinationLocation = transport.DestinationLocation.Trim(),
                AllowPartialCompletion = transport.AllowPartialCompletion,
                RouteGroupId = NormalizeOptional(transport.RouteGroupId),
                CargoSummary = NormalizeOptional(request.CargoSummary),
                VolumeCubicMeters = request.VolumeCubicMeters,
                RewardHintIsk = request.RewardHintIsk,
                CollateralHintIsk = request.CollateralHintIsk
            };

            var detailErrors = ValidateDetails(details);
            if (detailErrors.Count > 0)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.InvalidInput,
                    "Transport task details are invalid.",
                    traceId,
                    detailErrors.ToArray());
            }

            var task = CreatePendingTask(
                workspaceResult.Data.WorkspaceId,
                resolution,
                request.TitleOverride,
                request.SummaryOverride,
                request.Notes,
                details,
                request.Metadata);

            tasks[task.TaskId] = task;
            PersistState();

            return UseCaseResult<PendingTask>.Success(
                task,
                $"Created pending transport task from node '{resolution.Node.Title}'.",
                traceId);
        });
    }

    public UseCaseResult<PendingTask> CreateFromTradeNeed(CreateTaskFromTradeNeedRequest request)
    {
        var traceId = CreateTraceId("task_pool.create_from_trade_need");
        return ExecutePersistedMutation(traceId, "task_pool.create_from_trade_need", () =>
        {
            var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "task_pool.create_from_trade_need");
            if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
            {
                return Failure<PendingTask>(
                    workspaceResult.Status,
                    workspaceResult.Summary,
                    traceId,
                    workspaceResult.Errors.ToArray());
            }

            var nodeResult = ResolveNode(workspaceResult.Data.WorkspaceId, request.PlanId, request.NodeId, traceId, "task_pool.create_from_trade_need");
            if (!nodeResult.IsSuccess || nodeResult.Data is null)
            {
                return Failure<PendingTask>(
                    nodeResult.Status,
                    nodeResult.Summary,
                    traceId,
                    nodeResult.Errors.ToArray());
            }

            var resolution = nodeResult.Data;
            if (resolution.Node.Details is not TradeNodeDetails tradeNode)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "The selected node is not a trade node.",
                    traceId,
                    "Use task_pool.create_from_trade_need only with PlanNodeKind.Trade.");
            }

            if (tradeNode.TradeMode == PlanTradeMode.OutsourceInput)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Outsource trade nodes must be published through task_pool.create_from_outsource_need.",
                    traceId,
                    "The selected trade node already expresses outsource semantics.");
            }

            if (tradeNode.TradeMode != request.Need.TradeMode)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Trade need mode must match the source trade node mode.",
                    traceId,
                    "Do not rewrite existing trade node semantics during task creation.");
            }

            var marketScope = NormalizeOptional(request.Need.MarketScope) ?? NormalizeOptional(tradeNode.MarketScope);
            if (tradeNode.MarketScope is not null &&
                request.Need.MarketScope is not null &&
                !tradeNode.MarketScope.Equals(request.Need.MarketScope, StringComparison.OrdinalIgnoreCase))
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Trade need market scope must stay aligned with the source node.",
                    traceId,
                    "Do not rewrite existing trade node semantics during task creation.");
            }

            var details = new TradePendingTaskDetails
            {
                TradeMode = request.Need.TradeMode,
                TargetTypeId = request.Need.TargetTypeId.Trim(),
                TargetName = request.Need.TargetName.Trim(),
                Quantity = request.Need.Quantity,
                MarketScope = marketScope,
                UnitPricePreference = request.Need.UnitPricePreference ?? tradeNode.UnitPricePreference,
                UsesMarketFacts = request.Need.UsesMarketFacts && tradeNode.UsesMarketFacts,
                AllowSplitFulfillment = request.Need.AllowSplitFulfillment
            };

            var detailErrors = ValidateDetails(details);
            if (detailErrors.Count > 0)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.InvalidInput,
                    "Trade task details are invalid.",
                    traceId,
                    detailErrors.ToArray());
            }

            var task = CreatePendingTask(
                workspaceResult.Data.WorkspaceId,
                resolution,
                request.TitleOverride,
                request.SummaryOverride,
                request.Notes,
                details,
                request.Metadata);

            tasks[task.TaskId] = task;
            PersistState();

            return UseCaseResult<PendingTask>.Success(
                task,
                $"Created pending trade task from node '{resolution.Node.Title}'.",
                traceId);
        });
    }

    public UseCaseResult<PendingTask> CreateFromOutsourceNeed(CreateTaskFromOutsourceNeedRequest request)
    {
        var traceId = CreateTraceId("task_pool.create_from_outsource_need");
        return ExecutePersistedMutation(traceId, "task_pool.create_from_outsource_need", () =>
        {
            var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "task_pool.create_from_outsource_need");
            if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
            {
                return Failure<PendingTask>(
                    workspaceResult.Status,
                    workspaceResult.Summary,
                    traceId,
                    workspaceResult.Errors.ToArray());
            }

            var nodeResult = ResolveNode(workspaceResult.Data.WorkspaceId, request.PlanId, request.NodeId, traceId, "task_pool.create_from_outsource_need");
            if (!nodeResult.IsSuccess || nodeResult.Data is null)
            {
                return Failure<PendingTask>(
                    nodeResult.Status,
                    nodeResult.Summary,
                    traceId,
                    nodeResult.Errors.ToArray());
            }

            var resolution = nodeResult.Data;
            if (!SupportsOutsource(resolution.Node))
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "The selected node does not express an outsource-capable action.",
                    traceId,
                    "Use a production, reaction, copy/invention, or outsource-input trade node.");
            }

            if (resolution.Node.Details is TradeNodeDetails outsourceTrade &&
                outsourceTrade.TradeMode != PlanTradeMode.OutsourceInput)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Only outsource-input trade nodes may generate outsource tasks.",
                    traceId,
                    "Use task_pool.create_from_trade_need for purchase, sale, or private-exchange trade nodes.");
            }

            var details = new OutsourcePendingTaskDetails
            {
                ActivityLabel = request.Need.ActivityLabel.Trim(),
                TargetTypeId = request.Need.TargetTypeId.Trim(),
                TargetName = request.Need.TargetName.Trim(),
                Quantity = request.Need.Quantity,
                DeliveryLocation = NormalizeOptional(request.Need.DeliveryLocation) ?? NormalizeOptional(resolution.Node.LocationLabel),
                MaterialsProvidedByRequester = request.Need.MaterialsProvidedByRequester,
                BlueprintProvidedByRequester = request.Need.BlueprintProvidedByRequester,
                QuoteHintIsk = request.Need.QuoteHintIsk,
                DueByUtc = request.Need.DueByUtc
            };

            var detailErrors = ValidateDetails(details);
            if (detailErrors.Count > 0)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.InvalidInput,
                    "Outsource task details are invalid.",
                    traceId,
                    detailErrors.ToArray());
            }

            var task = CreatePendingTask(
                workspaceResult.Data.WorkspaceId,
                resolution,
                request.TitleOverride,
                request.SummaryOverride,
                request.Notes,
                details,
                request.Metadata);

            tasks[task.TaskId] = task;
            PersistState();

            return UseCaseResult<PendingTask>.Success(
                task,
                $"Created pending outsource task from node '{resolution.Node.Title}'.",
                traceId);
        });
    }

    public UseCaseResult<PendingTask> UpdatePendingTask(UpdatePendingTaskRequest request)
    {
        var traceId = CreateTraceId("task_pool.update_pending_task");
        return ExecutePersistedMutation(traceId, "task_pool.update_pending_task", () =>
        {
            var taskResult = RequireTask(request.WorkspaceId, request.TaskId, traceId, "task_pool.update_pending_task");
            if (!taskResult.IsSuccess || taskResult.Data is null)
            {
                return Failure<PendingTask>(
                    taskResult.Status,
                    taskResult.Summary,
                    traceId,
                    taskResult.Errors.ToArray());
            }

            var existing = taskResult.Data;
            if (existing.Status != PendingTaskStatus.Pending)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Only pending tasks can be updated.",
                    traceId,
                    "Move the task back to pending before editing, or create a replacement task.");
            }

            var title = request.Title is null ? existing.Title : NormalizeRequired(request.Title);
            var summary = request.Summary is null ? existing.Summary : NormalizeRequired(request.Summary);
            if (title is null || summary is null)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.InvalidInput,
                    "Task title and summary must stay non-empty.",
                    traceId,
                    "Title and Summary cannot be blank when provided.");
            }

            var details = request.Details ?? existing.Details;
            if (request.Details is not null && request.Details.Kind != existing.Kind)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Task kind cannot change during update.",
                    traceId,
                    "Create a new task instead of changing transport/trade/outsource semantics.");
            }

            var detailErrors = ValidateDetails(details);
            if (detailErrors.Count > 0)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.InvalidInput,
                    "Updated task details are invalid.",
                    traceId,
                    detailErrors.ToArray());
            }

            var updated = existing with
            {
                Title = title,
                Summary = summary,
                Notes = request.Notes is null ? existing.Notes : NormalizeOptional(request.Notes),
                Details = details,
                Metadata = request.Metadata is null ? existing.Metadata : NormalizeMetadata(request.Metadata),
                UpdatedAtUtc = timeProvider.GetUtcNow()
            };

            tasks[updated.TaskId] = updated;
            PersistState();
            return UseCaseResult<PendingTask>.Success(updated, $"Updated pending task '{updated.Title}'.", traceId);
        });
    }

    public UseCaseResult<PendingTask> MarkReadyForPublish(MarkReadyForPublishRequest request)
    {
        var traceId = CreateTraceId("task_pool.mark_ready_for_publish");
        return ExecutePersistedMutation(traceId, "task_pool.mark_ready_for_publish", () =>
        {
            var taskResult = RequireTask(request.WorkspaceId, request.TaskId, traceId, "task_pool.mark_ready_for_publish");
            if (!taskResult.IsSuccess || taskResult.Data is null)
            {
                return Failure<PendingTask>(
                    taskResult.Status,
                    taskResult.Summary,
                    traceId,
                    taskResult.Errors.ToArray());
            }

            var existing = taskResult.Data;
            if (existing.Status == PendingTaskStatus.ReadyForPublish)
            {
                return Failure<PendingTask>(
                    UseCaseStatus.Conflict,
                    "Task is already ready for publish.",
                    traceId,
                    $"Task '{existing.TaskId}' is already marked ReadyForPublish.");
            }

            var readyAt = timeProvider.GetUtcNow();
            var metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase);
            metadata.Remove("execution_review_required");
            metadata.Remove("execution_review_reason");

            var updated = existing with
            {
                Status = PendingTaskStatus.ReadyForPublish,
                Metadata = metadata,
                ReadyForPublishAtUtc = readyAt,
                UpdatedAtUtc = readyAt
            };

            tasks[updated.TaskId] = updated;
            PersistState();
            return UseCaseResult<PendingTask>.Success(updated, $"Marked task '{updated.Title}' ready for publish.", traceId);
        });
    }

    public UseCaseResult<ManualPublishExportEnvelope> ExportManualPublishPayload(ExportManualPublishPayloadRequest request)
    {
        var traceId = CreateTraceId("task_pool.export_manual_publish_payload");
        var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "task_pool.export_manual_publish_payload");
        if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
        {
            return Failure<ManualPublishExportEnvelope>(
                workspaceResult.Status,
                workspaceResult.Summary,
                traceId,
                workspaceResult.Errors.ToArray());
        }

        var workspaceId = workspaceResult.Data.WorkspaceId;
        var planId = NormalizeOptional(request.PlanId);
        if (planId is not null)
        {
            var planResult = planService.Get(new GetPlanRequest
            {
                WorkspaceId = workspaceId,
                PlanId = planId
            });

            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return Failure<ManualPublishExportEnvelope>(
                    planResult.Status,
                    planResult.Summary,
                    traceId,
                    planResult.Errors.ToArray());
            }
        }

        var requestedIds = request.TaskIds
            .Select(NormalizeOptional)
            .Where(taskId => taskId is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        PendingTask[] workspaceTasks;
        lock (syncRoot)
        {
            ReloadPersistedStateIfNeeded();
            workspaceTasks = tasks.Values
                .Where(task =>
                    task.WorkspaceId.Equals(workspaceId, StringComparison.OrdinalIgnoreCase) &&
                    (planId is null || task.Origin.PlanId.Equals(planId, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        var selected = requestedIds.Length == 0
            ? workspaceTasks.Where(task => task.Status == PendingTaskStatus.ReadyForPublish).ToArray()
            : workspaceTasks.Where(task => requestedIds.Contains(task.TaskId, StringComparer.OrdinalIgnoreCase)).ToArray();

        if (requestedIds.Length > 0)
        {
            var missing = requestedIds
                .Except(selected.Select(task => task.TaskId), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (missing.Length > 0)
            {
                return Failure<ManualPublishExportEnvelope>(
                    UseCaseStatus.NotFound,
                    "One or more requested tasks were not found in the workspace task pool.",
                    traceId,
                    $"Unknown task id(s): {string.Join(", ", missing)}.");
            }
        }

        var notReady = selected
            .Where(task => task.Status != PendingTaskStatus.ReadyForPublish)
            .Select(task => task.TaskId)
            .ToArray();

        if (notReady.Length > 0)
        {
            return Failure<ManualPublishExportEnvelope>(
                UseCaseStatus.Conflict,
                "Only ready tasks can be exported for manual publish.",
                traceId,
                $"Task id(s) not ready: {string.Join(", ", notReady)}.");
        }

        if (selected.Length == 0)
        {
            return Failure<ManualPublishExportEnvelope>(
                UseCaseStatus.NotFound,
                "No ready tasks matched the export request.",
                traceId,
                "Mark at least one task ready before exporting.");
        }

        var payload = new ManualPublishExportEnvelope
        {
            WorkspaceId = workspaceId,
            PlanId = planId,
            ExportedAtUtc = timeProvider.GetUtcNow(),
            Tasks = selected
                .OrderBy(task => task.ReadyForPublishAtUtc)
                .Select(task => new ManualPublishTaskPayload
                {
                    TaskId = task.TaskId,
                    Title = task.Title,
                    Summary = task.Summary,
                    PublishText = BuildPublishText(task),
                    Origin = task.Origin,
                    Details = task.Details,
                    Metadata = task.Metadata,
                    ReadyForPublishAtUtc = task.ReadyForPublishAtUtc ?? task.UpdatedAtUtc
                })
                .ToArray()
        };

        return UseCaseResult<ManualPublishExportEnvelope>.Success(
            payload,
            $"Exported {payload.Tasks.Count} ready task(s) for manual publish.",
            traceId);
    }

    IReadOnlyList<PendingTask> ITaskPoolExecutionCoordinator.ReturnReadyTasksToPending(
        string workspaceId,
        string planId,
        IReadOnlyCollection<string> blockedNodeIds,
        string reason)
    {
        var normalizedWorkspaceId = NormalizeRequired(workspaceId);
        var normalizedPlanId = NormalizeRequired(planId);
        if (normalizedWorkspaceId is null || normalizedPlanId is null || blockedNodeIds.Count == 0)
        {
            return Array.Empty<PendingTask>();
        }

        var blockedNodeSet = blockedNodeIds
            .Select(NormalizeOptional)
            .Where(nodeId => nodeId is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (blockedNodeSet.Count == 0)
        {
            return Array.Empty<PendingTask>();
        }

        lock (syncRoot)
        {
            if (stateStore is not null)
            {
                ReloadPersistedState();
            }

            var reverted = new List<PendingTask>();
            var updatedAt = timeProvider.GetUtcNow();

            foreach (var existing in tasks.Values
                         .Where(task =>
                             task.WorkspaceId.Equals(normalizedWorkspaceId, StringComparison.OrdinalIgnoreCase) &&
                             task.Origin.PlanId.Equals(normalizedPlanId, StringComparison.OrdinalIgnoreCase) &&
                             task.Status == PendingTaskStatus.ReadyForPublish &&
                             blockedNodeSet.Contains(task.Origin.NodeId))
                         .ToArray())
            {
                var metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["execution_review_required"] = "true",
                    ["execution_review_reason"] = reason
                };

                var revertedTask = existing with
                {
                    Status = PendingTaskStatus.Pending,
                    Metadata = metadata,
                    ReadyForPublishAtUtc = null,
                    UpdatedAtUtc = updatedAt
                };

                tasks[revertedTask.TaskId] = revertedTask;
                reverted.Add(revertedTask);
            }

            if (reverted.Count > 0)
            {
                PersistState();
            }

            return reverted;
        }
    }

    private UseCaseResult<WorkspaceSummary> RequireWorkspace(string workspaceId, string traceId, string useCaseName)
    {
        var normalizedWorkspaceId = NormalizeRequired(workspaceId);
        if (normalizedWorkspaceId is null)
        {
            return Failure<WorkspaceSummary>(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires a workspace id.",
                traceId,
                "WorkspaceId must not be empty.");
        }

        return workspaceService.GetSummary(new GetWorkspaceSummaryRequest
        {
            WorkspaceId = normalizedWorkspaceId
        });
    }

    private UseCaseResult<NodeResolution> ResolveNode(string workspaceId, string planId, string nodeId, string traceId, string useCaseName)
    {
        var normalizedWorkspaceId = NormalizeRequired(workspaceId);
        var normalizedPlanId = NormalizeRequired(planId);
        var normalizedNodeId = NormalizeRequired(nodeId);
        if (normalizedWorkspaceId is null || normalizedPlanId is null || normalizedNodeId is null)
        {
            return Failure<NodeResolution>(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires workspace, plan, and node identifiers.",
                traceId,
                "WorkspaceId, PlanId, and NodeId must not be empty.");
        }

        var planResult = planService.Get(new GetPlanRequest
        {
            WorkspaceId = normalizedWorkspaceId,
            PlanId = normalizedPlanId
        });

        if (!planResult.IsSuccess || planResult.Data is null)
        {
            return Failure<NodeResolution>(
                planResult.Status,
                planResult.Summary,
                traceId,
                planResult.Errors.ToArray());
        }

        var plan = planResult.Data;
        var node = plan.Nodes.FirstOrDefault(candidate => candidate.NodeId.Equals(normalizedNodeId, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            return Failure<NodeResolution>(
                UseCaseStatus.NotFound,
                $"Node '{normalizedNodeId}' was not found in the requested plan.",
                traceId,
                "Unknown node id.");
        }

        return UseCaseResult<NodeResolution>.Success(
            new NodeResolution(plan.PlanId, plan.Name, node),
            "Resolved plan node for task creation.",
            traceId);
    }

    private UseCaseResult<PendingTask> RequireTask(string workspaceId, string taskId, string traceId, string useCaseName)
    {
        var normalizedWorkspaceId = NormalizeRequired(workspaceId);
        var normalizedTaskId = NormalizeRequired(taskId);
        if (normalizedWorkspaceId is null || normalizedTaskId is null)
        {
            return Failure<PendingTask>(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires workspace and task identifiers.",
                traceId,
                "WorkspaceId and TaskId must not be empty.");
        }

        if (!tasks.TryGetValue(normalizedTaskId, out var task) ||
            !task.WorkspaceId.Equals(normalizedWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<PendingTask>(
                UseCaseStatus.NotFound,
                $"{useCaseName} could not resolve the task in the requested workspace.",
                traceId,
                $"Task '{normalizedTaskId}' does not exist in workspace '{normalizedWorkspaceId}'.");
        }

        return UseCaseResult<PendingTask>.Success(task, "Resolved task from internal task pool.", traceId);
    }

    private PendingTask CreatePendingTask(
        string workspaceId,
        NodeResolution resolution,
        string? titleOverride,
        string? summaryOverride,
        string? notes,
        PendingTaskDetails details,
        IReadOnlyDictionary<string, string> metadata)
    {
        var taskId = $"task-{Guid.NewGuid():N}";
        var createdAt = timeProvider.GetUtcNow();

        return new PendingTask
        {
            TaskId = taskId,
            WorkspaceId = workspaceId,
            Origin = new TaskOriginReference
            {
                SourceKind = details.Kind switch
                {
                    PendingTaskKind.Transport => TaskSourceKind.TransportNode,
                    PendingTaskKind.Trade => TaskSourceKind.TradeNeed,
                    PendingTaskKind.Outsource => TaskSourceKind.OutsourceNeed,
                    _ => throw new InvalidOperationException("Unsupported pending task kind.")
                },
                PlanId = resolution.PlanId,
                PlanName = resolution.PlanName,
                NodeId = resolution.Node.NodeId,
                NodeKind = resolution.Node.Kind,
                NodeTitle = resolution.Node.Title
            },
            Title = NormalizeOptional(titleOverride) ?? BuildTitle(resolution.Node, details),
            Summary = NormalizeOptional(summaryOverride) ?? BuildSummary(resolution.PlanName, resolution.Node, details),
            Notes = NormalizeOptional(notes),
            Status = PendingTaskStatus.Pending,
            Details = details,
            Metadata = MergeMetadata(resolution.Node.Metadata, metadata),
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        };
    }

    private static string BuildTitle(PlanNode node, PendingTaskDetails details)
    {
        return details switch
        {
            TransportPendingTaskDetails transport => $"{node.Title}: {transport.SourceLocation} -> {transport.DestinationLocation}",
            TradePendingTaskDetails trade => trade.TradeMode switch
            {
                PlanTradeMode.Purchase => $"Acquire {trade.TargetName}",
                PlanTradeMode.Sale => $"Sell {trade.TargetName}",
                PlanTradeMode.PrivateExchange => $"Arrange Private Exchange: {trade.TargetName}",
                _ => node.Title
            },
            OutsourcePendingTaskDetails outsource => $"Outsource {outsource.ActivityLabel}: {outsource.TargetName}",
            _ => node.Title
        };
    }

    private static string BuildSummary(string planName, PlanNode node, PendingTaskDetails details)
    {
        return details switch
        {
            TransportPendingTaskDetails transport =>
                $"Plan '{planName}' requires transport from {transport.SourceLocation} to {transport.DestinationLocation} via node '{node.Title}'.",
            TradePendingTaskDetails trade =>
                $"Plan '{planName}' requires a {trade.TradeMode} action for {trade.Quantity} x {trade.TargetName} via node '{node.Title}'.",
            OutsourcePendingTaskDetails outsource =>
                $"Plan '{planName}' requires outsourcing '{outsource.ActivityLabel}' for {outsource.Quantity} x {outsource.TargetName} via node '{node.Title}'.",
            _ => $"Plan '{planName}' requires action via node '{node.Title}'."
        };
    }

    private static string BuildPublishText(PendingTask task)
    {
        return task.Details switch
        {
            TransportPendingTaskDetails transport =>
                $"Transport request from {transport.SourceLocation} to {transport.DestinationLocation}. Cargo: {transport.CargoSummary ?? "see plan context"}. Partial completion allowed: {(transport.AllowPartialCompletion ? "yes" : "no")}.",
            TradePendingTaskDetails trade =>
                $"{trade.TradeMode} request for {trade.Quantity} x {trade.TargetName}{FormatOptionalSegment(" in ", trade.MarketScope)}{FormatOptionalSegment(" at unit price ", trade.UnitPricePreference?.ToString("0.##"))}.",
            OutsourcePendingTaskDetails outsource =>
                $"Outsource {outsource.ActivityLabel} for {outsource.Quantity} x {outsource.TargetName}{FormatOptionalSegment(" with delivery to ", outsource.DeliveryLocation)}. Materials provided: {(outsource.MaterialsProvidedByRequester ? "yes" : "no")}. Blueprint provided: {(outsource.BlueprintProvidedByRequester ? "yes" : "no")}.",
            _ => task.Summary
        };
    }

    private static string FormatOptionalSegment(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}{value}";

    private static IReadOnlyList<string> ValidateDetails(PendingTaskDetails details)
    {
        var errors = new List<string>();

        switch (details)
        {
            case TransportPendingTaskDetails transport:
                if (string.IsNullOrWhiteSpace(transport.SourceLocation))
                {
                    errors.Add("Transport task requires source_location.");
                }

                if (string.IsNullOrWhiteSpace(transport.DestinationLocation))
                {
                    errors.Add("Transport task requires destination_location.");
                }

                if (transport.VolumeCubicMeters.HasValue && transport.VolumeCubicMeters <= 0)
                {
                    errors.Add("Transport task volume_cubic_meters must be greater than zero when provided.");
                }

                if (transport.RewardHintIsk.HasValue && transport.RewardHintIsk < 0)
                {
                    errors.Add("Transport task reward_hint_isk cannot be negative.");
                }

                if (transport.CollateralHintIsk.HasValue && transport.CollateralHintIsk < 0)
                {
                    errors.Add("Transport task collateral_hint_isk cannot be negative.");
                }
                break;

            case TradePendingTaskDetails trade:
                if (string.IsNullOrWhiteSpace(trade.TargetTypeId))
                {
                    errors.Add("Trade task requires target_type_id.");
                }

                if (string.IsNullOrWhiteSpace(trade.TargetName))
                {
                    errors.Add("Trade task requires target_name.");
                }

                if (trade.Quantity <= 0)
                {
                    errors.Add("Trade task quantity must be greater than zero.");
                }

                if (trade.UnitPricePreference.HasValue && trade.UnitPricePreference < 0)
                {
                    errors.Add("Trade task unit_price_preference cannot be negative.");
                }
                break;

            case OutsourcePendingTaskDetails outsource:
                if (string.IsNullOrWhiteSpace(outsource.ActivityLabel))
                {
                    errors.Add("Outsource task requires activity_label.");
                }

                if (string.IsNullOrWhiteSpace(outsource.TargetTypeId))
                {
                    errors.Add("Outsource task requires target_type_id.");
                }

                if (string.IsNullOrWhiteSpace(outsource.TargetName))
                {
                    errors.Add("Outsource task requires target_name.");
                }

                if (outsource.Quantity <= 0)
                {
                    errors.Add("Outsource task quantity must be greater than zero.");
                }

                if (outsource.QuoteHintIsk.HasValue && outsource.QuoteHintIsk < 0)
                {
                    errors.Add("Outsource task quote_hint_isk cannot be negative.");
                }
                break;
        }

        return errors;
    }

    private static bool SupportsOutsource(PlanNode node)
    {
        return node.Kind == PlanNodeKind.Production
            || node.Kind == PlanNodeKind.Reaction
            || node.Kind == PlanNodeKind.CopyOrInvention
            || node.Kind == PlanNodeKind.Trade;
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> upstream,
        IReadOnlyDictionary<string, string> local)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in NormalizeMetadata(upstream))
        {
            merged[pair.Key] = pair.Value;
        }

        foreach (var pair in NormalizeMetadata(local))
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in metadata)
        {
            var key = NormalizeOptional(pair.Key);
            if (key is null)
            {
                continue;
            }

            normalized[key] = pair.Value.Trim();
        }

        return normalized;
    }

    private static string CreateTraceId(string useCaseName) => $"{useCaseName}:{Guid.NewGuid():N}";

    private static string? NormalizeRequired(string? value) => NormalizeOptional(value);

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

    private UseCaseResult<PendingTask> ExecutePersistedMutation(
        string traceId,
        string useCaseName,
        Func<UseCaseResult<PendingTask>> operation)
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

        return Failure<PendingTask>(
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

        tasks.Clear();

        foreach (var task in stateStore.Load().Tasks)
        {
            tasks[task.TaskId] = task;
        }
    }

    private void ReloadPersistedStateIfNeeded()
    {
        if (stateStore is null)
        {
            return;
        }

        ReloadPersistedState();
    }

    private void PersistState()
    {
        stateStore?.Save(new PendingTaskRuntimeState
        {
            Tasks = tasks.Values
                .OrderBy(task => task.CreatedAtUtc)
                .ToArray()
        });
    }

    private sealed record NodeResolution(string PlanId, string PlanName, PlanNode Node);
}

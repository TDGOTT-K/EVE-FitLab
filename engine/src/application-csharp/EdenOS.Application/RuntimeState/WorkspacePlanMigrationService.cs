using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Runtime;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.RuntimeState;

public sealed class WorkspacePlanMigrationService : IWorkspacePlanStateMigrationService
{
    private const string AuditExportCanonicalizationKind = "workspace-plan-repair-audit-bundle-json/v1";

    private static readonly JsonSerializerOptions AuditExportCanonicalJsonOptions = CreateAuditExportCanonicalJsonOptions();

    private sealed record IndexedPlan(int Index, IndustryPlan Plan);

    private sealed record IndexedTask(int Index, PendingTask Task);

    private sealed record IndexedExecutionState(int Index, PersistedExecutionStateRecord State);

    private sealed record IndexedManualMapping(
        int Index,
        string? PlanId,
        string? WorkspaceId,
        string? Source,
        string? Reason,
        string? RequestedBy);

    private sealed record PlannedRepair(
        WorkspacePlanStateRepairKind Kind,
        WorkspacePlanStateRepairSourceKind SourceKind,
        int Index,
        string TargetId,
        string? PlanId,
        string? FromWorkspaceId,
        string ToWorkspaceId,
        string? RequestedBy,
        string? Source,
        string? Reason,
        string Summary);

    private sealed record AnalysisResult(
        int LegacyPlanCount,
        IReadOnlyList<WorkspacePlanStateIssue> Issues,
        IReadOnlyList<WorkspacePlanManualReviewItem> ManualReviewPlans,
        IReadOnlyList<WorkspacePlanManualMappingEvaluation> ManualMappingEvaluations,
        IReadOnlyList<PlannedRepair> PlannedRepairs);

    private sealed record ApplyRepairsResult(
        IndustryPlanRuntimeState PlanState,
        PendingTaskRuntimeState TaskState,
        ExecutionRuntimeState ExecutionState,
        IReadOnlyList<WorkspacePlanStateRepair> AppliedRepairs);

    private sealed record AuditViewData(
        IReadOnlyList<WorkspacePlanRepairAuditRun> Runs,
        IReadOnlyList<WorkspacePlanRepairAuditCompactionSummary> Compactions,
        WorkspacePlanRepairAuditRetentionView Retention,
        WorkspacePlanRepairAuditIntegrityView Integrity,
        WorkspacePlanRepairAuditExportScopeKind ScopeKind,
        bool DetailedRunLimitApplied,
        bool EntryLimitApplied);

    private const int PersistenceRetryCount = 3;
    private const int AuditDetailedRunRetentionLimit = 12;

    private readonly IWorkspaceService workspaceService;
    private readonly TimeProvider timeProvider;
    private readonly LocalJsonStateStore<IndustryPlanRuntimeState>? planStateStore;
    private readonly LocalJsonStateStore<PendingTaskRuntimeState>? taskStateStore;
    private readonly LocalJsonStateStore<ExecutionRuntimeState>? executionStateStore;
    private readonly LocalJsonStateStore<WorkspacePlanRepairAuditState>? auditStateStore;
    private readonly object syncRoot = new();

    internal WorkspacePlanMigrationService(
        IWorkspaceService workspaceService,
        TimeProvider timeProvider,
        LocalJsonStateStore<IndustryPlanRuntimeState>? planStateStore,
        LocalJsonStateStore<PendingTaskRuntimeState>? taskStateStore,
        LocalJsonStateStore<ExecutionRuntimeState>? executionStateStore,
        LocalJsonStateStore<WorkspacePlanRepairAuditState>? auditStateStore)
    {
        this.workspaceService = workspaceService;
        this.timeProvider = timeProvider;
        this.planStateStore = planStateStore;
        this.taskStateStore = taskStateStore;
        this.executionStateStore = executionStateStore;
        this.auditStateStore = auditStateStore;
    }

    public UseCaseResult<WorkspacePlanStateReport> Report(ReportWorkspacePlanStateRequest request)
    {
        return Execute(
            tracePrefix: "runtime.inspect_plan_workspaces",
            operationKind: WorkspacePlanRepairOperationKind.Inspect,
            applyChanges: false,
            requestedBy: null,
            realignTaskWorkspaces: true,
            realignExecutionWorkspaces: true,
            manualMappings: Array.Empty<WorkspacePlanManualMapping>());
    }

    public UseCaseResult<WorkspacePlanStateReport> Preview(PreviewWorkspacePlanStateRepairRequest request)
    {
        return Execute(
            tracePrefix: "runtime.preview_plan_workspace_repair",
            operationKind: WorkspacePlanRepairOperationKind.Preview,
            applyChanges: false,
            requestedBy: NormalizeOptional(request.RequestedBy),
            realignTaskWorkspaces: request.RealignTaskWorkspaces,
            realignExecutionWorkspaces: request.RealignExecutionWorkspaces,
            manualMappings: request.ManualMappings);
    }

    public UseCaseResult<WorkspacePlanStateReport> Repair(RepairWorkspacePlanStateRequest request)
    {
        return Execute(
            tracePrefix: "runtime.repair_plan_workspaces",
            operationKind: WorkspacePlanRepairOperationKind.Apply,
            applyChanges: true,
            requestedBy: NormalizeOptional(request.RequestedBy),
            realignTaskWorkspaces: request.RealignTaskWorkspaces,
            realignExecutionWorkspaces: request.RealignExecutionWorkspaces,
            manualMappings: request.ManualMappings);
    }

    public UseCaseResult<WorkspacePlanRepairHistoryView> GetHistory(GetWorkspacePlanRepairHistoryRequest request)
    {
        var traceId = $"runtime.repair_plan_workspace_history:{Guid.NewGuid():N}";
        if (auditStateStore is null)
        {
            return UseCaseResult<WorkspacePlanRepairHistoryView>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Workspace-plan repair history requires persistent runtime audit files.",
                traceId,
                ["Configure runtime.state.root_path before running workspace-plan repair history commands."]);
        }

        var normalizedPlanId = NormalizeOptional(request.PlanId);
        var normalizedRepairRunId = NormalizeOptional(request.RepairRunId);
        var runLimit = request.RunLimit > 0 ? request.RunLimit : 20;
        var entryLimit = request.EntryLimit > 0 ? request.EntryLimit : 100;

        lock (syncRoot)
        {
            var auditState = auditStateStore.Load();
            var currentState = CaptureCurrentStateFingerprint();
            var viewData = BuildAuditViewData(
                auditState,
                normalizedPlanId,
                normalizedRepairRunId,
                runLimit,
                entryLimit,
                currentState);

            var view = new WorkspacePlanRepairHistoryView
            {
                CheckedAtUtc = timeProvider.GetUtcNow(),
                PlanId = normalizedPlanId,
                RepairRunId = normalizedRepairRunId,
                RunCount = viewData.Runs.Count,
                EntryCount = viewData.Runs.Sum(run => run.Entries.Count),
                CompactionCount = viewData.Compactions.Count,
                Retention = viewData.Retention,
                Integrity = viewData.Integrity,
                Compactions = viewData.Compactions,
                Runs = viewData.Runs
            };

            return UseCaseResult<WorkspacePlanRepairHistoryView>.Success(
                view,
                BuildHistorySummary(view.RunCount, view.EntryCount, view.CompactionCount, normalizedPlanId, normalizedRepairRunId),
                traceId);
        }
    }

    public UseCaseResult<WorkspacePlanRepairAuditExportView> ExportAudit(ExportWorkspacePlanRepairAuditRequest request)
    {
        var traceId = $"runtime.export_plan_workspace_repair_audit:{Guid.NewGuid():N}";
        if (auditStateStore is null)
        {
            return UseCaseResult<WorkspacePlanRepairAuditExportView>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Workspace-plan repair audit export requires persistent runtime audit files.",
                traceId,
                ["Configure runtime.state.root_path before running workspace-plan repair audit export commands."]);
        }

        var normalizedPlanId = NormalizeOptional(request.PlanId);
        var normalizedRepairRunId = NormalizeOptional(request.RepairRunId);
        var runLimit = request.RunLimit > 0 ? request.RunLimit : 200;
        var entryLimit = request.EntryLimit > 0 ? request.EntryLimit : 1000;

        lock (syncRoot)
        {
            var auditState = auditStateStore.Load();
            var currentState = CaptureCurrentStateFingerprint();
            var viewData = BuildAuditViewData(
                auditState,
                normalizedPlanId,
                normalizedRepairRunId,
                runLimit,
                entryLimit,
                currentState);
            var exportedAtUtc = timeProvider.GetUtcNow();
            var manifest = BuildAuditExportManifest(
                exportedAtUtc,
                normalizedPlanId,
                normalizedRepairRunId,
                viewData);
            var deliveryContract = BuildAuditExportDeliveryContract(request);

            var baseView = new WorkspacePlanRepairAuditExportView
            {
                ExportedAtUtc = exportedAtUtc,
                PlanId = normalizedPlanId,
                RepairRunId = normalizedRepairRunId,
                RunCount = viewData.Runs.Count,
                EntryCount = viewData.Runs.Sum(run => run.Entries.Count),
                CompactionCount = viewData.Compactions.Count,
                Retention = viewData.Retention,
                Integrity = viewData.Integrity,
                Compactions = viewData.Compactions,
                Runs = viewData.Runs,
                Manifest = manifest,
                DeliveryContract = deliveryContract
            };

            WorkspacePlanRepairAuditExportSeal seal;
            try
            {
                seal = BuildAuditExportSeal(request, baseView);
            }
            catch (CryptographicException ex)
            {
                return UseCaseResult<WorkspacePlanRepairAuditExportView>.Failure(
                    UseCaseStatus.InvalidInput,
                    "Workspace-plan repair audit export sealing failed.",
                    traceId,
                    [ex.Message]);
            }

            var view = baseView with
            {
                Seal = seal
            };

            return UseCaseResult<WorkspacePlanRepairAuditExportView>.Success(
                view,
                BuildHistorySummary(view.RunCount, view.EntryCount, view.CompactionCount, normalizedPlanId, normalizedRepairRunId),
                traceId);
        }
    }

    public UseCaseResult<WorkspacePlanRepairAuditVerificationView> VerifyAudit(VerifyWorkspacePlanRepairAuditRequest request)
    {
        var traceId = $"runtime.verify_plan_workspace_repair_audit:{Guid.NewGuid():N}";
        if (auditStateStore is null)
        {
            return UseCaseResult<WorkspacePlanRepairAuditVerificationView>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Workspace-plan repair audit verification requires persistent runtime audit files.",
                traceId,
                ["Configure runtime.state.root_path before running workspace-plan repair audit verification commands."]);
        }

        lock (syncRoot)
        {
            var auditState = auditStateStore.Load();
            var currentState = CaptureCurrentStateFingerprint();
            var retention = BuildRetentionView(auditState);
            var integrity = BuildIntegrityView(auditState, currentState);
            var view = new WorkspacePlanRepairAuditVerificationView
            {
                VerifiedAtUtc = timeProvider.GetUtcNow(),
                IntegrityOk = integrity.IntegrityOk,
                Retention = retention,
                Integrity = integrity
            };

            return UseCaseResult<WorkspacePlanRepairAuditVerificationView>.Success(
                view,
                BuildVerificationSummary(integrity),
                traceId);
        }
    }

    private UseCaseResult<WorkspacePlanStateReport> Execute(
        string tracePrefix,
        WorkspacePlanRepairOperationKind operationKind,
        bool applyChanges,
        string? requestedBy,
        bool realignTaskWorkspaces,
        bool realignExecutionWorkspaces,
        IReadOnlyList<WorkspacePlanManualMapping> manualMappings)
    {
        var traceId = $"{tracePrefix}:{Guid.NewGuid():N}";
        if (planStateStore is null || taskStateStore is null || executionStateStore is null)
        {
            return UseCaseResult<WorkspacePlanStateReport>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Workspace-plan migration requires persistent runtime state files.",
                traceId,
                ["Configure runtime.state.root_path before running workspace-plan migration commands."]);
        }

        if (RequiresAudit(operationKind) && auditStateStore is null)
        {
            return UseCaseResult<WorkspacePlanStateReport>.Failure(
                UseCaseStatus.DependencyUnavailable,
                "Workspace-plan migration audit requires persistent runtime audit files.",
                traceId,
                ["Configure runtime.state.root_path before running workspace-plan preview or repair commands."]);
        }

        lock (syncRoot)
        {
            for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
            {
                var planState = planStateStore.Load();
                var taskState = taskStateStore.Load();
                var executionState = executionStateStore.Load();
                var stateBefore = CaptureCurrentStateFingerprint();

                if (applyChanges && auditStateStore is not null)
                {
                    var auditState = auditStateStore.Load();
                    var integrity = BuildIntegrityView(auditState, stateBefore);
                    if (!integrity.LatestAppliedStateConsistent)
                    {
                        return UseCaseResult<WorkspacePlanStateReport>.Failure(
                            UseCaseStatus.Conflict,
                            "Workspace-plan runtime state changed outside the latest audited repair checkpoint.",
                            traceId,
                            integrity.Issues.Select(issue => issue.Summary).ToArray());
                    }
                }

                var initialAnalysis = Analyze(
                    planState,
                    taskState,
                    executionState,
                    manualMappings,
                    requestedBy,
                    includeResolvedReferences: true,
                    realignTaskWorkspaces,
                    realignExecutionWorkspaces);
                var checkedAtUtc = timeProvider.GetUtcNow();
                var repairRunId = RequiresAudit(operationKind)
                    ? $"repair-run-{Guid.NewGuid():N}"
                    : null;

                if (!applyChanges || initialAnalysis.PlannedRepairs.Count == 0)
                {
                    var report = BuildReport(
                        initialAnalysis,
                        initialAnalysis.PlannedRepairs,
                        Array.Empty<WorkspacePlanStateRepair>(),
                        checkedAtUtc,
                        operationKind,
                        repairRunId,
                        auditRecorded: false);

                    report = MaybePersistAudit(report, requestedBy, stateBefore, stateBefore);
                    return UseCaseResult<WorkspacePlanStateReport>.Success(
                        report,
                        BuildSummary(report.OperationKind, report.IssueCount, report.RepairableIssueCount, report.ManualReviewCount, report.AppliedRepairCount, report.AcceptedManualMappingCount),
                        traceId);
                }

                try
                {
                    var applyResult = ApplyRepairs(planState, taskState, executionState, initialAnalysis.PlannedRepairs);

                    planStateStore.Save(applyResult.PlanState);
                    taskStateStore.Save(applyResult.TaskState);
                    executionStateStore.Save(applyResult.ExecutionState);
                    var stateAfter = CaptureCurrentStateFingerprint();

                    var finalAnalysis = Analyze(
                        applyResult.PlanState,
                        applyResult.TaskState,
                        applyResult.ExecutionState,
                        manualMappings: Array.Empty<WorkspacePlanManualMapping>(),
                        requestedBy: null,
                        includeResolvedReferences: true,
                        realignTaskWorkspaces,
                        realignExecutionWorkspaces) with
                    {
                        ManualMappingEvaluations = initialAnalysis.ManualMappingEvaluations
                    };

                    var report = BuildReport(
                        finalAnalysis,
                        initialAnalysis.PlannedRepairs,
                        applyResult.AppliedRepairs,
                        timeProvider.GetUtcNow(),
                        operationKind,
                        repairRunId,
                        auditRecorded: false);

                    report = MaybePersistAudit(report, requestedBy, stateBefore, stateAfter);
                    return UseCaseResult<WorkspacePlanStateReport>.Success(
                        report,
                        BuildSummary(report.OperationKind, report.IssueCount, report.RepairableIssueCount, report.ManualReviewCount, report.AppliedRepairCount, report.AcceptedManualMappingCount),
                        traceId);
                }
                catch (RuntimeStateConcurrencyException) when (attempt < PersistenceRetryCount - 1)
                {
                }
            }
        }

        return UseCaseResult<WorkspacePlanStateReport>.Failure(
            UseCaseStatus.Conflict,
            "Workspace-plan migration detected concurrent runtime-state updates.",
            traceId,
            ["Runtime state changed while migration was saving repairs. Retry the migration command."]);
    }

    private AnalysisResult Analyze(
        IndustryPlanRuntimeState planState,
        PendingTaskRuntimeState taskState,
        ExecutionRuntimeState executionState,
        IReadOnlyList<WorkspacePlanManualMapping> manualMappings,
        string? requestedBy,
        bool includeResolvedReferences,
        bool realignTaskWorkspaces,
        bool realignExecutionWorkspaces)
    {
        var normalizedRequestedBy = NormalizeOptional(requestedBy);
        var planLookup = planState.Plans
            .Select((plan, index) => new IndexedPlan(index, plan))
            .ToDictionary(entry => entry.Plan.PlanId, StringComparer.OrdinalIgnoreCase);
        var tasksByPlan = taskState.Tasks
            .Select((task, index) => new IndexedTask(index, task))
            .GroupBy(entry => entry.Task.Origin.PlanId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var executionByPlan = executionState.States
            .Select((state, index) => new IndexedExecutionState(index, state))
            .GroupBy(entry => entry.State.PlanId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var workspaceCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<WorkspacePlanStateIssue>();
        var repairs = new List<PlannedRepair>();
        var manualReviewPlans = new List<WorkspacePlanManualReviewItem>();
        var manualMappingEvaluations = new List<WorkspacePlanManualMappingEvaluation>();

        var normalizedManualMappings = manualMappings
            .Select((mapping, index) => new IndexedManualMapping(
                index,
                NormalizeOptional(mapping.PlanId),
                NormalizeOptional(mapping.WorkspaceId),
                NormalizeOptional(mapping.Source),
                NormalizeOptional(mapping.Reason),
                NormalizeOptional(mapping.RequestedBy) ?? normalizedRequestedBy))
            .ToArray();
        foreach (var mapping in normalizedManualMappings.Where(mapping => mapping.PlanId is null || mapping.WorkspaceId is null))
        {
            manualMappingEvaluations.Add(new WorkspacePlanManualMappingEvaluation
            {
                PlanId = mapping.PlanId,
                WorkspaceId = mapping.WorkspaceId,
                Status = WorkspacePlanManualMappingStatus.InvalidMapping,
                Accepted = false,
                RequestedBy = mapping.RequestedBy,
                Source = mapping.Source,
                Reason = mapping.Reason,
                Summary = "Manual mapping requires both plan_id and workspace_id."
            });
        }

        var duplicatePlanIds = normalizedManualMappings
            .Where(mapping => mapping.PlanId is not null && mapping.WorkspaceId is not null)
            .GroupBy(mapping => mapping.PlanId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in normalizedManualMappings.Where(mapping => mapping.PlanId is not null && duplicatePlanIds.Contains(mapping.PlanId)))
        {
            manualMappingEvaluations.Add(new WorkspacePlanManualMappingEvaluation
            {
                PlanId = mapping.PlanId,
                WorkspaceId = mapping.WorkspaceId,
                Status = WorkspacePlanManualMappingStatus.DuplicatePlanId,
                Accepted = false,
                RequestedBy = mapping.RequestedBy,
                Source = mapping.Source,
                Reason = mapping.Reason,
                Summary = $"Multiple manual mappings were provided for plan '{mapping.PlanId}'. Keep exactly one decision per plan."
            });
        }

        var uniqueManualMappings = normalizedManualMappings
            .Where(mapping =>
                mapping.PlanId is not null
                && mapping.WorkspaceId is not null
                && !duplicatePlanIds.Contains(mapping.PlanId))
            .ToDictionary(mapping => mapping.PlanId!, mapping => mapping, StringComparer.OrdinalIgnoreCase);
        var visitedMappedPlanIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var indexedTask in taskState.Tasks.Select((task, index) => new IndexedTask(index, task)))
        {
            if (planLookup.ContainsKey(indexedTask.Task.Origin.PlanId))
            {
                continue;
            }

            issues.Add(new WorkspacePlanStateIssue
            {
                Kind = WorkspacePlanStateIssueKind.OrphanTaskReference,
                PlanId = indexedTask.Task.Origin.PlanId,
                WorkspaceId = NormalizeOptional(indexedTask.Task.WorkspaceId),
                RelatedId = indexedTask.Task.TaskId,
                Summary = $"Task '{indexedTask.Task.TaskId}' points at missing plan '{indexedTask.Task.Origin.PlanId}'.",
                References = includeResolvedReferences
                    ? [BuildTaskReference(indexedTask.Task)]
                    : Array.Empty<WorkspacePlanStateReference>()
            });
        }

        foreach (var indexedState in executionState.States.Select((state, index) => new IndexedExecutionState(index, state)))
        {
            if (planLookup.ContainsKey(indexedState.State.PlanId))
            {
                continue;
            }

            issues.Add(new WorkspacePlanStateIssue
            {
                Kind = WorkspacePlanStateIssueKind.OrphanExecutionReference,
                PlanId = indexedState.State.PlanId,
                WorkspaceId = NormalizeOptional(indexedState.State.WorkspaceId),
                RelatedId = BuildExecutionReferenceId(indexedState.State),
                Summary = $"Execution state '{BuildExecutionReferenceId(indexedState.State)}' points at missing plan '{indexedState.State.PlanId}'.",
                References = includeResolvedReferences
                    ? [BuildExecutionReference(indexedState.State)]
                    : Array.Empty<WorkspacePlanStateReference>()
            });
        }

        foreach (var indexedPlan in planState.Plans.Select((plan, index) => new IndexedPlan(index, plan)))
        {
            tasksByPlan.TryGetValue(indexedPlan.Plan.PlanId, out var taskRefs);
            executionByPlan.TryGetValue(indexedPlan.Plan.PlanId, out var executionRefs);

            var references = BuildReferences(taskRefs, executionRefs);
            var candidateWorkspaceIds = references
                .Select(reference => NormalizeOptional(reference.WorkspaceId))
                .Where(workspaceId => workspaceId is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(workspaceId => workspaceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var validCandidateWorkspaceIds = candidateWorkspaceIds
                .Where(workspaceId => WorkspaceExists(workspaceCache, workspaceId))
                .ToArray();

            var normalizedPlanWorkspaceId = NormalizeOptional(indexedPlan.Plan.WorkspaceId);
            var hasValidExistingWorkspace = normalizedPlanWorkspaceId is not null
                && WorkspaceExists(workspaceCache, normalizedPlanWorkspaceId);
            var planIssueKinds = new List<WorkspacePlanStateIssueKind>();
            PlannedRepair? automaticPlanRepair = null;
            var effectivePlanWorkspaceId = hasValidExistingWorkspace ? normalizedPlanWorkspaceId : null;

            if (normalizedPlanWorkspaceId is null)
            {
                issues.Add(new WorkspacePlanStateIssue
                {
                    Kind = WorkspacePlanStateIssueKind.PlanMissingWorkspaceBinding,
                    PlanId = indexedPlan.Plan.PlanId,
                    Summary = $"Plan '{indexedPlan.Plan.PlanId}' has no workspace binding.",
                    CandidateWorkspaceIds = candidateWorkspaceIds,
                    References = includeResolvedReferences ? references : Array.Empty<WorkspacePlanStateReference>()
                });
                planIssueKinds.Add(WorkspacePlanStateIssueKind.PlanMissingWorkspaceBinding);

                if (validCandidateWorkspaceIds.Length == 1)
                {
                    automaticPlanRepair = new PlannedRepair(
                        WorkspacePlanStateRepairKind.BindPlanWorkspace,
                        WorkspacePlanStateRepairSourceKind.Automatic,
                        indexedPlan.Index,
                        indexedPlan.Plan.PlanId,
                        indexedPlan.Plan.PlanId,
                        null,
                        validCandidateWorkspaceIds[0],
                        null,
                        null,
                        null,
                        $"Bind plan '{indexedPlan.Plan.PlanId}' to workspace '{validCandidateWorkspaceIds[0]}' from a single valid runtime reference.");
                    repairs.Add(automaticPlanRepair);
                    effectivePlanWorkspaceId = automaticPlanRepair.ToWorkspaceId;
                }
                else if (candidateWorkspaceIds.Length > 1)
                {
                    issues.Add(new WorkspacePlanStateIssue
                    {
                        Kind = WorkspacePlanStateIssueKind.AmbiguousPlanWorkspaceBinding,
                        PlanId = indexedPlan.Plan.PlanId,
                        Summary = $"Plan '{indexedPlan.Plan.PlanId}' is referenced by multiple workspaces and cannot be rebound safely.",
                        CandidateWorkspaceIds = candidateWorkspaceIds,
                        References = includeResolvedReferences ? references : Array.Empty<WorkspacePlanStateReference>()
                    });
                    planIssueKinds.Add(WorkspacePlanStateIssueKind.AmbiguousPlanWorkspaceBinding);
                }
            }
            else if (!hasValidExistingWorkspace)
            {
                issues.Add(new WorkspacePlanStateIssue
                {
                    Kind = WorkspacePlanStateIssueKind.PlanWorkspaceMissing,
                    PlanId = indexedPlan.Plan.PlanId,
                    WorkspaceId = normalizedPlanWorkspaceId,
                    Summary = $"Plan '{indexedPlan.Plan.PlanId}' points at missing workspace '{normalizedPlanWorkspaceId}'.",
                    CandidateWorkspaceIds = candidateWorkspaceIds,
                    References = includeResolvedReferences ? references : Array.Empty<WorkspacePlanStateReference>()
                });
                planIssueKinds.Add(WorkspacePlanStateIssueKind.PlanWorkspaceMissing);

                if (validCandidateWorkspaceIds.Length == 1)
                {
                    automaticPlanRepair = new PlannedRepair(
                        WorkspacePlanStateRepairKind.RebindPlanWorkspace,
                        WorkspacePlanStateRepairSourceKind.Automatic,
                        indexedPlan.Index,
                        indexedPlan.Plan.PlanId,
                        indexedPlan.Plan.PlanId,
                        normalizedPlanWorkspaceId,
                        validCandidateWorkspaceIds[0],
                        null,
                        null,
                        null,
                        $"Rebind plan '{indexedPlan.Plan.PlanId}' from missing workspace '{normalizedPlanWorkspaceId}' to '{validCandidateWorkspaceIds[0]}' from a single valid runtime reference.");
                    repairs.Add(automaticPlanRepair);
                    effectivePlanWorkspaceId = automaticPlanRepair.ToWorkspaceId;
                }
            }

            if (uniqueManualMappings.TryGetValue(indexedPlan.Plan.PlanId, out var manualMapping))
            {
                visitedMappedPlanIds.Add(indexedPlan.Plan.PlanId);

                var manualEvaluation = EvaluateManualMapping(
                    indexedPlan,
                    manualMapping,
                    normalizedPlanWorkspaceId,
                    hasValidExistingWorkspace,
                    automaticPlanRepair,
                    workspaceCache);
                manualMappingEvaluations.Add(manualEvaluation);

                if (manualEvaluation.Accepted && manualMapping.WorkspaceId is not null)
                {
                    var manualRepair = new PlannedRepair(
                        normalizedPlanWorkspaceId is null
                            ? WorkspacePlanStateRepairKind.BindPlanWorkspace
                            : WorkspacePlanStateRepairKind.RebindPlanWorkspace,
                        WorkspacePlanStateRepairSourceKind.ManualMapping,
                        indexedPlan.Index,
                        indexedPlan.Plan.PlanId,
                        indexedPlan.Plan.PlanId,
                        normalizedPlanWorkspaceId,
                        manualMapping.WorkspaceId,
                        manualMapping.RequestedBy,
                        manualMapping.Source,
                        manualMapping.Reason,
                        BuildManualRepairSummary(
                            indexedPlan.Plan.PlanId,
                            normalizedPlanWorkspaceId,
                            manualMapping.WorkspaceId,
                            manualMapping.RequestedBy,
                            manualMapping.Source,
                            manualMapping.Reason));
                    repairs.Add(manualRepair);
                    effectivePlanWorkspaceId = manualRepair.ToWorkspaceId;
                }
            }

            if (effectivePlanWorkspaceId is null && planIssueKinds.Count > 0)
            {
                manualReviewPlans.Add(new WorkspacePlanManualReviewItem
                {
                    PlanId = indexedPlan.Plan.PlanId,
                    WorkspaceId = normalizedPlanWorkspaceId,
                    Summary = BuildManualReviewSummary(indexedPlan.Plan.PlanId, planIssueKinds, candidateWorkspaceIds, normalizedPlanWorkspaceId),
                    BlockingIssueKinds = planIssueKinds,
                    CandidateWorkspaceIds = candidateWorkspaceIds,
                    References = includeResolvedReferences ? references : Array.Empty<WorkspacePlanStateReference>()
                });
            }

            if (effectivePlanWorkspaceId is null)
            {
                continue;
            }

            if (taskRefs is not null)
            {
                foreach (var indexedTask in taskRefs)
                {
                    var taskWorkspaceId = NormalizeOptional(indexedTask.Task.WorkspaceId);
                    if (string.Equals(taskWorkspaceId, effectivePlanWorkspaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    issues.Add(new WorkspacePlanStateIssue
                    {
                        Kind = WorkspacePlanStateIssueKind.TaskWorkspaceMismatch,
                        PlanId = indexedPlan.Plan.PlanId,
                        WorkspaceId = taskWorkspaceId,
                        RelatedId = indexedTask.Task.TaskId,
                        Summary = $"Task '{indexedTask.Task.TaskId}' uses workspace '{taskWorkspaceId ?? "<missing>"}' but plan '{indexedPlan.Plan.PlanId}' belongs to '{effectivePlanWorkspaceId}'.",
                        CandidateWorkspaceIds = [effectivePlanWorkspaceId],
                        References = includeResolvedReferences
                            ? [BuildTaskReference(indexedTask.Task)]
                            : Array.Empty<WorkspacePlanStateReference>()
                    });

                    if (realignTaskWorkspaces)
                    {
                        repairs.Add(new PlannedRepair(
                            WorkspacePlanStateRepairKind.RealignTaskWorkspace,
                            WorkspacePlanStateRepairSourceKind.Automatic,
                            indexedTask.Index,
                            indexedTask.Task.TaskId,
                            indexedPlan.Plan.PlanId,
                            taskWorkspaceId,
                            effectivePlanWorkspaceId,
                            null,
                            null,
                            null,
                            $"Realign task '{indexedTask.Task.TaskId}' to workspace '{effectivePlanWorkspaceId}' after confirming plan ownership."));
                    }
                }
            }

            if (executionRefs is not null)
            {
                foreach (var indexedState in executionRefs)
                {
                    var executionWorkspaceId = NormalizeOptional(indexedState.State.WorkspaceId);
                    if (string.Equals(executionWorkspaceId, effectivePlanWorkspaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var executionReferenceId = BuildExecutionReferenceId(indexedState.State);
                    issues.Add(new WorkspacePlanStateIssue
                    {
                        Kind = WorkspacePlanStateIssueKind.ExecutionWorkspaceMismatch,
                        PlanId = indexedPlan.Plan.PlanId,
                        WorkspaceId = executionWorkspaceId,
                        RelatedId = executionReferenceId,
                        Summary = $"Execution state '{executionReferenceId}' uses workspace '{executionWorkspaceId ?? "<missing>"}' but plan '{indexedPlan.Plan.PlanId}' belongs to '{effectivePlanWorkspaceId}'.",
                        CandidateWorkspaceIds = [effectivePlanWorkspaceId],
                        References = includeResolvedReferences
                            ? [BuildExecutionReference(indexedState.State)]
                            : Array.Empty<WorkspacePlanStateReference>()
                    });

                    if (realignExecutionWorkspaces)
                    {
                        repairs.Add(new PlannedRepair(
                            WorkspacePlanStateRepairKind.RealignExecutionWorkspace,
                            WorkspacePlanStateRepairSourceKind.Automatic,
                            indexedState.Index,
                            executionReferenceId,
                            indexedPlan.Plan.PlanId,
                            executionWorkspaceId,
                            effectivePlanWorkspaceId,
                            null,
                            null,
                            null,
                            $"Realign execution state '{executionReferenceId}' to workspace '{effectivePlanWorkspaceId}' after confirming plan ownership."));
                    }
                }
            }
        }

        foreach (var missingPlanMapping in uniqueManualMappings.Values.Where(mapping => !visitedMappedPlanIds.Contains(mapping.PlanId!)))
        {
            manualMappingEvaluations.Add(new WorkspacePlanManualMappingEvaluation
            {
                PlanId = missingPlanMapping.PlanId,
                WorkspaceId = missingPlanMapping.WorkspaceId,
                Status = WorkspacePlanManualMappingStatus.MissingPlan,
                Accepted = false,
                RequestedBy = missingPlanMapping.RequestedBy,
                Source = missingPlanMapping.Source,
                Reason = missingPlanMapping.Reason,
                Summary = $"Manual mapping references missing plan '{missingPlanMapping.PlanId}'."
            });
        }

        return new AnalysisResult(
            planState.Plans.Count(plan => string.IsNullOrWhiteSpace(plan.WorkspaceId)),
            issues,
            manualReviewPlans
                .OrderBy(item => item.PlanId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            manualMappingEvaluations
                .OrderBy(item => item.PlanId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.WorkspaceId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            repairs
                .GroupBy(repair => $"{repair.Kind}:{repair.TargetId}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray());
    }

    private ApplyRepairsResult ApplyRepairs(
        IndustryPlanRuntimeState planState,
        PendingTaskRuntimeState taskState,
        ExecutionRuntimeState executionState,
        IReadOnlyList<PlannedRepair> plannedRepairs)
    {
        var now = timeProvider.GetUtcNow();
        var plans = planState.Plans.ToArray();
        var tasks = taskState.Tasks.ToArray();
        var states = executionState.States.ToArray();
        var appliedRepairs = new List<WorkspacePlanStateRepair>(plannedRepairs.Count);

        foreach (var repair in plannedRepairs)
        {
            switch (repair.Kind)
            {
                case WorkspacePlanStateRepairKind.BindPlanWorkspace:
                case WorkspacePlanStateRepairKind.RebindPlanWorkspace:
                {
                    var plan = plans[repair.Index];
                    plans[repair.Index] = plan with
                    {
                        WorkspaceId = repair.ToWorkspaceId,
                        UpdatedAtUtc = now
                    };
                    appliedRepairs.Add(ToReportRepair(repair, applied: true));
                    break;
                }

                case WorkspacePlanStateRepairKind.RealignTaskWorkspace:
                {
                    var task = tasks[repair.Index];
                    tasks[repair.Index] = task with
                    {
                        WorkspaceId = repair.ToWorkspaceId,
                        UpdatedAtUtc = now
                    };
                    appliedRepairs.Add(ToReportRepair(repair, applied: true));
                    break;
                }

                case WorkspacePlanStateRepairKind.RealignExecutionWorkspace:
                {
                    var state = states[repair.Index];
                    states[repair.Index] = state with
                    {
                        WorkspaceId = repair.ToWorkspaceId,
                        Events = state.Events
                            .Select(evt => evt with { WorkspaceId = repair.ToWorkspaceId })
                            .ToArray(),
                        UpdatedAtUtc = now
                    };
                    appliedRepairs.Add(ToReportRepair(repair, applied: true));
                    break;
                }
            }
        }

        return new ApplyRepairsResult(
            planState with { Plans = plans },
            taskState with { Tasks = tasks },
            executionState with { States = states },
            appliedRepairs);
    }

    private WorkspacePlanStateReport BuildReport(
        AnalysisResult analysis,
        IReadOnlyList<PlannedRepair> plannedRepairs,
        IReadOnlyList<WorkspacePlanStateRepair> appliedRepairs,
        DateTimeOffset checkedAtUtc,
        WorkspacePlanRepairOperationKind operationKind,
        string? repairRunId,
        bool auditRecorded)
    {
        return new WorkspacePlanStateReport
        {
            CheckedAtUtc = checkedAtUtc,
            OperationKind = operationKind,
            RepairRunId = repairRunId,
            AuditRecorded = auditRecorded,
            LegacyPlanCount = analysis.LegacyPlanCount,
            IssueCount = analysis.Issues.Count,
            RepairableIssueCount = plannedRepairs.Count,
            ManualReviewCount = analysis.ManualReviewPlans.Count,
            ManualMappingCount = analysis.ManualMappingEvaluations.Count,
            AcceptedManualMappingCount = analysis.ManualMappingEvaluations.Count(item => item.Accepted),
            AppliedRepairCount = appliedRepairs.Count,
            Issues = analysis.Issues,
            ManualReviewPlans = analysis.ManualReviewPlans,
            ManualMappingEvaluations = analysis.ManualMappingEvaluations,
            PlannedRepairs = plannedRepairs.Select(repair => ToReportRepair(repair, applied: false)).ToArray(),
            Repairs = appliedRepairs
        };
    }

    private WorkspacePlanStateReport MaybePersistAudit(
        WorkspacePlanStateReport report,
        string? requestedBy,
        WorkspacePlanRuntimeStateFingerprint stateBefore,
        WorkspacePlanRuntimeStateFingerprint stateAfter)
    {
        if (!RequiresAudit(report.OperationKind) || string.IsNullOrWhiteSpace(report.RepairRunId))
        {
            return report;
        }

        var auditRecorded = TryPersistAuditRun(
            report,
            NormalizeOptional(requestedBy),
            stateBefore,
            stateAfter);
        return auditRecorded
            ? report with { AuditRecorded = true }
            : report;
    }

    private bool TryPersistAuditRun(
        WorkspacePlanStateReport report,
        string? requestedBy,
        WorkspacePlanRuntimeStateFingerprint stateBefore,
        WorkspacePlanRuntimeStateFingerprint stateAfter)
    {
        if (auditStateStore is null)
        {
            return false;
        }

        for (var attempt = 0; attempt < PersistenceRetryCount; attempt++)
        {
            try
            {
                var auditState = auditStateStore.Load();
                var auditRun = BuildAuditRun(
                    report,
                    requestedBy,
                    stateBefore,
                    stateAfter,
                    auditState.LatestSequenceNumber + 1,
                    auditState.HeadRunHash);
                var updatedState = AppendAuditRun(auditState, auditRun);
                auditStateStore.Save(updatedState);
                return true;
            }
            catch (RuntimeStateConcurrencyException) when (attempt < PersistenceRetryCount - 1)
            {
            }
        }

        return false;
    }

    private static WorkspacePlanRepairAuditRun BuildAuditRun(
        WorkspacePlanStateReport report,
        string? requestedBy,
        WorkspacePlanRuntimeStateFingerprint stateBefore,
        WorkspacePlanRuntimeStateFingerprint stateAfter,
        long sequenceNumber,
        string? previousRunHash)
    {
        var recordedAtUtc = report.CheckedAtUtc;
        var entries = new List<WorkspacePlanRepairAuditEntry>();

        foreach (var evaluation in report.ManualMappingEvaluations)
        {
            entries.Add(new WorkspacePlanRepairAuditEntry
            {
                EntryId = $"audit-entry-{Guid.NewGuid():N}",
                Kind = WorkspacePlanRepairAuditEntryKind.ManualMappingDecision,
                Outcome = evaluation.Accepted
                    ? report.OperationKind == WorkspacePlanRepairOperationKind.Apply
                        ? WorkspacePlanRepairAuditOutcome.Applied
                        : WorkspacePlanRepairAuditOutcome.PreviewOnly
                    : WorkspacePlanRepairAuditOutcome.Rejected,
                RecordedAtUtc = recordedAtUtc,
                PlanId = evaluation.PlanId,
                ToWorkspaceId = evaluation.WorkspaceId,
                ManualMappingStatus = evaluation.Status,
                RequestedBy = evaluation.RequestedBy ?? requestedBy,
                Source = evaluation.Source,
                Reason = evaluation.Reason,
                Summary = evaluation.Summary
            });
        }

        foreach (var repair in report.OperationKind == WorkspacePlanRepairOperationKind.Apply ? report.Repairs : report.PlannedRepairs)
        {
            entries.Add(new WorkspacePlanRepairAuditEntry
            {
                EntryId = $"audit-entry-{Guid.NewGuid():N}",
                Kind = WorkspacePlanRepairAuditEntryKind.Repair,
                Outcome = report.OperationKind == WorkspacePlanRepairOperationKind.Apply
                    ? WorkspacePlanRepairAuditOutcome.Applied
                    : WorkspacePlanRepairAuditOutcome.PreviewOnly,
                RecordedAtUtc = recordedAtUtc,
                PlanId = repair.PlanId,
                RelatedId = repair.RelatedId,
                FromWorkspaceId = repair.FromWorkspaceId,
                ToWorkspaceId = repair.ToWorkspaceId,
                RepairKind = repair.Kind,
                RepairSourceKind = repair.SourceKind,
                RequestedBy = repair.RequestedBy ?? requestedBy,
                Source = repair.Source,
                Reason = repair.Reason,
                Summary = repair.Summary
            });
        }

        foreach (var manualReview in report.ManualReviewPlans)
        {
            entries.Add(new WorkspacePlanRepairAuditEntry
            {
                EntryId = $"audit-entry-{Guid.NewGuid():N}",
                Kind = WorkspacePlanRepairAuditEntryKind.ManualReview,
                Outcome = WorkspacePlanRepairAuditOutcome.PendingManualReview,
                RecordedAtUtc = recordedAtUtc,
                PlanId = manualReview.PlanId,
                FromWorkspaceId = manualReview.WorkspaceId,
                Summary = manualReview.Summary
            });
        }

        var draftRun = new WorkspacePlanRepairAuditRun
        {
            SequenceNumber = sequenceNumber,
            RepairRunId = report.RepairRunId!,
            OperationKind = report.OperationKind,
            RecordedAtUtc = recordedAtUtc,
            PreviousRunHash = previousRunHash,
            RequestedBy = requestedBy,
            IssueCount = report.IssueCount,
            RepairableIssueCount = report.RepairableIssueCount,
            ManualReviewCount = report.ManualReviewCount,
            AcceptedManualMappingCount = report.AcceptedManualMappingCount,
            AppliedRepairCount = report.AppliedRepairCount,
            StateBefore = stateBefore,
            StateAfter = stateAfter,
            Entries = entries
        };

        var payloadHash = ComputeAuditRunPayloadHash(draftRun);
        return draftRun with
        {
            PayloadHash = payloadHash,
            RunHash = ComputeAuditRunHash(previousRunHash, payloadHash)
        };
    }

    private static WorkspacePlanRepairAuditState AppendAuditRun(
        WorkspacePlanRepairAuditState auditState,
        WorkspacePlanRepairAuditRun auditRun)
    {
        var runs = auditState.Runs.ToList();
        var compactions = auditState.Compactions.ToList();
        runs.Insert(0, auditRun);

        var compactedThroughSequenceNumber = auditState.CompactedThroughSequenceNumber;
        var compactedHeadRunHash = auditState.CompactedHeadRunHash;
        if (runs.Count > AuditDetailedRunRetentionLimit)
        {
            var removedRuns = runs
                .Skip(AuditDetailedRunRetentionLimit)
                .OrderBy(run => run.SequenceNumber)
                .ToArray();

            if (removedRuns.Length > 0)
            {
                runs = runs.Take(AuditDetailedRunRetentionLimit).ToList();
                var compaction = BuildCompactionSummary(removedRuns);
                compactions.Add(compaction);
                compactedThroughSequenceNumber = compaction.NewestSequenceNumber;
                compactedHeadRunHash = compaction.NewestRunHash;
            }
        }

        return auditState with
        {
            LatestSequenceNumber = auditRun.SequenceNumber,
            HeadRunHash = auditRun.RunHash,
            CompactedThroughSequenceNumber = compactedThroughSequenceNumber,
            CompactedHeadRunHash = compactedHeadRunHash,
            LatestAppliedCheckpoint = auditRun.OperationKind == WorkspacePlanRepairOperationKind.Apply
                ? new WorkspacePlanRepairAuditCheckpoint
                {
                    RepairRunId = auditRun.RepairRunId,
                    RecordedAtUtc = auditRun.RecordedAtUtc,
                    StateAfter = auditRun.StateAfter
                }
                : auditState.LatestAppliedCheckpoint,
            Compactions = compactions.ToArray(),
            Runs = runs.ToArray()
        };
    }

    private AuditViewData BuildAuditViewData(
        WorkspacePlanRepairAuditState auditState,
        string? normalizedPlanId,
        string? normalizedRepairRunId,
        int runLimit,
        int entryLimit,
        WorkspacePlanRuntimeStateFingerprint currentState)
    {
        var scopeKind = DetermineExportScopeKind(normalizedPlanId, normalizedRepairRunId);
        var candidateRuns = auditState.Runs
            .OrderByDescending(run => run.SequenceNumber)
            .AsEnumerable();

        if (normalizedRepairRunId is not null)
        {
            candidateRuns = candidateRuns.Where(run => string.Equals(run.RepairRunId, normalizedRepairRunId, StringComparison.OrdinalIgnoreCase));
        }

        var matchedRuns = new List<WorkspacePlanRepairAuditRun>();
        var entryLimitApplied = false;
        foreach (var run in candidateRuns)
        {
            var matchedEntries = run.Entries
                .Where(entry => normalizedPlanId is null || EntryMatchesPlan(entry, normalizedPlanId))
                .ToArray();

            if (normalizedPlanId is not null && matchedEntries.Length == 0)
            {
                continue;
            }

            entryLimitApplied |= matchedEntries.Length > entryLimit;
            matchedRuns.Add(run with { Entries = matchedEntries.Take(entryLimit).ToArray() });
        }

        var detailedRunLimitApplied = matchedRuns.Count > runLimit;
        var filteredRuns = matchedRuns
            .Take(runLimit)
            .ToArray();
        var filteredCompactions = auditState.Compactions
            .Where(compaction =>
                (normalizedPlanId is null || compaction.PlanIds.Contains(normalizedPlanId, StringComparer.OrdinalIgnoreCase))
                && (normalizedRepairRunId is null || compaction.RepairRunIds.Contains(normalizedRepairRunId, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(compaction => compaction.NewestSequenceNumber)
            .ToArray();

        return new AuditViewData(
            filteredRuns,
            filteredCompactions,
            BuildRetentionView(auditState),
            BuildIntegrityView(auditState, currentState),
            scopeKind,
            detailedRunLimitApplied,
            entryLimitApplied);
    }

    private WorkspacePlanRuntimeStateFingerprint CaptureCurrentStateFingerprint()
    {
        return new WorkspacePlanRuntimeStateFingerprint
        {
            PlanState = planStateStore?.CurrentFingerprint ?? new RuntimeStateFileFingerprint(),
            TaskState = taskStateStore?.CurrentFingerprint ?? new RuntimeStateFileFingerprint(),
            ExecutionState = executionStateStore?.CurrentFingerprint ?? new RuntimeStateFileFingerprint()
        };
    }

    private static WorkspacePlanRepairAuditRetentionView BuildRetentionView(WorkspacePlanRepairAuditState auditState)
    {
        return new WorkspacePlanRepairAuditRetentionView
        {
            DetailedRunRetentionLimit = AuditDetailedRunRetentionLimit,
            DetailedRunCount = auditState.Runs.Count,
            CompactionCount = auditState.Compactions.Count,
            CompactedRunCount = auditState.Compactions.Sum(compaction => compaction.RunCount),
            CompactedEntryCount = auditState.Compactions.Sum(compaction => compaction.EntryCount),
            CompactedThroughSequenceNumber = auditState.CompactedThroughSequenceNumber,
            LastCompactedAtUtc = auditState.Compactions.Count == 0
                ? null
                : auditState.Compactions.Max(compaction => compaction.CompactedAtUtc)
        };
    }

    private static WorkspacePlanRepairAuditIntegrityView BuildIntegrityView(
        WorkspacePlanRepairAuditState auditState,
        WorkspacePlanRuntimeStateFingerprint currentState)
    {
        var issues = new List<WorkspacePlanRepairAuditIntegrityIssue>();
        var chainIntact = true;
        var orderedRuns = auditState.Runs
            .OrderBy(run => run.SequenceNumber)
            .ToArray();
        var expectedSequence = auditState.CompactedThroughSequenceNumber + 1;
        var previousRunHash = auditState.CompactedHeadRunHash;

        foreach (var run in orderedRuns)
        {
            if (run.SequenceNumber != expectedSequence)
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "sequence_gap",
                    RepairRunId = run.RepairRunId,
                    Summary = $"Audit run '{run.RepairRunId}' has sequence {run.SequenceNumber}, but {expectedSequence} was expected."
                });
                expectedSequence = run.SequenceNumber;
            }

            if (!string.Equals(run.PreviousRunHash, previousRunHash, StringComparison.Ordinal))
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "previous_hash_mismatch",
                    RepairRunId = run.RepairRunId,
                    Summary = $"Audit run '{run.RepairRunId}' does not point at the expected previous hash."
                });
            }

            var expectedPayloadHash = ComputeAuditRunPayloadHash(run);
            if (!string.Equals(run.PayloadHash, expectedPayloadHash, StringComparison.Ordinal))
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "payload_hash_mismatch",
                    RepairRunId = run.RepairRunId,
                    Summary = $"Audit run '{run.RepairRunId}' payload hash does not match its recorded contents."
                });
            }

            var expectedRunHash = ComputeAuditRunHash(run.PreviousRunHash, expectedPayloadHash);
            if (!string.Equals(run.RunHash, expectedRunHash, StringComparison.Ordinal))
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "run_hash_mismatch",
                    RepairRunId = run.RepairRunId,
                    Summary = $"Audit run '{run.RepairRunId}' chain hash does not match its recorded payload."
                });
            }

            previousRunHash = run.RunHash;
            expectedSequence = run.SequenceNumber + 1;
        }

        if (orderedRuns.Length == 0)
        {
            if (auditState.LatestSequenceNumber != auditState.CompactedThroughSequenceNumber)
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "missing_head_run",
                    Summary = "Audit state metadata references detailed runs that are no longer present."
                });
            }
        }
        else
        {
            var latestRun = orderedRuns[^1];
            if (latestRun.SequenceNumber != auditState.LatestSequenceNumber)
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "head_sequence_mismatch",
                    RepairRunId = latestRun.RepairRunId,
                    Summary = $"Audit state head sequence is {auditState.LatestSequenceNumber}, but latest detailed run is {latestRun.SequenceNumber}."
                });
            }

            if (!string.Equals(latestRun.RunHash, auditState.HeadRunHash, StringComparison.Ordinal))
            {
                chainIntact = false;
                issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
                {
                    Code = "head_hash_mismatch",
                    RepairRunId = latestRun.RepairRunId,
                    Summary = $"Audit state head hash does not match latest run '{latestRun.RepairRunId}'."
                });
            }
        }

        var latestAppliedStateConsistent = auditState.LatestAppliedCheckpoint is null
            || StateFingerprintsMatch(auditState.LatestAppliedCheckpoint.StateAfter, currentState);
        if (!latestAppliedStateConsistent && auditState.LatestAppliedCheckpoint is not null)
        {
            issues.Add(new WorkspacePlanRepairAuditIntegrityIssue
            {
                Code = "state_checkpoint_mismatch",
                RepairRunId = auditState.LatestAppliedCheckpoint.RepairRunId,
                Summary = BuildStateCheckpointMismatchSummary(auditState.LatestAppliedCheckpoint.StateAfter, currentState)
            });
        }

        var coverage = BuildCoverageView(auditState, orderedRuns);
        var boundaryStatements = BuildIntegrityBoundaryStatements(auditState, orderedRuns, coverage, latestAppliedStateConsistent);

        return new WorkspacePlanRepairAuditIntegrityView
        {
            IntegrityOk = chainIntact && latestAppliedStateConsistent,
            ChainIntact = chainIntact,
            LatestAppliedStateConsistent = latestAppliedStateConsistent,
            LatestSequenceNumber = auditState.LatestSequenceNumber,
            HeadRunHash = auditState.HeadRunHash,
            CompactedHeadRunHash = auditState.CompactedHeadRunHash,
            CurrentState = currentState,
            LatestAppliedCheckpoint = auditState.LatestAppliedCheckpoint,
            Coverage = coverage,
            BoundaryStatements = boundaryStatements,
            Issues = issues
        };
    }

    private static WorkspacePlanRepairAuditCoverageView BuildCoverageView(
        WorkspacePlanRepairAuditState auditState,
        IReadOnlyList<WorkspacePlanRepairAuditRun> orderedRuns)
    {
        var oldestDetailedRun = orderedRuns.Count == 0 ? null : orderedRuns[0];
        var newestDetailedRun = orderedRuns.Count == 0 ? null : orderedRuns[^1];
        var hasCompactedPrefix = auditState.CompactedThroughSequenceNumber > 0;

        string summary;
        if (oldestDetailedRun is null)
        {
            summary = hasCompactedPrefix
                ? $"Detailed repair runs are no longer retained. Sequences 1..{auditState.CompactedThroughSequenceNumber} are represented only by compaction summaries."
                : "No workspace-plan repair audit runs are recorded yet.";
        }
        else if (hasCompactedPrefix)
        {
            summary = $"Detailed repair runs cover sequences {oldestDetailedRun.SequenceNumber}..{newestDetailedRun!.SequenceNumber}; earlier sequences 1..{auditState.CompactedThroughSequenceNumber} are represented only by compaction summaries.";
        }
        else
        {
            summary = $"Detailed repair runs cover sequences {oldestDetailedRun.SequenceNumber}..{newestDetailedRun!.SequenceNumber} without a compacted prefix.";
        }

        return new WorkspacePlanRepairAuditCoverageView
        {
            HasDetailedRuns = oldestDetailedRun is not null,
            HasCompactedPrefix = hasCompactedPrefix,
            OldestDetailedSequenceNumber = oldestDetailedRun?.SequenceNumber,
            NewestDetailedSequenceNumber = newestDetailedRun?.SequenceNumber,
            CompactedThroughSequenceNumber = auditState.CompactedThroughSequenceNumber,
            CompactedHeadRunHash = auditState.CompactedHeadRunHash,
            HeadRunHash = auditState.HeadRunHash,
            Summary = summary
        };
    }

    private static IReadOnlyList<string> BuildIntegrityBoundaryStatements(
        WorkspacePlanRepairAuditState auditState,
        IReadOnlyList<WorkspacePlanRepairAuditRun> orderedRuns,
        WorkspacePlanRepairAuditCoverageView coverage,
        bool latestAppliedStateConsistent)
    {
        var statements = new List<string>
        {
            coverage.Summary
        };

        if (auditState.Compactions.Count > 0)
        {
            statements.Add($"Compaction summaries retained: {auditState.Compactions.Count}. Compacted entry total: {auditState.Compactions.Sum(compaction => compaction.EntryCount)}.");
        }

        if (orderedRuns.Count > 0)
        {
            statements.Add($"Current retained head sequence is {orderedRuns[^1].SequenceNumber} with head hash '{orderedRuns[^1].RunHash}'.");
        }

        statements.Add(
            latestAppliedStateConsistent
                ? "Latest audited apply checkpoint still matches the current runtime state."
                : "Latest audited apply checkpoint no longer matches the current runtime state.");

        return statements;
    }

    private static WorkspacePlanRepairAuditExportManifest BuildAuditExportManifest(
        DateTimeOffset exportedAtUtc,
        string? normalizedPlanId,
        string? normalizedRepairRunId,
        AuditViewData viewData)
    {
        var scopeFiltered = normalizedPlanId is not null || normalizedRepairRunId is not null;
        var chainContinuityPreserved = !scopeFiltered && !viewData.DetailedRunLimitApplied;
        var scopeSummary = BuildExportScopeSummary(viewData.ScopeKind, normalizedPlanId, normalizedRepairRunId);
        var boundarySummary = BuildExportBoundarySummary(
            viewData.Integrity.Coverage,
            scopeFiltered,
            viewData.DetailedRunLimitApplied,
            viewData.EntryLimitApplied);

        return new WorkspacePlanRepairAuditExportManifest
        {
            SchemaVersion = "workspace-plan-repair-audit-export/v1",
            ExportedAtUtc = exportedAtUtc,
            ScopeKind = viewData.ScopeKind,
            ScopeFiltered = scopeFiltered,
            ChainContinuityPreserved = chainContinuityPreserved,
            DetailedRunLimitApplied = viewData.DetailedRunLimitApplied,
            EntryLimitApplied = viewData.EntryLimitApplied,
            PlanId = normalizedPlanId,
            RepairRunId = normalizedRepairRunId,
            DetailedRunCount = viewData.Runs.Count,
            DetailedEntryCount = viewData.Runs.Sum(run => run.Entries.Count),
            CompactionCount = viewData.Compactions.Count,
            Retention = viewData.Retention,
            Coverage = viewData.Integrity.Coverage,
            ScopeSummary = scopeSummary,
            BoundarySummary = boundarySummary
        };
    }

    private static WorkspacePlanRepairAuditExportSeal BuildAuditExportSeal(
        ExportWorkspacePlanRepairAuditRequest request,
        WorkspacePlanRepairAuditExportView exportView)
    {
        var manifestHash = ComputeHash(SerializeAuditExportCanonicalJson(exportView.Manifest));
        var payload = new
        {
            exportView.ExportedAtUtc,
            exportView.PlanId,
            exportView.RepairRunId,
            exportView.RunCount,
            exportView.EntryCount,
            exportView.CompactionCount,
            exportView.Retention,
            exportView.Integrity,
            exportView.Compactions,
            exportView.Runs,
            exportView.Manifest,
            exportView.DeliveryContract
        };
        var payloadJson = SerializeAuditExportCanonicalJson(payload);
        var payloadHash = ComputeHash(payloadJson);
        var sealedAtUtc = exportView.ExportedAtUtc;
        var keyId = NormalizeOptional(request.SealKeyId);
        var privateKeyPem = NormalizeOptional(request.SealPrivateKeyPem);

        if (privateKeyPem is null)
        {
            return new WorkspacePlanRepairAuditExportSeal
            {
                SealKind = WorkspacePlanRepairAuditSealKind.DigestOnly,
                IsSealed = false,
                SealedAtUtc = sealedAtUtc,
                KeyId = keyId,
                ManifestHash = manifestHash,
                PayloadHash = payloadHash,
                CanonicalManifestJson = SerializeAuditExportCanonicalJson(exportView.Manifest),
                CanonicalPayloadJson = payloadJson,
                TrustAnchor = BuildAuditExportTrustAnchor(
                    WorkspacePlanRepairAuditTrustAnchorKind.DetachedDigest,
                    keyId,
                    publicKeyFingerprint: null,
                    publicKeyEmbedded: false),
                Summary = $"Audit export includes detached manifest and payload digests computed with canonicalization '{AuditExportCanonicalizationKind}'."
            };
        }

        using var signer = ECDsa.Create();
        signer.ImportFromPem(privateKeyPem);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var signatureBytes = signer.SignData(payloadBytes, HashAlgorithmName.SHA256);
        var publicKeyPem = signer.ExportSubjectPublicKeyInfoPem();
        var publicKeyFingerprint = ComputeHash(publicKeyPem);

        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(publicKeyPem);
        if (!verifier.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256))
        {
            throw new CryptographicException("Workspace-plan repair audit export seal could not be verified with the derived public key.");
        }

        return new WorkspacePlanRepairAuditExportSeal
        {
            SealKind = WorkspacePlanRepairAuditSealKind.EcdsaP256Sha256,
            IsSealed = true,
            SealedAtUtc = sealedAtUtc,
            KeyId = keyId,
            ManifestHash = manifestHash,
            PayloadHash = payloadHash,
            Signature = Convert.ToBase64String(signatureBytes),
            PublicKeyPem = publicKeyPem,
            PublicKeyFingerprint = publicKeyFingerprint,
            CanonicalManifestJson = SerializeAuditExportCanonicalJson(exportView.Manifest),
            CanonicalPayloadJson = payloadJson,
            TrustAnchor = BuildAuditExportTrustAnchor(
                WorkspacePlanRepairAuditTrustAnchorKind.EmbeddedPublicKey,
                keyId,
                publicKeyFingerprint,
                publicKeyEmbedded: true),
            Summary = $"Audit export is sealed with ECDSA P-256 over the canonical payload hash '{payloadHash}' using canonicalization '{AuditExportCanonicalizationKind}'."
        };
    }

    private static WorkspacePlanRepairAuditTrustAnchor BuildAuditExportTrustAnchor(
        WorkspacePlanRepairAuditTrustAnchorKind anchorKind,
        string? keyId,
        string? publicKeyFingerprint,
        bool publicKeyEmbedded)
    {
        return new WorkspacePlanRepairAuditTrustAnchor
        {
            AnchorKind = anchorKind,
            CanonicalizationKind = AuditExportCanonicalizationKind,
            KeyId = keyId,
            PublicKeyFingerprint = publicKeyFingerprint,
            PublicKeyEmbedded = publicKeyEmbedded,
            RequiresExternalPinning = true,
            WitnessStrategy = new WorkspacePlanRepairAuditWitnessStrategy
            {
                StrategyKind = WorkspacePlanRepairAuditWitnessStrategyKind.DetachedWitnessReceipt,
                ReceiptSchemaVersion = "workspace-plan-repair-audit-witness-receipt/v1",
                SupportsIndependentWitness = true,
                RequiresSeparateWitnessIdentity = true,
                OperatorBoundarySummary = "Operator self-signing proves authorship and integrity of the exported bundle, but it does not by itself create an independent witness. Treat the operator key as the producer identity unless another party pins or countersigns it out-of-band.",
                WitnessBoundarySummary = "An independent witness receipt must bind the manifest hash or payload hash with a witness key that is separate from the operator seal key. The witness only becomes independently trusted when the verifier pins the witness key or witness fingerprint out-of-band and confirms that it is distinct from the operator identity.",
                VerificationSummary = "Anchor-aware verification should first validate the operator seal or detached digest, then validate any detached witness receipt over the same canonical payload. A pinned witness receipt raises the result above operator self-signing; an embedded witness key without out-of-band pinning remains self-described material."
            },
            Summary = anchorKind switch
            {
                WorkspacePlanRepairAuditTrustAnchorKind.EmbeddedPublicKey =>
                    $"Bundle embeds signer verification material{(publicKeyFingerprint is null ? string.Empty : $" with fingerprint '{publicKeyFingerprint}'")}. Treat the embedded key as self-described verification material until the key or fingerprint is pinned externally.",
                _ =>
                    "Bundle exposes detached digest material only. The payload hash must be pinned, countersigned, or otherwise anchored outside the bundle before an external verifier can treat it as independently trusted."
            },
            ExternalVerificationSummary = anchorKind switch
            {
                WorkspacePlanRepairAuditTrustAnchorKind.EmbeddedPublicKey =>
                    "External verifiers should import a separately pinned public key or pinned fingerprint, then verify the detached signature over the canonical payload. The embedded public key may be used as convenience material, but it is not by itself an independent trust anchor.",
                _ =>
                    "External verifiers should pin the payload hash or manifest hash in an external system, or obtain a separately distributed signing key before accepting the bundle as externally anchored."
            }
        };
    }

    private static WorkspacePlanRepairAuditDeliveryContract BuildAuditExportDeliveryContract(
        ExportWorkspacePlanRepairAuditRequest request)
    {
        var currentStage = string.IsNullOrWhiteSpace(request.SealPrivateKeyPem)
            ? WorkspacePlanRepairAuditPromotionStageKind.DetachedDigestBundle
            : WorkspacePlanRepairAuditPromotionStageKind.SealedBundle;
        var recommendedNextStage = currentStage switch
        {
            WorkspacePlanRepairAuditPromotionStageKind.DetachedDigestBundle => WorkspacePlanRepairAuditPromotionStageKind.SealedBundle,
            _ => WorkspacePlanRepairAuditPromotionStageKind.WitnessedBundle
        };

        return new WorkspacePlanRepairAuditDeliveryContract
        {
            SchemaVersion = "workspace-plan-repair-audit-delivery-contract/v1",
            CurrentStage = currentStage,
            RecommendedNextStage = recommendedNextStage,
            ExternalVerificationRequiredForPromotion = true,
            ExternalVerificationRequiredForPublish = true,
            WitnessReceiptRecommended = true,
            IndependentWitnessPreferred = true,
            PromotionSummary = currentStage switch
            {
                WorkspacePlanRepairAuditPromotionStageKind.SealedBundle =>
                    "This export is already sealed and ready for bundle-level promotion into a witnessed bundle. The next formal step is to attach a detached witness receipt over the same canonical manifest and payload hashes before treating it as a delivery candidate.",
                _ =>
                    "This export currently exposes detached digest material only. Promote it to a sealed bundle first, then attach a detached witness receipt before treating it as a delivery candidate."
            },
            PublishBoundarySummary = "Publishing does not mutate the sealed payload. Publish is a packaging and promotion step that may bundle the sealed archive, detached witness receipts, verifier materials, and verification reports after local verification confirms an external trust anchor. Without that external verification, the result remains a sealed or witnessed bundle, not a published delivery package.",
            DeliveryArtifactSummary = "A delivery-oriented package should include the sealed bundle archive, delivery contract, verifier material with pinned key or payload expectations, the latest verification report, and any detached witness receipt or witness key material needed for external verification."
        };
    }

    private static WorkspacePlanRepairAuditExportScopeKind DetermineExportScopeKind(
        string? normalizedPlanId,
        string? normalizedRepairRunId)
    {
        if (normalizedRepairRunId is not null)
        {
            return WorkspacePlanRepairAuditExportScopeKind.RepairRunExtract;
        }

        return normalizedPlanId is not null
            ? WorkspacePlanRepairAuditExportScopeKind.PlanFilteredExtract
            : WorkspacePlanRepairAuditExportScopeKind.RetainedLedger;
    }

    private static string BuildExportScopeSummary(
        WorkspacePlanRepairAuditExportScopeKind scopeKind,
        string? normalizedPlanId,
        string? normalizedRepairRunId)
    {
        return scopeKind switch
        {
            WorkspacePlanRepairAuditExportScopeKind.RepairRunExtract => $"This export is scoped to repair run '{normalizedRepairRunId}'.",
            WorkspacePlanRepairAuditExportScopeKind.PlanFilteredExtract => $"This export is scoped to audit entries for plan '{normalizedPlanId}'.",
            _ => "This export covers the full retained workspace-plan repair audit ledger."
        };
    }

    private static string BuildExportBoundarySummary(
        WorkspacePlanRepairAuditCoverageView coverage,
        bool scopeFiltered,
        bool detailedRunLimitApplied,
        bool entryLimitApplied)
    {
        var parts = new List<string>
        {
            coverage.Summary
        };

        if (scopeFiltered)
        {
            parts.Add("Because the export is scope-filtered, the included runs are an extract rather than a standalone replayable proof of the full audit chain.");
        }

        if (detailedRunLimitApplied)
        {
            parts.Add("Older matching detailed runs were omitted by run_limit.");
        }

        if (entryLimitApplied)
        {
            parts.Add("At least one included run was truncated by entry_limit.");
        }

        return string.Join(" ", parts);
    }

    private static WorkspacePlanRepairAuditCompactionSummary BuildCompactionSummary(IReadOnlyList<WorkspacePlanRepairAuditRun> runs)
    {
        return new WorkspacePlanRepairAuditCompactionSummary
        {
            CompactionId = $"audit-compaction-{Guid.NewGuid():N}",
            CompactedAtUtc = DateTimeOffset.UtcNow,
            OldestSequenceNumber = runs.Min(run => run.SequenceNumber),
            NewestSequenceNumber = runs.Max(run => run.SequenceNumber),
            OldestRecordedAtUtc = runs.Min(run => run.RecordedAtUtc),
            NewestRecordedAtUtc = runs.Max(run => run.RecordedAtUtc),
            NewestRunHash = runs
                .OrderBy(run => run.SequenceNumber)
                .Last()
                .RunHash,
            RunCount = runs.Count,
            EntryCount = runs.Sum(run => run.Entries.Count),
            PreviewRunCount = runs.Count(run => run.OperationKind == WorkspacePlanRepairOperationKind.Preview),
            ApplyRunCount = runs.Count(run => run.OperationKind == WorkspacePlanRepairOperationKind.Apply),
            RejectedEntryCount = runs.Sum(run => run.Entries.Count(entry => entry.Outcome == WorkspacePlanRepairAuditOutcome.Rejected)),
            PendingManualReviewEntryCount = runs.Sum(run => run.Entries.Count(entry => entry.Outcome == WorkspacePlanRepairAuditOutcome.PendingManualReview)),
            PlanIds = runs
                .SelectMany(run => run.Entries.Select(entry => entry.PlanId))
                .Where(planId => !string.IsNullOrWhiteSpace(planId))
                .Select(planId => planId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(planId => planId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RepairRunIds = runs
                .Select(run => run.RepairRunId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(runId => runId, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string ComputeAuditRunPayloadHash(WorkspacePlanRepairAuditRun run)
    {
        var payload = new
        {
            run.SequenceNumber,
            run.RepairRunId,
            run.OperationKind,
            run.RecordedAtUtc,
            run.RequestedBy,
            run.IssueCount,
            run.RepairableIssueCount,
            run.ManualReviewCount,
            run.AcceptedManualMappingCount,
            run.AppliedRepairCount,
            run.StateBefore,
            run.StateAfter,
            Entries = run.Entries.Select(entry => new
            {
                entry.EntryId,
                entry.Kind,
                entry.Outcome,
                entry.RecordedAtUtc,
                entry.PlanId,
                entry.RelatedId,
                entry.FromWorkspaceId,
                entry.ToWorkspaceId,
                entry.RepairKind,
                entry.RepairSourceKind,
                entry.ManualMappingStatus,
                entry.RequestedBy,
                entry.Source,
                entry.Reason,
                entry.Summary
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        return ComputeHash(json);
    }

    private static string ComputeAuditRunHash(string? previousRunHash, string payloadHash)
    {
        return ComputeHash($"{previousRunHash ?? string.Empty}\n{payloadHash}");
    }

    private static string SerializeAuditExportCanonicalJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, AuditExportCanonicalJsonOptions);
    }

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static JsonSerializerOptions CreateAuditExportCanonicalJsonOptions()
    {
        return global::EdenOS.Application.JsonSerializerOptionsFactory.CreateSnakeCase();
    }

    private static bool StateFingerprintsMatch(
        WorkspacePlanRuntimeStateFingerprint expected,
        WorkspacePlanRuntimeStateFingerprint actual)
    {
        return FileFingerprintsMatch(expected.PlanState, actual.PlanState)
            && FileFingerprintsMatch(expected.TaskState, actual.TaskState)
            && FileFingerprintsMatch(expected.ExecutionState, actual.ExecutionState);
    }

    private static bool FileFingerprintsMatch(RuntimeStateFileFingerprint expected, RuntimeStateFileFingerprint actual)
    {
        return expected.Revision == actual.Revision
            && string.Equals(expected.ContentHash, actual.ContentHash, StringComparison.Ordinal);
    }

    private static string BuildStateCheckpointMismatchSummary(
        WorkspacePlanRuntimeStateFingerprint expected,
        WorkspacePlanRuntimeStateFingerprint actual)
    {
        var mismatches = new List<string>();

        AppendStateMismatch(mismatches, "plan", expected.PlanState, actual.PlanState);
        AppendStateMismatch(mismatches, "task", expected.TaskState, actual.TaskState);
        AppendStateMismatch(mismatches, "execution", expected.ExecutionState, actual.ExecutionState);

        return $"Latest applied repair checkpoint no longer matches runtime state: {string.Join("; ", mismatches)}.";
    }

    private static void AppendStateMismatch(
        IList<string> mismatches,
        string stateName,
        RuntimeStateFileFingerprint expected,
        RuntimeStateFileFingerprint actual)
    {
        if (FileFingerprintsMatch(expected, actual))
        {
            return;
        }

        mismatches.Add(
            $"{stateName} revision/hash expected {expected.Revision}/{expected.ContentHash}, actual {actual.Revision}/{actual.ContentHash}");
    }

    private static bool EntryMatchesPlan(WorkspacePlanRepairAuditEntry entry, string planId)
    {
        return string.Equals(entry.PlanId, planId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresAudit(WorkspacePlanRepairOperationKind operationKind)
    {
        return operationKind is WorkspacePlanRepairOperationKind.Preview or WorkspacePlanRepairOperationKind.Apply;
    }

    private static string BuildSummary(
        WorkspacePlanRepairOperationKind operationKind,
        int issueCount,
        int repairableIssueCount,
        int manualReviewCount,
        int appliedRepairCount,
        int acceptedManualMappingCount)
    {
        if (operationKind != WorkspacePlanRepairOperationKind.Apply)
        {
            if (issueCount == 0)
            {
                return "Workspace-plan runtime state is already consistent.";
            }

            return acceptedManualMappingCount > 0
                ? $"Previewed {repairableIssueCount} workspace-plan repair(s), including {acceptedManualMappingCount} accepted manual mapping(s); {manualReviewCount} plan(s) still need manual review."
                : $"Found {issueCount} workspace-plan state issue(s); {repairableIssueCount} repair preview(s) are available and {manualReviewCount} plan(s) still need manual review.";
        }

        if (appliedRepairCount == 0)
        {
            return issueCount == 0
                ? "Workspace-plan runtime state was already consistent."
                : $"No workspace-plan repairs were applied; {issueCount} issue(s) remain and {manualReviewCount} plan(s) still need manual review.";
        }

        return issueCount == 0
            ? $"Applied {appliedRepairCount} workspace-plan repair(s); runtime state is now consistent."
            : $"Applied {appliedRepairCount} workspace-plan repair(s); {issueCount} issue(s) remain and {manualReviewCount} plan(s) still need manual review.";
    }

    private static string BuildHistorySummary(int runCount, int entryCount, int compactionCount, string? planId, string? repairRunId)
    {
        if (runCount == 0)
        {
            if (compactionCount > 0)
            {
                return repairRunId is not null
                    ? $"Repair run '{repairRunId}' is no longer in detailed history and is now covered by {compactionCount} compaction summary record(s)."
                    : planId is not null
                        ? $"No detailed workspace-plan repair audit history matched plan '{planId}', but {compactionCount} compaction summary record(s) still reference it."
                        : $"No detailed workspace-plan repair audit history is recorded yet, but {compactionCount} compaction summary record(s) exist.";
            }

            return repairRunId is not null
                ? $"No workspace-plan repair audit run matched '{repairRunId}'."
                : planId is not null
                    ? $"No workspace-plan repair audit history matched plan '{planId}'."
                    : "No workspace-plan repair audit history is recorded yet.";
        }

        return repairRunId is not null
            ? $"Loaded workspace-plan repair audit run '{repairRunId}' with {entryCount} ledger entrie(s)."
            : planId is not null
                ? $"Loaded {runCount} workspace-plan repair audit run(s) for plan '{planId}' with {entryCount} ledger entrie(s); {compactionCount} compaction summary record(s) also matched."
                : $"Loaded {runCount} workspace-plan repair audit run(s) with {entryCount} ledger entrie(s); {compactionCount} compaction summary record(s) are available.";
    }

    private static string BuildVerificationSummary(WorkspacePlanRepairAuditIntegrityView integrity)
    {
        return integrity.IntegrityOk
            ? $"Workspace-plan repair audit integrity is valid. {integrity.Coverage.Summary}"
            : $"Workspace-plan repair audit verification found {integrity.Issues.Count} integrity issue(s).";
    }

    private WorkspacePlanManualMappingEvaluation EvaluateManualMapping(
        IndexedPlan indexedPlan,
        IndexedManualMapping mapping,
        string? normalizedPlanWorkspaceId,
        bool hasValidExistingWorkspace,
        PlannedRepair? automaticPlanRepair,
        IDictionary<string, bool> workspaceCache)
    {
        if (mapping.WorkspaceId is null)
        {
            return new WorkspacePlanManualMappingEvaluation
            {
                PlanId = mapping.PlanId,
                WorkspaceId = mapping.WorkspaceId,
                Status = WorkspacePlanManualMappingStatus.InvalidMapping,
                Accepted = false,
                RequestedBy = mapping.RequestedBy,
                Source = mapping.Source,
                Reason = mapping.Reason,
                Summary = "Manual mapping requires a non-empty workspace_id."
            };
        }

        if (!WorkspaceExists(workspaceCache, mapping.WorkspaceId))
        {
            return new WorkspacePlanManualMappingEvaluation
            {
                PlanId = indexedPlan.Plan.PlanId,
                WorkspaceId = mapping.WorkspaceId,
                Status = WorkspacePlanManualMappingStatus.WorkspaceNotFound,
                Accepted = false,
                RequestedBy = mapping.RequestedBy,
                Source = mapping.Source,
                Reason = mapping.Reason,
                Summary = $"Manual mapping points at missing workspace '{mapping.WorkspaceId}'."
            };
        }

        if (hasValidExistingWorkspace && normalizedPlanWorkspaceId is not null)
        {
            return string.Equals(normalizedPlanWorkspaceId, mapping.WorkspaceId, StringComparison.OrdinalIgnoreCase)
                ? new WorkspacePlanManualMappingEvaluation
                {
                    PlanId = indexedPlan.Plan.PlanId,
                    WorkspaceId = mapping.WorkspaceId,
                    Status = WorkspacePlanManualMappingStatus.AlreadyBoundToWorkspace,
                    Accepted = false,
                    RequestedBy = mapping.RequestedBy,
                    Source = mapping.Source,
                    Reason = mapping.Reason,
                    Summary = $"Plan '{indexedPlan.Plan.PlanId}' is already bound to workspace '{normalizedPlanWorkspaceId}'."
                }
                : new WorkspacePlanManualMappingEvaluation
                {
                    PlanId = indexedPlan.Plan.PlanId,
                    WorkspaceId = mapping.WorkspaceId,
                    Status = WorkspacePlanManualMappingStatus.ConflictsWithExistingWorkspaceBinding,
                    Accepted = false,
                    RequestedBy = mapping.RequestedBy,
                    Source = mapping.Source,
                    Reason = mapping.Reason,
                    Summary = $"Plan '{indexedPlan.Plan.PlanId}' is already bound to workspace '{normalizedPlanWorkspaceId}'. Manual mapping cannot override an intact binding."
                };
        }

        if (automaticPlanRepair is not null)
        {
            return string.Equals(automaticPlanRepair.ToWorkspaceId, mapping.WorkspaceId, StringComparison.OrdinalIgnoreCase)
                ? new WorkspacePlanManualMappingEvaluation
                {
                    PlanId = indexedPlan.Plan.PlanId,
                    WorkspaceId = mapping.WorkspaceId,
                    Status = WorkspacePlanManualMappingStatus.RedundantWithAutomaticRepair,
                    Accepted = false,
                    RequestedBy = mapping.RequestedBy,
                    Source = mapping.Source,
                    Reason = mapping.Reason,
                    Summary = $"Plan '{indexedPlan.Plan.PlanId}' already has a safe automatic repair to workspace '{automaticPlanRepair.ToWorkspaceId}'."
                }
                : new WorkspacePlanManualMappingEvaluation
                {
                    PlanId = indexedPlan.Plan.PlanId,
                    WorkspaceId = mapping.WorkspaceId,
                    Status = WorkspacePlanManualMappingStatus.ConflictsWithAutomaticRepair,
                    Accepted = false,
                    RequestedBy = mapping.RequestedBy,
                    Source = mapping.Source,
                    Reason = mapping.Reason,
                    Summary = $"Plan '{indexedPlan.Plan.PlanId}' already has a safe automatic repair to workspace '{automaticPlanRepair.ToWorkspaceId}'. Remove the manual mapping or use the automatic repair."
                };
        }

        return new WorkspacePlanManualMappingEvaluation
        {
            PlanId = indexedPlan.Plan.PlanId,
            WorkspaceId = mapping.WorkspaceId,
            Status = WorkspacePlanManualMappingStatus.Accepted,
            Accepted = true,
            RequestedBy = mapping.RequestedBy,
            Source = mapping.Source,
            Reason = mapping.Reason,
            Summary = $"Manual mapping will bind plan '{indexedPlan.Plan.PlanId}' to workspace '{mapping.WorkspaceId}'."
        };
    }

    private bool WorkspaceExists(IDictionary<string, bool> cache, string workspaceId)
    {
        if (cache.TryGetValue(workspaceId, out var exists))
        {
            return exists;
        }

        var result = workspaceService.GetSummary(new GetWorkspaceSummaryRequest
        {
            WorkspaceId = workspaceId
        });

        exists = result.IsSuccess && result.Data is not null;
        cache[workspaceId] = exists;
        return exists;
    }

    private static IReadOnlyList<WorkspacePlanStateReference> BuildReferences(
        IReadOnlyList<IndexedTask>? taskRefs,
        IReadOnlyList<IndexedExecutionState>? executionRefs)
    {
        var references = new List<WorkspacePlanStateReference>();

        if (taskRefs is not null)
        {
            references.AddRange(taskRefs.Select(entry => BuildTaskReference(entry.Task)));
        }

        if (executionRefs is not null)
        {
            references.AddRange(executionRefs.Select(entry => BuildExecutionReference(entry.State)));
        }

        return references;
    }

    private static WorkspacePlanStateReference BuildTaskReference(PendingTask task)
    {
        return new WorkspacePlanStateReference
        {
            Kind = WorkspacePlanStateReferenceKind.Task,
            ReferenceId = task.TaskId,
            WorkspaceId = NormalizeOptional(task.WorkspaceId),
            Summary = task.Title
        };
    }

    private static WorkspacePlanStateReference BuildExecutionReference(PersistedExecutionStateRecord state)
    {
        return new WorkspacePlanStateReference
        {
            Kind = WorkspacePlanStateReferenceKind.ExecutionState,
            ReferenceId = BuildExecutionReferenceId(state),
            WorkspaceId = NormalizeOptional(state.WorkspaceId),
            Summary = $"{state.Events.Count} event(s), {state.CompletedNodeIds.Count} completed node(s)"
        };
    }

    private static WorkspacePlanStateRepair ToReportRepair(PlannedRepair repair, bool applied)
    {
        return new WorkspacePlanStateRepair
        {
            Kind = repair.Kind,
            SourceKind = repair.SourceKind,
            PlanId = repair.PlanId,
            RelatedId = repair.TargetId,
            FromWorkspaceId = repair.FromWorkspaceId,
            ToWorkspaceId = repair.ToWorkspaceId,
            Applied = applied,
            RequestedBy = repair.RequestedBy,
            Source = repair.Source,
            Reason = repair.Reason,
            Summary = repair.Summary
        };
    }

    private static string BuildManualRepairSummary(
        string planId,
        string? fromWorkspaceId,
        string toWorkspaceId,
        string? requestedBy,
        string? source,
        string? reason)
    {
        var summaryParts = new List<string>
        {
            fromWorkspaceId is null
                ? $"Bind plan '{planId}' to workspace '{toWorkspaceId}' from an explicit manual mapping."
                : $"Rebind plan '{planId}' from workspace '{fromWorkspaceId}' to '{toWorkspaceId}' from an explicit manual mapping."
        };

        if (!string.IsNullOrWhiteSpace(source))
        {
            summaryParts.Add($"Source: {source.Trim().TrimEnd('.')}.");
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            summaryParts.Add($"Reason: {reason.Trim().TrimEnd('.')}.");
        }

        if (!string.IsNullOrWhiteSpace(requestedBy))
        {
            summaryParts.Add($"Requested by: {requestedBy.Trim()}.");
        }

        return string.Join(" ", summaryParts);
    }

    private static string BuildManualReviewSummary(
        string planId,
        IReadOnlyCollection<WorkspacePlanStateIssueKind> issueKinds,
        IReadOnlyList<string> candidateWorkspaceIds,
        string? currentWorkspaceId)
    {
        if (issueKinds.Contains(WorkspacePlanStateIssueKind.AmbiguousPlanWorkspaceBinding))
        {
            return $"Plan '{planId}' still needs a manual workspace decision because multiple candidate workspaces remain ({string.Join(", ", candidateWorkspaceIds)}).";
        }

        if (issueKinds.Contains(WorkspacePlanStateIssueKind.PlanWorkspaceMissing) && !string.IsNullOrWhiteSpace(currentWorkspaceId))
        {
            return $"Plan '{planId}' points at missing workspace '{currentWorkspaceId}' and has no safe automatic replacement. Provide a manual mapping.";
        }

        return candidateWorkspaceIds.Count == 0
            ? $"Plan '{planId}' has no safe runtime reference for workspace ownership. Provide a manual mapping."
            : $"Plan '{planId}' still needs a manual workspace mapping. Candidate workspaces observed in runtime state: {string.Join(", ", candidateWorkspaceIds)}.";
    }

    private static string BuildExecutionReferenceId(PersistedExecutionStateRecord state)
    {
        return $"{NormalizeOptional(state.WorkspaceId) ?? "<missing>"}::{state.PlanId}";
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

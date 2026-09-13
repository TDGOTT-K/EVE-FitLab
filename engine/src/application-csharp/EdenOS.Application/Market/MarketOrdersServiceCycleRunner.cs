using System.Text.Json;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class MarketOrdersServiceCycleRunner
{
    private static readonly JsonSerializerOptions JsonOptions =
        JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);

    private readonly MarketOrdersServiceScheduler _scheduler;
    private readonly MarketOrdersServiceWorker _worker;
    private readonly MarketOrdersServiceOpsStore _opsStore;
    private readonly TimeProvider _timeProvider;

    public MarketOrdersServiceCycleRunner(
        MarketOrdersServiceScheduler? scheduler = null,
        MarketOrdersServiceWorker? worker = null,
        TimeProvider? timeProvider = null)
    {
        _scheduler = scheduler ?? new MarketOrdersServiceScheduler();
        _worker = worker ?? new MarketOrdersServiceWorker();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _opsStore = new MarketOrdersServiceOpsStore(_timeProvider);
    }

    public Task<UseCaseResult<MarketOrdersServiceCycleView>> RunCycleAsync(
        MarketOrdersServiceOptions options,
        string hostInstanceId,
        int cycleNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = _timeProvider.GetUtcNow();
        var traceId = $"market.orders.service.cycle:{Guid.NewGuid():N}";
        var runtime = new LocalMarketOrdersRuntime(options.MarketFactsDirectoryPath, _timeProvider);
        var statusResult = runtime.GetStatus(new MarketOrdersRuntimeStatusRequest
        {
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RecentRunLimit = 10,
            RecentSnapshotArtifactLimit = 5,
            ProbeLease = options.ProbeLease
        });

        if (!statusResult.IsSuccess || statusResult.Data is null)
        {
            return Task.FromResult(UseCaseResult<MarketOrdersServiceCycleView>.Failure(
                UseCaseStatus.Error,
                "market orders service could not read runtime status before dispatch.",
                traceId,
                statusResult.Errors.Count > 0 ? statusResult.Errors : [statusResult.Summary]));
        }

        var planResult = _scheduler.PlanCycle(options, statusResult.Data, startedAtUtc);
        if (!planResult.IsSuccess || planResult.Data is null)
        {
            return Task.FromResult(UseCaseResult<MarketOrdersServiceCycleView>.Failure(
                planResult.Status,
                planResult.Summary,
                traceId,
                planResult.Errors));
        }

        var plan = planResult.Data;
        UseCaseResult<MarketOrdersServiceDispatchView> dispatchResult;
        if (LeaseBlocksDispatch(plan.LeaseStatusBeforeDispatch))
        {
            dispatchResult = UseCaseResult<MarketOrdersServiceDispatchView>.Success(
                new MarketOrdersServiceDispatchView
                {
                    Status = "blocked_by_active_lease",
                    ExecutedRuns = Array.Empty<MarketOrdersRuntimeRunView>(),
                    Errors = Array.Empty<string>(),
                    Notes = [$"Dispatch skipped because runtime lease status is '{plan.LeaseStatusBeforeDispatch}'."]
                },
                "market orders service skipped dispatch because another orders lease is active.",
                traceId);
        }
        else
        {
            dispatchResult = _worker.DispatchDueOperations(runtime, options, plan);
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var healthResult = runtime.GetHealth(new MarketOrdersRuntimeHealthRequest
        {
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RecentRunLimit = 10,
            RecentSnapshotArtifactLimit = 5,
            ProbeLease = options.ProbeLease,
            WarningAgeMinutes = options.WarningAgeMinutes,
            ErrorAgeMinutes = options.ErrorAgeMinutes
        });
        var artifactPaths = _opsStore.ResolveArtifactPaths(options, plan.CycleId);

        var cycle = new MarketOrdersServiceCycleView
        {
            ServiceName = options.ServiceName,
            HostKind = MarketOrdersServiceConventions.HostKind,
            SchedulerKind = MarketOrdersServiceConventions.SchedulerKind,
            WorkerKind = MarketOrdersServiceConventions.WorkerKind,
            HostInstanceId = hostInstanceId,
            CycleNumber = cycleNumber,
            CycleId = plan.CycleId,
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RequestedBy = options.RequestedBy,
            TriggerKind = options.TriggerKind,
            HeartbeatPath = plan.HeartbeatPath,
            ArtifactPaths = artifactPaths,
            RetentionPolicy = new MarketOrdersServiceRetentionPolicyView
            {
                CycleHistoryLimit = options.CycleHistoryLimit
            },
            Status = DetermineCycleStatus(plan, dispatchResult),
            PlannedAtUtc = plan.PlannedAtUtc,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            PollIntervalSeconds = options.PollIntervalSeconds,
            LeaseStatusBeforeDispatch = plan.LeaseStatusBeforeDispatch,
            LeaseStatusAfterCycle = healthResult.Data?.Lease.Status ?? statusResult.Data.Lease.Status,
            RuntimeHealthStatusAfterCycle = healthResult.Data?.OverallStatus ?? "unknown",
            RuntimeRetentionPolicy = healthResult.Data?.RetentionPolicy ?? statusResult.Data.RetentionPolicy,
            CurrentStoreSummary = healthResult.Data?.CurrentStoreSummary ?? statusResult.Data.CurrentStoreSummary,
            Operations = plan.Operations,
            ExecutedRuns = dispatchResult.Data?.ExecutedRuns ?? Array.Empty<MarketOrdersRuntimeRunView>(),
            Errors = MergeMessages(dispatchResult.Errors, healthResult.IsSuccess ? Array.Empty<string>() : healthResult.Errors),
            Notes = MergeMessages(dispatchResult.Warnings, dispatchResult.Data?.Notes ?? Array.Empty<string>(), healthResult.Warnings)
        };

        WriteHeartbeat(cycle.HeartbeatPath, cycle);
        var opsWriteResult = _opsStore.WriteArtifacts(options, cycle, healthResult.Data);
        cycle = cycle with
        {
            RetainedCycleCount = opsWriteResult.RetainedCycleCount,
            PrunedCycleLogCount = opsWriteResult.PrunedCycleLogCount
        };
        WriteHeartbeat(cycle.HeartbeatPath, cycle);
        WriteCycleLog(cycle.ArtifactPaths.CycleLogPath, cycle);

        if (!dispatchResult.IsSuccess)
        {
            return Task.FromResult(new UseCaseResult<MarketOrdersServiceCycleView>
            {
                Status = dispatchResult.Status,
                Summary = dispatchResult.Summary,
                Data = cycle,
                Errors = cycle.Errors,
                Warnings = cycle.Notes,
                TraceId = traceId
            });
        }

        return Task.FromResult(UseCaseResult<MarketOrdersServiceCycleView>.Success(
            cycle,
            cycle.Status switch
            {
                "idle" => "market orders service completed a heartbeat cycle with no due imports.",
                "blocked_by_active_lease" => "market orders service skipped dispatch because an orders lease is already active.",
                _ => $"market orders service completed cycle {cycle.CycleNumber} with {cycle.ExecutedRuns.Count} import run(s)."
            },
            traceId,
            cycle.Notes));
    }

    private static bool LeaseBlocksDispatch(string leaseStatus)
    {
        return leaseStatus is "active" or "expired_but_locked" or "active_without_state";
    }

    private static string DetermineCycleStatus(
        MarketOrdersServiceCyclePlan plan,
        UseCaseResult<MarketOrdersServiceDispatchView> dispatchResult)
    {
        var dueCount = plan.Operations.Count(operation => operation.IsDue);
        if (dispatchResult.Data?.Status == "blocked_by_active_lease")
        {
            return "blocked_by_active_lease";
        }

        if (dueCount == 0)
        {
            return "idle";
        }

        return dispatchResult.Data?.Status switch
        {
            "completed_with_failures" => "completed_with_failures",
            "failed" => dispatchResult.Data.ExecutedRuns.Count > 0 ? "completed_with_failures" : "failed",
            _ => dispatchResult.IsSuccess ? "completed" : "failed"
        };
    }

    private static IReadOnlyList<string> MergeMessages(params IReadOnlyList<string>[] collections)
    {
        return collections
            .Where(collection => collection.Count > 0)
            .SelectMany(collection => collection)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteHeartbeat(string heartbeatPath, MarketOrdersServiceCycleView cycle)
    {
        WriteJsonAtomically(heartbeatPath, cycle);
    }

    private static void WriteCycleLog(string cycleLogPath, MarketOrdersServiceCycleView cycle)
    {
        WriteJsonAtomically(cycleLogPath, cycle);
    }

    private static void WriteJsonAtomically(string path, MarketOrdersServiceCycleView cycle)
    {
        var directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, cycle, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}

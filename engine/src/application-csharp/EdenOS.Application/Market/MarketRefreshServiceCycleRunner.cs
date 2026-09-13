using System.Text.Json;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class MarketRefreshServiceCycleRunner
{
    private static readonly JsonSerializerOptions JsonOptions =
        JsonSerializerOptionsFactory.CreateSnakeCase(writeIndented: true);

    private readonly MarketRefreshServiceScheduler _scheduler;
    private readonly MarketRefreshServiceWorker _worker;
    private readonly MarketRefreshServiceOpsStore _opsStore;
    private readonly TimeProvider _timeProvider;

    public MarketRefreshServiceCycleRunner(
        MarketRefreshServiceScheduler? scheduler = null,
        MarketRefreshServiceWorker? worker = null,
        TimeProvider? timeProvider = null)
    {
        _scheduler = scheduler ?? new MarketRefreshServiceScheduler();
        _worker = worker ?? new MarketRefreshServiceWorker();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _opsStore = new MarketRefreshServiceOpsStore(_timeProvider);
    }

    public Task<UseCaseResult<MarketRefreshServiceCycleView>> RunCycleAsync(
        MarketRefreshServiceOptions options,
        string hostInstanceId,
        int cycleNumber,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = _timeProvider.GetUtcNow();
        var traceId = $"market.refresh.service.cycle:{Guid.NewGuid():N}";
        var runtime = new LocalMarketRefreshRuntime(options.MarketFactsDirectoryPath, _timeProvider);
        var statusResult = runtime.GetStatus(new MarketRefreshStatusRequest
        {
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RecentRunLimit = 10,
            RecentBatchLimit = 5,
            ProbeLease = options.ProbeLease
        });

        if (!statusResult.IsSuccess || statusResult.Data is null)
        {
            return Task.FromResult(UseCaseResult<MarketRefreshServiceCycleView>.Failure(
                UseCaseStatus.Error,
                "market refresh service could not read runtime status before dispatch.",
                traceId,
                statusResult.Errors.Count > 0 ? statusResult.Errors : [statusResult.Summary]));
        }

        var planResult = _scheduler.PlanCycle(options, statusResult.Data, startedAtUtc);
        if (!planResult.IsSuccess || planResult.Data is null)
        {
            return Task.FromResult(UseCaseResult<MarketRefreshServiceCycleView>.Failure(
                planResult.Status,
                planResult.Summary,
                traceId,
                planResult.Errors));
        }

        var plan = planResult.Data;
        UseCaseResult<MarketRefreshServiceDispatchView> dispatchResult;
        if (LeaseBlocksDispatch(plan.LeaseStatusBeforeDispatch))
        {
            dispatchResult = UseCaseResult<MarketRefreshServiceDispatchView>.Success(
                new MarketRefreshServiceDispatchView
                {
                    Status = "blocked_by_active_lease",
                    ExecutedRuns = Array.Empty<MarketRefreshRunView>(),
                    Errors = Array.Empty<string>(),
                    Notes = [$"Dispatch skipped because runtime lease status is '{plan.LeaseStatusBeforeDispatch}'."]
                },
                "market refresh service skipped dispatch because another refresh lease is active.",
                traceId);
        }
        else
        {
            dispatchResult = _worker.DispatchDueOperations(runtime, options, plan);
        }

        var completedAtUtc = _timeProvider.GetUtcNow();
        var healthResult = runtime.GetHealth(new MarketRefreshHealthRequest
        {
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RecentRunLimit = 10,
            RecentBatchLimit = 5,
            ProbeLease = options.ProbeLease,
            WarningLagMinutes = options.WarningLagMinutes,
            ErrorLagMinutes = options.ErrorLagMinutes
        });
        var artifactPaths = _opsStore.ResolveArtifactPaths(options, plan.CycleId);

        var cycle = new MarketRefreshServiceCycleView
        {
            ServiceName = options.ServiceName,
            HostKind = MarketRefreshServiceConventions.HostKind,
            SchedulerKind = MarketRefreshServiceConventions.SchedulerKind,
            WorkerKind = MarketRefreshServiceConventions.WorkerKind,
            HostInstanceId = hostInstanceId,
            CycleNumber = cycleNumber,
            CycleId = plan.CycleId,
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RequestedBy = options.RequestedBy,
            TriggerKind = options.TriggerKind,
            HeartbeatPath = plan.HeartbeatPath,
            ArtifactPaths = artifactPaths,
            RetentionPolicy = new MarketRefreshServiceRetentionPolicyView
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
            OverallHealthStatusAfterCycle = healthResult.Data?.OverallStatus ?? "unknown",
            Operations = plan.Operations,
            Batch = dispatchResult.Data?.Batch,
            ExecutedRuns = dispatchResult.Data?.ExecutedRuns ?? Array.Empty<MarketRefreshRunView>(),
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
            return Task.FromResult(new UseCaseResult<MarketRefreshServiceCycleView>
            {
                Status = dispatchResult.Status,
                Summary = dispatchResult.Summary,
                Data = cycle,
                Errors = cycle.Errors,
                Warnings = cycle.Notes,
                TraceId = traceId
            });
        }

        return Task.FromResult(UseCaseResult<MarketRefreshServiceCycleView>.Success(
            cycle,
            cycle.Status switch
            {
                "idle" => "market refresh service completed a heartbeat cycle with no due operations.",
                "blocked_by_active_lease" => "market refresh service skipped dispatch because a refresh lease is already active.",
                _ => $"market refresh service completed cycle {cycle.CycleNumber} with {cycle.ExecutedRuns.Count} run(s)."
            },
            traceId,
            cycle.Notes));
    }

    private static bool LeaseBlocksDispatch(string leaseStatus)
    {
        return leaseStatus is "active" or "expired_but_locked" or "active_without_state";
    }

    private static string DetermineCycleStatus(
        MarketRefreshServiceCyclePlan plan,
        UseCaseResult<MarketRefreshServiceDispatchView> dispatchResult)
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

        return dispatchResult.IsSuccess
            ? "completed"
            : dispatchResult.Data?.ExecutedRuns.Count > 0
                ? "completed_with_failures"
                : "failed";
    }

    private static IReadOnlyList<string> MergeMessages(params IReadOnlyList<string>[] collections)
    {
        return collections
            .Where(collection => collection.Count > 0)
            .SelectMany(collection => collection)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void WriteHeartbeat(string heartbeatPath, MarketRefreshServiceCycleView cycle)
    {
        WriteJsonAtomically(heartbeatPath, cycle);
    }

    private static void WriteCycleLog(string cycleLogPath, MarketRefreshServiceCycleView cycle)
    {
        WriteJsonAtomically(cycleLogPath, cycle);
    }

    private static void WriteJsonAtomically(string path, MarketRefreshServiceCycleView cycle)
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

using System.Globalization;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class MarketRefreshServiceScheduler
{
    public UseCaseResult<MarketRefreshServiceCyclePlan> PlanCycle(
        MarketRefreshServiceOptions options,
        MarketRefreshStatusView runtimeStatus,
        DateTimeOffset plannedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtimeStatus);

        var traceId = $"market.refresh.service.plan:{Guid.NewGuid():N}";
        var validationErrors = ValidateOptions(options);
        if (validationErrors.Count > 0)
        {
            return UseCaseResult<MarketRefreshServiceCyclePlan>.Failure(
                UseCaseStatus.InvalidInput,
                "market refresh service configuration is invalid.",
                traceId,
                validationErrors);
        }

        var operationStates = runtimeStatus.Operations
            .ToDictionary(operation => operation.Operation, StringComparer.OrdinalIgnoreCase);
        var plannedOperations = new List<MarketRefreshServicePlannedOperationView>(options.Operations.Count);

        foreach (var operationOptions in options.Operations)
        {
            var operation = MarketRefreshOperationKind.Parse(operationOptions.Operation);
            operationStates.TryGetValue(operation.OperationName, out var operationState);

            if (!operationOptions.Enabled)
            {
                plannedOperations.Add(new MarketRefreshServicePlannedOperationView
                {
                    Operation = operation.OperationName,
                    Enabled = false,
                    IsDue = false,
                    IntervalMinutes = operationOptions.IntervalMinutes,
                    PayloadPath = operationOptions.PayloadPath,
                    Decision = "disabled",
                    LastStatus = operationState?.LastStatus,
                    LastStartedAtUtc = operationState?.LastStartedAtUtc,
                    LastCompletedAtUtc = operationState?.LastCompletedAtUtc,
                    LastSucceededAtUtc = operationState?.LastSucceededAtUtc
                });
                continue;
            }

            var lastActivityAtUtc = operationState?.LastCompletedAtUtc
                ?? operationState?.LastStartedAtUtc
                ?? operationState?.LastSucceededAtUtc;
            var nextDueAtUtc = lastActivityAtUtc?.AddMinutes(operationOptions.IntervalMinutes);
            var isDue = lastActivityAtUtc is null || nextDueAtUtc <= plannedAtUtc;

            plannedOperations.Add(new MarketRefreshServicePlannedOperationView
            {
                Operation = operation.OperationName,
                Enabled = true,
                IsDue = isDue,
                IntervalMinutes = operationOptions.IntervalMinutes,
                PayloadPath = operationOptions.PayloadPath,
                GeneratedCursor = isDue ? BuildCursor(options.ServiceName, operation, operationOptions, plannedAtUtc) : null,
                Decision = DetermineDecision(operationState, lastActivityAtUtc, nextDueAtUtc, plannedAtUtc),
                LastStatus = operationState?.LastStatus,
                LastStartedAtUtc = operationState?.LastStartedAtUtc,
                LastCompletedAtUtc = operationState?.LastCompletedAtUtc,
                LastSucceededAtUtc = operationState?.LastSucceededAtUtc,
                NextDueAtUtc = nextDueAtUtc
            });
        }

        var plan = new MarketRefreshServiceCyclePlan
        {
            ServiceName = options.ServiceName,
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RequestedBy = options.RequestedBy,
            TriggerKind = options.TriggerKind,
            HeartbeatPath = ResolveHeartbeatPath(options),
            LeaseStatusBeforeDispatch = runtimeStatus.Lease.Status,
            PlannedAtUtc = plannedAtUtc,
            CycleId = $"service-cycle-{plannedAtUtc:yyyyMMddTHHmmssfffZ}",
            Operations = plannedOperations
        };

        return UseCaseResult<MarketRefreshServiceCyclePlan>.Success(
            plan,
            $"market refresh service planned {plannedOperations.Count(operation => operation.IsDue)} due operation(s).",
            traceId);
    }

    internal static string ResolveHeartbeatPath(MarketRefreshServiceOptions options)
    {
        return string.IsNullOrWhiteSpace(options.HeartbeatPath)
            ? Path.Combine(ResolveServiceStateDirectoryPath(options), "heartbeat.json")
            : options.HeartbeatPath!;
    }

    internal static string ResolveServiceStateDirectoryPath(MarketRefreshServiceOptions options)
    {
        return string.IsNullOrWhiteSpace(options.ServiceStateDirectoryPath)
            ? Path.Combine(options.MarketFactsDirectoryPath, "refresh-service")
            : options.ServiceStateDirectoryPath!;
    }

    private static List<string> ValidateOptions(MarketRefreshServiceOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.MarketFactsDirectoryPath))
        {
            errors.Add("market_facts_directory_path is required.");
        }

        if (options.PollIntervalSeconds <= 0)
        {
            errors.Add("poll_interval_seconds must be greater than zero.");
        }

        if (options.WarningLagMinutes <= 0 || options.ErrorLagMinutes <= 0 || options.ErrorLagMinutes < options.WarningLagMinutes)
        {
            errors.Add("warning/error lag minutes must be positive, and error_lag_minutes must be >= warning_lag_minutes.");
        }

        if (options.CycleHistoryLimit <= 0)
        {
            errors.Add("cycle_history_limit must be greater than zero.");
        }

        if (options.ConsecutiveImpairedCycleAlertThreshold <= 0)
        {
            errors.Add("consecutive_impaired_cycle_alert_threshold must be greater than zero.");
        }

        if (options.ConsecutiveBlockedLeaseAlertThreshold <= 0)
        {
            errors.Add("consecutive_blocked_lease_alert_threshold must be greater than zero.");
        }

        if (options.Operations.Count == 0)
        {
            errors.Add("At least one service operation must be configured.");
        }

        foreach (var operationOptions in options.Operations)
        {
            try
            {
                var operation = MarketRefreshOperationKind.Parse(operationOptions.Operation);
                if (operationOptions.IntervalMinutes <= 0)
                {
                    errors.Add($"Operation '{operation.OperationName}' must set interval_minutes > 0.");
                }

                if (operation.RequiresPayload && operationOptions.Enabled && string.IsNullOrWhiteSpace(operationOptions.PayloadPath))
                {
                    errors.Add($"Operation '{operation.OperationName}' requires a payload_path when enabled.");
                }
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
            }
        }

        return errors;
    }

    private static string DetermineDecision(
        MarketRefreshOperationState? state,
        DateTimeOffset? lastActivityAtUtc,
        DateTimeOffset? nextDueAtUtc,
        DateTimeOffset plannedAtUtc)
    {
        if (state is null || lastActivityAtUtc is null)
        {
            return "never_ran";
        }

        if (nextDueAtUtc is null || nextDueAtUtc <= plannedAtUtc)
        {
            return string.Equals(state.LastStatus, "failed", StringComparison.OrdinalIgnoreCase)
                ? "retry_window_elapsed"
                : "interval_elapsed";
        }

        return "waiting_for_interval";
    }

    private static string BuildCursor(
        string serviceName,
        MarketRefreshOperationKind operation,
        MarketRefreshServiceOperationOptions operationOptions,
        DateTimeOffset plannedAtUtc)
    {
        var prefix = string.IsNullOrWhiteSpace(operationOptions.CursorPrefix)
            ? $"{serviceName}.{operation.OperationName}"
            : operationOptions.CursorPrefix!;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}.{plannedAtUtc:yyyyMMddTHHmmssfffZ}");
    }
}

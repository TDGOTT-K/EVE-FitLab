using System.Globalization;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.Market;

public sealed class MarketOrdersServiceScheduler
{
    public UseCaseResult<MarketOrdersServiceCyclePlan> PlanCycle(
        MarketOrdersServiceOptions options,
        MarketOrdersRuntimeStatusView runtimeStatus,
        DateTimeOffset plannedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtimeStatus);

        var traceId = $"market.orders.service.plan:{Guid.NewGuid():N}";
        var validationErrors = ValidateOptions(options);
        if (validationErrors.Count > 0)
        {
            return UseCaseResult<MarketOrdersServiceCyclePlan>.Failure(
                UseCaseStatus.InvalidInput,
                "market orders service configuration is invalid.",
                traceId,
                validationErrors);
        }

        var operationStates = runtimeStatus.Operations
            .ToDictionary(operation => operation.Operation, StringComparer.OrdinalIgnoreCase);
        var plannedOperations = new List<MarketOrdersServicePlannedOperationView>(options.Operations.Count);

        foreach (var operationOptions in options.Operations)
        {
            var operation = ParseOperation(operationOptions.Operation);
            operationStates.TryGetValue(operation, out var operationState);

            if (!operationOptions.Enabled)
            {
                plannedOperations.Add(new MarketOrdersServicePlannedOperationView
                {
                    Operation = operation,
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

            plannedOperations.Add(new MarketOrdersServicePlannedOperationView
            {
                Operation = operation,
                Enabled = true,
                IsDue = isDue,
                IntervalMinutes = operationOptions.IntervalMinutes,
                PayloadPath = operationOptions.PayloadPath,
                GeneratedCursor = isDue ? BuildCursor(options.ServiceName, operationOptions, plannedAtUtc) : null,
                Decision = DetermineDecision(operationState, lastActivityAtUtc, nextDueAtUtc, plannedAtUtc),
                LastStatus = operationState?.LastStatus,
                LastStartedAtUtc = operationState?.LastStartedAtUtc,
                LastCompletedAtUtc = operationState?.LastCompletedAtUtc,
                LastSucceededAtUtc = operationState?.LastSucceededAtUtc,
                NextDueAtUtc = nextDueAtUtc
            });
        }

        var plan = new MarketOrdersServiceCyclePlan
        {
            ServiceName = options.ServiceName,
            MarketFactsDirectoryPath = options.MarketFactsDirectoryPath,
            RequestedBy = options.RequestedBy,
            TriggerKind = options.TriggerKind,
            HeartbeatPath = ResolveHeartbeatPath(options),
            LeaseStatusBeforeDispatch = runtimeStatus.Lease.Status,
            PlannedAtUtc = plannedAtUtc,
            CycleId = $"orders-service-cycle-{plannedAtUtc:yyyyMMddTHHmmssfffZ}",
            Operations = plannedOperations
        };

        return UseCaseResult<MarketOrdersServiceCyclePlan>.Success(
            plan,
            $"market orders service planned {plannedOperations.Count(operation => operation.IsDue)} due operation(s).",
            traceId);
    }

    internal static string ResolveHeartbeatPath(MarketOrdersServiceOptions options)
    {
        return string.IsNullOrWhiteSpace(options.HeartbeatPath)
            ? Path.Combine(ResolveServiceStateDirectoryPath(options), "heartbeat.json")
            : options.HeartbeatPath!;
    }

    internal static string ResolveServiceStateDirectoryPath(MarketOrdersServiceOptions options)
    {
        return string.IsNullOrWhiteSpace(options.ServiceStateDirectoryPath)
            ? Path.Combine(options.MarketFactsDirectoryPath, "orders-service")
            : options.ServiceStateDirectoryPath!;
    }

    private static List<string> ValidateOptions(MarketOrdersServiceOptions options)
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

        if (options.WarningAgeMinutes <= 0 || options.ErrorAgeMinutes <= 0 || options.ErrorAgeMinutes < options.WarningAgeMinutes)
        {
            errors.Add("warning/error age minutes must be positive, and error_age_minutes must be >= warning_age_minutes.");
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
                var operation = ParseOperation(operationOptions.Operation);
                if (operationOptions.IntervalMinutes <= 0)
                {
                    errors.Add($"Operation '{operation}' must set interval_minutes > 0.");
                }

                if (operationOptions.Enabled && string.IsNullOrWhiteSpace(operationOptions.PayloadPath))
                {
                    errors.Add($"Operation '{operation}' requires a payload_path when enabled.");
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
        MarketOrdersRuntimeOperationState? state,
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
        MarketOrdersServiceOperationOptions operationOptions,
        DateTimeOffset plannedAtUtc)
    {
        var prefix = string.IsNullOrWhiteSpace(operationOptions.CursorPrefix)
            ? $"{serviceName}.{MarketOrdersServiceConventions.OrdersSnapshotOperation}"
            : operationOptions.CursorPrefix!;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}.{plannedAtUtc:yyyyMMddTHHmmssfffZ}");
    }

    private static string ParseOperation(string? operation)
    {
        if (string.Equals(operation, MarketOrdersServiceConventions.OrdersSnapshotOperation, StringComparison.OrdinalIgnoreCase))
        {
            return MarketOrdersServiceConventions.OrdersSnapshotOperation;
        }

        throw new InvalidOperationException($"Unsupported market orders service operation '{operation}'. Supported operations: {MarketOrdersServiceConventions.OrdersSnapshotOperation}.");
    }
}

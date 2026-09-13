namespace EdenOS.Contracts.Planning;

public sealed record CreatePlanRequest
{
    public required string WorkspaceId { get; init; }

    public string? PlanId { get; init; }

    public required string Name { get; init; }

    public string? Notes { get; init; }
}

public sealed record GetPlanRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }
}

public sealed record ListPlansRequest
{
    public required string WorkspaceId { get; init; }
}

public sealed record SetPlanGoalRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required PlanGoal Goal { get; init; }
}

public sealed record SetPlanPreferencesRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required PlanPreferences Preferences { get; init; }
}

public abstract record AddPlanNodeRequest<TDetails>
    where TDetails : PlanNodeDetails
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public string? NodeId { get; init; }

    public required string Title { get; init; }

    public string? Notes { get; init; }

    public string? LocationLabel { get; init; }

    public PlanNodeResourceProfile ResourceProfile { get; init; } = new();

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public required TDetails Details { get; init; }
}

public sealed record AddProductionNodeRequest : AddPlanNodeRequest<ProductionNodeDetails>;

public sealed record AddReactionNodeRequest : AddPlanNodeRequest<ReactionNodeDetails>;

public sealed record AddCopyOrInventionNodeRequest : AddPlanNodeRequest<CopyOrInventionNodeDetails>;

public sealed record AddInventoryPoolNodeRequest : AddPlanNodeRequest<InventoryPoolNodeDetails>;

public sealed record AddTransportNodeRequest : AddPlanNodeRequest<TransportNodeDetails>;

public sealed record AddTradeNodeRequest : AddPlanNodeRequest<TradeNodeDetails>;

public sealed record LinkPlanNodesRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string FromNodeId { get; init; }

    public required string ToNodeId { get; init; }

    public required PlanLinkKind Kind { get; init; }

    public string? Label { get; init; }

    public string? ResourceTypeId { get; init; }

    public decimal? Quantity { get; init; }

    public bool IsOptional { get; init; }
}

public sealed record UnlinkPlanNodesRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string LinkId { get; init; }
}

public sealed record UpdatePlanNodeRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string NodeId { get; init; }

    public string? Title { get; init; }

    public string? Notes { get; init; }

    public string? LocationLabel { get; init; }

    public PlanNodeDetails? Details { get; init; }

    public PlanNodeResourceProfile? ResourceProfile { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record RemovePlanNodeRequest
{
    public required string WorkspaceId { get; init; }

    public required string PlanId { get; init; }

    public required string NodeId { get; init; }
}

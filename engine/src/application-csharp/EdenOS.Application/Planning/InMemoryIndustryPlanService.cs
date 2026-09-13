using System.Collections.Concurrent;
using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.UseCases;
using EdenOS.Contracts.Workspaces;

namespace EdenOS.Application.Planning;

public sealed class InMemoryIndustryPlanService : IIndustryPlanService
{
    private const int PersistenceRetryCount = 3;

    private readonly IWorkspaceService workspaceService;
    private readonly ConcurrentDictionary<string, IndustryPlan> plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly object syncRoot = new();
    private readonly LocalJsonStateStore<IndustryPlanRuntimeState>? stateStore;

    public InMemoryIndustryPlanService(IWorkspaceService workspaceService)
        : this(workspaceService, stateStore: null)
    {
    }

    internal InMemoryIndustryPlanService(
        IWorkspaceService workspaceService,
        LocalJsonStateStore<IndustryPlanRuntimeState>? stateStore)
    {
        this.workspaceService = workspaceService;
        this.stateStore = stateStore;
        ReloadPersistedState();
    }

    public UseCaseResult<IndustryPlan> Create(CreatePlanRequest request)
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, "plan.create", () =>
        {
            var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "plan.create");
            if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
            {
                return FailurePlan(workspaceResult.Status, workspaceResult.Summary, traceId, workspaceResult.Errors.ToArray());
            }

            var planId = NormalizeOptional(request.PlanId) ?? $"plan-{Guid.NewGuid():N}";
            var name = NormalizeRequired(request.Name, nameof(request.Name));

            if (name is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Plan name is required.",
                    traceId,
                    $"{nameof(request.Name)} must not be empty.");
            }

            var plan = new IndustryPlan
            {
                WorkspaceId = workspaceResult.Data.WorkspaceId,
                PlanId = planId,
                Name = name,
                Notes = NormalizeOptional(request.Notes),
                Status = PlanStatus.Draft,
                Goal = null,
                Preferences = new PlanPreferences(),
                Nodes = Array.Empty<PlanNode>(),
                Links = Array.Empty<PlanLink>(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            if (!plans.TryAdd(plan.PlanId, plan))
            {
                return FailurePlan(
                    UseCaseStatus.Conflict,
                    $"Plan '{plan.PlanId}' already exists.",
                    traceId,
                    "Plan id must be unique.");
            }

            PersistState();

            return UseCaseResult<IndustryPlan>.Success(
                plan,
                $"Created industrial plan '{plan.Name}'.",
                traceId);
        });
    }

    public UseCaseResult<IndustryPlan> Get(GetPlanRequest request)
    {
        var traceId = NextTraceId();
        return RequirePlan(request.WorkspaceId, request.PlanId, traceId, "plan.get");
    }

    public UseCaseResult<IReadOnlyList<PlanSummary>> List(ListPlansRequest request)
    {
        var traceId = NextTraceId();
        var workspaceResult = RequireWorkspace(request.WorkspaceId, traceId, "plan.list");
        if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
        {
            return UseCaseResult<IReadOnlyList<PlanSummary>>.Failure(
                workspaceResult.Status,
                workspaceResult.Summary,
                traceId,
                workspaceResult.Errors);
        }

        var items = plans.Values
            .Where(plan => plan.WorkspaceId.Equals(workspaceResult.Data.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(plan => plan.UpdatedAtUtc)
            .Select(ToSummary)
            .ToArray();

        return UseCaseResult<IReadOnlyList<PlanSummary>>.Success(
            items,
            $"Listed {items.Length} industrial plan(s).",
            traceId);
    }

    public UseCaseResult<IndustryPlan> SetGoal(SetPlanGoalRequest request)
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, "plan.set_goal", () =>
        {
            var validationErrors = ValidateGoal(request.Goal);
            if (validationErrors.Count > 0)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Goal is invalid.",
                    traceId,
                    validationErrors.ToArray());
            }

            return UpdatePlan(
                request.WorkspaceId,
                request.PlanId,
                plan => plan with
                {
                    Goal = request.Goal with
                    {
                        TargetTypeId = request.Goal.TargetTypeId.Trim(),
                        TargetName = request.Goal.TargetName.Trim(),
                        Notes = NormalizeOptional(request.Goal.Notes)
                    },
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                "Updated plan goal.",
                traceId);
        });
    }

    public UseCaseResult<IndustryPlan> SetPreferences(SetPlanPreferencesRequest request)
    {
        var traceId = NextTraceId();
        var validationErrors = ValidatePreferences(request.Preferences);
        if (validationErrors.Count > 0)
        {
            return FailurePlan(
                UseCaseStatus.InvalidInput,
                "Planning preferences are invalid.",
                traceId,
                validationErrors.ToArray());
        }

        return ExecutePersistedMutation(traceId, "plan.set_preferences", () =>
            UpdatePlan(
                request.WorkspaceId,
                request.PlanId,
                plan => plan with
                {
                    Preferences = request.Preferences with
                    {
                        Notes = NormalizeOptional(request.Preferences.Notes)
                    },
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                },
                "Updated planning preferences.",
                traceId));
    }

    public UseCaseResult<IndustryPlan> AddProductionNode(AddProductionNodeRequest request) =>
        AddNode(request, request.Details, "plan.add_production_node");

    public UseCaseResult<IndustryPlan> AddReactionNode(AddReactionNodeRequest request) =>
        AddNode(request, request.Details, "plan.add_reaction_node");

    public UseCaseResult<IndustryPlan> AddCopyOrInventionNode(AddCopyOrInventionNodeRequest request) =>
        AddNode(request, request.Details, "plan.add_copy_or_invention_node");

    public UseCaseResult<IndustryPlan> AddInventoryPool(AddInventoryPoolNodeRequest request) =>
        AddNode(request, request.Details, "plan.add_inventory_pool");

    public UseCaseResult<IndustryPlan> AddTransportNode(AddTransportNodeRequest request) =>
        AddNode(request, request.Details, "plan.add_transport_node");

    public UseCaseResult<IndustryPlan> AddTradeNode(AddTradeNodeRequest request) =>
        AddNode(request, request.Details, "plan.add_trade_node");

    public UseCaseResult<IndustryPlan> LinkNodes(LinkPlanNodesRequest request)
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, "plan.link_nodes", () =>
        {
            var planResult = RequirePlan(request.WorkspaceId, request.PlanId, traceId, "plan.link_nodes");
            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return planResult;
            }

            var plan = planResult.Data;
            var fromNodeId = NormalizeRequired(request.FromNodeId, nameof(request.FromNodeId));
            var toNodeId = NormalizeRequired(request.ToNodeId, nameof(request.ToNodeId));

            if (fromNodeId is null || toNodeId is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Link endpoints are required.",
                    traceId,
                    "Both from_node_id and to_node_id must be provided.");
            }

            if (string.Equals(fromNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "A node cannot link to itself.",
                    traceId,
                    "Self links are not allowed.");
            }

            var fromNode = plan.Nodes.FirstOrDefault(node => node.NodeId.Equals(fromNodeId, StringComparison.OrdinalIgnoreCase));
            var toNode = plan.Nodes.FirstOrDefault(node => node.NodeId.Equals(toNodeId, StringComparison.OrdinalIgnoreCase));

            if (fromNode is null || toNode is null)
            {
                return FailurePlan(
                    UseCaseStatus.NotFound,
                    "One or more link endpoints were not found.",
                    traceId,
                    "Both nodes must exist in the target plan.");
            }

            if (plan.Links.Any(link =>
                    link.Kind == request.Kind &&
                    link.FromNodeId.Equals(fromNodeId, StringComparison.OrdinalIgnoreCase) &&
                    link.ToNodeId.Equals(toNodeId, StringComparison.OrdinalIgnoreCase)))
            {
                return FailurePlan(
                    UseCaseStatus.Conflict,
                    "The requested link already exists.",
                    traceId,
                    "Duplicate links with the same semantics are not allowed.");
            }

            var semanticErrors = ValidateLinkSemantics(request.Kind, fromNode, toNode);
            if (semanticErrors.Count > 0)
            {
                return FailurePlan(
                    UseCaseStatus.Conflict,
                    "The requested link violates node semantics.",
                    traceId,
                    semanticErrors.ToArray());
            }

            var link = new PlanLink
            {
                LinkId = $"link-{Guid.NewGuid():N}",
                FromNodeId = fromNode.NodeId,
                ToNodeId = toNode.NodeId,
                Kind = request.Kind,
                Label = NormalizeOptional(request.Label),
                ResourceTypeId = NormalizeOptional(request.ResourceTypeId),
                Quantity = request.Quantity,
                IsOptional = request.IsOptional
            };

            var updatedPlan = Touch(plan with
            {
                Links = plan.Links.Concat([link]).ToArray()
            });

            return ReplacePlan(updatedPlan, traceId, $"Linked '{fromNode.Title}' to '{toNode.Title}'.");
        });
    }

    public UseCaseResult<IndustryPlan> UnlinkNodes(UnlinkPlanNodesRequest request)
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, "plan.unlink_nodes", () =>
        {
            var planResult = RequirePlan(request.WorkspaceId, request.PlanId, traceId, "plan.unlink_nodes");
            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return planResult;
            }

            var plan = planResult.Data;
            var linkId = NormalizeRequired(request.LinkId, nameof(request.LinkId));
            if (linkId is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Link id is required.",
                    traceId,
                    $"{nameof(request.LinkId)} must not be empty.");
            }

            var updatedLinks = plan.Links.Where(link => !link.LinkId.Equals(linkId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (updatedLinks.Length == plan.Links.Count)
            {
                return FailurePlan(
                    UseCaseStatus.NotFound,
                    $"Link '{linkId}' was not found.",
                    traceId,
                    "Unknown link id.");
            }

            var updatedPlan = Touch(plan with { Links = updatedLinks });
            return ReplacePlan(updatedPlan, traceId, $"Removed link '{linkId}'.");
        });
    }

    public UseCaseResult<IndustryPlan> UpdateNode(UpdatePlanNodeRequest request)
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, "plan.update_node", () =>
        {
            var planResult = RequirePlan(request.WorkspaceId, request.PlanId, traceId, "plan.update_node");
            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return planResult;
            }

            var plan = planResult.Data;
            var nodeId = NormalizeRequired(request.NodeId, nameof(request.NodeId));
            if (nodeId is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node id is required.",
                    traceId,
                    $"{nameof(request.NodeId)} must not be empty.");
            }

            var existingNode = plan.Nodes.FirstOrDefault(node => node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
            if (existingNode is null)
            {
                return FailurePlan(
                    UseCaseStatus.NotFound,
                    $"Node '{nodeId}' was not found.",
                    traceId,
                    "Unknown node id.");
            }

            if (request.Details is not null && request.Details.Kind != existingNode.Kind)
            {
                return FailurePlan(
                    UseCaseStatus.Conflict,
                    "Node kind cannot be changed during update.",
                    traceId,
                    "Remove and recreate the node to change semantics.");
            }

            var title = request.Title is null ? existingNode.Title : NormalizeRequired(request.Title, nameof(request.Title));
            if (title is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node title cannot be empty.",
                    traceId,
                    $"{nameof(request.Title)} must not be empty when provided.");
            }

            var details = request.Details ?? existingNode.Details;
            var detailErrors = ValidateNodeDetails(details);
            if (detailErrors.Count > 0)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node details are invalid.",
                    traceId,
                    detailErrors.ToArray());
            }

            var resourceProfile = request.ResourceProfile ?? existingNode.ResourceProfile;
            var resourceErrors = ValidateResourceProfile(resourceProfile);
            if (resourceErrors.Count > 0)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node resource profile is invalid.",
                    traceId,
                    resourceErrors.ToArray());
            }

            var updatedNode = existingNode with
            {
                Title = title,
                Notes = request.Notes is null ? existingNode.Notes : NormalizeOptional(request.Notes),
                LocationLabel = request.LocationLabel is null ? existingNode.LocationLabel : NormalizeOptional(request.LocationLabel),
                Details = details,
                ResourceProfile = resourceProfile,
                Metadata = request.Metadata is null ? existingNode.Metadata : NormalizeMetadata(request.Metadata)
            };

            var updatedPlan = Touch(plan with
            {
                Nodes = plan.Nodes
                    .Select(node => node.NodeId.Equals(existingNode.NodeId, StringComparison.OrdinalIgnoreCase) ? updatedNode : node)
                    .ToArray()
            });

            return ReplacePlan(updatedPlan, traceId, $"Updated node '{updatedNode.Title}'.");
        });
    }

    public UseCaseResult<IndustryPlan> RemoveNode(RemovePlanNodeRequest request)
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, "plan.remove_node", () =>
        {
            var planResult = RequirePlan(request.WorkspaceId, request.PlanId, traceId, "plan.remove_node");
            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return planResult;
            }

            var plan = planResult.Data;
            var nodeId = NormalizeRequired(request.NodeId, nameof(request.NodeId));
            if (nodeId is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node id is required.",
                    traceId,
                    $"{nameof(request.NodeId)} must not be empty.");
            }

            var existingNode = plan.Nodes.FirstOrDefault(node => node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
            if (existingNode is null)
            {
                return FailurePlan(
                    UseCaseStatus.NotFound,
                    $"Node '{nodeId}' was not found.",
                    traceId,
                    "Unknown node id.");
            }

            var updatedPlan = Touch(plan with
            {
                Nodes = plan.Nodes.Where(node => !node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)).ToArray(),
                Links = plan.Links
                    .Where(link =>
                        !link.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase) &&
                        !link.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            });

            return ReplacePlan(updatedPlan, traceId, $"Removed node '{existingNode.Title}' and its incident links.");
        });
    }

    private UseCaseResult<IndustryPlan> AddNode<TDetails>(
        AddPlanNodeRequest<TDetails> request,
        TDetails details,
        string useCaseName)
        where TDetails : PlanNodeDetails
    {
        var traceId = NextTraceId();
        return ExecutePersistedMutation(traceId, useCaseName, () =>
        {
            var planResult = RequirePlan(request.WorkspaceId, request.PlanId, traceId, useCaseName);
            if (!planResult.IsSuccess || planResult.Data is null)
            {
                return planResult;
            }

            var title = NormalizeRequired(request.Title, nameof(request.Title));
            if (title is null)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node title is required.",
                    traceId,
                    $"{nameof(request.Title)} must not be empty.");
            }

            var detailErrors = ValidateNodeDetails(details);
            if (detailErrors.Count > 0)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node details are invalid.",
                    traceId,
                    detailErrors.ToArray());
            }

            var resourceErrors = ValidateResourceProfile(request.ResourceProfile);
            if (resourceErrors.Count > 0)
            {
                return FailurePlan(
                    UseCaseStatus.InvalidInput,
                    "Node resource profile is invalid.",
                    traceId,
                    resourceErrors.ToArray());
            }

            var plan = planResult.Data;
            var nodeId = NormalizeOptional(request.NodeId) ?? NextNodeId(details.Kind);
            if (plan.Nodes.Any(node => node.NodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)))
            {
                return FailurePlan(
                    UseCaseStatus.Conflict,
                    $"Node '{nodeId}' already exists in plan '{plan.Name}'.",
                    traceId,
                    "Node ids must be unique per plan.");
            }

            var node = new PlanNode
            {
                NodeId = nodeId,
                Title = title,
                Notes = NormalizeOptional(request.Notes),
                LocationLabel = NormalizeOptional(request.LocationLabel),
                Details = details,
                ResourceProfile = request.ResourceProfile,
                Metadata = NormalizeMetadata(request.Metadata)
            };

            var updatedPlan = Touch(plan with
            {
                Nodes = plan.Nodes.Concat([node]).ToArray()
            });

            return ReplacePlan(updatedPlan, traceId, $"Added {details.Kind} node '{title}'.");
        });
    }

    private static IReadOnlyList<string> ValidateGoal(PlanGoal goal)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(goal.TargetTypeId))
        {
            errors.Add("Goal target_type_id is required.");
        }

        if (string.IsNullOrWhiteSpace(goal.TargetName))
        {
            errors.Add("Goal target_name is required.");
        }

        if (goal.Quantity <= 0)
        {
            errors.Add("Goal quantity must be greater than zero.");
        }

        if (goal.DeliveryWindowStartUtc.HasValue &&
            goal.DeliveryDeadlineUtc.HasValue &&
            goal.DeliveryWindowStartUtc > goal.DeliveryDeadlineUtc)
        {
            errors.Add("Goal delivery window start must be earlier than the deadline.");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidatePreferences(PlanPreferences preferences)
    {
        var errors = new List<string>();

        if (preferences.MaxDailyManualOperations.HasValue && preferences.MaxDailyManualOperations.Value <= 0)
        {
            errors.Add("Max daily manual operations must be greater than zero when provided.");
        }

        if (preferences.MaxAcceptedTransportLegs.HasValue && preferences.MaxAcceptedTransportLegs.Value < 0)
        {
            errors.Add("Max accepted transport legs cannot be negative.");
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateNodeDetails(PlanNodeDetails details)
    {
        var errors = new List<string>();

        switch (details)
        {
            case ProductionNodeDetails production:
                if (production.RecipeTypeId is not null && string.IsNullOrWhiteSpace(production.RecipeTypeId))
                {
                    errors.Add("Production recipe_type_id cannot be whitespace.");
                }

                if (production.RecipeName is not null && string.IsNullOrWhiteSpace(production.RecipeName))
                {
                    errors.Add("Production recipe_name cannot be whitespace.");
                }
                break;

            case ReactionNodeDetails reaction:
                if (reaction.ReactionTypeId is not null && string.IsNullOrWhiteSpace(reaction.ReactionTypeId))
                {
                    errors.Add("Reaction reaction_type_id cannot be whitespace.");
                }

                if (reaction.ReactionName is not null && string.IsNullOrWhiteSpace(reaction.ReactionName))
                {
                    errors.Add("Reaction reaction_name cannot be whitespace.");
                }
                break;

            case CopyOrInventionNodeDetails copyOrInvention:
                if (string.IsNullOrWhiteSpace(copyOrInvention.BlueprintTypeId))
                {
                    errors.Add("Copy or invention nodes require blueprint_type_id.");
                }

                if (string.IsNullOrWhiteSpace(copyOrInvention.BlueprintName))
                {
                    errors.Add("Copy or invention nodes require blueprint_name.");
                }

                if (copyOrInvention.CopyRuns.HasValue && copyOrInvention.CopyRuns <= 0)
                {
                    errors.Add("Copy runs must be greater than zero when provided.");
                }
                break;

            case InventoryPoolNodeDetails inventoryPool:
                if (inventoryPool.PoolPurpose is not null && string.IsNullOrWhiteSpace(inventoryPool.PoolPurpose))
                {
                    errors.Add("Inventory pool purpose cannot be whitespace.");
                }
                break;

            case TransportNodeDetails transport:
                if (string.IsNullOrWhiteSpace(transport.SourceLocation))
                {
                    errors.Add("Transport nodes require source_location.");
                }

                if (string.IsNullOrWhiteSpace(transport.DestinationLocation))
                {
                    errors.Add("Transport nodes require destination_location.");
                }
                break;

            case TradeNodeDetails trade:
                if (trade.UnitPricePreference.HasValue && trade.UnitPricePreference < 0)
                {
                    errors.Add("Trade node unit_price_preference cannot be negative.");
                }
                break;
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateResourceProfile(PlanNodeResourceProfile resourceProfile)
    {
        var errors = new List<string>();

        if (resourceProfile.CharacterSlots < 0 ||
            resourceProfile.BlueprintSlots < 0 ||
            resourceProfile.BpcSlots < 0 ||
            resourceProfile.JobSlots < 0)
        {
            errors.Add("Resource slot counts cannot be negative.");
        }

        var duplicateAdditionalSlots = resourceProfile.AdditionalSlots
            .GroupBy(slot => slot.SlotId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateAdditionalSlots.Length > 0)
        {
            errors.Add($"Additional slot ids must be unique. Duplicates: {string.Join(", ", duplicateAdditionalSlots)}.");
        }

        foreach (var slot in resourceProfile.AdditionalSlots)
        {
            if (string.IsNullOrWhiteSpace(slot.SlotId))
            {
                errors.Add("Additional resource slot id is required.");
            }

            if (string.IsNullOrWhiteSpace(slot.Label))
            {
                errors.Add("Additional resource slot label is required.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<string> ValidateLinkSemantics(PlanLinkKind kind, PlanNode fromNode, PlanNode toNode)
    {
        var errors = new List<string>();

        if (kind == PlanLinkKind.LogisticsFlow &&
            fromNode.Kind != PlanNodeKind.Transport &&
            toNode.Kind != PlanNodeKind.Transport)
        {
            errors.Add("LogisticsFlow must involve a transport node.");
        }

        if (kind == PlanLinkKind.TradeFlow &&
            fromNode.Kind != PlanNodeKind.Trade &&
            toNode.Kind != PlanNodeKind.Trade)
        {
            errors.Add("TradeFlow must involve a trade node.");
        }

        if (kind == PlanLinkKind.Reservation &&
            fromNode.ResourceProfile.CharacterSlots +
            fromNode.ResourceProfile.BlueprintSlots +
            fromNode.ResourceProfile.BpcSlots +
            fromNode.ResourceProfile.JobSlots +
            toNode.ResourceProfile.CharacterSlots +
            toNode.ResourceProfile.BlueprintSlots +
            toNode.ResourceProfile.BpcSlots +
            toNode.ResourceProfile.JobSlots == 0 &&
            fromNode.ResourceProfile.AdditionalSlots.Count == 0 &&
            toNode.ResourceProfile.AdditionalSlots.Count == 0)
        {
            errors.Add("Reservation links should connect at least one node with resource slot requirements.");
        }

        return errors;
    }

    private UseCaseResult<IndustryPlan> UpdatePlan(
        string workspaceId,
        string planId,
        Func<IndustryPlan, IndustryPlan> update,
        string summary,
        string? traceId = null)
    {
        traceId ??= NextTraceId();
        var planResult = RequirePlan(workspaceId, planId, traceId, "plan.update");
        if (!planResult.IsSuccess || planResult.Data is null)
        {
            return planResult;
        }

        var updated = update(planResult.Data);
        return ReplacePlan(updated, traceId, summary);
    }

    private UseCaseResult<IndustryPlan> ReplacePlan(IndustryPlan updatedPlan, string traceId, string summary)
    {
        plans[updatedPlan.PlanId] = updatedPlan;
        PersistState();
        return UseCaseResult<IndustryPlan>.Success(updatedPlan, summary, traceId);
    }

    private UseCaseResult<WorkspaceSummary> RequireWorkspace(string workspaceId, string traceId, string useCaseName)
    {
        var normalizedWorkspaceId = NormalizeRequired(workspaceId, nameof(workspaceId));
        if (normalizedWorkspaceId is null)
        {
            return UseCaseResult<WorkspaceSummary>.Failure(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires a workspace id.",
                traceId,
                ["Workspace id must not be empty."]);
        }

        return workspaceService.GetSummary(new GetWorkspaceSummaryRequest
        {
            WorkspaceId = normalizedWorkspaceId
        });
    }

    private UseCaseResult<IndustryPlan> RequirePlan(
        string workspaceId,
        string planId,
        string traceId,
        string useCaseName)
    {
        var workspaceResult = RequireWorkspace(workspaceId, traceId, useCaseName);
        if (!workspaceResult.IsSuccess || workspaceResult.Data is null)
        {
            return FailurePlan(workspaceResult.Status, workspaceResult.Summary, traceId, workspaceResult.Errors.ToArray());
        }

        var normalizedPlanId = NormalizeRequired(planId, nameof(planId));
        if (normalizedPlanId is null)
        {
            return FailurePlan(
                UseCaseStatus.InvalidInput,
                $"{useCaseName} requires a plan id.",
                traceId,
                "Plan id must not be empty.");
        }

        if (!plans.TryGetValue(normalizedPlanId, out var plan))
        {
            return FailurePlan(UseCaseStatus.NotFound, $"Plan '{normalizedPlanId}' was not found.", traceId, "Unknown plan id.");
        }

        if (string.IsNullOrWhiteSpace(plan.WorkspaceId))
        {
            return FailurePlan(
                UseCaseStatus.Conflict,
                $"{useCaseName} found legacy plan state that requires explicit workspace migration.",
                traceId,
                $"Plan '{normalizedPlanId}' has no workspace binding. Run the runtime workspace-plan repair command before using this plan.");
        }

        if (!plan.WorkspaceId.Equals(workspaceResult.Data.WorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return FailurePlan(
                UseCaseStatus.NotFound,
                $"{useCaseName} could not resolve the plan in the requested workspace.",
                traceId,
                $"Plan '{normalizedPlanId}' does not belong to workspace '{workspaceResult.Data.WorkspaceId}'.");
        }

        return UseCaseResult<IndustryPlan>.Success(plan, $"Loaded plan '{plan.Name}'.", traceId);
    }

    private static PlanSummary ToSummary(IndustryPlan plan) =>
        new()
        {
            WorkspaceId = plan.WorkspaceId,
            PlanId = plan.PlanId,
            Name = plan.Name,
            Status = plan.Status,
            GoalTargetName = plan.Goal?.TargetName,
            GoalTargetTypeId = plan.Goal?.TargetTypeId,
            GoalQuantity = plan.Goal?.Quantity,
            NodeCount = plan.Nodes.Count,
            LinkCount = plan.Links.Count,
            UpdatedAtUtc = plan.UpdatedAtUtc
        };

    private static IndustryPlan Touch(IndustryPlan plan) =>
        plan with { UpdatedAtUtc = DateTimeOffset.UtcNow };

    private static string NextNodeId(PlanNodeKind kind)
    {
        var prefix = kind switch
        {
            PlanNodeKind.Production => "production",
            PlanNodeKind.Reaction => "reaction",
            PlanNodeKind.CopyOrInvention => "copy",
            PlanNodeKind.InventoryPool => "inventory",
            PlanNodeKind.Transport => "transport",
            PlanNodeKind.Trade => "trade",
            _ => "node"
        };

        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static string NextTraceId() => Guid.NewGuid().ToString("N");

    private static string? NormalizeRequired(string? value, string _)
    {
        var normalized = NormalizeOptional(value);
        return normalized is null ? null : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static IReadOnlyDictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string> metadata)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = pair.Value.Trim();
        }

        return normalized;
    }

    private static UseCaseResult<IndustryPlan> FailurePlan(
        UseCaseStatus status,
        string summary,
        string traceId,
        params string[] errors) =>
        UseCaseResult<IndustryPlan>.Failure(status, summary, traceId, errors);

    private UseCaseResult<IndustryPlan> ExecutePersistedMutation(
        string traceId,
        string useCaseName,
        Func<UseCaseResult<IndustryPlan>> operation)
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

        return FailurePlan(
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

        plans.Clear();

        foreach (var plan in stateStore.Load().Plans)
        {
            plans[plan.PlanId] = plan;
        }
    }

    private void PersistState()
    {
        stateStore?.Save(new IndustryPlanRuntimeState
        {
            Plans = plans.Values
                .OrderBy(plan => plan.CreatedAtUtc)
                .ToArray()
        });
    }
}

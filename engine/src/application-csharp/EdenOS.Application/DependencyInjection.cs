using EdenOS.Application.Abstractions;
using EdenOS.Application.Accounts;
using EdenOS.Application.Authentication;
using EdenOS.Application.CharacterProgression;
using EdenOS.Application.Combat;
using EdenOS.Application.Configuration;
using EdenOS.Application.Execution;
using EdenOS.Application.Fitting;
using EdenOS.Application.Fitting.Dogma.DataSources;
using EdenOS.Application.IdentityDirectory;
using EdenOS.Application.Market;
using EdenOS.Application.Planning;
using EdenOS.Application.RuntimeState;
using EdenOS.Application.Services;
using EdenOS.Application.StarMap;
using EdenOS.Application.Tasks;
using EdenOS.Application.UseCases;
using EdenOS.Contracts.Application;
using EdenOS.Contracts.Authentication;
using EdenOS.Contracts.CharacterPool;
using EdenOS.Contracts.CharacterProgression;
using EdenOS.Contracts.Combat;
using EdenOS.Contracts.Execution;
using EdenOS.Contracts.Fitting;
using EdenOS.Contracts.IdentityDirectory;
using EdenOS.Contracts.Market;
using EdenOS.Contracts.Planning;
using EdenOS.Contracts.Runtime;
using EdenOS.Contracts.StarMap;
using EdenOS.Contracts.Tasks;
using EdenOS.Contracts.Workspaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EdenOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEdenOsApplicationRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var runtimeStateRootPath = RuntimeStatePaths.ResolveRootPath(
            configuration["runtime:state:root_path"]
            ?? configuration["runtime:state:rootPath"]);

        services.Configure<RuntimeOptions>(configuration.GetSection("runtime"));
        services.AddHttpClient("esi-sso");
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RuntimeSessionState>();
        services.AddSingleton<IKernelGateway, PlaceholderKernelGateway>();
        services.AddSingleton<IFitService>(provider =>
        {
            var runtimeOptions = provider.GetRequiredService<IOptions<RuntimeOptions>>().Value;
            if (string.Equals(runtimeOptions.Dogma.SourceKind, "jsonl-filesystem", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(runtimeOptions.Dogma.SdeRootPath) &&
                !string.IsNullOrWhiteSpace(runtimeOptions.Dogma.DataRootPath))
            {
                return new InMemoryFitService(
                    provider.GetRequiredService<TimeProvider>(),
                    new JsonlFileDogmaDataSource(
                        runtimeOptions.Dogma.SdeRootPath,
                        runtimeOptions.Dogma.DataRootPath,
                        runtimeOptions.Dogma.DogmaEffectsOverlayJsonPath,
                        runtimeOptions.Dogma.RuleSetJsonPath));
            }

            return new InMemoryFitService(provider.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<ICombatSimulationService, InMemoryCombatSimulationService>();
        services.AddSingleton<IFitEnvironmentProfileService>(provider =>
            runtimeStateRootPath is null
                ? FitEnvironmentProfileServiceCollection.CreateInMemory(provider.GetRequiredService<TimeProvider>())
                : FitEnvironmentProfileServiceCollection.CreatePersistent(
                    runtimeStateRootPath,
                    provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<InMemoryAccountCharacterPoolService>(_ =>
            runtimeStateRootPath is null
                ? new InMemoryAccountCharacterPoolService(
                    LocalWorkspaceMarketGrantStore.LoadDefaultOrBootstrapFallback(),
                    stateStore: null,
                    identityDirectoryService: _.GetService<IIdentityDirectoryService>())
                : new InMemoryAccountCharacterPoolService(
                    LocalWorkspaceMarketGrantStore.LoadDefaultOrBootstrapFallback(),
                    new LocalJsonStateStore<WorkspaceRuntimeState>(
                        RuntimeStatePaths.GetWorkspaceStatePath(runtimeStateRootPath),
                        () => new WorkspaceRuntimeState()),
                    _.GetService<IIdentityDirectoryService>()));
        services.AddSingleton<IWorkspaceService>(provider => provider.GetRequiredService<InMemoryAccountCharacterPoolService>());
        services.AddSingleton<ICharacterPoolService>(provider => provider.GetRequiredService<InMemoryAccountCharacterPoolService>());
        services.AddSingleton<IOperatorPoolService>(provider => provider.GetRequiredService<InMemoryAccountCharacterPoolService>());
        services.AddSingleton<IWorkspaceBoundMarketAccessService>(provider => provider.GetRequiredService<InMemoryAccountCharacterPoolService>());
        services.AddSingleton<ICharacterProgressionService>(provider =>
        {
            var runtimeOptions = provider.GetRequiredService<IOptions<RuntimeOptions>>().Value;
            var sdeRootPath = CharacterProgressionSdePathResolver.ResolveSdeRootPath(runtimeOptions.Dogma.SdeRootPath);
            if (string.IsNullOrWhiteSpace(sdeRootPath))
            {
                throw new InvalidOperationException(
                    "Character progression service requires a JSONL SDE root path. Set runtime.dogma.sde_root_path or EDENOS_CHARACTER_PROGRESSION_SDE_ROOT.");
            }

            return new InMemoryCharacterProgressionService(
                provider.GetRequiredService<ICharacterPoolService>(),
                provider.GetRequiredService<TimeProvider>(),
                sdeRootPath);
        });
        services.AddSingleton<IIdentityDirectoryService>(_ =>
            runtimeStateRootPath is null
                ? IdentityDirectoryServiceCollection.CreateInMemory()
                : IdentityDirectoryServiceCollection.CreatePersistent(runtimeStateRootPath));
        services.AddSingleton<IEsiCredentialService>(provider =>
            runtimeStateRootPath is null
                ? new WorkspaceBoundEsiCredentialService(
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<IHttpClientFactory>(),
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RuntimeOptions>>())
                : new WorkspaceBoundEsiCredentialService(
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<IHttpClientFactory>(),
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RuntimeOptions>>(),
                    new LocalJsonStateStore<EsiCredentialRuntimeState>(
                        RuntimeStatePaths.GetEsiCredentialStatePath(runtimeStateRootPath),
                        () => new EsiCredentialRuntimeState())));
        services.AddSingleton<IIndustryPlanService>(provider =>
            runtimeStateRootPath is null
                ? IndustryPlanServiceCollection.CreateInMemory(provider.GetRequiredService<IWorkspaceService>())
                : IndustryPlanServiceCollection.CreatePersistent(
                    runtimeStateRootPath,
                    provider.GetRequiredService<IWorkspaceService>()));
        services.AddSingleton<IMarketFactsService>(provider =>
        {
            var workspaceBoundMarketAccessService = provider.GetRequiredService<IWorkspaceBoundMarketAccessService>();
            var configuredMarketFactsRootPath = RootedPathResolver.ResolveOptionalPath(
                AppContext.BaseDirectory,
                configuration["runtime:market_facts:root_path"]
                ?? configuration["runtime:marketFacts:rootPath"]);

            if (string.IsNullOrWhiteSpace(configuredMarketFactsRootPath))
            {
                return MarketFactsServiceCollection.CreateInMemory(workspaceBoundMarketAccessService);
            }

            return new InMemoryMarketFactsService(
                MetadataBootstrapCatalog.LoadDefault(),
                workspaceBoundMarketAccessService,
                new LocalMarketSnapshotStore(configuredMarketFactsRootPath),
                new LocalMarketHistoryStore(configuredMarketFactsRootPath),
                new LocalMarketStatisticsStore(configuredMarketFactsRootPath),
                new LocalMarketOrderStore(configuredMarketFactsRootPath),
                new LocalAdjustedPriceStore());
        });
        services.AddSingleton<IStarMapService>(_ => new InMemoryStarMapService());
        services.AddSingleton<IStarMapFactsService>(provider =>
            new FileBackedStarMapFactsService(
                configuration["runtime:star_map_facts:root_path"]
                ?? configuration["runtime:starMapFacts:rootPath"]));
        services.AddSingleton<ITaskPoolService>(provider =>
            runtimeStateRootPath is null
                ? TaskPoolServiceCollection.CreateInMemory(
                    provider.GetRequiredService<IIndustryPlanService>(),
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<TimeProvider>())
                : TaskPoolServiceCollection.CreatePersistent(
                    runtimeStateRootPath,
                    provider.GetRequiredService<IIndustryPlanService>(),
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IIndustryPlanComputationService>(provider =>
            IndustryPlanComputationServiceCollection.CreateInMemory(
                provider.GetRequiredService<IIndustryPlanService>(),
                provider.GetRequiredService<IMarketFactsService>(),
                provider.GetRequiredService<ICharacterPoolService>()));
        services.AddSingleton<IExecutionTrackingService>(provider =>
            runtimeStateRootPath is null
                ? ExecutionTrackingServiceCollection.CreateInMemory(
                    provider.GetRequiredService<IIndustryPlanService>(),
                    provider.GetRequiredService<IIndustryPlanComputationService>(),
                    provider.GetRequiredService<ITaskPoolService>(),
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<TimeProvider>())
                : ExecutionTrackingServiceCollection.CreatePersistent(
                    runtimeStateRootPath,
                    provider.GetRequiredService<IIndustryPlanService>(),
                    provider.GetRequiredService<IIndustryPlanComputationService>(),
                    provider.GetRequiredService<ITaskPoolService>(),
                    provider.GetRequiredService<IWorkspaceService>(),
                    provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IWorkspacePlanStateMigrationService>(provider =>
            new WorkspacePlanMigrationService(
                provider.GetRequiredService<IWorkspaceService>(),
                provider.GetRequiredService<TimeProvider>(),
                runtimeStateRootPath is null
                    ? null
                    : new LocalJsonStateStore<IndustryPlanRuntimeState>(
                        RuntimeStatePaths.GetPlanStatePath(runtimeStateRootPath),
                        () => new IndustryPlanRuntimeState()),
                runtimeStateRootPath is null
                    ? null
                    : new LocalJsonStateStore<PendingTaskRuntimeState>(
                        RuntimeStatePaths.GetTaskStatePath(runtimeStateRootPath),
                        () => new PendingTaskRuntimeState()),
                runtimeStateRootPath is null
                    ? null
                    : new LocalJsonStateStore<ExecutionRuntimeState>(
                        RuntimeStatePaths.GetExecutionStatePath(runtimeStateRootPath),
                        () => new ExecutionRuntimeState()),
                runtimeStateRootPath is null
                    ? null
                    : new LocalJsonStateStore<WorkspacePlanRepairAuditState>(
                        RuntimeStatePaths.GetWorkspacePlanRepairAuditStatePath(runtimeStateRootPath),
                        () => new WorkspacePlanRepairAuditState())));
        services.AddSingleton<IQueryUseCase<RuntimeHealthRequest, RuntimeStatus>, RuntimeHealthUseCase>();
        services.AddSingleton<IQueryUseCase<ReportWorkspacePlanStateRequest, WorkspacePlanStateReport>, ReportWorkspacePlanStateUseCase>();
        services.AddSingleton<IQueryUseCase<PreviewWorkspacePlanStateRepairRequest, WorkspacePlanStateReport>, PreviewWorkspacePlanStateRepairUseCase>();
        services.AddSingleton<ICommandUseCase<RepairWorkspacePlanStateRequest, WorkspacePlanStateReport>, RepairWorkspacePlanStateUseCase>();
        services.AddSingleton<IQueryUseCase<GetWorkspacePlanRepairHistoryRequest, WorkspacePlanRepairHistoryView>, GetWorkspacePlanRepairHistoryUseCase>();
        services.AddSingleton<IQueryUseCase<ExportWorkspacePlanRepairAuditRequest, WorkspacePlanRepairAuditExportView>, ExportWorkspacePlanRepairAuditUseCase>();
        services.AddSingleton<IQueryUseCase<VerifyWorkspacePlanRepairAuditRequest, WorkspacePlanRepairAuditVerificationView>, VerifyWorkspacePlanRepairAuditUseCase>();
        services.AddSingleton<IQueryUseCase<GetKnownTypesRequest, KnownTypesCatalog>, MarketGetKnownTypesUseCase>();
        services.AddSingleton<IQueryUseCase<GetItemProfileRequest, ItemMarketProfile>, MarketGetItemProfileUseCase>();
        services.AddSingleton<IQueryUseCase<GetAdjustedPricesRequest, AdjustedPriceCatalog>, MarketGetAdjustedPricesUseCase>();
        services.AddSingleton<IQueryUseCase<GetPriceSnapshotRequest, PriceSnapshot>, MarketGetPriceSnapshotUseCase>();
        services.AddSingleton<IQueryUseCase<GetMarketOrdersRequest, MarketOrdersEnvelope>, MarketGetMarketOrdersUseCase>();
        services.AddSingleton<IQueryUseCase<GetHistoryWindowRequest, HistoryWindow>, MarketGetHistoryWindowUseCase>();
        services.AddSingleton<IQueryUseCase<GetBasicStatisticsRequest, BasicStatisticsEnvelope>, MarketGetBasicStatisticsUseCase>();
        services.AddSingleton<IQueryUseCase<GetAccessibleStructuresRequest, AccessibleStructureCatalog>, MarketGetAccessibleStructuresUseCase>();
        services.AddSingleton<IQueryUseCase<ListStarMapRegionsRequest, StarMapRegionCatalog>, StarMapListRegionsUseCase>();
        services.AddSingleton<IQueryUseCase<GetRegionStarMapRequest, RegionStarMap>, StarMapGetRegionMapUseCase>();
        services.AddSingleton<IQueryUseCase<SearchStarMapSolarSystemsRequest, StarMapSolarSystemCatalog>, StarMapSearchSolarSystemsUseCase>();
        services.AddSingleton<IQueryUseCase<ResolveStarMapReferenceRequest, StarMapNodeResolution>, StarMapResolveReferenceUseCase>();
        services.AddSingleton<IQueryUseCase<GetStarMapNeighborsRequest, StarMapNeighborsView>, StarMapGetNeighborsUseCase>();
        services.AddSingleton<IQueryUseCase<FindStarMapRouteRequest, StarMapRouteView>, StarMapFindRouteUseCase>();
        services.AddSingleton<IQueryUseCase<GetStarMapSystemJumpsRequest, StarMapSystemJumpCatalog>, StarMapGetSystemJumpsUseCase>();
        services.AddSingleton<IQueryUseCase<GetStarMapSystemKillsRequest, StarMapSystemKillCatalog>, StarMapGetSystemKillsUseCase>();
        services.AddSingleton<IQueryUseCase<GetStarMapSovereigntyMapRequest, StarMapSovereigntyMapCatalog>, StarMapGetSovereigntyMapUseCase>();
        services.AddSingleton<IQueryUseCase<ResolveIdentityDirectorySubjectRequest, IdentityDirectoryResolvedSubject>, IdentityDirectoryResolveSubjectUseCase>();
        services.AddSingleton<ICommandUseCase<ProvisionIdentityDirectoryUserFromSubjectRequest, IdentityDirectoryUserSummary>, IdentityDirectoryProvisionUserFromSubjectUseCase>();
        services.AddSingleton<ICommandUseCase<AttachExternalIdentityRequest, IdentityDirectoryIdentitySummary>, IdentityDirectoryAttachExternalIdentityUseCase>();
        services.AddSingleton<ICommandUseCase<CreateVirtualIdentityRequest, IdentityDirectoryIdentitySummary>, IdentityDirectoryCreateVirtualIdentityUseCase>();
        services.AddSingleton<ICommandUseCase<CreateIdentityDirectoryActorRequest, IdentityDirectoryActorSummary>, IdentityDirectoryCreateActorUseCase>();
        services.AddSingleton<ICommandUseCase<BootstrapIdentityDirectoryUserSessionRequest, IdentityDirectoryUserSessionView>, IdentityDirectoryBootstrapUserSessionUseCase>();
        services.AddSingleton<IQueryUseCase<ListSwitchableIdentityDirectoryActorsRequest, IReadOnlyList<IdentityDirectoryActorSummary>>, IdentityDirectoryListSwitchableActorsUseCase>();
        services.AddSingleton<ICommandUseCase<SwitchActiveIdentityDirectoryActorRequest, IdentityDirectoryActorSummary>, IdentityDirectorySwitchActiveActorUseCase>();
        services.AddSingleton<ICommandUseCase<AddIdentityDirectoryAccessGrantRequest, IdentityDirectoryAccessGrantSummary>, IdentityDirectoryAddAccessGrantUseCase>();
        services.AddSingleton<IQueryUseCase<CheckIdentityDirectoryAccessRequest, IdentityDirectoryAccessDecision>, IdentityDirectoryCheckAccessUseCase>();
        services.AddSingleton<ICommandUseCase<OpenWorkspaceRequest, WorkspaceContext>, WorkspaceOpenUseCase>();
        services.AddSingleton<IQueryUseCase<GetWorkspaceSummaryRequest, WorkspaceSummary>, WorkspaceGetSummaryUseCase>();
        services.AddSingleton<IQueryUseCase<ListCharacterPoolRequest, CharacterPoolView>, CharacterPoolListUseCase>();
        services.AddSingleton<IQueryUseCase<ListOperatorPoolRequest, OperatorPoolView>, OperatorPoolListUseCase>();
        services.AddSingleton<IQueryUseCase<GetCharacterProgressionSkillCatalogRequest, CharacterProgressionSkillCatalogView>, CharacterProgressionGetSkillCatalogUseCase>();
        services.AddSingleton<ICommandUseCase<AddVirtualCharacterRequest, CharacterPoolEntry>, CharacterPoolAddVirtualCharacterUseCase>();
        services.AddSingleton<ICommandUseCase<AddOperatorRequest, OperatorPoolEntry>, OperatorPoolAddUseCase>();
        services.AddSingleton<ICommandUseCase<UpdateVirtualCharacterRequest, CharacterPoolEntry>, CharacterPoolUpdateVirtualCharacterUseCase>();
        services.AddSingleton<ICommandUseCase<UpdateOperatorRequest, OperatorPoolEntry>, OperatorPoolUpdateUseCase>();
        services.AddSingleton<ICommandUseCase<AttachEsiCharacterRequest, CharacterPoolEntry>, CharacterPoolAttachEsiCharacterUseCase>();
        services.AddSingleton<ICommandUseCase<UpdateEsiCharacterCapabilityProfileRequest, CharacterPoolEntry>, CharacterPoolUpdateEsiCharacterCapabilityProfileUseCase>();
        services.AddSingleton<ICommandUseCase<AssignCharacterOperatorRequest, CharacterPoolEntry>, OperatorPoolAssignCharacterUseCase>();
        services.AddSingleton<IWorkflowUseCase<SimulateCharacterProgressionRequest, CharacterProgressionSimulationView>, CharacterProgressionSimulateUseCase>();
        services.AddSingleton<ICommandUseCase<BindEsiCredentialRequest, EsiCredentialStatusView>, EsiCredentialBindUseCase>();
        services.AddSingleton<IQueryUseCase<GetEsiCredentialStatusRequest, EsiCredentialStatusView>, EsiCredentialGetStatusUseCase>();
        services.AddSingleton<IWorkflowUseCase<ResolveEsiAccessTokenRequest, EsiAccessTokenEnvelope>, EsiCredentialResolveAccessTokenUseCase>();
        services.AddSingleton<ICommandUseCase<CreateOrUpdateFitRequest, FitSnapshot>, FitCreateOrUpdateUseCase>();
        services.AddSingleton<IQueryUseCase<ValidateFitRequest, FitValidationResult>, FitValidateUseCase>();
        services.AddSingleton<IQueryUseCase<GetFitAttributesRequest, FitAttributeView>, FitGetAttributesUseCase>();
        services.AddSingleton<ICommandUseCase<ImportFitTextRequest, ImportedFitTextView>, FitImportTextUseCase>();
        services.AddSingleton<IQueryUseCase<ExportFitTextRequest, ExportedFitTextView>, FitExportTextUseCase>();
        services.AddSingleton<ICommandUseCase<SelectFitAmmoRequest, FitAmmoSelectionResult>, FitSelectAmmoUseCase>();
        services.AddSingleton<ICommandUseCase<SaveFitEnvironmentProfileRequest, FitEnvironmentProfile>, FitEnvironmentSaveProfileUseCase>();
        services.AddSingleton<IQueryUseCase<GetFitEnvironmentProfileRequest, FitEnvironmentProfile>, FitEnvironmentGetProfileUseCase>();
        services.AddSingleton<IQueryUseCase<ListFitEnvironmentProfilesRequest, FitEnvironmentProfileCatalogView>, FitEnvironmentListProfilesUseCase>();
        services.AddSingleton<ICommandUseCase<DeleteFitEnvironmentProfileRequest, FitEnvironmentProfileDeleteResult>, FitEnvironmentDeleteProfileUseCase>();
        services.AddSingleton<ICommandUseCase<ApplyFitEnvironmentProfileRequest, FitEnvironmentProfileApplyResult>, FitEnvironmentApplyProfileUseCase>();
        services.AddSingleton<IWorkflowUseCase<SimulateDuelRequest, CombatOutcome>, CombatSimulateDuelUseCase>();
        services.AddSingleton<ICommandUseCase<CreatePlanRequest, IndustryPlan>, PlanCreateUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanRequest, IndustryPlan>, PlanGetUseCase>();
        services.AddSingleton<IQueryUseCase<ListPlansRequest, IReadOnlyList<PlanSummary>>, PlanListUseCase>();
        services.AddSingleton<ICommandUseCase<SetPlanGoalRequest, IndustryPlan>, PlanSetGoalUseCase>();
        services.AddSingleton<ICommandUseCase<SetPlanPreferencesRequest, IndustryPlan>, PlanSetPreferencesUseCase>();
        services.AddSingleton<ICommandUseCase<AddProductionNodeRequest, IndustryPlan>, PlanAddProductionNodeUseCase>();
        services.AddSingleton<ICommandUseCase<AddReactionNodeRequest, IndustryPlan>, PlanAddReactionNodeUseCase>();
        services.AddSingleton<ICommandUseCase<AddCopyOrInventionNodeRequest, IndustryPlan>, PlanAddCopyOrInventionNodeUseCase>();
        services.AddSingleton<ICommandUseCase<AddInventoryPoolNodeRequest, IndustryPlan>, PlanAddInventoryPoolUseCase>();
        services.AddSingleton<ICommandUseCase<AddTransportNodeRequest, IndustryPlan>, PlanAddTransportNodeUseCase>();
        services.AddSingleton<ICommandUseCase<AddTradeNodeRequest, IndustryPlan>, PlanAddTradeNodeUseCase>();
        services.AddSingleton<ICommandUseCase<LinkPlanNodesRequest, IndustryPlan>, PlanLinkNodesUseCase>();
        services.AddSingleton<ICommandUseCase<UnlinkPlanNodesRequest, IndustryPlan>, PlanUnlinkNodesUseCase>();
        services.AddSingleton<ICommandUseCase<UpdatePlanNodeRequest, IndustryPlan>, PlanUpdateNodeUseCase>();
        services.AddSingleton<ICommandUseCase<RemovePlanNodeRequest, IndustryPlan>, PlanRemoveNodeUseCase>();
        services.AddSingleton<IQueryUseCase<ListPendingTasksRequest, PendingTaskPoolView>, TaskPoolListPendingUseCase>();
        services.AddSingleton<ICommandUseCase<CreateTaskFromTransportNodeRequest, PendingTask>, TaskPoolCreateFromTransportNodeUseCase>();
        services.AddSingleton<ICommandUseCase<CreateTaskFromTradeNeedRequest, PendingTask>, TaskPoolCreateFromTradeNeedUseCase>();
        services.AddSingleton<ICommandUseCase<CreateTaskFromOutsourceNeedRequest, PendingTask>, TaskPoolCreateFromOutsourceNeedUseCase>();
        services.AddSingleton<ICommandUseCase<UpdatePendingTaskRequest, PendingTask>, TaskPoolUpdatePendingTaskUseCase>();
        services.AddSingleton<ICommandUseCase<MarkReadyForPublishRequest, PendingTask>, TaskPoolMarkReadyForPublishUseCase>();
        services.AddSingleton<IQueryUseCase<ExportManualPublishPayloadRequest, ManualPublishExportEnvelope>, TaskPoolExportManualPublishPayloadUseCase>();
        services.AddSingleton<IQueryUseCase<GetNextActionsRequest, ExecutionNextActionsView>, ExecutionGetNextActionsUseCase>();
        services.AddSingleton<ICommandUseCase<MarkStepDoneRequest, ExecutionState>, ExecutionMarkStepDoneUseCase>();
        services.AddSingleton<ICommandUseCase<RecordMaterialArrivalRequest, ExecutionState>, ExecutionRecordMaterialArrivalUseCase>();
        services.AddSingleton<ICommandUseCase<RecordMaterialLossRequest, ExecutionState>, ExecutionRecordMaterialLossUseCase>();
        services.AddSingleton<ICommandUseCase<RecordMarketChangeRequest, ExecutionState>, ExecutionRecordMarketChangeUseCase>();
        services.AddSingleton<ICommandUseCase<RecordIndustryCostChangeRequest, ExecutionState>, ExecutionRecordIndustryCostChangeUseCase>();
        services.AddSingleton<ICommandUseCase<RecordLocationChangeRequest, ExecutionState>, ExecutionRecordLocationChangeUseCase>();
        services.AddSingleton<ICommandUseCase<RecordManualOverrideRequest, ExecutionState>, ExecutionRecordManualOverrideUseCase>();
        services.AddSingleton<ICommandUseCase<ResolveManualOverrideRequest, ExecutionState>, ExecutionResolveManualOverrideUseCase>();
        services.AddSingleton<IQueryUseCase<GetBlockersRequest, ExecutionBlockerView>, ExecutionGetBlockersUseCase>();
        services.AddSingleton<IWorkflowUseCase<ExecutionReplanRequest, ExecutionReplanResult>, ExecutionReplanUseCase>();
        services.AddSingleton<IWorkflowUseCase<PlanComputeRequest, PlanComputationResult>, PlanComputeUseCase>();
        services.AddSingleton<IWorkflowUseCase<PlanRecomputeRequest, PlanComputationResult>, PlanRecomputeUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanFeasibilityRequest, PlanFeasibilitySummary>, PlanGetFeasibilityUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanCostBreakdownRequest, PlanCostBreakdown>, PlanGetCostBreakdownUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanProfitEstimateRequest, PlanProfitEstimate>, PlanGetProfitEstimateUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanTimeEstimateRequest, PlanTimeEstimate>, PlanGetTimeEstimateUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanConstraintSummaryRequest, PlanConstraintSummary>, PlanGetConstraintSummaryUseCase>();
        services.AddSingleton<IQueryUseCase<GetPlanAlternativePathsRequest, IReadOnlyList<PlanAlternativePath>>, PlanGetAlternativePathsUseCase>();

        return services;
    }
}

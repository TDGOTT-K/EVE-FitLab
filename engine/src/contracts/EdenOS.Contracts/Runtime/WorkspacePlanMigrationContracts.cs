using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Runtime;

public enum WorkspacePlanStateReferenceKind
{
    Task,
    ExecutionState
}

public enum WorkspacePlanStateIssueKind
{
    PlanMissingWorkspaceBinding,
    PlanWorkspaceMissing,
    AmbiguousPlanWorkspaceBinding,
    TaskWorkspaceMismatch,
    ExecutionWorkspaceMismatch,
    OrphanTaskReference,
    OrphanExecutionReference
}

public enum WorkspacePlanStateRepairKind
{
    BindPlanWorkspace,
    RebindPlanWorkspace,
    RealignTaskWorkspace,
    RealignExecutionWorkspace
}

public enum WorkspacePlanStateRepairSourceKind
{
    Automatic,
    ManualMapping
}

public enum WorkspacePlanRepairOperationKind
{
    Inspect,
    Preview,
    Apply
}

public enum WorkspacePlanRepairAuditExportScopeKind
{
    RetainedLedger,
    PlanFilteredExtract,
    RepairRunExtract
}

public enum WorkspacePlanRepairAuditSealKind
{
    DigestOnly,
    EcdsaP256Sha256
}

public enum WorkspacePlanRepairAuditTrustAnchorKind
{
    DetachedDigest,
    EmbeddedPublicKey
}

public enum WorkspacePlanRepairAuditWitnessStrategyKind
{
    None,
    DetachedWitnessReceipt
}

public enum WorkspacePlanRepairAuditPromotionStageKind
{
    DetachedDigestBundle,
    SealedBundle,
    WitnessedBundle,
    PublishedDeliveryBundle
}

public enum WorkspacePlanRepairAuditEntryKind
{
    Repair,
    ManualMappingDecision,
    ManualReview
}

public enum WorkspacePlanRepairAuditOutcome
{
    PreviewOnly,
    Applied,
    Rejected,
    PendingManualReview
}

public enum WorkspacePlanManualMappingStatus
{
    Accepted,
    InvalidMapping,
    DuplicatePlanId,
    MissingPlan,
    WorkspaceNotFound,
    AlreadyBoundToWorkspace,
    ConflictsWithExistingWorkspaceBinding,
    RedundantWithAutomaticRepair,
    ConflictsWithAutomaticRepair
}

public sealed record WorkspacePlanStateReference
{
    public required WorkspacePlanStateReferenceKind Kind { get; init; }

    public required string ReferenceId { get; init; }

    public string? WorkspaceId { get; init; }

    public string? Summary { get; init; }
}

public sealed record WorkspacePlanStateIssue
{
    public required WorkspacePlanStateIssueKind Kind { get; init; }

    public string? PlanId { get; init; }

    public string? WorkspaceId { get; init; }

    public string? RelatedId { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<string> CandidateWorkspaceIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<WorkspacePlanStateReference> References { get; init; } = Array.Empty<WorkspacePlanStateReference>();
}

public sealed record WorkspacePlanStateRepair
{
    public required WorkspacePlanStateRepairKind Kind { get; init; }

    public WorkspacePlanStateRepairSourceKind SourceKind { get; init; } = WorkspacePlanStateRepairSourceKind.Automatic;

    public string? PlanId { get; init; }

    public string? RelatedId { get; init; }

    public string? FromWorkspaceId { get; init; }

    public string? ToWorkspaceId { get; init; }

    public bool Applied { get; init; }

    public string? RequestedBy { get; init; }

    public string? Source { get; init; }

    public string? Reason { get; init; }

    public required string Summary { get; init; }
}

public sealed record ReportWorkspacePlanStateRequest;

public sealed record WorkspacePlanManualMapping
{
    public required string PlanId { get; init; }

    public required string WorkspaceId { get; init; }

    public string? Source { get; init; }

    public string? Reason { get; init; }

    public string? RequestedBy { get; init; }
}

public sealed record WorkspacePlanManualMappingEvaluation
{
    public string? PlanId { get; init; }

    public string? WorkspaceId { get; init; }

    public WorkspacePlanManualMappingStatus Status { get; init; }

    public bool Accepted { get; init; }

    public string? RequestedBy { get; init; }

    public string? Source { get; init; }

    public string? Reason { get; init; }

    public required string Summary { get; init; }
}

public sealed record PreviewWorkspacePlanStateRepairRequest
{
    public bool RealignTaskWorkspaces { get; init; } = true;

    public bool RealignExecutionWorkspaces { get; init; } = true;

    public string? RequestedBy { get; init; }

    public IReadOnlyList<WorkspacePlanManualMapping> ManualMappings { get; init; } = Array.Empty<WorkspacePlanManualMapping>();
}

public sealed record RepairWorkspacePlanStateRequest
{
    public bool RealignTaskWorkspaces { get; init; } = true;

    public bool RealignExecutionWorkspaces { get; init; } = true;

    public string? RequestedBy { get; init; }

    public IReadOnlyList<WorkspacePlanManualMapping> ManualMappings { get; init; } = Array.Empty<WorkspacePlanManualMapping>();
}

public sealed record GetWorkspacePlanRepairHistoryRequest
{
    public string? PlanId { get; init; }

    public string? RepairRunId { get; init; }

    public int RunLimit { get; init; } = 20;

    public int EntryLimit { get; init; } = 100;
}

public sealed record ExportWorkspacePlanRepairAuditRequest
{
    public string? PlanId { get; init; }

    public string? RepairRunId { get; init; }

    public int RunLimit { get; init; } = 200;

    public int EntryLimit { get; init; } = 1000;

    public string? SealKeyId { get; init; }

    public string? SealPrivateKeyPem { get; init; }
}

public sealed record VerifyWorkspacePlanRepairAuditRequest;

public sealed record WorkspacePlanManualReviewItem
{
    public required string PlanId { get; init; }

    public string? WorkspaceId { get; init; }

    public required string Summary { get; init; }

    public IReadOnlyList<WorkspacePlanStateIssueKind> BlockingIssueKinds { get; init; } = Array.Empty<WorkspacePlanStateIssueKind>();

    public IReadOnlyList<string> CandidateWorkspaceIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<WorkspacePlanStateReference> References { get; init; } = Array.Empty<WorkspacePlanStateReference>();
}

public sealed record WorkspacePlanStateReport
{
    public required DateTimeOffset CheckedAtUtc { get; init; }

    public WorkspacePlanRepairOperationKind OperationKind { get; init; } = WorkspacePlanRepairOperationKind.Inspect;

    public string? RepairRunId { get; init; }

    public bool AuditRecorded { get; init; }

    public int LegacyPlanCount { get; init; }

    public int IssueCount { get; init; }

    public int RepairableIssueCount { get; init; }

    public int ManualReviewCount { get; init; }

    public int ManualMappingCount { get; init; }

    public int AcceptedManualMappingCount { get; init; }

    public int AppliedRepairCount { get; init; }

    public IReadOnlyList<WorkspacePlanStateIssue> Issues { get; init; } = Array.Empty<WorkspacePlanStateIssue>();

    public IReadOnlyList<WorkspacePlanManualReviewItem> ManualReviewPlans { get; init; } = Array.Empty<WorkspacePlanManualReviewItem>();

    public IReadOnlyList<WorkspacePlanManualMappingEvaluation> ManualMappingEvaluations { get; init; } = Array.Empty<WorkspacePlanManualMappingEvaluation>();

    public IReadOnlyList<WorkspacePlanStateRepair> PlannedRepairs { get; init; } = Array.Empty<WorkspacePlanStateRepair>();

    public IReadOnlyList<WorkspacePlanStateRepair> Repairs { get; init; } = Array.Empty<WorkspacePlanStateRepair>();
}

public sealed record WorkspacePlanRepairAuditEntry
{
    public required string EntryId { get; init; }

    public required WorkspacePlanRepairAuditEntryKind Kind { get; init; }

    public required WorkspacePlanRepairAuditOutcome Outcome { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }

    public string? PlanId { get; init; }

    public string? RelatedId { get; init; }

    public string? FromWorkspaceId { get; init; }

    public string? ToWorkspaceId { get; init; }

    public WorkspacePlanStateRepairKind? RepairKind { get; init; }

    public WorkspacePlanStateRepairSourceKind? RepairSourceKind { get; init; }

    public WorkspacePlanManualMappingStatus? ManualMappingStatus { get; init; }

    public string? RequestedBy { get; init; }

    public string? Source { get; init; }

    public string? Reason { get; init; }

    public required string Summary { get; init; }
}

public sealed record RuntimeStateFileFingerprint
{
    public long Revision { get; init; }

    public string? ContentHash { get; init; }
}

public sealed record WorkspacePlanRuntimeStateFingerprint
{
    public RuntimeStateFileFingerprint PlanState { get; init; } = new();

    public RuntimeStateFileFingerprint TaskState { get; init; } = new();

    public RuntimeStateFileFingerprint ExecutionState { get; init; } = new();
}

public sealed record WorkspacePlanRepairAuditCompactionSummary
{
    public required string CompactionId { get; init; }

    public required DateTimeOffset CompactedAtUtc { get; init; }

    public long OldestSequenceNumber { get; init; }

    public long NewestSequenceNumber { get; init; }

    public required DateTimeOffset OldestRecordedAtUtc { get; init; }

    public required DateTimeOffset NewestRecordedAtUtc { get; init; }

    public string? NewestRunHash { get; init; }

    public int RunCount { get; init; }

    public int EntryCount { get; init; }

    public int PreviewRunCount { get; init; }

    public int ApplyRunCount { get; init; }

    public int RejectedEntryCount { get; init; }

    public int PendingManualReviewEntryCount { get; init; }

    public IReadOnlyList<string> PlanIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RepairRunIds { get; init; } = Array.Empty<string>();
}

public sealed record WorkspacePlanRepairAuditRetentionView
{
    public int DetailedRunRetentionLimit { get; init; }

    public int DetailedRunCount { get; init; }

    public int CompactionCount { get; init; }

    public int CompactedRunCount { get; init; }

    public int CompactedEntryCount { get; init; }

    public long CompactedThroughSequenceNumber { get; init; }

    public DateTimeOffset? LastCompactedAtUtc { get; init; }
}

public sealed record WorkspacePlanRepairAuditIntegrityIssue
{
    public required string Code { get; init; }

    public required string Summary { get; init; }

    public string? RepairRunId { get; init; }
}

public sealed record WorkspacePlanRepairAuditCheckpoint
{
    public required string RepairRunId { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }

    public WorkspacePlanRuntimeStateFingerprint StateAfter { get; init; } = new();
}

public sealed record WorkspacePlanRepairAuditCoverageView
{
    public bool HasDetailedRuns { get; init; }

    public bool HasCompactedPrefix { get; init; }

    public long? OldestDetailedSequenceNumber { get; init; }

    public long? NewestDetailedSequenceNumber { get; init; }

    public long CompactedThroughSequenceNumber { get; init; }

    public string? CompactedHeadRunHash { get; init; }

    public string? HeadRunHash { get; init; }

    public required string Summary { get; init; }
}

public sealed record WorkspacePlanRepairAuditIntegrityView
{
    public bool IntegrityOk { get; init; }

    public bool ChainIntact { get; init; }

    public bool LatestAppliedStateConsistent { get; init; }

    public long LatestSequenceNumber { get; init; }

    public string? HeadRunHash { get; init; }

    public string? CompactedHeadRunHash { get; init; }

    public WorkspacePlanRuntimeStateFingerprint CurrentState { get; init; } = new();

    public WorkspacePlanRepairAuditCheckpoint? LatestAppliedCheckpoint { get; init; }

    public WorkspacePlanRepairAuditCoverageView Coverage { get; init; } = new()
    {
        Summary = "No workspace-plan repair audit coverage is recorded yet."
    };

    public IReadOnlyList<string> BoundaryStatements { get; init; } = Array.Empty<string>();

    public IReadOnlyList<WorkspacePlanRepairAuditIntegrityIssue> Issues { get; init; } = Array.Empty<WorkspacePlanRepairAuditIntegrityIssue>();
}

public sealed record WorkspacePlanRepairAuditRun
{
    public long SequenceNumber { get; init; }

    public required string RepairRunId { get; init; }

    public required WorkspacePlanRepairOperationKind OperationKind { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }

    public string? PreviousRunHash { get; init; }

    public string? PayloadHash { get; init; }

    public string? RunHash { get; init; }

    public string? RequestedBy { get; init; }

    public int IssueCount { get; init; }

    public int RepairableIssueCount { get; init; }

    public int ManualReviewCount { get; init; }

    public int AcceptedManualMappingCount { get; init; }

    public int AppliedRepairCount { get; init; }

    public WorkspacePlanRuntimeStateFingerprint StateBefore { get; init; } = new();

    public WorkspacePlanRuntimeStateFingerprint StateAfter { get; init; } = new();

    public IReadOnlyList<WorkspacePlanRepairAuditEntry> Entries { get; init; } = Array.Empty<WorkspacePlanRepairAuditEntry>();
}

public sealed record WorkspacePlanRepairHistoryView
{
    public required DateTimeOffset CheckedAtUtc { get; init; }

    public string? PlanId { get; init; }

    public string? RepairRunId { get; init; }

    public int RunCount { get; init; }

    public int EntryCount { get; init; }

    public int CompactionCount { get; init; }

    public WorkspacePlanRepairAuditRetentionView Retention { get; init; } = new();

    public WorkspacePlanRepairAuditIntegrityView Integrity { get; init; } = new();

    public IReadOnlyList<WorkspacePlanRepairAuditCompactionSummary> Compactions { get; init; } = Array.Empty<WorkspacePlanRepairAuditCompactionSummary>();

    public IReadOnlyList<WorkspacePlanRepairAuditRun> Runs { get; init; } = Array.Empty<WorkspacePlanRepairAuditRun>();
}

public sealed record WorkspacePlanRepairAuditExportView
{
    public required DateTimeOffset ExportedAtUtc { get; init; }

    public string? PlanId { get; init; }

    public string? RepairRunId { get; init; }

    public int RunCount { get; init; }

    public int EntryCount { get; init; }

    public int CompactionCount { get; init; }

    public WorkspacePlanRepairAuditRetentionView Retention { get; init; } = new();

    public WorkspacePlanRepairAuditIntegrityView Integrity { get; init; } = new();

    public IReadOnlyList<WorkspacePlanRepairAuditCompactionSummary> Compactions { get; init; } = Array.Empty<WorkspacePlanRepairAuditCompactionSummary>();

    public IReadOnlyList<WorkspacePlanRepairAuditRun> Runs { get; init; } = Array.Empty<WorkspacePlanRepairAuditRun>();

    public WorkspacePlanRepairAuditExportManifest Manifest { get; init; } = new()
    {
        SchemaVersion = "workspace-plan-repair-audit-export/v1",
        ExportedAtUtc = DateTimeOffset.UnixEpoch,
        ScopeKind = WorkspacePlanRepairAuditExportScopeKind.RetainedLedger,
        ScopeSummary = "No workspace-plan repair audit export scope has been recorded.",
        BoundarySummary = "No workspace-plan repair audit export boundary has been recorded."
    };

    public WorkspacePlanRepairAuditDeliveryContract DeliveryContract { get; init; } = new()
    {
        SchemaVersion = "workspace-plan-repair-audit-delivery-contract/v1",
        PromotionSummary = "No workspace-plan repair audit delivery contract has been recorded.",
        PublishBoundarySummary = "No workspace-plan repair audit publish boundary has been recorded.",
        DeliveryArtifactSummary = "No workspace-plan repair audit delivery artifact guidance is recorded."
    };

    public WorkspacePlanRepairAuditExportSeal Seal { get; init; } = new()
    {
        SealedAtUtc = DateTimeOffset.UnixEpoch,
        ManifestHash = string.Empty,
        PayloadHash = string.Empty,
        Summary = "No workspace-plan repair audit seal has been recorded."
    };
}

public sealed record WorkspacePlanRepairAuditExportManifest
{
    public required string SchemaVersion { get; init; }

    public required DateTimeOffset ExportedAtUtc { get; init; }

    public WorkspacePlanRepairAuditExportScopeKind ScopeKind { get; init; } = WorkspacePlanRepairAuditExportScopeKind.RetainedLedger;

    public bool ScopeFiltered { get; init; }

    public bool ChainContinuityPreserved { get; init; }

    public bool DetailedRunLimitApplied { get; init; }

    public bool EntryLimitApplied { get; init; }

    public string? PlanId { get; init; }

    public string? RepairRunId { get; init; }

    public int DetailedRunCount { get; init; }

    public int DetailedEntryCount { get; init; }

    public int CompactionCount { get; init; }

    public WorkspacePlanRepairAuditRetentionView Retention { get; init; } = new();

    public WorkspacePlanRepairAuditCoverageView Coverage { get; init; } = new()
    {
        Summary = "No workspace-plan repair audit coverage is recorded yet."
    };

    public required string ScopeSummary { get; init; }

    public required string BoundarySummary { get; init; }
}

public sealed record WorkspacePlanRepairAuditDeliveryContract
{
    public required string SchemaVersion { get; init; }

    public WorkspacePlanRepairAuditPromotionStageKind CurrentStage { get; init; } = WorkspacePlanRepairAuditPromotionStageKind.DetachedDigestBundle;

    public WorkspacePlanRepairAuditPromotionStageKind RecommendedNextStage { get; init; } = WorkspacePlanRepairAuditPromotionStageKind.SealedBundle;

    public bool ExternalVerificationRequiredForPromotion { get; init; } = true;

    public bool ExternalVerificationRequiredForPublish { get; init; } = true;

    public bool WitnessReceiptRecommended { get; init; } = true;

    public bool IndependentWitnessPreferred { get; init; } = true;

    public required string PromotionSummary { get; init; }

    public required string PublishBoundarySummary { get; init; }

    public required string DeliveryArtifactSummary { get; init; }
}

public sealed record WorkspacePlanRepairAuditExportSeal
{
    public WorkspacePlanRepairAuditSealKind SealKind { get; init; } = WorkspacePlanRepairAuditSealKind.DigestOnly;

    public bool IsSealed { get; init; }

    public required DateTimeOffset SealedAtUtc { get; init; }

    public string? KeyId { get; init; }

    public required string ManifestHash { get; init; }

    public required string PayloadHash { get; init; }

    public string? Signature { get; init; }

    public string? PublicKeyPem { get; init; }

    public string? PublicKeyFingerprint { get; init; }

    public string? CanonicalManifestJson { get; init; }

    public string? CanonicalPayloadJson { get; init; }

    public WorkspacePlanRepairAuditTrustAnchor TrustAnchor { get; init; } = new()
    {
        CanonicalizationKind = "workspace-plan-repair-audit-bundle-json/v1",
        Summary = "No workspace-plan repair audit trust anchor is recorded.",
        ExternalVerificationSummary = "No external verification guidance is recorded."
    };

    public required string Summary { get; init; }
}

public sealed record WorkspacePlanRepairAuditTrustAnchor
{
    public WorkspacePlanRepairAuditTrustAnchorKind AnchorKind { get; init; } = WorkspacePlanRepairAuditTrustAnchorKind.DetachedDigest;

    public required string CanonicalizationKind { get; init; }

    public string? KeyId { get; init; }

    public string? PublicKeyFingerprint { get; init; }

    public bool PublicKeyEmbedded { get; init; }

    public bool RequiresExternalPinning { get; init; } = true;

    public WorkspacePlanRepairAuditWitnessStrategy WitnessStrategy { get; init; } = new()
    {
        StrategyKind = WorkspacePlanRepairAuditWitnessStrategyKind.None,
        OperatorBoundarySummary = "No workspace-plan repair audit witness strategy is recorded.",
        WitnessBoundarySummary = "No independent witness guidance is recorded.",
        VerificationSummary = "Verification currently relies on the trust anchor only."
    };

    public required string Summary { get; init; }

    public required string ExternalVerificationSummary { get; init; }
}

public sealed record WorkspacePlanRepairAuditWitnessStrategy
{
    public WorkspacePlanRepairAuditWitnessStrategyKind StrategyKind { get; init; } = WorkspacePlanRepairAuditWitnessStrategyKind.None;

    public string? ReceiptSchemaVersion { get; init; }

    public bool SupportsIndependentWitness { get; init; }

    public bool RequiresSeparateWitnessIdentity { get; init; } = true;

    public required string OperatorBoundarySummary { get; init; }

    public required string WitnessBoundarySummary { get; init; }

    public required string VerificationSummary { get; init; }
}

public sealed record WorkspacePlanRepairAuditVerificationView
{
    public required DateTimeOffset VerifiedAtUtc { get; init; }

    public bool IntegrityOk { get; init; }

    public WorkspacePlanRepairAuditRetentionView Retention { get; init; } = new();

    public WorkspacePlanRepairAuditIntegrityView Integrity { get; init; } = new();
}

public interface IWorkspacePlanStateMigrationService
{
    UseCaseResult<WorkspacePlanStateReport> Report(ReportWorkspacePlanStateRequest request);

    UseCaseResult<WorkspacePlanStateReport> Preview(PreviewWorkspacePlanStateRepairRequest request);

    UseCaseResult<WorkspacePlanStateReport> Repair(RepairWorkspacePlanStateRequest request);

    UseCaseResult<WorkspacePlanRepairHistoryView> GetHistory(GetWorkspacePlanRepairHistoryRequest request);

    UseCaseResult<WorkspacePlanRepairAuditExportView> ExportAudit(ExportWorkspacePlanRepairAuditRequest request);

    UseCaseResult<WorkspacePlanRepairAuditVerificationView> VerifyAudit(VerifyWorkspacePlanRepairAuditRequest request);
}

using EdenOS.Contracts.Runtime;
using EdenOS.Contracts.Application;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class ReportWorkspacePlanStateUseCase(IWorkspacePlanStateMigrationService migrationService)
    : IQueryUseCase<ReportWorkspacePlanStateRequest, WorkspacePlanStateReport>
{
    public Task<UseCaseResult<WorkspacePlanStateReport>> ExecuteAsync(
        ReportWorkspacePlanStateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(migrationService.Report(request));
    }
}

public sealed class PreviewWorkspacePlanStateRepairUseCase(IWorkspacePlanStateMigrationService migrationService)
    : IQueryUseCase<PreviewWorkspacePlanStateRepairRequest, WorkspacePlanStateReport>
{
    public Task<UseCaseResult<WorkspacePlanStateReport>> ExecuteAsync(
        PreviewWorkspacePlanStateRepairRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(migrationService.Preview(request));
    }
}

public sealed class RepairWorkspacePlanStateUseCase(IWorkspacePlanStateMigrationService migrationService)
    : ICommandUseCase<RepairWorkspacePlanStateRequest, WorkspacePlanStateReport>
{
    public Task<UseCaseResult<WorkspacePlanStateReport>> ExecuteAsync(
        RepairWorkspacePlanStateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(migrationService.Repair(request));
    }
}

public sealed class GetWorkspacePlanRepairHistoryUseCase(IWorkspacePlanStateMigrationService migrationService)
    : IQueryUseCase<GetWorkspacePlanRepairHistoryRequest, WorkspacePlanRepairHistoryView>
{
    public Task<UseCaseResult<WorkspacePlanRepairHistoryView>> ExecuteAsync(
        GetWorkspacePlanRepairHistoryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(migrationService.GetHistory(request));
    }
}

public sealed class ExportWorkspacePlanRepairAuditUseCase(IWorkspacePlanStateMigrationService migrationService)
    : IQueryUseCase<ExportWorkspacePlanRepairAuditRequest, WorkspacePlanRepairAuditExportView>
{
    public Task<UseCaseResult<WorkspacePlanRepairAuditExportView>> ExecuteAsync(
        ExportWorkspacePlanRepairAuditRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(migrationService.ExportAudit(request));
    }
}

public sealed class VerifyWorkspacePlanRepairAuditUseCase(IWorkspacePlanStateMigrationService migrationService)
    : IQueryUseCase<VerifyWorkspacePlanRepairAuditRequest, WorkspacePlanRepairAuditVerificationView>
{
    public Task<UseCaseResult<WorkspacePlanRepairAuditVerificationView>> ExecuteAsync(
        VerifyWorkspacePlanRepairAuditRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(migrationService.VerifyAudit(request));
    }
}

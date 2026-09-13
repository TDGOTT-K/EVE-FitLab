using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Execution;

public interface IExecutionTrackingService
{
    UseCaseResult<ExecutionNextActionsView> GetNextActions(GetNextActionsRequest request);

    UseCaseResult<ExecutionState> MarkStepDone(MarkStepDoneRequest request);

    UseCaseResult<ExecutionState> RecordMaterialArrival(RecordMaterialArrivalRequest request);

    UseCaseResult<ExecutionState> RecordMaterialLoss(RecordMaterialLossRequest request);

    UseCaseResult<ExecutionState> RecordMarketChange(RecordMarketChangeRequest request);

    UseCaseResult<ExecutionState> RecordIndustryCostChange(RecordIndustryCostChangeRequest request);

    UseCaseResult<ExecutionState> RecordLocationChange(RecordLocationChangeRequest request);

    UseCaseResult<ExecutionState> RecordManualOverride(RecordManualOverrideRequest request);

    UseCaseResult<ExecutionState> ResolveManualOverride(ResolveManualOverrideRequest request);

    UseCaseResult<ExecutionBlockerView> GetBlockers(GetBlockersRequest request);

    UseCaseResult<ExecutionReplanResult> Replan(ExecutionReplanRequest request);
}

using EdenOS.Contracts.Application;
using EdenOS.Contracts.Fitting;
using EdenOS.Contracts.UseCases;

namespace EdenOS.Application.UseCases;

public sealed class FitCreateOrUpdateUseCase(IFitService fitService)
    : ICommandUseCase<CreateOrUpdateFitRequest, FitSnapshot>
{
    public Task<UseCaseResult<FitSnapshot>> ExecuteAsync(CreateOrUpdateFitRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fitService.CreateOrUpdate(request));
    }
}

public sealed class FitValidateUseCase(IFitService fitService)
    : IQueryUseCase<ValidateFitRequest, FitValidationResult>
{
    public Task<UseCaseResult<FitValidationResult>> ExecuteAsync(ValidateFitRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fitService.Validate(request));
    }
}

public sealed class FitGetAttributesUseCase(IFitService fitService)
    : IQueryUseCase<GetFitAttributesRequest, FitAttributeView>
{
    public Task<UseCaseResult<FitAttributeView>> ExecuteAsync(GetFitAttributesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fitService.GetAttributes(request));
    }
}

public sealed class FitImportTextUseCase(IFitService fitService)
    : ICommandUseCase<ImportFitTextRequest, ImportedFitTextView>
{
    public Task<UseCaseResult<ImportedFitTextView>> ExecuteAsync(ImportFitTextRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fitService.ImportText(request));
    }
}

public sealed class FitExportTextUseCase(IFitService fitService)
    : IQueryUseCase<ExportFitTextRequest, ExportedFitTextView>
{
    public Task<UseCaseResult<ExportedFitTextView>> ExecuteAsync(ExportFitTextRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fitService.ExportText(request));
    }
}

public sealed class FitSelectAmmoUseCase(IFitService fitService)
    : ICommandUseCase<SelectFitAmmoRequest, FitAmmoSelectionResult>
{
    public Task<UseCaseResult<FitAmmoSelectionResult>> ExecuteAsync(SelectFitAmmoRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(fitService.SelectAmmo(request));
    }
}

public sealed class FitEnvironmentSaveProfileUseCase(IFitEnvironmentProfileService profileService)
    : ICommandUseCase<SaveFitEnvironmentProfileRequest, FitEnvironmentProfile>
{
    public Task<UseCaseResult<FitEnvironmentProfile>> ExecuteAsync(SaveFitEnvironmentProfileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(profileService.SaveProfile(request));
    }
}

public sealed class FitEnvironmentGetProfileUseCase(IFitEnvironmentProfileService profileService)
    : IQueryUseCase<GetFitEnvironmentProfileRequest, FitEnvironmentProfile>
{
    public Task<UseCaseResult<FitEnvironmentProfile>> ExecuteAsync(GetFitEnvironmentProfileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(profileService.GetProfile(request));
    }
}

public sealed class FitEnvironmentListProfilesUseCase(IFitEnvironmentProfileService profileService)
    : IQueryUseCase<ListFitEnvironmentProfilesRequest, FitEnvironmentProfileCatalogView>
{
    public Task<UseCaseResult<FitEnvironmentProfileCatalogView>> ExecuteAsync(ListFitEnvironmentProfilesRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(profileService.ListProfiles(request));
    }
}

public sealed class FitEnvironmentDeleteProfileUseCase(IFitEnvironmentProfileService profileService)
    : ICommandUseCase<DeleteFitEnvironmentProfileRequest, FitEnvironmentProfileDeleteResult>
{
    public Task<UseCaseResult<FitEnvironmentProfileDeleteResult>> ExecuteAsync(DeleteFitEnvironmentProfileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(profileService.DeleteProfile(request));
    }
}

public sealed class FitEnvironmentApplyProfileUseCase(IFitEnvironmentProfileService profileService)
    : ICommandUseCase<ApplyFitEnvironmentProfileRequest, FitEnvironmentProfileApplyResult>
{
    public Task<UseCaseResult<FitEnvironmentProfileApplyResult>> ExecuteAsync(ApplyFitEnvironmentProfileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(profileService.ApplyProfile(request));
    }
}

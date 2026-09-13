using EdenOS.Contracts.Fitting;
using EdenOS.Contracts.UseCases;
using EdenOS.Application.Fitting.Dogma.DataSources;
using EdenOS.Application.Fitting.Dogma;
using EdenOS.Application.Fitting.Internal;

namespace EdenOS.Application.Fitting;

public sealed class InMemoryFitService : IFitService
{
    private readonly FitComputationEngine computationEngine;
    private readonly IDogmaDataSource? dogmaDataSource;
    private readonly DogmaFitAdapter? dogmaFitAdapter;
    private readonly FitAmmoAdvisor? ammoAdvisor;

    public InMemoryFitService(
        TimeProvider timeProvider,
        string? dogmaSdeRootPath = null,
        string? dogmaDataRootPath = null,
        string? dogmaEffectsOverlayJsonPath = null,
        string? dogmaRuleSetJsonPath = null)
    {
        computationEngine = new FitComputationEngine(timeProvider);
        if (!string.IsNullOrWhiteSpace(dogmaSdeRootPath) && !string.IsNullOrWhiteSpace(dogmaDataRootPath))
        {
            dogmaDataSource = new JsonlFileDogmaDataSource(dogmaSdeRootPath, dogmaDataRootPath, dogmaEffectsOverlayJsonPath, dogmaRuleSetJsonPath);
            dogmaFitAdapter = new DogmaFitAdapter(dogmaDataSource);
            ammoAdvisor = new FitAmmoAdvisor(dogmaDataSource);
        }
    }

    public InMemoryFitService(
        TimeProvider timeProvider,
        IDogmaDataSource dogmaDataSource)
    {
        computationEngine = new FitComputationEngine(timeProvider);
        this.dogmaDataSource = dogmaDataSource;
        dogmaFitAdapter = new DogmaFitAdapter(dogmaDataSource);
        ammoAdvisor = new FitAmmoAdvisor(dogmaDataSource);
    }

    public bool UsesCharges(int moduleTypeId) => ammoAdvisor?.UsesCharges(moduleTypeId) == true;
    public bool IsAmmoCompatible(int moduleTypeId, int chargeTypeId) => ammoAdvisor?.IsCompatible(new FittedModule
    {
        TypeId = "type:" + moduleTypeId, DogmaTypeId = moduleTypeId, Name = "", SlotId = "preview", SlotKind = ModuleSlotKind.High
    }, chargeTypeId) == true;

    public UseCaseResult<FitSnapshot> CreateOrUpdate(CreateOrUpdateFitRequest request)
    {
        var traceId = CreateTraceId("fit.create_or_update");
        var shapeErrors = FitComputationEngine.ValidateSnapshotShape(request.Snapshot);
        if (shapeErrors.Count > 0)
        {
            return UseCaseResult<FitSnapshot>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit snapshot is missing required hull metadata.",
                traceId,
                shapeErrors);
        }

        var normalized = Compute(request.Snapshot, simulationMode: request.SimulationMode).Snapshot;
        return UseCaseResult<FitSnapshot>.Success(
            normalized,
            $"Prepared fit '{normalized.FitId}' for downstream validation and combat simulation.",
            traceId);
    }

    public UseCaseResult<FitValidationResult> Validate(ValidateFitRequest request)
    {
        var traceId = CreateTraceId("fit.validate");
        var shapeErrors = FitComputationEngine.ValidateSnapshotShape(request.Snapshot);
        if (shapeErrors.Count > 0)
        {
            return UseCaseResult<FitValidationResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit snapshot is missing required hull metadata.",
                traceId,
                shapeErrors);
        }

        var analysis = Compute(request.Snapshot, simulationMode: request.SimulationMode);
        var result = new FitValidationResult
        {
            FitId = analysis.Snapshot.FitId,
            IsValid = analysis.Issues.Count == 0,
            Snapshot = analysis.Snapshot,
            Attributes = analysis.Attributes,
            Issues = analysis.Issues,
            Warnings = analysis.Warnings
        };

        return UseCaseResult<FitValidationResult>.Success(
            result,
            result.IsValid
                ? $"Fit '{result.FitId}' passed slot and resource validation."
                : $"Fit '{result.FitId}' has validation issues.",
            traceId,
            result.Warnings);
    }

    public UseCaseResult<FitAttributeView> GetAttributes(GetFitAttributesRequest request)
    {
        var traceId = CreateTraceId("fit.get_attributes");
        var shapeErrors = FitComputationEngine.ValidateSnapshotShape(request.Snapshot);
        if (shapeErrors.Count > 0)
        {
            return UseCaseResult<FitAttributeView>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit snapshot is missing required hull metadata.",
                traceId,
                shapeErrors);
        }

        var analysis = Compute(request.Snapshot, simulationMode: request.SimulationMode);
        var warnings = analysis.Warnings.ToList();
        if (analysis.Issues.Count > 0)
        {
            warnings.AddRange(analysis.Issues.Select(issue => $"validation:{issue.Code}:{issue.Message}"));
        }

        return UseCaseResult<FitAttributeView>.Success(
            analysis.Attributes,
            analysis.Issues.Count == 0
                ? $"Resolved attributes for fit '{analysis.Snapshot.FitId}'."
                : $"Resolved attributes for fit '{analysis.Snapshot.FitId}', but the fit has validation issues.",
            traceId,
            warnings);
    }

    public UseCaseResult<ImportedFitTextView> ImportText(ImportFitTextRequest request)
    {
        var traceId = CreateTraceId("fit.import_text");
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return UseCaseResult<ImportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit text is required.",
                traceId,
                ["text is required."]);
        }

        if (request.Format != FitTextFormat.Eft)
        {
            return UseCaseResult<ImportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                $"Unsupported fit text format '{request.Format}'.",
                traceId,
                [$"format '{request.Format}' is not supported."]);
        }

        if (dogmaDataSource is null)
        {
            return UseCaseResult<ImportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                "Importing EFT fit text requires a configured dogma data source for type-name resolution.",
                traceId,
                ["runtime dogma data source is not configured."]);
        }

        try
        {
            var requestedFitId = string.IsNullOrWhiteSpace(request.FitId) ? $"fit-{Guid.NewGuid():N}" : request.FitId.Trim();
            var imported = FitTextCodec.Import(requestedFitId, request.Text, dogmaDataSource, request.Locale);
            var analysis = Compute(imported.Snapshot, request.Locale);
            var normalized = analysis.Snapshot;
            return UseCaseResult<ImportedFitTextView>.Success(
                new ImportedFitTextView
                {
                    Format = request.Format,
                    Snapshot = normalized,
                    Locale = DogmaLocalization.NormalizeLocale(request.Locale),
                    AmmoSelectionPrompts = analysis.Attributes.AmmoSelectionPrompts
                },
                $"Imported {request.Format} fit text into snapshot '{normalized.FitId}'.",
                traceId,
                imported.Warnings.Concat(analysis.Warnings).ToArray());
        }
        catch (Exception ex)
        {
            return UseCaseResult<ImportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                "Failed to import fit text.",
                traceId,
                [ex.Message]);
        }
    }

    public UseCaseResult<FitAmmoSelectionResult> SelectAmmo(SelectFitAmmoRequest request)
    {
        var traceId = CreateTraceId("fit.select_ammo");
        var shapeErrors = FitComputationEngine.ValidateSnapshotShape(request.Snapshot);
        if (shapeErrors.Count > 0)
        {
            return UseCaseResult<FitAmmoSelectionResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit snapshot is missing required hull metadata.",
                traceId,
                shapeErrors);
        }

        if (ammoAdvisor is null)
        {
            return UseCaseResult<FitAmmoSelectionResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Selecting ammo requires a configured dogma data source.",
                traceId,
                ["runtime dogma data source is not configured."]);
        }

        if (request.ChargeDogmaTypeId is null or <= 0 &&
            string.IsNullOrWhiteSpace(request.ChargeTypeId) &&
            string.IsNullOrWhiteSpace(request.ChargeName))
        {
            return UseCaseResult<FitAmmoSelectionResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Selecting ammo requires charge_dogma_type_id, charge_type_id, or charge_name.",
                traceId,
                ["one of charge_dogma_type_id, charge_type_id, or charge_name is required."]);
        }

        try
        {
            var selection = ammoAdvisor.ApplySelection(request.Snapshot, request);
            var analysis = Compute(selection.Snapshot, request.Locale);
            var result = new FitAmmoSelectionResult
            {
                Snapshot = analysis.Snapshot,
                SelectedChargeTypeId = selection.SelectedCharge.TypeId,
                SelectedChargeName = selection.SelectedCharge.Name,
                SelectedChargeDogmaTypeId = selection.SelectedCharge.DogmaTypeId,
                UpdatedSlotIds = selection.UpdatedSlotIds,
                Locale = DogmaLocalization.NormalizeLocale(request.Locale),
                RemainingAmmoSelectionPrompts = analysis.Attributes.AmmoSelectionPrompts
            };

            return UseCaseResult<FitAmmoSelectionResult>.Success(
                result,
                $"Applied charge '{result.SelectedChargeName}' to {result.UpdatedSlotIds.Count} compatible slot(s).",
                traceId,
                analysis.Warnings);
        }
        catch (Exception ex)
        {
            return UseCaseResult<FitAmmoSelectionResult>.Failure(
                UseCaseStatus.InvalidInput,
                "Failed to select ammo.",
                traceId,
                [ex.Message]);
        }
    }

    public UseCaseResult<ExportedFitTextView> ExportText(ExportFitTextRequest request)
    {
        var traceId = CreateTraceId("fit.export_text");
        var shapeErrors = FitComputationEngine.ValidateSnapshotShape(request.Snapshot);
        if (shapeErrors.Count > 0)
        {
            return UseCaseResult<ExportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                "Fit snapshot is missing required hull metadata.",
                traceId,
                shapeErrors);
        }

        if (request.Format != FitTextFormat.Eft)
        {
            return UseCaseResult<ExportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                $"Unsupported fit text format '{request.Format}'.",
                traceId,
                [$"format '{request.Format}' is not supported."]);
        }

        try
        {
            var analysis = Compute(request.Snapshot, request.Locale);
            var exported = FitTextCodec.Export(analysis.Snapshot, analysis.Attributes, dogmaDataSource, request.IncludeEmptySlots, request.Locale);
            return UseCaseResult<ExportedFitTextView>.Success(
                new ExportedFitTextView
                {
                    FitId = analysis.Snapshot.FitId,
                    Format = request.Format,
                    Text = exported.Text,
                    Locale = DogmaLocalization.NormalizeLocale(request.Locale)
                },
                $"Exported fit '{analysis.Snapshot.FitId}' as {request.Format} text.",
                traceId,
                analysis.Warnings.Concat(exported.Warnings).ToArray());
        }
        catch (Exception ex)
        {
            return UseCaseResult<ExportedFitTextView>.Failure(
                UseCaseStatus.InvalidInput,
                "Failed to export fit text.",
                traceId,
                [ex.Message]);
        }
    }

    private FitAnalysisResult Compute(
        FitSnapshot snapshot,
        string? locale = null,
        FitSimulationMode simulationMode = FitSimulationMode.SingleShip)
    {
        if (dogmaFitAdapter is not null && dogmaFitAdapter.CanHandle(snapshot))
        {
            var analysis = dogmaFitAdapter.Compute(snapshot, simulationMode: simulationMode);
            var prompts = ammoAdvisor?.DescribePrompts(analysis.Snapshot, locale) ?? Array.Empty<FitAmmoSelectionPrompt>();
            return new FitAnalysisResult(
                analysis.Snapshot,
                analysis.Attributes with
                {
                    AmmoSelectionPrompts = prompts
                },
                analysis.Issues,
                analysis.Warnings.Concat(prompts.Select(prompt => $"ammo_selection_required:{prompt.SlotId}:{prompt.Message}")).ToArray());
        }

        var legacy = computationEngine.Compute(snapshot);
        return new FitAnalysisResult(legacy.Snapshot, legacy.Attributes, legacy.Issues, legacy.Warnings);
    }

    private static string CreateTraceId(string operation) => $"{operation}:{Guid.NewGuid():N}";

    private sealed record FitAnalysisResult(
        FitSnapshot Snapshot,
        FitAttributeView Attributes,
        IReadOnlyList<FitValidationIssue> Issues,
        IReadOnlyList<string> Warnings);
}

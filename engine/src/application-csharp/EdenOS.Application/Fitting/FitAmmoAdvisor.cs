using EdenOS.Application.Fitting.Dogma.DataSources;
using EdenOS.Application.Fitting.Dogma.Engine;
using EdenOS.Contracts.Fitting;

namespace EdenOS.Application.Fitting;

internal sealed class FitAmmoAdvisor
{
    private static readonly HashSet<string> MissileEffectNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "useMissiles",
        "useMissilesFoe",
        "missileLaunching",
        "missileLaunchingForEntity",
        "dotMissileLaunching",
        "fofMissileLaunching",
        "defenderMissileLaunching"
    };

    private readonly IDogmaDataSource dataSource;
    private readonly DogmaContext dogmaContext;

    public FitAmmoAdvisor(IDogmaDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
        dogmaContext = DogmaContext.GetOrCreate(dataSource);
    }

    public IReadOnlyList<FitAmmoSelectionPrompt> DescribePrompts(FitSnapshot snapshot, string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalizedLocale = DogmaLocalization.NormalizeLocale(locale);
        var prompts = new List<FitAmmoSelectionPrompt>();
        foreach (var module in snapshot.Modules)
        {
            if (module.Charge is not null)
            {
                continue;
            }

            var options = GetCompatibleCargoOptions(snapshot, module, normalizedLocale);
            if (options.Count == 0)
            {
                continue;
            }

            var localizedModuleName = DogmaLocalization.ResolveLocalizedTypeName(dataSource, module.DogmaTypeId, module.Name, normalizedLocale);
            var optionNames = string.Join(", ", options.Select(option => option.Name));
            prompts.Add(new FitAmmoSelectionPrompt
            {
                SlotId = module.SlotId,
                ModuleTypeId = module.TypeId,
                ModuleName = localizedModuleName,
                Message = $"Module '{localizedModuleName}' in slot '{module.SlotId}' has no charge selected. Compatible cargo charges: {optionNames}.",
                Options = options
            });
        }

        return prompts;
    }

    public FitAmmoSelectionExecution ApplySelection(FitSnapshot snapshot, SelectFitAmmoRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        var selectedOption = ResolveSelectedCharge(snapshot, request);
        if (selectedOption.DogmaTypeId is null or <= 0)
        {
            throw new InvalidOperationException("Selected charge must resolve to a dogma type.");
        }

        var requestedSlotIds = request.SlotIds
            .Where(slotId => !string.IsNullOrWhiteSpace(slotId))
            .Select(slotId => slotId.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var compatibleSlots = snapshot.Modules
            .Where(module => IsCompatible(module, selectedOption.DogmaTypeId.Value))
            .ToList();

        if (requestedSlotIds.Count > 0)
        {
            compatibleSlots = compatibleSlots
                .Where(module => requestedSlotIds.Contains(module.SlotId))
                .ToList();
        }
        else if (!request.ApplyToAllCompatibleSlots)
        {
            compatibleSlots = compatibleSlots
                .Where(module => module.Charge is null)
                .Take(1)
                .ToList();
        }

        if (compatibleSlots.Count == 0)
        {
            throw new InvalidOperationException("No compatible module slots were found for the selected charge.");
        }

        var updatedSlotIds = compatibleSlots
            .Select(module => module.SlotId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(slotId => slotId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var updatedSlotIdSet = updatedSlotIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updatedModules = snapshot.Modules
            .Select(module => updatedSlotIdSet.Contains(module.SlotId)
                ? module with
                {
                    Charge = new Charge
                    {
                        ChargeId = selectedOption.TypeId,
                        Name = selectedOption.Name,
                        DogmaTypeId = selectedOption.DogmaTypeId
                    }
                }
                : module)
            .ToArray();

        var updatedSnapshot = snapshot with
        {
            Modules = updatedModules
        };

        return new FitAmmoSelectionExecution(
            updatedSnapshot,
            selectedOption,
            updatedSlotIds,
            DescribePrompts(updatedSnapshot, request.Locale));
    }

    private FitAmmoOption ResolveSelectedCharge(FitSnapshot snapshot, SelectFitAmmoRequest request)
    {
        var cargoOptions = snapshot.Cargo
            .Where(entry => entry.Quantity > 0 && entry.DogmaTypeId is > 0)
            .GroupBy(
                entry => entry.DogmaTypeId!.Value,
                entry => entry,
                (dogmaTypeId, entries) =>
                {
                    var first = entries.First();
                    return new FitAmmoOption
                    {
                        TypeId = first.TypeId,
                        Name = DogmaLocalization.ResolveLocalizedTypeName(dataSource, dogmaTypeId, first.Name, request.Locale),
                        DogmaTypeId = dogmaTypeId,
                        CargoQuantity = entries.Sum(entry => entry.Quantity)
                    };
                })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FitAmmoOption? selected = null;
        if (request.ChargeDogmaTypeId is > 0)
        {
            selected = cargoOptions.FirstOrDefault(option => option.DogmaTypeId == request.ChargeDogmaTypeId);
        }
        else if (!string.IsNullOrWhiteSpace(request.ChargeTypeId))
        {
            selected = cargoOptions.FirstOrDefault(option => string.Equals(option.TypeId, request.ChargeTypeId.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(request.ChargeName))
        {
            var requestedName = request.ChargeName.Trim();
            selected = cargoOptions.FirstOrDefault(option => OptionMatchesRequestedName(option, requestedName, request.Locale));
        }

        if (selected is not null)
        {
            return selected;
        }

        if (cargoOptions.Count == 0)
        {
            throw new InvalidOperationException("Fit cargo does not contain any resolvable charge entries.");
        }

        throw new InvalidOperationException(
            "Requested charge was not found in cargo. Available charges: "
            + string.Join(", ", cargoOptions.Select(option => option.Name)));
    }

    private IReadOnlyList<FitAmmoOption> GetCompatibleCargoOptions(FitSnapshot snapshot, FittedModule module, string? locale)
    {
        if (module.DogmaTypeId is null or <= 0)
        {
            return Array.Empty<FitAmmoOption>();
        }

        return snapshot.Cargo
            .Where(entry => entry.Quantity > 0 && entry.DogmaTypeId is > 0 && IsCompatible(module, entry.DogmaTypeId.Value))
            .GroupBy(
                entry => entry.DogmaTypeId!.Value,
                entry => entry,
                (dogmaTypeId, entries) =>
                {
                    var first = entries.First();
                    return new FitAmmoOption
                    {
                        TypeId = first.TypeId,
                        Name = DogmaLocalization.ResolveLocalizedTypeName(dataSource, dogmaTypeId, first.Name, locale),
                        DogmaTypeId = dogmaTypeId,
                        CargoQuantity = entries.Sum(entry => entry.Quantity)
                    };
                })
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool OptionMatchesRequestedName(FitAmmoOption option, string requestedName, string? locale)
    {
        if (string.Equals(option.Name, requestedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var candidate in DogmaLocalization.GetCandidateTypeNames(dataSource, option.DogmaTypeId, option.Name, locale))
        {
            if (string.Equals(candidate, requestedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool UsesCharges(int moduleTypeId) => GetChargeGroupIds(moduleTypeId).Count > 0 || HasAnyEffectName(moduleTypeId, MissileEffectNames);

    public bool IsCompatible(FittedModule module, int chargeDogmaTypeId)
    {
        if (module.DogmaTypeId is null or <= 0 || chargeDogmaTypeId <= 0)
        {
            return false;
        }

        if (dogmaContext.GetTypeCategoryId(chargeDogmaTypeId) != 8) return false;
        var moduleSize = dogmaContext.GetTypeAttributeValue(module.DogmaTypeId.Value, "chargeSize", 0d);
        var chargeSize = dogmaContext.GetTypeAttributeValue(chargeDogmaTypeId, "chargeSize", 0d);
        if (moduleSize > 0 && chargeSize > 0 && moduleSize != chargeSize) return false;
        var chargeGroupIds = GetChargeGroupIds(module.DogmaTypeId.Value);
        var chargeTypeGroupId = dogmaContext.GetTypeGroupId(chargeDogmaTypeId);
        // Explicit module charge groups are authoritative. A reverse launcherGroup
        // on an unrelated charge must not override the module's allowed families.
        if (chargeGroupIds.Count > 0)
        {
            return chargeTypeGroupId > 0 && chargeGroupIds.Contains(chargeTypeGroupId);
        }

        var launcherGroupIds = GetLauncherGroupIds(chargeDogmaTypeId);
        if (launcherGroupIds.Count > 0 &&
            launcherGroupIds.Contains(dogmaContext.GetTypeGroupId(module.DogmaTypeId.Value)))
        {
            return true;
        }

        if (chargeGroupIds.Count > 0 || launcherGroupIds.Count > 0)
        {
            return false;
        }

        if (!HasAnyEffectName(module.DogmaTypeId.Value, MissileEffectNames))
        {
            return false;
        }

        return dogmaContext.GetTypeCategoryId(chargeDogmaTypeId) == 8 &&
               SumDamageAttributes(chargeDogmaTypeId) > 0d;
    }

    private bool HasAnyEffectName(int typeId, IReadOnlySet<string> effectNames)
    {
        if (typeId <= 0)
        {
            return false;
        }

        var typeDogma = dogmaContext.GetTypeDogma(typeId);
        if (typeDogma?.DogmaEffects is null)
        {
            return false;
        }

        foreach (var typeEffect in typeDogma.DogmaEffects)
        {
            var effect = dogmaContext.GetEffect(typeEffect.EffectId);
            if (effect?.Name is { Length: > 0 } effectName &&
                effectNames.Contains(effectName))
            {
                return true;
            }
        }

        return false;
    }

    private double SumDamageAttributes(int typeId)
    {
        if (typeId <= 0)
        {
            return 0d;
        }

        return Math.Max(0d, dogmaContext.GetTypeAttributeValue(typeId, "emDamage", 0d))
             + Math.Max(0d, dogmaContext.GetTypeAttributeValue(typeId, "thermalDamage", 0d))
             + Math.Max(0d, dogmaContext.GetTypeAttributeValue(typeId, "kineticDamage", 0d))
             + Math.Max(0d, dogmaContext.GetTypeAttributeValue(typeId, "explosiveDamage", 0d));
    }

    private HashSet<int> GetChargeGroupIds(int moduleTypeId)
    {
        var groups = new HashSet<int>();
        foreach (var attributeName in new[] { "chargeGroup1", "chargeGroup2", "chargeGroup3", "chargeGroup4" })
        {
            var groupId = (int)Math.Round(Math.Max(0d, dogmaContext.GetTypeAttributeValue(moduleTypeId, attributeName, 0d)));
            if (groupId > 0)
            {
                groups.Add(groupId);
            }
        }

        return groups;
    }

    private HashSet<int> GetLauncherGroupIds(int chargeTypeId)
    {
        var groups = new HashSet<int>();
        foreach (var attributeName in new[] { "launcherGroup", "launcherGroup2", "launcherGroup3", "launcherGroup4", "launcherGroup5", "launcherGroup6" })
        {
            var groupId = (int)Math.Round(Math.Max(0d, dogmaContext.GetTypeAttributeValue(chargeTypeId, attributeName, 0d)));
            if (groupId > 0)
            {
                groups.Add(groupId);
            }
        }

        return groups;
    }
}

internal sealed record FitAmmoSelectionExecution(
    FitSnapshot Snapshot,
    FitAmmoOption SelectedCharge,
    IReadOnlyList<string> UpdatedSlotIds,
    IReadOnlyList<FitAmmoSelectionPrompt> RemainingPrompts);

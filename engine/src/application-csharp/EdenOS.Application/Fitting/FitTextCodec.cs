using System.Text;
using EdenOS.Application.Fitting.Dogma.DataSources;
using EdenOS.Application.Fitting.Dogma.Sde;
using EdenOS.Contracts.Fitting;

namespace EdenOS.Application.Fitting;

public static class FitTextCodec
{
    public static ImportedFitTextDocument Import(
        string fitId,
        string eftContent,
        IDogmaDataSource dataSource,
        string? locale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eftContent);
        ArgumentNullException.ThrowIfNull(dataSource);

        var warnings = new List<string>();
        var lines = eftContent.Replace("\r", string.Empty).Split('\n');
        var headerLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
        if (string.IsNullOrWhiteSpace(headerLine) || !headerLine.StartsWith('[') || !headerLine.EndsWith(']'))
        {
            throw new InvalidOperationException("Invalid EFT header.");
        }

        var headerBody = headerLine[1..^1];
        var separatorIndex = headerBody.IndexOf(',');
        if (separatorIndex < 0)
        {
            throw new InvalidOperationException("EFT header must be '[Hull, FitName]'.");
        }

        var hullName = headerBody[..separatorIndex].Trim();
        var fitName = headerBody[(separatorIndex + 1)..].Trim();
        var normalizedLocale = DogmaLocalization.NormalizeLocale(locale);
        var hullTypeId = ResolveTypeId(dataSource, hullName);
        var slotCounters = new Dictionary<EftSlotClass, int>();
        var modules = new List<FittedModule>();
        var rigs = new List<Rig>();
        var drones = new List<DroneStack>();
        var cargo = new List<FitCargoEntry>();

        foreach (var section in SplitSections(lines.SkipWhile(line => !string.Equals(line.Trim(), headerLine, StringComparison.Ordinal)).Skip(1)))
        {
            if (section.Count == 0)
            {
                continue;
            }

            if (section.All(IsStackLine))
            {
                ImportStacks(section, dataSource, drones, cargo, warnings, normalizedLocale);
                continue;
            }

            foreach (var rawLine in section)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (TryParseEmptySlot(line, out var emptySlotClass))
                {
                    if (emptySlotClass is not EftSlotClass.Unknown)
                    {
                        slotCounters[emptySlotClass] = slotCounters.GetValueOrDefault(emptySlotClass) + 1;
                    }

                    continue;
                }

                var offline = false;
                if (line.EndsWith("/offline", StringComparison.OrdinalIgnoreCase))
                {
                    offline = true;
                    line = line[..^8].TrimEnd();
                }

                var commaIndex = line.IndexOf(',');
                var moduleName = commaIndex >= 0 ? line[..commaIndex].Trim() : line;
                var chargeName = commaIndex >= 0 ? line[(commaIndex + 1)..].Trim() : null;

                var moduleTypeId = ResolveTypeId(dataSource, moduleName);
                int? chargeTypeId = string.IsNullOrWhiteSpace(chargeName) ? null : ResolveTypeId(dataSource, chargeName!);
                var localizedModuleName = DogmaLocalization.ResolveLocalizedTypeName(dataSource, moduleTypeId, moduleName, normalizedLocale);
                var localizedChargeName = chargeTypeId is > 0
                    ? DogmaLocalization.ResolveLocalizedTypeName(dataSource, chargeTypeId.Value, chargeName!, normalizedLocale)
                    : chargeName;
                var slotClass = InferSlotClass(dataSource, moduleTypeId);
                if (slotClass is EftSlotClass.Unknown)
                {
                    throw new InvalidOperationException($"Unsupported EFT slot class for module '{moduleName}'.");
                }

                var slotIndex = slotCounters.GetValueOrDefault(slotClass) + 1;
                slotCounters[slotClass] = slotIndex;

                switch (slotClass)
                {
                    case EftSlotClass.High:
                    case EftSlotClass.Mid:
                    case EftSlotClass.Low:
                    case EftSlotClass.Subsystem:
                    case EftSlotClass.Service:
                        modules.Add(new FittedModule
                        {
                            TypeId = $"type:{moduleTypeId}",
                            Name = localizedModuleName,
                            DogmaTypeId = moduleTypeId,
                            SlotId = BuildSlotId(slotClass, slotIndex),
                            SlotKind = ToModuleSlotKind(slotClass),
                            State = offline ? FittingItemState.Offline : FittingItemState.Active,
                            Charge = chargeTypeId is null
                                ? null
                                : new Charge
                                {
                                    ChargeId = $"type:{chargeTypeId.Value}",
                                    Name = localizedChargeName!,
                                    DogmaTypeId = chargeTypeId.Value
                                }
                        });
                        break;
                    case EftSlotClass.Rig:
                        rigs.Add(new Rig
                        {
                            TypeId = $"type:{moduleTypeId}",
                            Name = localizedModuleName,
                            DogmaTypeId = moduleTypeId,
                            SlotId = BuildSlotId(slotClass, slotIndex)
                        });
                        break;
                }
            }
        }

        return new ImportedFitTextDocument(
            new FitSnapshot
            {
                FitId = fitId,
                Name = fitName,
                ShipHull = new ShipHull
                {
                    HullId = $"type:{hullTypeId}",
                    Name = DogmaLocalization.ResolveLocalizedTypeName(dataSource, hullTypeId, hullName, normalizedLocale),
                    DogmaTypeId = hullTypeId,
                    Slots = BuildImportedHullSlots(slotCounters)
                },
                Modules = modules,
                Rigs = rigs,
                DroneBay = new DroneBay
                {
                    Drones = drones
                },
                Cargo = cargo
            },
            warnings);
    }

    private static IReadOnlyList<ModuleSlot> BuildImportedHullSlots(IReadOnlyDictionary<EftSlotClass, int> slotCounters)
    {
        var slots = new List<ModuleSlot>();
        foreach (var (slotClass, count) in slotCounters.OrderBy(entry => entry.Key))
        {
            var slotKind = slotClass switch
            {
                EftSlotClass.High => ModuleSlotKind.High,
                EftSlotClass.Mid => ModuleSlotKind.Mid,
                EftSlotClass.Low => ModuleSlotKind.Low,
                EftSlotClass.Rig => ModuleSlotKind.Rig,
                EftSlotClass.Subsystem => ModuleSlotKind.Subsystem,
                EftSlotClass.Service => ModuleSlotKind.Service,
                _ => (ModuleSlotKind?)null
            };

            if (slotKind is null || count <= 0)
            {
                continue;
            }

            for (var index = 1; index <= count; index++)
            {
                slots.Add(new ModuleSlot
                {
                    SlotId = BuildSlotId(slotClass, index),
                    Kind = slotKind.Value,
                    Index = index,
                    Label = $"{slotKind.Value} {index}"
                });
            }
        }

        return slots;
    }

    public static ExportedFitTextDocument Export(
        FitSnapshot snapshot,
        FitAttributeView attributes,
        IDogmaDataSource? dataSource,
        bool includeEmptySlots,
        string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(attributes);

        var warnings = new List<string>();
        var normalizedLocale = DogmaLocalization.NormalizeLocale(locale);
        var lines = new List<string>
        {
            $"[{ResolveFitName(snapshot.ShipHull.Name, snapshot.ShipHull.DogmaTypeId, dataSource, normalizedLocale)}, {ResolveExportFitName(snapshot)}]",
            string.Empty
        };

        AppendSection(lines, BuildModuleSection(snapshot, attributes, ModuleSlotKind.Low, includeEmptySlots, dataSource, normalizedLocale));
        AppendSection(lines, BuildModuleSection(snapshot, attributes, ModuleSlotKind.Mid, includeEmptySlots, dataSource, normalizedLocale));
        AppendSection(lines, BuildModuleSection(snapshot, attributes, ModuleSlotKind.High, includeEmptySlots, dataSource, normalizedLocale));
        AppendSection(lines, BuildRigSection(snapshot, attributes, includeEmptySlots, dataSource, normalizedLocale));

        var subsystemSection = BuildModuleSection(snapshot, attributes, ModuleSlotKind.Subsystem, includeEmptySlots, dataSource, normalizedLocale);
        if (subsystemSection.Count > 0 || ResolveAvailableSlots(attributes, snapshot, ModuleSlotKind.Subsystem) > 0)
        {
            AppendSection(lines, subsystemSection);
        }

        var serviceSection = BuildModuleSection(snapshot, attributes, ModuleSlotKind.Service, includeEmptySlots, dataSource, normalizedLocale);
        if (serviceSection.Count > 0 || ResolveAvailableSlots(attributes, snapshot, ModuleSlotKind.Service) > 0)
        {
            AppendSection(lines, serviceSection);
        }

        var droneLines = BuildDroneSection(snapshot, warnings, dataSource, normalizedLocale);
        if (droneLines.Count > 0)
        {
            AppendSection(lines, droneLines);
        }

        var cargoLines = snapshot.Cargo
            .Where(entry => entry.Quantity > 0)
            .Select(entry => $"{ResolveEntryName(entry.Name, entry.DogmaTypeId, dataSource, normalizedLocale)} x{entry.Quantity}")
            .ToList();
        if (cargoLines.Count > 0)
        {
            AppendSection(lines, cargoLines);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new ExportedFitTextDocument(string.Join(Environment.NewLine, lines), warnings);
    }

    private static void ImportStacks(
        IReadOnlyList<string> lines,
        IDogmaDataSource dataSource,
        ICollection<DroneStack> drones,
        ICollection<FitCargoEntry> cargo,
        ICollection<string> warnings,
        string? locale = null)
    {
        var normalizedLocale = DogmaLocalization.NormalizeLocale(locale);
        var entries = new List<(int? TypeId, string Name, int Quantity, bool IsDrone)>();
        foreach (var line in lines)
        {
            var separatorIndex = line.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
            if (separatorIndex < 0 || !int.TryParse(line[(separatorIndex + 2)..].Trim(), out var quantity))
            {
                throw new InvalidOperationException($"Invalid EFT stack line '{line}'.");
            }

            var typeName = line[..separatorIndex].Trim();
            var typeId = dataSource.TryResolveTypeIdByName(typeName);
            var isDrone = typeId is > 0 && IsDroneType(dataSource, typeId.Value);
            if (typeId is null or <= 0)
            {
                warnings.Add($"Unable to resolve stack type name '{typeName}' from dogma data source. Imported as opaque cargo entry.");
            }

            entries.Add((
                typeId,
                typeId is > 0
                    ? DogmaLocalization.ResolveLocalizedTypeName(dataSource, typeId.Value, typeName, normalizedLocale)
                    : typeName,
                quantity,
                isDrone));
        }

        if (entries.All(entry => entry.IsDrone && entry.TypeId is > 0))
        {
            warnings.Add("EFT drone stacks do not encode launched-vs-bay state. Imported drones are treated as launched stacks for offensive parity.");
            foreach (var entry in entries)
            {
                drones.Add(new DroneStack
                {
                    DroneTypeId = $"type:{entry.TypeId!.Value}",
                    Name = entry.Name,
                    DogmaTypeId = entry.TypeId!.Value,
                    Quantity = entry.Quantity,
                    BayQuantity = 0,
                    State = FittingItemState.Active
                });
            }

            return;
        }

        foreach (var entry in entries)
        {
            cargo.Add(new FitCargoEntry
            {
                TypeId = entry.TypeId is > 0
                    ? $"type:{entry.TypeId.Value}"
                    : BuildOpaqueTypeId("cargo", entry.Name),
                Name = entry.Name,
                DogmaTypeId = entry.TypeId,
                Quantity = entry.Quantity
            });
        }
    }

    private static string BuildOpaqueTypeId(string prefix, string name)
    {
        var normalized = new string(name
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized)
            ? $"{prefix}:unresolved"
            : $"{prefix}:unresolved:{normalized}";
    }

    private static List<string> BuildModuleSection(
        FitSnapshot snapshot,
        FitAttributeView attributes,
        ModuleSlotKind slotKind,
        bool includeEmptySlots,
        IDogmaDataSource? dataSource,
        string? locale)
    {
        var modules = snapshot.Modules
            .Where(module => module.SlotKind == slotKind)
            .OrderBy(module => ResolveSlotIndex(module.SlotId))
            .ToList();
        var lines = modules.Select(module => BuildModuleLine(module, dataSource, locale)).ToList();

        if (!includeEmptySlots)
        {
            return lines;
        }

        var available = ResolveAvailableSlots(attributes, snapshot, slotKind);
        for (var index = modules.Count; index < available; index++)
        {
            lines.Add(BuildEmptySlotLabel(slotKind));
        }

        return lines;
    }

    private static List<string> BuildRigSection(
        FitSnapshot snapshot,
        FitAttributeView attributes,
        bool includeEmptySlots,
        IDogmaDataSource? dataSource,
        string? locale)
    {
        var rigs = snapshot.Rigs
            .OrderBy(rig => ResolveSlotIndex(rig.SlotId))
            .ToList();
        var lines = rigs
            .Select(rig => DogmaLocalization.ResolveLocalizedTypeName(dataSource, rig.DogmaTypeId, rig.Name, locale))
            .ToList();

        if (!includeEmptySlots)
        {
            return lines;
        }

        var available = ResolveAvailableSlots(attributes, snapshot, ModuleSlotKind.Rig);
        for (var index = rigs.Count; index < available; index++)
        {
            lines.Add(BuildEmptySlotLabel(ModuleSlotKind.Rig));
        }

        return lines;
    }

    private static List<string> BuildDroneSection(
        FitSnapshot snapshot,
        ICollection<string> warnings,
        IDogmaDataSource? dataSource,
        string? locale)
    {
        var lines = new List<string>();
        foreach (var drone in snapshot.DroneBay.Drones.Where(drone => ResolveDroneExportQuantity(drone) > 0))
        {
            if (drone.Quantity > 0 && drone.BayQuantity is > 0 && drone.BayQuantity != drone.Quantity)
            {
                warnings.Add(
                    $"Drone stack '{drone.Name}' has launched quantity {drone.Quantity} and bay quantity {drone.BayQuantity}. EFT export can only serialize one stack number and used {ResolveDroneExportQuantity(drone)}.");
            }
            var droneName = DogmaLocalization.ResolveLocalizedTypeName(dataSource, drone.DogmaTypeId, drone.Name, locale);
            lines.Add($"{droneName} x{ResolveDroneExportQuantity(drone)}");
        }

        return lines;
    }

    private static void AppendSection(ICollection<string> output, IReadOnlyList<string> sectionLines)
    {
        foreach (var line in sectionLines)
        {
            output.Add(line);
        }

        output.Add(string.Empty);
    }

    private static IEnumerable<List<string>> SplitSections(IEnumerable<string> lines)
    {
        var section = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (section.Count > 0)
                {
                    yield return section;
                    section = new List<string>();
                }

                continue;
            }

            section.Add(line.Trim());
        }

        if (section.Count > 0)
        {
            yield return section;
        }
    }

    private static bool IsStackLine(string line)
    {
        var separatorIndex = line.LastIndexOf(" x", StringComparison.OrdinalIgnoreCase);
        return separatorIndex > 0 && int.TryParse(line[(separatorIndex + 2)..].Trim(), out _);
    }

    private static bool TryParseEmptySlot(string line, out EftSlotClass slotClass)
    {
        slotClass = line switch
        {
            "[Empty High slot]" => EftSlotClass.High,
            "[Empty Med slot]" => EftSlotClass.Mid,
            "[Empty Mid slot]" => EftSlotClass.Mid,
            "[Empty Low slot]" => EftSlotClass.Low,
            "[Empty Rig slot]" => EftSlotClass.Rig,
            "[Empty Subsystem slot]" => EftSlotClass.Subsystem,
            "[Empty Service slot]" => EftSlotClass.Service,
            _ => EftSlotClass.Unknown
        };

        return slotClass is not EftSlotClass.Unknown;
    }

    private static bool IsDroneType(IDogmaDataSource dataSource, int typeId)
    {
        var type = dataSource.GetType(typeId);
        if (type is null)
        {
            return false;
        }

        var group = dataSource.GetGroup(type.GroupId);
        return group?.CategoryId == 18;
    }

    private static int ResolveTypeId(IDogmaDataSource dataSource, string typeName)
    {
        var resolved = dataSource.TryResolveTypeIdByName(typeName);
        if (resolved is > 0)
        {
            return resolved.Value;
        }

        throw new InvalidOperationException($"Unable to resolve type name '{typeName}' from dogma data source.");
    }

    private static string ResolveFitName(string fallbackName, int? dogmaTypeId, IDogmaDataSource? dataSource, string? locale) =>
        DogmaLocalization.ResolveLocalizedTypeName(dataSource, dogmaTypeId, fallbackName, locale);

    private static string ResolveEntryName(string fallbackName, int? dogmaTypeId, IDogmaDataSource? dataSource, string? locale) =>
        DogmaLocalization.ResolveLocalizedTypeName(dataSource, dogmaTypeId, fallbackName, locale);

    private static string ResolveExportFitName(FitSnapshot snapshot) =>
        string.IsNullOrWhiteSpace(snapshot.Name) ? snapshot.FitId : snapshot.Name.Trim();

    private static string BuildModuleLine(FittedModule module, IDogmaDataSource? dataSource, string? locale)
    {
        var builder = new StringBuilder(DogmaLocalization.ResolveLocalizedTypeName(dataSource, module.DogmaTypeId, module.Name, locale));
        if (module.Charge is not null && !string.IsNullOrWhiteSpace(module.Charge.Name))
        {
            builder.Append(", ");
            builder.Append(DogmaLocalization.ResolveLocalizedTypeName(dataSource, module.Charge.DogmaTypeId, module.Charge.Name, locale));
        }

        if (module.State == FittingItemState.Offline)
        {
            builder.Append(" /offline");
        }

        return builder.ToString();
    }

    private static string BuildEmptySlotLabel(ModuleSlotKind slotKind) =>
        slotKind switch
        {
            ModuleSlotKind.High => "[Empty High slot]",
            ModuleSlotKind.Mid => "[Empty Med slot]",
            ModuleSlotKind.Low => "[Empty Low slot]",
            ModuleSlotKind.Rig => "[Empty Rig slot]",
            ModuleSlotKind.Subsystem => "[Empty Subsystem slot]",
            ModuleSlotKind.Service => "[Empty Service slot]",
            _ => "[Empty slot]"
        };

    private static int ResolveAvailableSlots(
        FitAttributeView attributes,
        FitSnapshot snapshot,
        ModuleSlotKind slotKind)
    {
        var usage = attributes.SlotUsage.FirstOrDefault(entry => entry.Kind == slotKind);
        if (usage is not null && usage.Available > 0)
        {
            return usage.Available;
        }

        return snapshot.ShipHull.Slots.Count(slot => slot.Kind == slotKind);
    }

    private static int ResolveDroneExportQuantity(DroneStack drone)
    {
        if (drone.BayQuantity is > 0)
        {
            return drone.BayQuantity.Value;
        }

        return Math.Max(0, drone.Quantity);
    }

    private static EftSlotClass InferSlotClass(IDogmaDataSource dataSource, int typeId)
    {
        var typeDogma = dataSource.GetTypeDogma(typeId);
        if (typeDogma?.DogmaEffects is null)
        {
            return EftSlotClass.Unknown;
        }

        foreach (var typeEffect in typeDogma.DogmaEffects)
        {
            switch (typeEffect.EffectId)
            {
                case 11:
                    return EftSlotClass.Low;
                case 12:
                    return EftSlotClass.High;
                case 13:
                    return EftSlotClass.Mid;
                case 2663:
                    return EftSlotClass.Rig;
                case 3772:
                    return EftSlotClass.Subsystem;
                case 6306:
                    return EftSlotClass.Service;
            }

            var effect = dataSource.GetEffect(typeEffect.EffectId);
            if (effect?.Name is null)
            {
                continue;
            }

            switch (effect.Name)
            {
                case "loPower":
                    return EftSlotClass.Low;
                case "hiPower":
                    return EftSlotClass.High;
                case "medPower":
                    return EftSlotClass.Mid;
                case "rigSlot":
                    return EftSlotClass.Rig;
                case "subSystem":
                    return EftSlotClass.Subsystem;
                case "serviceSlot":
                    return EftSlotClass.Service;
            }
        }

        return EftSlotClass.Unknown;
    }

    private static ModuleSlotKind ToModuleSlotKind(EftSlotClass slotClass) =>
        slotClass switch
        {
            EftSlotClass.High => ModuleSlotKind.High,
            EftSlotClass.Mid => ModuleSlotKind.Mid,
            EftSlotClass.Low => ModuleSlotKind.Low,
            EftSlotClass.Rig => ModuleSlotKind.Rig,
            EftSlotClass.Subsystem => ModuleSlotKind.Subsystem,
            EftSlotClass.Service => ModuleSlotKind.Service,
            _ => throw new InvalidOperationException($"Slot class '{slotClass}' cannot map to fitting module slot kind.")
        };

    private static string BuildSlotId(EftSlotClass slotClass, int index) =>
        slotClass switch
        {
            EftSlotClass.High => $"slot-high-{index}",
            EftSlotClass.Mid => $"slot-mid-{index}",
            EftSlotClass.Low => $"slot-low-{index}",
            EftSlotClass.Rig => $"slot-rig-{index}",
            EftSlotClass.Subsystem => $"slot-subsystem-{index}",
            EftSlotClass.Service => $"slot-service-{index}",
            _ => $"slot-unknown-{index}"
        };

    private static int ResolveSlotIndex(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId))
        {
            return 0;
        }

        var parts = slotId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return int.TryParse(parts.LastOrDefault(), out var index) ? index : 0;
    }

    private enum EftSlotClass
    {
        Unknown,
        High,
        Mid,
        Low,
        Rig,
        Subsystem,
        Service
    }
}

public sealed record ImportedFitTextDocument(
    FitSnapshot Snapshot,
    IReadOnlyList<string> Warnings);

public sealed record ExportedFitTextDocument(
    string Text,
    IReadOnlyList<string> Warnings);

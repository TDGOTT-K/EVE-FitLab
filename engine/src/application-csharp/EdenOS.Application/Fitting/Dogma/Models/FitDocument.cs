namespace EdenOS.Application.Fitting.Dogma.Models;

public sealed class FitDocument
{
    public int ShipTypeId { get; set; }
    public string ShipTypeName { get; set; } = string.Empty;
    public List<FitModuleSlot> Modules { get; set; } = new();
    public List<FitDroneEntry> Drones { get; set; } = new();
    public List<FitCargoEntry> Cargo { get; set; } = new();
    public List<FitImplantEntry> Implants { get; set; } = new();
    public List<FitBoosterEntry> Boosters { get; set; } = new();
}

public sealed class FitModuleSlot
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string SlotGroup { get; set; } = "high";
    public int SlotIndex { get; set; }
    public string State { get; set; } = "active";
    public int? ChargeTypeId { get; set; }
    public string? ChargeTypeName { get; set; }
}

public sealed class FitDroneEntry
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int? BayQuantity { get; set; }
    public string State { get; set; } = "active";
}

public sealed class FitCargoEntry
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class FitImplantEntry
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int? SlotIndex { get; set; }
    public string State { get; set; } = "passive";
}

public sealed class FitBoosterEntry
{
    public int TypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public int? BoosterSlot { get; set; }
    public string State { get; set; } = "active";
    public List<FitBoosterSideEffectEntry> SideEffects { get; set; } = new();
}

public sealed class FitBoosterSideEffectEntry
{
    public int EffectId { get; set; }
    public bool Active { get; set; }
}

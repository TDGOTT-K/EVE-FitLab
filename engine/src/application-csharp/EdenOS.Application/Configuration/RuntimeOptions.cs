namespace EdenOS.Application.Configuration;

public sealed class RuntimeOptions
{
    public ApplicationOptions Application { get; set; } = new();

    public KernelOptions Kernel { get; set; } = new();

    public RuntimeStateOptions State { get; set; } = new();

    public DogmaOptions Dogma { get; set; } = new();

    public EsiSsoOptions EsiSso { get; set; } = new();

    public HostOptions Hosts { get; set; } = new();
}

public sealed class ApplicationOptions
{
    public string Name { get; set; } = "EdenOS Rewrite";

    public string Version { get; set; } = "0.1.0";
}

public sealed class KernelOptions
{
    public string Mode { get; set; } = "placeholder";

    public string Version { get; set; } = "0.1.0";

    public string Transport { get; set; } = "in-process-contract";
}

public sealed class HostOptions
{
    public string[] Enabled { get; set; } = Array.Empty<string>();
}

public sealed class RuntimeStateOptions
{
    public string? RootPath { get; set; }
}

public sealed class DogmaOptions
{
    public string SourceKind { get; set; } = "jsonl-filesystem";

    public string? SdeRootPath { get; set; }

    public string? DataRootPath { get; set; }

    public string? DogmaEffectsOverlayJsonPath { get; set; }

    public string? RuleSetJsonPath { get; set; }
}

public sealed class EsiSsoOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string TokenEndpoint { get; set; } = "https://login.eveonline.com/v2/oauth/token";

    public int RefreshSkewSeconds { get; set; } = 300;
}

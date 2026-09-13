namespace EdenOS.Contracts.Runtime;

public enum KernelMode
{
    Placeholder = 0,
    NativeRustLibrary = 1,
    ExternalProcess = 2
}

public sealed record RuntimeHealthRequest(string InvocationSource, bool IncludeCapabilities = true);

public sealed record KernelHealthSnapshot(
    KernelMode Mode,
    string KernelVersion,
    string Transport,
    bool IsReady,
    IReadOnlyDictionary<string, string> Capabilities,
    DateTimeOffset CheckedAtUtc);

public sealed record RuntimeStatus(
    string ApplicationName,
    string ApplicationVersion,
    string EnvironmentName,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<string> EnabledHosts,
    KernelHealthSnapshot Kernel);

using EdenOS.Application.Abstractions;
using EdenOS.Application.Configuration;
using EdenOS.Contracts.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EdenOS.Application.Services;

public sealed class PlaceholderKernelGateway(
    IOptions<RuntimeOptions> options,
    ILogger<PlaceholderKernelGateway> logger) : IKernelGateway
{
    public Task<KernelHealthSnapshot> GetHealthSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeOptions = options.Value;
        var kernelMode = ResolveMode(runtimeOptions.Kernel.Mode);

        logger.LogDebug(
            "Returning placeholder kernel snapshot for mode {KernelMode} and transport {Transport}.",
            kernelMode,
            runtimeOptions.Kernel.Transport);

        var snapshot = new KernelHealthSnapshot(
            kernelMode,
            runtimeOptions.Kernel.Version,
            runtimeOptions.Kernel.Transport,
            IsReady: true,
            Capabilities: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["plan"] = "reserved",
                ["market"] = "reserved",
                ["execution"] = "reserved",
                ["task_pool"] = "reserved"
            },
            CheckedAtUtc: DateTimeOffset.UtcNow);

        return Task.FromResult(snapshot);
    }

    private static KernelMode ResolveMode(string rawMode) =>
        rawMode.Trim().ToLowerInvariant() switch
        {
            "placeholder" => KernelMode.Placeholder,
            "native-rust-library" => KernelMode.NativeRustLibrary,
            "external-process" => KernelMode.ExternalProcess,
            _ => throw new InvalidOperationException(
                $"Unsupported kernel mode '{rawMode}'. Expected placeholder, native-rust-library, or external-process.")
        };
}

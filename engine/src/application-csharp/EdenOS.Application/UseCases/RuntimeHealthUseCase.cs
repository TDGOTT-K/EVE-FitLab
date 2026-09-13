using EdenOS.Application.Abstractions;
using EdenOS.Application.Configuration;
using EdenOS.Application.Services;
using EdenOS.Contracts.Application;
using EdenOS.Contracts.Runtime;
using EdenOS.Contracts.UseCases;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EdenOS.Application.UseCases;

public sealed class RuntimeHealthUseCase(
    IKernelGateway kernelGateway,
    IOptions<RuntimeOptions> options,
    IHostEnvironment hostEnvironment,
    RuntimeSessionState sessionState,
    ILogger<RuntimeHealthUseCase> logger)
    : IQueryUseCase<RuntimeHealthRequest, RuntimeStatus>
{
    public async Task<UseCaseResult<RuntimeStatus>> ExecuteAsync(
        RuntimeHealthRequest request,
        CancellationToken cancellationToken)
    {
        var runtimeOptions = options.Value;
        var validationErrors = Validate(runtimeOptions);

        if (validationErrors.Count > 0)
        {
            return UseCaseResult<RuntimeStatus>.Failure(
                UseCaseStatus.InvalidInput,
                "Runtime configuration is invalid.",
                CreateTraceId(),
                validationErrors);
        }

        try
        {
            var kernelSnapshot = await kernelGateway.GetHealthSnapshotAsync(cancellationToken);
            var runtimeStatus = new RuntimeStatus(
                runtimeOptions.Application.Name,
                runtimeOptions.Application.Version,
                hostEnvironment.EnvironmentName,
                sessionState.StartedAtUtc,
                runtimeOptions.Hosts.Enabled,
                kernelSnapshot);

            var warnings = new List<string>();
            if (kernelSnapshot.Mode == KernelMode.Placeholder)
            {
                warnings.Add("Kernel adapter is a placeholder. Business capabilities are reserved but not implemented yet.");
            }

            logger.LogInformation(
                "Runtime health queried via {InvocationSource}; kernel mode is {KernelMode}.",
                request.InvocationSource,
                kernelSnapshot.Mode);

            return UseCaseResult<RuntimeStatus>.Success(
                runtimeStatus,
                warnings.Count == 0
                    ? "Headless runtime started successfully."
                    : "Headless runtime started with placeholders.",
                CreateTraceId(),
                warnings);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Runtime configuration could not be projected into a kernel snapshot.");

            return UseCaseResult<RuntimeStatus>.Failure(
                UseCaseStatus.InvalidInput,
                "Kernel configuration is invalid.",
                CreateTraceId(),
                [exception.Message]);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Runtime health query failed unexpectedly.");

            return UseCaseResult<RuntimeStatus>.Failure(
                UseCaseStatus.Error,
                "Runtime health query failed.",
                CreateTraceId(),
                ["The headless runtime failed before any business capability was invoked."]);
        }
    }

    private static List<string> Validate(RuntimeOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Application.Name))
        {
            errors.Add("Runtime application name must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Application.Version))
        {
            errors.Add("Runtime application version must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Kernel.Mode))
        {
            errors.Add("Kernel mode must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Kernel.Transport))
        {
            errors.Add("Kernel transport must be configured.");
        }

        if (options.Hosts.Enabled.Length == 0)
        {
            errors.Add("At least one host must be declared in the runtime configuration.");
        }

        return errors;
    }

    private static string CreateTraceId() => Guid.NewGuid().ToString("N");
}

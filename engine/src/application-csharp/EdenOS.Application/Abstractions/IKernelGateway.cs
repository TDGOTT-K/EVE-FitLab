using EdenOS.Contracts.Runtime;

namespace EdenOS.Application.Abstractions;

public interface IKernelGateway
{
    Task<KernelHealthSnapshot> GetHealthSnapshotAsync(CancellationToken cancellationToken);
}

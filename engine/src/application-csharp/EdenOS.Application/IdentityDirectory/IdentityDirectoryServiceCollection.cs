using EdenOS.Application.RuntimeState;
using EdenOS.Contracts.IdentityDirectory;

namespace EdenOS.Application.IdentityDirectory;

public static class IdentityDirectoryServiceCollection
{
    public static IIdentityDirectoryService CreateInMemory() =>
        new InMemoryIdentityDirectoryService();

    public static IIdentityDirectoryService CreatePersistent(string rootPath) =>
        new InMemoryIdentityDirectoryService(
            new LocalJsonStateStore<IdentityDirectoryRuntimeState>(
                RuntimeStatePaths.GetIdentityDirectoryStatePath(rootPath),
                () => new IdentityDirectoryRuntimeState()));
}

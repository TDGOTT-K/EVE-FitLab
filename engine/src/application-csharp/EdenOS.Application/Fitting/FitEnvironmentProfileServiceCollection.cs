using EdenOS.Application.RuntimeState;

namespace EdenOS.Application.Fitting;

public static class FitEnvironmentProfileServiceCollection
{
    public static InMemoryFitEnvironmentProfileService CreateInMemory(TimeProvider timeProvider) =>
        new(timeProvider);

    public static InMemoryFitEnvironmentProfileService CreatePersistent(
        string rootPath,
        TimeProvider timeProvider) =>
        new(
            timeProvider,
            new LocalJsonStateStore<FittingRuntimeState>(
                RuntimeStatePaths.GetFittingStatePath(rootPath),
                () => new FittingRuntimeState()));
}

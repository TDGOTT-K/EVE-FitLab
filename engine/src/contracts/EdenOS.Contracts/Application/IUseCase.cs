using EdenOS.Contracts.UseCases;

namespace EdenOS.Contracts.Application;

public interface IUseCase<in TRequest, TResponse>
{
    Task<UseCaseResult<TResponse>> ExecuteAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IQueryUseCase<in TRequest, TResponse> : IUseCase<TRequest, TResponse>;

public interface ICommandUseCase<in TRequest, TResponse> : IUseCase<TRequest, TResponse>;

public interface IWorkflowUseCase<in TRequest, TResponse> : IUseCase<TRequest, TResponse>;

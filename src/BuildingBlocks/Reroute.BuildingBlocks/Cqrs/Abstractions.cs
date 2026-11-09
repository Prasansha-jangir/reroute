namespace Reroute.BuildingBlocks.Cqrs;

/// <summary>Intent to change state. Exactly one handler.</summary>
public interface ICommand<TResult>;

/// <summary>Request for data. Must not change state.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Cross-cutting step wrapped around every handler (tracing, validation, ...).</summary>
public interface IPipelineBehavior<in TRequest, TResult>
{
    Task<TResult> Handle(TRequest request, Func<Task<TResult>> next, CancellationToken cancellationToken);
}

public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}

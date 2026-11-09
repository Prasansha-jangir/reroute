using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Reroute.BuildingBlocks.Cqrs;

/// <summary>
/// Minimal mediator replacing MediatR (commercial licence since v13).
/// Reflection is used once per request type to build a strongly-typed wrapper, then cached.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (RequestWrapper<TResult>)Wrappers.GetOrAdd(
            command.GetType(),
            static t => Activator.CreateInstance(typeof(CommandWrapper<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(command, serviceProvider, cancellationToken);
    }

    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (RequestWrapper<TResult>)Wrappers.GetOrAdd(
            query.GetType(),
            static t => Activator.CreateInstance(typeof(QueryWrapper<,>).MakeGenericType(t, typeof(TResult)))!);
        return wrapper.Handle(query, serviceProvider, cancellationToken);
    }

    private abstract class RequestWrapper<TResult>
    {
        public abstract Task<TResult> Handle(object request, IServiceProvider sp, CancellationToken ct);

        protected static Task<TResult> RunPipeline<TRequest>(
            TRequest request, IServiceProvider sp, Func<Task<TResult>> handler, CancellationToken ct)
        {
            // Behaviours registered first run outermost.
            var next = handler;
            foreach (var behaviour in sp.GetServices<IPipelineBehavior<TRequest, TResult>>().Reverse())
            {
                var current = next;
                next = () => behaviour.Handle(request, current, ct);
            }

            return next();
        }
    }

    private sealed class CommandWrapper<TCommand, TResult> : RequestWrapper<TResult>
        where TCommand : ICommand<TResult>
    {
        public override Task<TResult> Handle(object request, IServiceProvider sp, CancellationToken ct)
        {
            var command = (TCommand)request;
            var handler = sp.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            return RunPipeline(command, sp, () => handler.Handle(command, ct), ct);
        }
    }

    private sealed class QueryWrapper<TQuery, TResult> : RequestWrapper<TResult>
        where TQuery : IQuery<TResult>
    {
        public override Task<TResult> Handle(object request, IServiceProvider sp, CancellationToken ct)
        {
            var query = (TQuery)request;
            var handler = sp.GetRequiredService<IQueryHandler<TQuery, TResult>>();
            return RunPipeline(query, sp, () => handler.Handle(query, ct), ct);
        }
    }
}

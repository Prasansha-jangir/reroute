using System.Reflection;
using FluentValidation;
using Reroute.SharedKernel;

namespace Reroute.BuildingBlocks.Cqrs;

/// <summary>
/// Runs all FluentValidation validators for the request. Invalid input short-circuits into a
/// <see cref="Result"/> failure (mapped to HTTP 400 problem+json) instead of throwing.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResult>
{
    public async Task<TResult> Handle(TRequest request, Func<Task<TResult>> next, CancellationToken cancellationToken)
    {
        var validatorList = validators.ToList();
        if (validatorList.Count == 0)
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results.SelectMany(r => r.Errors).ToList();
        if (failures.Count == 0)
        {
            return await next();
        }

        var details = failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray(), StringComparer.Ordinal);

        return ResultFactory.Failure<TResult>(Error.Validation(details))
            ?? throw new ValidationException(failures);
    }
}

internal static class ResultFactory
{
    /// <summary>Creates a failed Result or Result&lt;T&gt; for the requested result type; null for other types.</summary>
    public static TResult? Failure<TResult>(Error error)
    {
        var type = typeof(TResult);
        if (type == typeof(Result))
        {
            return (TResult)(object)Result.Failure(error);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = type.GetMethod(
                nameof(Result.Failure),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                [typeof(Error)]);
            return (TResult)method!.Invoke(null, [error])!;
        }

        return default;
    }
}

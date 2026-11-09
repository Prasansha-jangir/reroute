using System.Reflection;
using Reroute.Claims.Application.Claims;
using Reroute.Claims.Domain;
using Reroute.SharedKernel;
using Shouldly;
using Xunit;

namespace Reroute.ArchitectureTests;

/// <summary>
/// Clean Architecture enforced by the build, not by code review alone:
/// dependencies point inwards (Api → Infrastructure → Application → Domain).
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Claim).Assembly;
    private static readonly Assembly Application = typeof(FileClaimCommand).Assembly;

    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

    [Theory]
    [InlineData("Dapper")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Confluent.Kafka")]
    [InlineData("StackExchange.Redis")]
    [InlineData("Reroute.BuildingBlocks")]
    [InlineData("Reroute.Claims.Application")]
    [InlineData("Reroute.Claims.Infrastructure")]
    public void Domain_has_no_infrastructure_dependencies(string forbidden) =>
        References(Domain).ShouldNotContain(r => r.StartsWith(forbidden, StringComparison.Ordinal));

    [Theory]
    [InlineData("Dapper")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Confluent.Kafka")]
    [InlineData("Reroute.Claims.Infrastructure")]
    [InlineData("Reroute.Claims.Api")]
    public void Application_does_not_depend_on_adapters(string forbidden) =>
        References(Application).ShouldNotContain(r => r.StartsWith(forbidden, StringComparison.Ordinal));

    [Fact]
    public void Domain_events_are_immutable_sealed_records()
    {
        var events = Domain.GetTypes().Where(t => typeof(IDomainEvent).IsAssignableFrom(t) && !t.IsInterface).ToList();

        events.ShouldNotBeEmpty();
        foreach (var e in events)
        {
            e.IsSealed.ShouldBeTrue($"{e.Name} must be sealed");
            e.GetProperties().Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p))
                .ShouldBeEmpty($"{e.Name} must not have public setters");
        }
    }

    [Fact]
    public void Handlers_are_internal_to_the_application_layer() =>
        Application.GetTypes()
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal) && !t.IsInterface)
            .ShouldAllBe(t => !t.IsPublic);

    private static bool IsInitOnly(PropertyInfo p) =>
        p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit");
}

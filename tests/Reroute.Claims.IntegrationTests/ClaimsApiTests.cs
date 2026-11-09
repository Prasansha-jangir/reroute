using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.SqlClient;
using Reroute.Claims.Application.Claims;
using Reroute.Claims.Domain;
using Shouldly;
using Xunit;

namespace Reroute.Claims.IntegrationTests;

[Collection(ClaimsApiCollection.Name)]
public sealed class ClaimsApiTests(ClaimsApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FileClaimCommand ValidCommand() =>
        new("KL1234", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)), "AMS", "ATH", DisruptionType.Delay, 210);

    private static async Task<FileClaimResponse> FileAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/claims", ValidCommand(), Json, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FileClaimResponse>(Json, Ct))!;
    }

    [Fact]
    public async Task Filing_a_claim_persists_events_read_model_and_outbox_atomically()
    {
        var client = factory.CreateClientFor("traveller-a");

        var filed = await FileAsync(client);

        filed.IsEligible.ShouldBeTrue();
        filed.CompensationAmount.ShouldBe(400);

        var claim = await client.GetFromJsonAsync<ClaimDto>($"/api/claims/{filed.ClaimId}", Json, Ct);
        claim!.Status.ShouldBe(ClaimStatus.Eligible); // read-your-writes via inline projection
        claim.Version.ShouldBe(2);

        var timeline = await client.GetFromJsonAsync<List<ClaimTimelineEntryDto>>($"/api/claims/{filed.ClaimId}/timeline", Json, Ct);
        timeline!.Select(e => e.EventType).ShouldBe(["ClaimFiled", "EligibilityAssessed"]);

        await using var db = new SqlConnection(factory.ConnectionString);
        var outbox = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM claims.OutboxMessages WHERE MessageKey = @key", new { key = filed.ClaimId.ToString() });
        outbox.ShouldBe(2);
    }

    [Fact]
    public async Task Invalid_command_returns_problem_details_with_field_errors()
    {
        var client = factory.CreateClientFor("traveller-b");

        var response = await client.PostAsJsonAsync("/api/claims", ValidCommand() with { Destination = "AMS" }, Json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("Destination");
    }

    [Fact]
    public async Task Another_traveller_cannot_see_my_claim()
    {
        var filed = await FileAsync(factory.CreateClientFor("owner"));

        var response = await factory.CreateClientFor("intruder").GetAsync($"/api/claims/{filed.ClaimId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // 404, not 403: don't leak existence (IDOR)
    }

    [Fact]
    public async Task Concurrent_submissions_produce_exactly_one_success()
    {
        var client = factory.CreateClientFor("traveller-c");
        var filed = await FileAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsync($"/api/claims/{filed.ClaimId}/submit", content: null, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4);
    }

    [Fact]
    public async Task Unauthenticated_requests_are_rejected()
    {
        var response = await factory.CreateClient().GetAsync("/api/claims", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

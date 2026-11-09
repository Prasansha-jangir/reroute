using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Reroute.BuildingBlocks.Web;

public static class Policies
{
    public const string Traveller = "traveller";
    public const string Ops = "ops";
}

public interface ICurrentUser
{
    string Id { get; }

    bool IsInRole(string role);
}

/// <summary>
/// Every service is a stateless OAuth 2.0 resource server: it validates the JWT relayed by the gateway
/// (issuer, audience, lifetime, signature via the IdP's JWKS endpoint).
/// </summary>
public static class AuthenticationExtensions
{
    public static WebApplicationBuilder AddJwtResourceServer(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("Auth");

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = section["Authority"];               // e.g. http://keycloak:8080/realms/reroute
                o.Audience = section["Audience"] ?? "reroute-api";
                o.RequireHttpsMetadata = !builder.Environment.IsDevelopmentLike();
                o.MapInboundClaims = false;                       // keep "sub", "roles" as-is
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);

                // SignalR over WebSockets cannot send headers from browsers; the gateway relays the bearer header,
                // but direct (dev) connections may pass ?access_token= on hub paths only.
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query.TryGetValue("access_token", out var token))
                        {
                            ctx.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Traveller, p => p.RequireAuthenticatedUser())
            .AddPolicy(Policies.Ops, p => p.RequireRole("ops"));

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return builder;
    }

    private static bool IsDevelopmentLike(this Microsoft.Extensions.Hosting.IHostEnvironment env) =>
        env.EnvironmentName is "Development" or "Testing" or "Docker";
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string Id => User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("Authenticated user has no 'sub' claim.");

    public bool IsInRole(string role) => User.IsInRole(role);
}

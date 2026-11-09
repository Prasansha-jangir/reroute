using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Reroute.BuildingBlocks.Observability;
using StackExchange.Redis;
using Yarp.ReverseProxy.Transforms;

// ============================================================================================
// API Gateway + Backend-for-Frontend (BFF)
//  - OIDC Authorization Code + PKCE with the IdP; tokens stay server-side (encrypted cookie)
//  - Token relay: adds "Authorization: Bearer <access_token>" to proxied requests
//  - CSRF defence: SameSite=Lax cookie + mandatory X-CSRF header on /api
//  - Per-user token-bucket rate limiting
// ============================================================================================
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("gateway", "Yarp.ReverseProxy");

var redis = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redis))
{
    // Shared Data Protection keys → any gateway replica can decrypt any session cookie.
    builder.Services.AddDataProtection()
        .SetApplicationName("reroute-gateway")
        .PersistKeysToStackExchangeRedis(ConnectionMultiplexer.Connect(redis), "reroute:dataprotection-keys");
}

var oidc = builder.Configuration.GetSection("Oidc");
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.Name = "__Host-reroute";        // __Host- prefix: Secure, Path=/, no Domain
        o.Cookie.HttpOnly = true;                 // JavaScript can never read it
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;    // Strict would drop the cookie on the first navigation after the IdP redirect
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        // APIs get 401 instead of a 302 to the IdP; the SPA decides when to navigate to /bff/login.
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = oidc["Authority"];
        o.ClientId = oidc["ClientId"];
        o.ClientSecret = oidc["ClientSecret"];
        o.ResponseType = OpenIdConnectResponseType.Code;   // Authorization Code flow
        o.UsePkce = true;                                  // + PKCE
        o.SaveTokens = true;                               // tokens stored in the encrypted auth cookie
        o.GetClaimsFromUserInfoEndpoint = true;
        o.MapInboundClaims = false;
        o.RequireHttpsMetadata = !builder.Environment.IsDevelopment() && builder.Environment.EnvironmentName != "Docker";
        o.Scope.Clear();
        foreach (var scope in (oidc["Scopes"] ?? "openid profile email offline_access").Split(' '))
        {
            o.Scope.Add(scope);
        }

        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.TokenValidationParameters.RoleClaimType = "roles";
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-user", ctx => RateLimitPartition.GetTokenBucketLimiter(
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,
            TokensPerPeriod = 50,
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 0,
        }));
    // AI endpoints are expensive → much tighter budget.
    o.AddPolicy("copilot", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(ctx =>
    {
        ctx.AddRequestTransform(async transform =>
        {
            // Token relay: downstream services are plain JWT resource servers and never see cookies.
            var accessToken = await transform.HttpContext.GetTokenAsync("access_token");
            if (accessToken is not null)
            {
                transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            transform.ProxyRequest.Headers.Remove("Cookie");
        });
    });

var app = builder.Build();

app.UseServiceDefaults();
app.UseAuthentication();

// CSRF: a cross-site form/post cannot add custom headers, so requiring X-CSRF blocks it (defence in depth with SameSite=Lax).
app.Use(async (context, next) =>
{
    var isApi = context.Request.Path.StartsWithSegments("/api");
    var isSafe = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
    if (isApi && !isSafe && context.Request.Headers["X-CSRF"] != "1")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next(context);
});

app.UseRateLimiter();
app.UseAuthorization();

// ---- BFF endpoints used by the SPA ----
var bff = app.MapGroup("/bff");
bff.MapGet("/login", (string? returnUrl) => Results.Challenge(
    new AuthenticationProperties { RedirectUri = IsLocalUrl(returnUrl) ? returnUrl : "/" },
    [OpenIdConnectDefaults.AuthenticationScheme]));

bff.MapPost("/logout", () => Results.SignOut(
    new AuthenticationProperties { RedirectUri = "/" },
    [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
    .RequireAuthorization();

bff.MapGet("/user", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
    ? Results.Ok(new
    {
        id = user.FindFirstValue("sub"),
        name = user.FindFirstValue("name") ?? user.Identity.Name,
        email = user.FindFirstValue("email"),
        roles = user.FindAll("roles").Select(c => c.Value),
    })
    : Results.Unauthorized());

app.MapServiceDefaults();
app.MapReverseProxy();

await app.RunAsync();

static bool IsLocalUrl(string? url) =>
    !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);

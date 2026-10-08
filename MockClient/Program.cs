using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured through DI (not read eagerly here) so the integration tests can override the Jwt:* settings.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, config) =>
    {
        var key = config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key must be set (same value as the SSO Gateway).");

        options.MapInboundClaims = false;   // keep claim names exactly as issued: "sub", "email", "groups", ...
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                if (context.Exception is SecurityTokenExpiredException)
                {
                    context.Response.Headers["Token-Expired"] = "true";
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/login", (IConfiguration config) =>
{
    var ssoBase = (config["Sso:BaseUrl"]
        ?? throw new InvalidOperationException("Sso:BaseUrl is not configured.")).TrimEnd('/');
    var callback = config["Sso:ClientCallbackUrl"]
        ?? throw new InvalidOperationException("Sso:ClientCallbackUrl is not configured.");

    return Results.Redirect(QueryHelpers.AddQueryString($"{ssoBase}/Auth/Login", "returnUrl", callback));
});

app.MapGet("/api/userinfo", (ClaimsPrincipal user) =>
{
    var levelsJson = user.FindFirst("levels")?.Value ?? "{}";
    var levels = JsonSerializer.Deserialize<Dictionary<string, int>>(levelsJson) ?? new Dictionary<string, int>();

    var groups = (user.FindFirst("groups")?.Value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries);

    return Results.Ok(new
    {
        sub = user.FindFirst("sub")?.Value,
        email = user.FindFirst("email")?.Value,
        tenantApp = user.FindFirst("tenant_app")?.Value,
        groups,
        levels
    });
}).RequireAuthorization();

app.Run();

// Lets the integration tests (Task 7) start this app in memory.
public class MockClientMarker { }
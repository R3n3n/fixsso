using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Gateway.Controllers;
using Gateway.Models;
using Gateway.Services;
using ITElectiveSSO.Models;
using ITELECTIVE_SSO.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Tests.Integration
{
    public class FullSsoFlowTests
    {
        private const string JwtKey = "integration-test-secret-key-that-is-at-least-32-chars";
        private const string Issuer = "IT_ELECTIVE_SSO";
        private const string Audience = "IT_ELECTIVE_SSO_CLIENTS";
        private const string CallbackUrl = "https://localhost:7300/callback.html";
        private const string Email = "flow@itelectivesso.local";
        private const string Password = "Password1";

        // ---------- Gateway side: the real AuthController + JwtTokenService ----------

        private static ServiceProvider BuildGatewayProvider(string dbName)
        {
            var services = new ServiceCollection();

            services.AddLogging();

            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = JwtKey,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:ExpiryHours"] = "9"
                })
                .Build());

            services.AddDbContext<SsoDbContext>(o => o.UseInMemoryDatabase(dbName));

            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<SsoDbContext>()
            .AddDefaultTokenProviders();

            services.AddScoped<IReturnUrlValidator, ReturnUrlValidator>();
            services.AddScoped<IAuditService, AuditService>();
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddTransient<AuthController>();

            return services.BuildServiceProvider();
        }

        private static async Task SeedGatewayAsync(IServiceProvider provider)
        {
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = provider.GetRequiredService<SsoDbContext>();

            var app = new TenantApp { Name = "SalesApp", ReturnUrl = CallbackUrl, IsActive = true, CreatedAt = DateTime.UtcNow };
            context.TenantApps.Add(app);
            await context.SaveChangesAsync();

            var adminGroup = new Group { TenantAppId = app.Id, Name = "SalesApp-Admin", PowerLevel = 0, CreatedAt = DateTime.UtcNow };
            var managerGroup = new Group { TenantAppId = app.Id, Name = "SalesApp-Manager", PowerLevel = 1, CreatedAt = DateTime.UtcNow };
            context.Groups.AddRange(adminGroup, managerGroup);
            await context.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = Email,
                Email = Email,
                EmailConfirmed = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await userManager.CreateAsync(user, Password);

            context.UserGroups.AddRange(
                new UserGroup { UserId = user.Id, GroupId = adminGroup.Id },
                new UserGroup { UserId = user.Id, GroupId = managerGroup.Id });
            await context.SaveChangesAsync();
        }

        private static async Task<string> LoginOnGatewayAndGetTokenAsync()
        {
            var provider = BuildGatewayProvider(Guid.NewGuid().ToString());
            await SeedGatewayAsync(provider);
            var controller = provider.GetRequiredService<AuthController>();

            var result = await controller.Login(new LoginViewModel { Email = Email, Password = Password, ReturnUrl = CallbackUrl });

            var redirect = Assert.IsType<RedirectResult>(result);
            var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
            return query["token"].ToString();
        }

        // ---------- Mock client side: the real MockClient app, run in memory ----------

        private static WebApplicationFactory<MockClientMarker> CreateMockClient() =>
            new WebApplicationFactory<MockClientMarker>().WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = JwtKey,
                        ["Jwt:Issuer"] = Issuer,
                        ["Jwt:Audience"] = Audience
                    })));

        private static async Task<HttpResponseMessage> GetUserInfoAsync(HttpClient client, string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/userinfo");
            if (token != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return await client.SendAsync(request);
        }

        private static string CreateToken(string signingKey, DateTime expires)
        {
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: new[] { new Claim("sub", "some-user-id"), new Claim("email", "someone@example.com") },
                notBefore: expires.AddHours(-9),
                expires: expires,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ---------- Tests ----------

        [Fact]
        public async Task FullFlow_LoginOnGateway_TokenIsAcceptedByMockClient()
        {
            var token = await LoginOnGatewayAndGetTokenAsync();

            using var factory = CreateMockClient();
            using var client = factory.CreateClient();

            var response = await GetUserInfoAsync(client, token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Email, body.GetProperty("email").GetString());
            Assert.Equal("SalesApp", body.GetProperty("tenantApp").GetString());

            var groups = body.GetProperty("groups").EnumerateArray().Select(g => g.GetString()).ToList();
            Assert.Contains("SalesApp-Admin", groups);
            Assert.Contains("SalesApp-Manager", groups);

            var levels = body.GetProperty("levels");
            Assert.Equal(0, levels.GetProperty("SalesApp-Admin").GetInt32());
            Assert.Equal(1, levels.GetProperty("SalesApp-Manager").GetInt32());
        }

        [Fact]
        public async Task UserInfo_WithoutToken_ReturnsUnauthorized()
        {
            using var factory = CreateMockClient();
            using var client = factory.CreateClient();

            var response = await GetUserInfoAsync(client, null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task UserInfo_WithExpiredToken_ReturnsUnauthorizedWithExpiredHeader()
        {
            using var factory = CreateMockClient();
            using var client = factory.CreateClient();

            var response = await GetUserInfoAsync(client, CreateToken(JwtKey, DateTime.UtcNow.AddHours(-1)));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(response.Headers.Contains("Token-Expired"));
        }

        [Fact]
        public async Task UserInfo_WithTokenSignedByWrongKey_ReturnsUnauthorized()
        {
            using var factory = CreateMockClient();
            using var client = factory.CreateClient();

            var forged = CreateToken("a-completely-different-key-that-is-long-enough", DateTime.UtcNow.AddHours(1));
            var response = await GetUserInfoAsync(client, forged);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.False(response.Headers.Contains("Token-Expired"));
        }
    }
}
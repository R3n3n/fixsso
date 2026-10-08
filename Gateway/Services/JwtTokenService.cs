using System.IdentityModel.Tokens.Jwt;
using System.Text;
using ITElectiveSSO.Models;
using ITELECTIVE_SSO.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Services
{
    public class JwtTokenService : IJwtTokenService
    {
        private readonly SsoDbContext _context;
        private readonly IConfiguration _configuration;

        public JwtTokenService(SsoDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<string> CreateTokenAsync(ApplicationUser user, TenantApp app)
        {
            var groups = await _context.UserGroups
                .Where(ug => ug.UserId == user.Id && ug.Group.TenantAppId == app.Id)
                .Select(ug => new { ug.Group.Name, ug.Group.PowerLevel })
                .ToListAsync();

            var levels = groups.ToDictionary(g => g.Name, g => g.PowerLevel);

            var secret = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key must be set in appsettings.json.");
            var expiryHours = int.TryParse(_configuration["Jwt:ExpiryHours"], out var hours) ? hours : 9;

            var now = DateTime.UtcNow;
            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256);

            var payload = new JwtPayload(
                _configuration["Jwt:Issuer"],
                _configuration["Jwt:Audience"],
                null,
                now,
                now.AddHours(expiryHours),
                now)
            {
                { JwtRegisteredClaimNames.Sub, user.Id },
                { JwtRegisteredClaimNames.Email, user.Email! },
                { "tenant_app", app.Name },
                { "groups", string.Join(",", levels.Keys) },
                { "levels", levels }
            };

            var token = new JwtSecurityToken(new JwtHeader(credentials), payload);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
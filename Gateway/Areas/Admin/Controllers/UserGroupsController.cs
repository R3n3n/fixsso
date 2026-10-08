using Gateway.Areas.Admin.Models;
using Gateway.Services;
using ITELECTIVE_SSO.Data;
using ITElectiveSSO.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/Users/{userId}/Groups")]
    public class UserGroupsController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SsoDbContext _context;
        private readonly IAuditService _auditService;

        public UserGroupsController(
            UserManager<ApplicationUser> userManager,
            SsoDbContext context,
            IAuditService auditService)
        {
            _userManager = userManager;
            _context = context;
            _auditService = auditService;
        }

        private string? GetClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();

        [HttpGet("Available")]
        public async Task<IActionResult> Available(string userId)
        {
            var assignedGroupIds = await _context.UserGroups
                .Where(ug => ug.UserId == userId)
                .Select(ug => ug.GroupId)
                .ToListAsync();

            var availableGroups = await _context.Groups
                .Where(g => !assignedGroupIds.Contains(g.Id))
                .Include(g => g.TenantApp)
                .Select(g => new
                {
                    groupId = g.Id,
                    name = g.Name,
                    appName = g.TenantApp.Name
                })
                .ToListAsync();

            return Json(availableGroups);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            var assignedGroups = await _context.UserGroups
                .Where(ug => ug.UserId == userId)
                .Include(ug => ug.Group)
                    .ThenInclude(g => g.TenantApp)
                .Select(ug => new
                {
                    groupId = ug.Group.Id,
                    name = ug.Group.Name,
                    appName = ug.Group.TenantApp.Name,
                    powerLevel = ug.Group.PowerLevel
                })
                .ToListAsync();

            return Json(assignedGroups);
        }

        // POST /Admin/Users/{userId}/Groups
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(string userId, [FromBody] AssignGroupViewModel model)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var group = await _context.Groups.FindAsync(model.GroupId);
            if (group == null)
            {
                return NotFound(new { message = "Group not found." });
            }

            var alreadyAssigned = await _context.UserGroups
                .AnyAsync(ug => ug.UserId == userId && ug.GroupId == model.GroupId);

            if (alreadyAssigned)
            {
                return Conflict(new { message = "User is already assigned to this group." });
            }

            _context.UserGroups.Add(new UserGroup
            {
                UserId = userId,
                GroupId = model.GroupId
            });

            await _context.SaveChangesAsync();

            await _auditService.LogActionAsync(
                userId,
                "GroupAssigned",
                $"User '{user.Email}' was assigned to group '{group.Name}' by an administrator.",
                GetClientIp());

            return Ok(new { success = true, message = $"User assigned to group '{group.Name}'." });
        }

        [HttpDelete("{groupId}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unassign(string userId, int groupId)
        {
            var userGroup = await _context.UserGroups
                .FirstOrDefaultAsync(ug => ug.UserId == userId && ug.GroupId == groupId);

            if (userGroup == null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(userId);
            var group = await _context.Groups.FindAsync(groupId);

            _context.UserGroups.Remove(userGroup);
            await _context.SaveChangesAsync();

            await _auditService.LogActionAsync(
                userId,
                "GroupUnassigned",
                $"User '{user?.Email ?? userId}' was removed from group '{group?.Name ?? groupId.ToString()}' by an administrator.",
                GetClientIp());

            return Ok(new { success = true });
        }
    }
}
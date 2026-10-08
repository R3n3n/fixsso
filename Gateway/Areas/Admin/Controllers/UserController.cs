using ITELECTIVE_SSO.Data;
using ITElectiveSSO.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Gateway.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SsoDbContext _context;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            SsoDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Index(int page = 1)
        {
            const int pageSize = 10;

            var query = _userManager.Users
                .OrderByDescending(u => u.CreatedAt)
                .ThenBy(u => u.Email);

            var totalUsers = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalUsers / (double)pageSize);

            var users = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(users);
        }

        // GET: /Admin/Users/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Admin/Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string? Email,
            string? Password,
            string? ConfirmPassword)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            // AJAX (modal) gets JSON with the field the error belongs to; plain form post gets the view back.
            IActionResult Fail(string? field, string message)
            {
                if (isAjax)
                {
                    return BadRequest(new { success = false, field, message });
                }

                ModelState.AddModelError(string.Empty, message);
                return View();
            }

            Email = Email?.Trim();

            if (string.IsNullOrWhiteSpace(Email))
            {
                return Fail("email", "Email is required.");
            }

            if (string.IsNullOrEmpty(Password))
            {
                return Fail("password", "Password is required.");
            }

            if (Password != ConfirmPassword)
            {
                return Fail("confirmPassword", "Passwords do not match!");
            }

            // Duplicate check (FindByEmailAsync is case-insensitive)
            var existingUser = await _userManager.FindByEmailAsync(Email);

            if (existingUser != null)
            {
                return Fail("email", "A user with this email already exists.");
            }

            var user = new ApplicationUser
            {
                UserName = Email,
                Email = Email,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, Password);

            if (result.Succeeded)
            {
                return isAjax
                    ? Json(new { success = true })
                    : RedirectToAction(nameof(Index));
            }

            var error = result.Errors.First();
            string? errorField =
                error.Code.StartsWith("Password") ? "password" :
                (error.Code.Contains("Email") || error.Code.Contains("UserName")) ? "email" :
                null;

            return Fail(errorField, error.Description);
        }

        // GET: /Admin/Users/Details/{id}
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: /Admin/Users/Delete/{id}
        [HttpPost]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = false;
            await _userManager.UpdateAsync(user);

            return RedirectToAction(nameof(Index));
        }

        // POST: /Admin/Users/ToggleActive/{id}
        [HttpPost]
        public async Task<IActionResult> ToggleActive(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);

            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            if (isAjax)
            {
                return Json(new { success = true, isActive = user.IsActive });
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /Admin/Users/ResetPassword/{id} endpoint
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var tempPassword = GenerateTemporaryPassword();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, tempPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                return BadRequest(new { success = false, message = errors });
            }

            // Clear any lockout from earlier failed attempts so the temp password works right away
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = user.Id,
                Action = "PasswordReset",
                Details = $"Password was reset for user '{user.Email}' by an administrator.",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Json(new { success = true, tempPassword });
        }

        private static string GenerateTemporaryPassword()
        {
            const string lowercase = "abcdefghijkmnpqrstuvwxyz";
            const string uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string digits = "23456789";
            const string allChars = lowercase + uppercase + digits;

            var passwordChars = new List<char>
            {
                lowercase[RandomNumberGenerator.GetInt32(lowercase.Length)],
                uppercase[RandomNumberGenerator.GetInt32(uppercase.Length)],
                digits[RandomNumberGenerator.GetInt32(digits.Length)],
                digits[RandomNumberGenerator.GetInt32(digits.Length)]
            };

            for (int i = 0; i < 6; i++)
            {
                passwordChars.Add(allChars[RandomNumberGenerator.GetInt32(allChars.Length)]);
            }

            // Fisher-Yates shuffle
            for (int i = passwordChars.Count - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (passwordChars[i], passwordChars[j]) = (passwordChars[j], passwordChars[i]);
            }

            return new string(passwordChars.ToArray());
        }
    }
}
using Gateway.Models;
using Gateway.Services;
using ITElectiveSSO.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Gateway.Controllers
{
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private const string InvalidCredentialsMessage = "Invalid email or password.";
        private const string SuspendedMessage = "Account Suspended";
        private const string LockedOutMessage = "Too many failed login attempts. Please try again in 15 minutes.";

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IReturnUrlValidator _returnUrlValidator;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IAuditService _auditService;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IReturnUrlValidator returnUrlValidator,
            IJwtTokenService jwtTokenService,
            IAuditService auditService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _returnUrlValidator = returnUrlValidator;
            _jwtTokenService = jwtTokenService;
            _auditService = auditService;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl)
        {

            var app = await _returnUrlValidator.ValidateAsync(returnUrl, GetClientIp());

            if (app == null)
            {
                return View("UnapprovedApp");
            }
            return View(new LoginViewModel { ReturnUrl = returnUrl, AppName = app.Name });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            var ip = GetClientIp();

            var app = await _returnUrlValidator.ValidateAsync(model.ReturnUrl, ip);
            if (app == null)
            {
                return View("UnapprovedApp");
            }
            model.AppName = app.Name;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                await _auditService.LogLoginAsync(null, model.Email, false, "Unknown email", ip);

                ModelState.AddModelError(string.Empty, InvalidCredentialsMessage);
                return View(model);
            }

            var signInResult = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);

            if (signInResult.IsLockedOut)
            {
                await _auditService.LogLoginAsync(user.Id, model.Email, false, "Account locked out", ip);
                ModelState.AddModelError(string.Empty, LockedOutMessage);
                return View(model);
            }

            if (!signInResult.Succeeded)
            {
                await _auditService.LogLoginAsync(user.Id, model.Email, false, "Invalid password", ip);
                ModelState.AddModelError(string.Empty, InvalidCredentialsMessage);
                return View(model);
            }

            if (!user.IsActive)
            {
                await _auditService.LogLoginAsync(user.Id, model.Email, false, "Account suspended", ip);
                ModelState.AddModelError(string.Empty, SuspendedMessage);
                return View(model);
            }

            var token = await _jwtTokenService.CreateTokenAsync(user, app);

            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            await _auditService.LogLoginAsync(user.Id, user.Email!, true, null, ip);

            return Redirect(QueryHelpers.AddQueryString(app.ReturnUrl, "token", token));
        }

        private string? GetClientIp() => HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }
}
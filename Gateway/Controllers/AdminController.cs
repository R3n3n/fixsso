using Microsoft.AspNetCore.Mvc;

namespace SSO_Gateway.Controllers
{
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            ViewBag.HasData = false;

            ViewBag.TotalUsers = 0;
            ViewBag.ActiveApps = 0;
            ViewBag.AssignedGroups = 0;
            ViewBag.AuditEvents = 0;

            return View();
        }

        public IActionResult Users() => RedirectToAction("Index", "Users", new { area = "Admin" });
        public IActionResult Apps() => RedirectToAction("Index", "TenantApps", new { area = "Admin" });
        public IActionResult Groups() => RedirectToAction("Index", "Groups", new { area = "Admin" });
        public IActionResult AuditLogs() => RedirectToAction("Index", "AuditLogs", new { area = "Admin" });
    }
}

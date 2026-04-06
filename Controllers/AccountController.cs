using MachineStatusUpdate.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MachineStatusUpdate.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ────────────────────────────────────────────────────────────────
        // GET /Account/Login
        // ────────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            if (!string.IsNullOrEmpty(HttpContext.Session.GetString("UserRole")))
                return RedirectToRoleHome(HttpContext.Session.GetString("UserRole")!);

            return View();
        }

        // ────────────────────────────────────────────────────────────────
        // POST /Account/Login
        // ────────────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "请填写所有字段 / Please fill in all fields.";
                return View();
            }

            // ── Tìm user trong DB ──
            var user = await _context.SVN_Downtime_Accounts
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.Trim().ToLower());

            if (user == null)
            {
                ViewBag.Error = "用户名不存在 / Username does not exist.";
                return View();
            }

            if (user.Password != password)
            {
                ViewBag.Error = "密码错误 / Incorrect password.";
                return View();
            }

            // ── Lưu session ──
            HttpContext.Session.SetString("UserName", user.Username);
            HttpContext.Session.SetString("UserRole", user.Role);

            return RedirectToRoleHome(user.Role);
        }

        // ────────────────────────────────────────────────────────────────
        // GET /Account/Logout
        // ────────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        // ────────────────────────────────────────────────────────────────
        // Helper
        // ────────────────────────────────────────────────────────────────
        private IActionResult RedirectToRoleHome(string role) => role switch
        {
            "Admin"      => RedirectToAction("AdminPanel",            "Status"),
            "Technical"  => RedirectToAction("NotificationDashboard", "Status"),
            _            => RedirectToAction("CreateDownTime",         "Status"),
        };
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RetailVision.Web.Models;
namespace RetailVision.Web.Controllers;

[EnableRateLimiting("public")]
public sealed class AccountController(SignInManager<IdentityUser> signIn) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginViewModel { ReturnUrl = returnUrl });
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await signIn.PasswordSignInAsync(model.Email, model.Password, false, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/home/settings");
        ModelState.AddModelError("", "Вход не выполнен. Проверьте данные или повторите попытку позже.");
        return View(model);
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
    public IActionResult Denied()
    {
        Response.StatusCode = 403;
        return View("~/Views/Home/Feature.cshtml", new FeatureViewModel("Доступ ограничен", "Для этого раздела нужна роль администратора.", "Недостаточно прав"));
    }
}

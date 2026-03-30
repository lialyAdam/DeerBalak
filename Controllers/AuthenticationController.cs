using Deerbalak.Data.Helpers.Constants;
using Deerbalak.Data.Models;
using DeerBalak.ViewModels.Authentication;
using DeerBalak.ViewModels.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Deerbalak.Controllers
{
    public class AuthenticationController : Controller
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;
        public AuthenticationController(UserManager<User> userManager,
            SignInManager<User> signInManager,
            RoleManager<IdentityRole<int>> roleManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
        }

        //public async Task<IActionResult> Login()
        //{
        //    return View();
        //}
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginVM loginVM)
        {
            if (!ModelState.IsValid)
                return View(loginVM);

            var existingUser = await _userManager.FindByEmailAsync(loginVM.Email);
            if (existingUser == null)
            {
                ModelState.AddModelError("", "Invalid email or password. Please, try again");
                return View(loginVM);
            }

            var existingUserClaims = await _userManager.GetClaimsAsync(existingUser);
            if (!existingUserClaims.Any(c => c.Type == CustomClaim.FullName))
                await _userManager.AddClaimAsync(existingUser, new Claim(CustomClaim.FullName, existingUser.FullName));

            var result = await _signInManager.PasswordSignInAsync(existingUser.UserName, loginVM.Password, false, false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError("", "Invalid email or password. Please, try again");
                return View(loginVM);
            }

            var userRoles = await _userManager.GetRolesAsync(existingUser);
            await _signInManager.RefreshSignInAsync(existingUser);

            if (userRoles.Contains(AppRoles.Admin))
                return RedirectToAction("Index", "Admin");

            return RedirectToAction("Index", "Home");
        }

        //public async Task<IActionResult> Register()
        //{
        //    return View();
        //}
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterVM registerVM)
        {
            if (!ModelState.IsValid)
                return View(registerVM);

            var newUser = new User()
            {
                FullName = $"{registerVM.FirstName} {registerVM.LastName}",
                Email = registerVM.Email,
                UserName = registerVM.Email
            };

            var existingUser = await _userManager.FindByEmailAsync(registerVM.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "Email already exists");
                return View(registerVM);
            }

            var result = await _userManager.CreateAsync(newUser, registerVM.Password);

            if (result.Succeeded)
            {
                // Ensure role exists before assigning
                if (!await _roleManager.RoleExistsAsync(AppRoles.User))
                {
                    await _roleManager.CreateAsync(new IdentityRole<int>(AppRoles.User));
                }

                await _userManager.AddToRoleAsync(newUser, AppRoles.User);
                await _userManager.AddClaimAsync(newUser, new Claim(CustomClaim.FullName, newUser.FullName));
                await _signInManager.SignInAsync(newUser, isPersistent: false);
                return RedirectToAction("Index", "Home");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            return View(registerVM);
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePassword(UpdatePasswordVM updatePasswordVM)
        {
            if (updatePasswordVM.NewPassword != updatePasswordVM.ConfirmPassword)
            {
                TempData["PasswordError"] = "Passwords do not match";
                TempData["ActiveTab"] = "Password";

                return RedirectToAction("Index", "Settings");
            }

            var loggedInUser = await _userManager.GetUserAsync(User);
            var isCurrentPasswordValid = await _userManager.CheckPasswordAsync(loggedInUser, updatePasswordVM.CurrentPassword);

            if (!isCurrentPasswordValid)
            {
                TempData["PasswordError"] = "Current password is invalid";
                TempData["ActiveTab"] = "Password";
                return RedirectToAction("Index", "Settings");
            }

            var result = await _userManager.ChangePasswordAsync(loggedInUser, updatePasswordVM.CurrentPassword, updatePasswordVM.NewPassword);

            if (result.Succeeded)
            {
                TempData["PasswordSuccess"] = "Password updated successfully";
                TempData["ActiveTab"] = "Password";
                await _signInManager.RefreshSignInAsync(loggedInUser);
            }

            return RedirectToAction("Index", "Settings");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile(UpdateProfileVM profileVM)
        {
            var loggedInUser = await _userManager.GetUserAsync(User);
            if (loggedInUser == null)
                return RedirectToAction("Login");

            loggedInUser.FullName = profileVM.FullName;
            loggedInUser.UserName = profileVM.UserName;
            loggedInUser.Bio = profileVM.Bio;

            var result = await _userManager.UpdateAsync(loggedInUser);
            if (!result.Succeeded)
            {
                TempData["UserProfileError"] = "User profile could not be updated";
                TempData["ActiveTab"] = "Profile";
            }

            await _signInManager.RefreshSignInAsync(loggedInUser);
            return RedirectToAction("Index", "Settings");
        }


        public IActionResult ExternalLogin(string provider)
        {
            var redirectUrl = Url.Action("ExternalLoginCallback", "Authentication");
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        public async Task<IActionResult> ExternalLoginCallback()
        {
            var info = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
            if (info == null)
                return RedirectToAction("Login");

            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var user = await _userManager.FindByEmailAsync(email);

            if (user == null)
            {
                var newUser = new User()
                {
                    Email = email,
                    UserName = email,
                    FullName = info.Principal.FindFirstValue(ClaimTypes.Name),
                    EmailConfirmed = true
                };
                var result = await _userManager.CreateAsync(newUser);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(newUser, AppRoles.User);
                    await _userManager.AddClaimAsync(newUser, new Claim(CustomClaim.FullName, newUser.FullName));

                    await _signInManager.SignInAsync(newUser, isPersistent: false);

                    return RedirectToAction("Index", "Home");
                }
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }
    }
}
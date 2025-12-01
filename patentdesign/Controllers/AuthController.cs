using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Services;

namespace patentdesign.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController(AuthServices authServices) : ControllerBase
    {
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto req)
        {
            var newUser = await authServices.CreateUser(req);
            if (!newUser) return BadRequest("Failed to Register");
            return Ok(new { message = "User created successfully" });
        }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto req)
        {
            var user = await authServices.LoginUser(req);
            if (user == null) return Unauthorized("Invalid email or password");
            return Ok(user);
        }
        [HttpPost("transfer")]
        public async Task<IActionResult> Transfer([FromBody] MigrateUserDto req)
        {
            var result = await authServices.TransferUser(req);
            if (!result)
            {
                return BadRequest("Transfer failed");
            }
            return Ok(new { message = "Transfer successful" });
        }
        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto req)
        {
            var result = await authServices.ResetPassword(req);
            if (!result)
            {
                return BadRequest("Password reset failed");
            }
            return Ok(new { message = "Password reset successful" });
        }
        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto req)
        {
            var result = await authServices.ChangePassword(req);
            if (!result)
            {
                return BadRequest("Password change failed");
            }
            return Ok(new { message = "Password changed successfully" });
        }
    }
}

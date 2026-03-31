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
        [HttpPost("ResetPasswordRequest")]
        public async Task<IActionResult> ResetPasswordRequest([FromQuery]string email)
        {
            var result = await authServices.RequestPasswordReset(email);
            if (!result)
            {
                return BadRequest("Password reset request failed");
            }
            return Ok(new { message = "Password reset requested" });
        }
        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var result = await authServices.ResetPassword(dto);
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
        [Authorize]
        [HttpPost("UpdateProfile")]
        public async Task<IActionResult> UpdateUserProfileAsync([FromBody] ProfileDto req)
        {
            var res = await authServices.UpdateUserProfile(req);
            if (!res)
            {
                return BadRequest("Failed to Update Profile");
            }
            return Ok(new { message = "Profile Updated Successfully" });
        }
        [HttpGet("GetUser")]
        public async Task<IActionResult> GetUserById([FromQuery] string userId)
        {
            var res = await authServices.GetUser(userId);
            if(res is null)
            {
                return NotFound("Failed to fetch User Details");
            }
            return Ok(res);
        }
        
    }
}

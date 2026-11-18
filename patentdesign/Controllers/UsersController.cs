using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Security.Claims;
using patentdesign.Dtos.Request;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;
using patentdesign.Utils;

namespace patentdesign.Controllers;

[Authorize]
[ApiController] [Route("api/users")]

public class UsersController(UsersService usersService, RoleManagementService roleManagementService) :ControllerBase
{
    //[HttpPost("updateSignature")]
    //public async Task<IActionResult> UploadSignature([FromBody] UpdateSigReq userInfo)
    //{
    //    var url=await usersService.UpdateUserSig(userInfo);
    //    return Ok(url);
    //}
    //[HttpGet("getSignature")]
    //public async Task<IActionResult> getSignature([FromQuery] string userId)
    //{
    //    var url=usersService.GetSignature(userId);
    //    return Ok(url);
    //}

    //[HttpPost("Performances")]
    //public async Task<IActionResult> GetPerformances([FromBody] FinanceQueryType data)
    //{
    //    var value=await usersService.GetPerformances(data);
    //    return Ok(value);
    //}
    //[HttpPost("DefaultCorr")]
    //public async Task<IActionResult> LoadDefaultCorrespondence([FromBody] UserCreateType user)
    //{
    //    var value=await usersService.LoadDefaultCorrespondence(user);
    //    return Ok(value);
    //}
    //[HttpPost("UpdateCorr")]
    //public async Task<IActionResult> SaveNewCorrespondence([FromBody] CorrReqData data)
    //{
    //    var value=await usersService.SaveNewCorrespondence(data.corr, data.user);
    //    return Ok(value);
    //}

    [HttpGet("SearchNameId")]
    public async Task<IActionResult> SearchNameId([FromQuery] string? nameId, [FromQuery] string? email)
    {
        var value = await usersService.SearchUsersByNameId(nameId, email);
        return Ok(value);
    }
    [HttpPost("LoadUsers")]
    public async Task<IActionResult> LoadUsers([FromBody] GetUsersRequest user)
    {
        var result=await usersService.LoadUsers(user);
        return Ok(result);
    }
    [HttpPut("UpdateUserRoles")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> UpdateUserRoles([FromBody] UserRoleDto request)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorId))
            return Unauthorized();
        try
        {
            var updated = await roleManagementService.UpdateUserRoles(request, actorId, HttpContext.RequestAborted);
            if (!updated)
                return NotFound(new { message = "User not found." });
            return Ok(new { message = "User roles updated successfully." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DBConcurrencyException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("account-types")]
    [Authorize(Roles = "SuperAdmin")]
    public IActionResult GetAccountTypes() => Ok(Enum.GetValues<AccountType>()
        .Select(type => new { id = (int)type, name = type.ToString() }));

    [HttpGet("{userId}/account-type")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetAccountType(string userId, [FromServices] UserAccountService accountService)
    {
        var result = await accountService.GetAccountType(userId, HttpContext.RequestAborted);
        if (result == null)
            return NotFound(new { message = "User not found." });
        return Ok(result);
    }

    [HttpPut("{userId}/account-type")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> UpdateAccountType(string userId, [FromBody] UpdateAccountTypeDto request,
        [FromServices] UserAccountService accountService)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorId))
            return Unauthorized();
        try
        {
            var result = await accountService.UpdateAccountType(userId, request, actorId, HttpContext.RequestAborted);
            if (result == null)
                return NotFound(new { message = "User not found." });
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DBConcurrencyException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("roles")]
    [Authorize(Roles = "SuperAdmin")]
    public IActionResult GetRoles() => Ok(Enum.GetValues<Roles>().Select(role => new { id = (int)role, name = role.ToString() }));

    [HttpGet("roles/{role}/users")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetUsersByRole(Roles role, [FromQuery] RolePageQuery query)
    {
        if (!Enum.IsDefined(role))
            return BadRequest(new { message = "Invalid role." });
        return Ok(await roleManagementService.GetUsersByRole(role, query, HttpContext.RequestAborted));
    }

    [HttpGet("role-assigners")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetRoleAssigners([FromQuery] RolePageQuery query) =>
        Ok(await roleManagementService.GetAssigners(query, HttpContext.RequestAborted));

    [HttpGet("role-assignment-history")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetRoleAssignmentHistory([FromQuery] RoleHistoryQuery query) =>
        Ok(await roleManagementService.GetHistory(query, HttpContext.RequestAborted));

    [HttpPost("GetAllUsers")]
    public async Task<IActionResult> GetAllUsers([FromBody] GetUsersDto request)
    {
        var users = await usersService.GetAllUsers(request);
        return Ok(users);
    }

    [HttpGet("GetUserById")]
    public async Task<IActionResult> GetUserById([FromQuery] string id)
    {
        var user = await usersService.GetUserById(id);
        return Ok(user);
    }
    [HttpGet("GetOtherApplications")]
    [Authorize]
    public async Task<IActionResult> GetOtherApplications([FromQuery] string? userId = null)
    {
        try
        {
            var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(callerId))
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(userId) && !OtherApplicationsHistoryRules.CanViewAll(User))
            {
                return Forbid();
            }

            if (OtherApplicationsHistoryRules.ShouldViewAll(userId, User))
            {
                var allApplications = await usersService.FetchAllOtherApplications();
                return Ok(allApplications);
            }

            if (!OtherApplicationsHistoryRules.CanViewRequestedOwner(userId, User))
            {
                return Forbid();
            }

            var applications = await usersService.FetchOtherApplications(userId);
            return Ok(applications ?? new List<ApplicationInfo>());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message, details = ex.InnerException?.Message });
        }
    }
}
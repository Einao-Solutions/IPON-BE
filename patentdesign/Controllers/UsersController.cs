using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Dtos.Response;
using patentdesign.Enums;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;

[ApiController] [Route("api/users")]

public class UsersController(UsersService usersService) :ControllerBase
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
    public async Task<IActionResult> SearchNameId([FromQuery] string nameId)
    {
        var value = await usersService.SearchUsersByNameId(nameId);
        return Ok(value);
    }
    [HttpPost("LoadUsers")]
    public async Task<IActionResult> LoadUsers([FromBody] GetUsersRequest user)
    {
        var result=await usersService.LoadUsers(user);
        return Ok(result);
    }
    [HttpPut("UpdateUserRoles")]
    public async Task<IActionResult> UpdateUserRoles([FromBody] UserRoleDto request)
    {
        var updated = await usersService.UpdateUserRoles(request);
        if (!updated)
            return NotFound(new { message = "User not found or no role changes provided." });

        return Ok(new { message = "User roles updated successfully." });
    }
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
}
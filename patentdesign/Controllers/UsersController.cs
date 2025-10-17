using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;

[ApiController] [Route("api/users")]

public class UsersController(UsersService usersService) :ControllerBase
{
    [HttpPost("updateSignature")]
    public async Task<IActionResult> UploadSignature([FromBody] UpdateSigReq userInfo)
    {
        var url=await usersService.UpdateUserSig(userInfo);
        return Ok(url);
    }
    [HttpGet("getSignature")]
    public async Task<IActionResult> getSignature([FromQuery] string userId)
    {
        var url=usersService.GetSignature(userId);
        return Ok(url);
    }

    [HttpPost("Performances")]
    public async Task<IActionResult> GetPerformances([FromBody] FinanceQueryType data)
    {
        var value=await usersService.GetPerformances(data);
        return Ok(value);
    }
    [HttpPost("DefaultCorr")]
    public async Task<IActionResult> LoadDefaultCorrespondence([FromBody] UserCreateType user)
    {
        var value=await usersService.LoadDefaultCorrespondence(user);
        return Ok(value);
    }
    [HttpPost("UpdateCorr")]
    public async Task<IActionResult> SaveNewCorrespondence([FromBody] CorrReqData data)
    {
        var value=await usersService.SaveNewCorrespondence(data.corr, data.user);
        return Ok(value);
    }

    [HttpGet("SearchNameId")]
    public async Task<IActionResult> SearchNameId([FromQuery] string nameId)
    {
        var value = await usersService.SearchUsersByNameId(nameId);
        return Ok(value);
    }

    [HttpGet("verify")]
    public async Task<IActionResult> VerifyUser([FromQuery] string userId)
    {
        var value = await usersService.VerifyUser(userId);
        return Ok(value);
    }

    public record  UserLogin
    {
        public string? email { get; set; }
        public string? password { get; set; }
    }
    [HttpPost("GetUser")]
    public async Task<IActionResult> GetUser([FromQuery] string userId, [FromBody] UserLogin user)
    {
        var value = await usersService.GetUser(userId, user);
        return Ok(value);
    }
    [HttpGet("GetUserById")]
    public async Task<IActionResult> GetUserById([FromQuery] string userId)
    {
        var value = await usersService.GetUserById(userId);
        return Ok(value);
    }
    
    [HttpGet("fetchall")]
    public async Task<IActionResult> fetchall()
    {
        var value = await usersService.FetchAll();
        return Ok(value);
    }

    public record AddIDS
    {
        public string uuid { get; set; }
        public string id { get; set; }
    }
    [HttpPost("AddIds")]
    public async Task<IActionResult> AddIds([FromBody] List<AddIDS> ids)
    {
         await usersService.AddIds(ids);
        return Ok(true);
    }
    [HttpPost("CreateUser")]
    public async Task<IActionResult> CreateUser([FromBody] UserCreateType user)
    {
         var result=await usersService.CreateUser(user);
        return Ok(result);
    }
    [HttpPost("LoadUsers")]
    public async Task<IActionResult> LoadUsers([FromBody] GetUsersRequest user)
    {
        var result=await usersService.LoadUsers(user);
        return Ok(result);
    }
     [HttpPost("UpdateUser")]
    public async Task<IActionResult> UpdateUser([FromBody] UserCreateType user)
    {
        var result=await usersService.UpdateUser(user);
        return Ok(result);
    }

}
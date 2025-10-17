using Microsoft.AspNetCore.Mvc;
using patentdesign.Models;
using patentdesign.Services;

namespace patentdesign.Controllers;


[ApiController]
[Route("api/payments")]
public class PaymentController(PaymentService paymentService) : ControllerBase
{
    [HttpPost("generate")]
    public async Task<ActionResult> Generate([FromQuery] string id, string name, string email, string number)
    {
        var data = await paymentService.GeneratePayment(
            id, name, email, number);
        return Ok(data);
    }

    [HttpPost("SaveOtherPayment")]
    public async Task<ActionResult> SaveOtherPayment([FromBody] OtherPaymentModel data)
    {
        var res = await paymentService.SaveOtherPayment(data);
        return Ok(res);
    }

    [HttpGet("GetOtherPayment")]
    public async Task<ActionResult> GetOtherPayment([FromQuery] int count, int skip, string? userId)
    {
        var res = await paymentService.GetOtherPayment(count, skip, userId);
        return Ok(res);
    }

    [HttpPost("UpdatePayment")]
    public async Task<ActionResult> UpdatePayment([FromBody] PaymentServiceModel data)
    {
        var res = await paymentService.UpdatePayment(data);
        return Ok(res);
    }

    [HttpGet("GetAllPayment")]
    public async Task<ActionResult> GetAllPayment()
    {
        var res = await paymentService.GetAllPayment();
        return Ok(res);
    }
    [HttpPost("AddPayment")]
    public async Task<ActionResult> AddPayment([FromBody] PaymentServiceModel data)
    {
        await paymentService.AddPayment(data);
        return Ok(true);
    }
    [HttpGet("Check")]
    public async Task<ActionResult> AddPayment([FromQuery]string id )
    {
        var result=await paymentService.CheckPayment(id);
        return Ok(result);
    }
 
}
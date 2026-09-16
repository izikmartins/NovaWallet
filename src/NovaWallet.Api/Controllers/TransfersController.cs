using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.DTOs;
using NovaWallet.Api.Services;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/transfers")]
[EnableRateLimiting("transfers")]
public class TransfersController : ControllerBase
{
    private readonly IWalletService _walletService;

    public TransfersController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpPost]
    public async Task<ActionResult<TransferResponse>> Transfer(
        [FromBody] TransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await _walletService.TransferAsync(
            request,
            idempotencyKey,
            cancellationToken);

        return Ok(result);
    }
}

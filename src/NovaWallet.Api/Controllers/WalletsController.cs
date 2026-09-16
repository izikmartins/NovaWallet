using Microsoft.AspNetCore.Mvc;
using NovaWallet.Api.DTOs;
using NovaWallet.Api.Services;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/wallets")]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletsController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<WalletResponse>> CreateWallet(
        [FromBody] CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var wallet = await _walletService.CreateWalletAsync(
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetBalance),
            new { walletId = wallet.Id },
            wallet);
    }

    [HttpGet("{walletId:guid}/balance")]
    public async Task<ActionResult<BalanceResponse>> GetBalance(
        Guid walletId,
        CancellationToken cancellationToken)
    {
        return Ok(await _walletService.GetBalanceAsync(
            walletId,
            cancellationToken));
    }

    [HttpPost("{walletId:guid}/credit")]
    public async Task<ActionResult<CreditResponse>> Credit(
        Guid walletId,
        [FromBody] CreditWalletRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _walletService.CreditAsync(
            walletId,
            request,
            cancellationToken));
    }

    [HttpGet("{walletId:guid}/statement")]
    public async Task<ActionResult<StatementResponse>> Statement(
        Guid walletId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _walletService.GetStatementAsync(
            walletId,
            page,
            pageSize,
            cancellationToken));
    }
}

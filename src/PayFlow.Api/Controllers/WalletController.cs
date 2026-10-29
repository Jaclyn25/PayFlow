using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;

namespace PayFlow.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Route("api/wallets")]
public class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStatementService _statementService;

    public WalletController(
        IWalletService walletService,
        ICurrentUserService currentUserService,
        IStatementService statementService)
    {
        _walletService = walletService;
        _currentUserService = currentUserService;
        _statementService = statementService;
    }

    [HttpGet]
    [HttpGet("me")]
    [ProducesResponseType(typeof(WalletResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWallet(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var wallet = await _walletService.GetWalletByUserIdAsync(userId, cancellationToken);
        return Ok(wallet);
    }

    [HttpPost("deposit")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Deposit(
        [FromBody] DepositRequest request,
        [FromServices] IValidator<DepositRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var userId = _currentUserService.UserId!.Value;
        var result = await _walletService.DepositAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("withdraw")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Withdraw(
        [FromBody] WithdrawRequest request,
        [FromServices] IValidator<WithdrawRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var userId = _currentUserService.UserId!.Value;
        var result = await _walletService.WithdrawAsync(userId, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("statement")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StatementSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUserStatement(
        [FromQuery] StatementQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        return await HandleStatementFormatAsync(userId, null, parameters, cancellationToken);
    }

    [HttpGet("{id:guid}/statement")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StatementSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWalletStatement(
        [FromRoute] Guid id,
        [FromQuery] StatementQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        return await HandleStatementFormatAsync(userId, id, parameters, cancellationToken);
    }

    private async Task<IActionResult> HandleStatementFormatAsync(
        Guid userId,
        Guid? walletId,
        StatementQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var format = parameters.Format?.Trim().ToLowerInvariant() ?? "pdf";
        var fromDate = parameters.From;
        var toDate = parameters.To;

        if (format == "csv")
        {
            var csvBytes = await _statementService.GenerateCsvStatementAsync(userId, walletId, fromDate, toDate, cancellationToken);
            var fileName = $"Statement_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(csvBytes, "text/csv", fileName);
        }

        if (format == "json")
        {
            var data = await _statementService.GetStatementDataAsync(userId, walletId, fromDate, toDate, cancellationToken);
            return Ok(data);
        }

        var pdfBytes = await _statementService.GeneratePdfStatementAsync(userId, walletId, fromDate, toDate, cancellationToken);
        var pdfFileName = $"Statement_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
        return File(pdfBytes, "application/pdf", pdfFileName);
    }

    [HttpGet("limits")]
    [ProducesResponseType(typeof(KycTierLimitsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetKycLimits(
        [FromServices] IKycLimitService kycLimitService,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var limits = await kycLimitService.GetUserLimitsAsync(userId, cancellationToken);
        return Ok(limits);
    }
}

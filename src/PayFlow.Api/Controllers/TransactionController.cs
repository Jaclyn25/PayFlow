using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;

namespace PayFlow.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TransactionController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ICurrentUserService _currentUserService;

    public TransactionController(ITransactionService transactionService, ICurrentUserService currentUserService)
    {
        _transactionService = transactionService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TransactionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] TransactionFilterParams filterParams,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _transactionService.GetTransactionsAsync(userId, filterParams, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _transactionService.GetTransactionByIdAsync(userId, id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reverse")]
    [ProducesResponseType(typeof(TransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReverseTransaction(
        [FromRoute] Guid id,
        [FromBody] ReverseTransactionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _transactionService.ReverseTransactionAsync(userId, id, request, idempotencyKey, cancellationToken);
        return Ok(result);
    }
}

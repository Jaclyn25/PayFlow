using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;
using PayFlow.Domain.Enums;

namespace PayFlow.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpPost("wallets/{walletId:guid}/freeze")]
    [ProducesResponseType(typeof(WalletFreezeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FreezeWalletByRoute(
        [FromRoute] Guid walletId,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        var result = await _adminService.FreezeWalletAsync(walletId, reason, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpPost("wallets/freeze")]
    [ProducesResponseType(typeof(WalletFreezeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FreezeWallet(
        [FromBody] FreezeWalletRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.WalletId.HasValue || request.WalletId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing Wallet ID",
                Detail = "A valid WalletId must be provided in the request body.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _adminService.FreezeWalletAsync(request.WalletId.Value, request.Reason, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpPost("wallets/{walletId:guid}/unfreeze")]
    [ProducesResponseType(typeof(WalletFreezeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnfreezeWalletByRoute(
        [FromRoute] Guid walletId,
        CancellationToken cancellationToken)
    {
        var result = await _adminService.UnfreezeWalletAsync(walletId, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpPost("wallets/unfreeze")]
    [ProducesResponseType(typeof(WalletFreezeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnfreezeWallet(
        [FromBody] UnfreezeWalletRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.WalletId.HasValue || request.WalletId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing Wallet ID",
                Detail = "A valid WalletId must be provided in the request body.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _adminService.UnfreezeWalletAsync(request.WalletId.Value, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAuditorRole)]
    [HttpGet("wallets")]
    [ProducesResponseType(typeof(PagedResult<WalletResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWallets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isFrozen = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _adminService.GetWalletsAsync(page, pageSize, isFrozen, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAuditorRole)]
    [HttpGet("ledger/reconcile")]
    [ProducesResponseType(typeof(LedgerReconciliationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLedgerReconciliation(CancellationToken cancellationToken)
    {
        var result = await _adminService.GetLedgerReconciliationAsync(cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAuditorRole)]
    [HttpGet("users")]
    [ProducesResponseType(typeof(PagedResult<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] AdminUserFilterParams filterParams,
        CancellationToken cancellationToken)
    {
        var result = await _adminService.GetUsersAsync(filterParams, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpPut("users/{userId:guid}/role")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserRole(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adminService.UpdateUserRoleAsync(userId, request.Role, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpGet("dlq/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<DeadLetterMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDeadLetterMessages(
        [FromQuery] int count,
        [FromServices] IDeadLetterQueueService dlqService,
        CancellationToken cancellationToken)
    {
        var messages = await dlqService.GetDeadLetterMessagesAsync(count <= 0 ? 20 : count, cancellationToken);
        return Ok(messages);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpPost("dlq/redeliver")]
    [ProducesResponseType(typeof(DlqRedeliveryResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RedeliverDeadLetterMessages(
        [FromQuery] int count,
        [FromServices] IDeadLetterQueueService dlqService,
        CancellationToken cancellationToken)
    {
        var result = await dlqService.RedeliverDeadLetterMessagesAsync(count <= 0 ? 10 : count, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = RoleConstants.RequireAdminRole)]
    [HttpDelete("dlq/purge")]
    [ProducesResponseType(typeof(DlqPurgeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PurgeDeadLetterQueue(
        [FromServices] IDeadLetterQueueService dlqService,
        CancellationToken cancellationToken)
    {
        var purgedCount = await dlqService.PurgeDeadLetterQueueAsync(cancellationToken);
        return Ok(new DlqPurgeResult(purgedCount, $"Successfully purged {purgedCount} message(s) from Dead Letter Queue."));
    }
}

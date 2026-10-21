using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;

namespace PayFlow.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/webhooks")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookSubscriptionService _subscriptionService;
    private readonly ICurrentUserService _currentUserService;

    public WebhookController(
        IWebhookSubscriptionService subscriptionService,
        ICurrentUserService currentUserService)
    {
        _subscriptionService = subscriptionService;
        _currentUserService = currentUserService;
    }

    [HttpPost("subscriptions")]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateSubscription(
        [FromBody] CreateWebhookSubscriptionRequest request,
        [FromServices] IValidator<CreateWebhookSubscriptionRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.CreateSubscriptionAsync(userId, request, cancellationToken);

        return CreatedAtAction(nameof(GetSubscriptionById), new { id = result.Id }, result);
    }

    [HttpGet("subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookSubscriptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSubscriptions(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.GetSubscriptionsAsync(userId, cancellationToken);
        return Ok(result);
    }

    [HttpGet("subscriptions/{id:guid}")]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscriptionById(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.GetSubscriptionByIdAsync(userId, id, cancellationToken);
        return Ok(result);
    }

    [HttpPut("subscriptions/{id:guid}")]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSubscription(
        Guid id,
        [FromBody] UpdateWebhookSubscriptionRequest request,
        [FromServices] IValidator<UpdateWebhookSubscriptionRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.UpdateSubscriptionAsync(userId, id, request, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("subscriptions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSubscription(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        await _subscriptionService.DeleteSubscriptionAsync(userId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("subscriptions/{id:guid}/rotate-secret")]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateSecret(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.RotateSecretAsync(userId, id, cancellationToken);
        return Ok(result);
    }

    [HttpGet("subscriptions/{id:guid}/deliveries")]
    [ProducesResponseType(typeof(PagedResult<WebhookDeliveryLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeliveryLogs(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.GetDeliveryLogsAsync(userId, id, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpPost("subscriptions/{id:guid}/test")]
    [ProducesResponseType(typeof(WebhookTestPingResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestPing(Guid id, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.TestPingAsync(userId, id, cancellationToken);
        return Ok(result);
    }

    [HttpPost("deliveries/{deliveryId:guid}/redeliver")]
    [ProducesResponseType(typeof(WebhookDeliveryLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Redeliver(Guid deliveryId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var result = await _subscriptionService.RedeliverAsync(userId, deliveryId, cancellationToken);
        return Ok(result);
    }
}

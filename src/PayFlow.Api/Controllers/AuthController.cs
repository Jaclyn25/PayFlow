using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayFlow.Application.Common.Interfaces;
using PayFlow.Application.DTOs;

namespace PayFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(IAuthService authService, ICurrentUserService currentUserService)
    {
        _authService = authService;
        _currentUserService = currentUserService;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        [FromServices] IValidator<RegisterRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var result = await _authService.RegisterAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IValidator<LoginRequest> validator,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        var result = await _authService.LoginAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RefreshTokenAsync(request, cancellationToken);
        return Ok(result);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] RevokeTokenRequest request,
        CancellationToken cancellationToken)
    {
        await _authService.RevokeTokenAsync(request, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var profile = await _authService.GetProfileAsync(userId, cancellationToken);
        return Ok(profile);
    }

    [Authorize]
    [HttpPut("kyc-tier")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateKycTier(
        [FromBody] UpdateKycTierRequest request,
        [FromServices] IKycLimitService kycLimitService,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var profile = await kycLimitService.UpdateUserTierAsync(userId, request.Tier, cancellationToken);
        return Ok(profile);
    }

    [Authorize]
    [HttpPost("2fa/setup")]
    [ProducesResponseType(typeof(TwoFactorSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SetupTwoFactor(
        [FromServices] ITwoFactorAuthService twoFactorService,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var setup = await twoFactorService.SetupAsync(userId, cancellationToken);
        return Ok(setup);
    }

    [Authorize]
    [HttpPost("2fa/enable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> EnableTwoFactor(
        [FromBody] EnableTwoFactorRequest request,
        [FromServices] ITwoFactorAuthService twoFactorService,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var success = await twoFactorService.EnableAsync(userId, request.Code, cancellationToken);
        if (!success)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Code",
                Detail = "The provided two-factor authentication code is invalid or expired.",
                Status = StatusCodes.Status400BadRequest
            });
        }
        return Ok(new { enabled = true, message = "Two-factor authentication has been successfully enabled." });
    }

    [Authorize]
    [HttpPost("2fa/disable")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DisableTwoFactor(
        [FromBody] DisableTwoFactorRequest request,
        [FromServices] ITwoFactorAuthService twoFactorService,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var success = await twoFactorService.DisableAsync(userId, request.Code, cancellationToken);
        if (!success)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Code",
                Detail = "The provided two-factor authentication code or recovery code is invalid.",
                Status = StatusCodes.Status400BadRequest
            });
        }
        return Ok(new { enabled = false, message = "Two-factor authentication has been disabled." });
    }

    [Authorize]
    [HttpGet("2fa/status")]
    [ProducesResponseType(typeof(TwoFactorStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTwoFactorStatus(
        [FromServices] ITwoFactorAuthService twoFactorService,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId!.Value;
        var status = await twoFactorService.GetStatusAsync(userId, cancellationToken);
        return Ok(status);
    }

    [AllowAnonymous]
    [HttpPost("2fa/verify")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyTwoFactor(
        [FromBody] VerifyTwoFactorRequest request,
        [FromServices] ITwoFactorAuthService twoFactorService,
        CancellationToken cancellationToken)
    {
        var authResponse = await twoFactorService.VerifyTwoFactorLoginAsync(request, cancellationToken);
        return Ok(authResponse);
    }
}



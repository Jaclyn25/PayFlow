using FluentValidation;
using PayFlow.Application.DTOs;
using PayFlow.Domain.Constants;

namespace PayFlow.Application.Validators;

public class CreateWebhookSubscriptionRequestValidator : AbstractValidator<CreateWebhookSubscriptionRequest>
{
    public CreateWebhookSubscriptionRequestValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty().WithMessage("Webhook URL is required.")
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && 
                         (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
            .WithMessage("Webhook URL must be a valid absolute HTTP or HTTPS URL.")
            .MaximumLength(2048).WithMessage("Webhook URL cannot exceed 2048 characters.");

        RuleFor(x => x.Events)
            .NotEmpty().WithMessage("At least one event type must be specified.")
            .Must(events => events.All(e => e == "*" || WebhookEventConstants.IsSupported(e)))
            .WithMessage($"All events must be valid event types or '*' (wildcard). Supported: {string.Join(", ", WebhookEventConstants.AllSupportedEvents)}.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}

public class UpdateWebhookSubscriptionRequestValidator : AbstractValidator<UpdateWebhookSubscriptionRequest>
{
    public UpdateWebhookSubscriptionRequestValidator()
    {
        RuleFor(x => x.Url)
            .Must(uri => string.IsNullOrEmpty(uri) || (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && 
                         (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)))
            .WithMessage("Webhook URL must be a valid absolute HTTP or HTTPS URL.")
            .MaximumLength(2048).WithMessage("Webhook URL cannot exceed 2048 characters.");

        RuleFor(x => x.Events)
            .Must(events => events == null || events.Count == 0 || events.All(e => e == "*" || WebhookEventConstants.IsSupported(e)))
            .WithMessage($"All events must be valid event types or '*' (wildcard). Supported: {string.Join(", ", WebhookEventConstants.AllSupportedEvents)}.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}

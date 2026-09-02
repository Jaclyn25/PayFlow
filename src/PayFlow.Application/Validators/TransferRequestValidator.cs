using FluentValidation;
using PayFlow.Application.DTOs;

namespace PayFlow.Application.Validators;

public class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.ReceiverEmail)
            .NotEmpty().WithMessage("Receiver email is required.")
            .EmailAddress().WithMessage("A valid receiver email address is required.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Transfer amount must be greater than zero.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Transfer amount cannot exceed 1,000,000.");

        RuleFor(x => x.Description)
            .MaximumLength(255).WithMessage("Description cannot exceed 255 characters.");
    }
}

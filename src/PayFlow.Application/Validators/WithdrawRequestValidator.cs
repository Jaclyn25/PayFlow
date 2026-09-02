using FluentValidation;
using PayFlow.Application.DTOs;

namespace PayFlow.Application.Validators;

public class WithdrawRequestValidator : AbstractValidator<WithdrawRequest>
{
    public WithdrawRequestValidator()
    {
        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Withdrawal amount must be greater than zero.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Withdrawal amount cannot exceed 1,000,000.");

        RuleFor(x => x.Description)
            .MaximumLength(255).WithMessage("Description cannot exceed 255 characters.");
    }
}

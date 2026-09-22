using FluentValidation;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Validators;

public sealed class CreateItemRequestValidator : AbstractValidator<CreateItemRequest>
{
    public CreateItemRequestValidator()
    {
        RuleFor(request => request.Sku)
            .NotEmpty()
            .Length(2, 20)
            .Matches("^[A-Z0-9-]+$")
            .WithMessage("Sku may only contain upper-case letters, digits and hyphens.");

        RuleFor(request => request.Name)
            .NotEmpty()
            .Length(2, 100);

        RuleFor(request => request.Quantity)
            .InclusiveBetween(0, 1_000_000);
    }
}

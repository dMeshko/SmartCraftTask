using FluentValidation;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Validators;

public sealed class UpdateItemRequestValidator : AbstractValidator<UpdateItemRequest>
{
    public UpdateItemRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .Length(2, 100);

        RuleFor(request => request.Quantity)
            .InclusiveBetween(0, 1_000_000);
    }
}

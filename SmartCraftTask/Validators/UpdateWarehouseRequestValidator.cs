using FluentValidation;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Validators;

public sealed class UpdateWarehouseRequestValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseRequestValidator(IValidator<AddressDto> addressValidator)
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .Length(2, 100);

        RuleFor(request => request.Address)
            .NotNull()
            .WithMessage("Address is required.");

        RuleFor(request => request.Address!)
            .SetValidator(addressValidator)
            .When(request => request.Address is not null);

        RuleFor(request => request.CapacityInPallets)
            .InclusiveBetween(0, 100_000);
    }
}

using FluentValidation;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Validators;

public sealed class AddressDtoValidator : AbstractValidator<AddressDto>
{
    public AddressDtoValidator()
    {
        RuleFor(address => address.Street)
            .NotEmpty()
            .Length(2, 200);

        RuleFor(address => address.PostalCode)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(address => address.City)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(address => address.Country)
            .NotEmpty()
            .MaximumLength(100);
    }
}

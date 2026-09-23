using FluentValidation;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Validators;

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(request => request.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("pageNumber is one-based: the first page is 1.");

        // Refused rather than clamped. Silently serving 100 rows to a caller who asked for 5000
        // looks like success and hides the bug; a 400 says which knob was wrong.
        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize)
            .WithMessage($"pageSize must be between 1 and {PageRequest.MaxPageSize}.");
    }
}

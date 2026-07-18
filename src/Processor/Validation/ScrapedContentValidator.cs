using FluentValidation;
using Processor.Models;

namespace Processor.Validation;

public class ScrapedContentValidator : AbstractValidator<ScrapedContent>
{
    public ScrapedContentValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(512);
        RuleFor(x => x.BodyText).NotEmpty()
            .WithMessage("Body text must not be empty after cleaning");
        RuleFor(x => x.BodyText).MinimumLength(50)
            .WithMessage("Body text is too short - likely failed extraction");
        RuleForEach(x => x.Tables).SetValidator(new TableDataValidator());
    }
}

public class TableDataValidator : AbstractValidator<TableData>
{
    public TableDataValidator()
    {
        RuleFor(x => x.Rows).NotEmpty().WithMessage("Table must have at least one row");
    }
}

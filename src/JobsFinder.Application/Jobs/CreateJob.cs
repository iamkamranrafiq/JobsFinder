using FluentValidation;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Domain.Jobs;

namespace JobsFinder.Application.Jobs;

public sealed record CreateJobCommand(
    string Title,
    string Company,
    string? Location,
    RemotePolicy RemotePolicy,
    Uri Url,
    DateTimeOffset PostedAt);

public sealed class CreateJobValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Job.TitleMaxLength);
        RuleFor(x => x.Company).NotEmpty().MaximumLength(Job.CompanyMaxLength);
        RuleFor(x => x.Location).MaximumLength(Job.LocationMaxLength);
        RuleFor(x => x.RemotePolicy).IsInEnum();
        RuleFor(x => x.Url)
            .NotNull()
            .Must(u => u.IsAbsoluteUri && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Url must be an absolute http(s) URL.")
            .Must(u => u.OriginalString.Length <= Job.UrlMaxLength);
        RuleFor(x => x.PostedAt).LessThanOrEqualTo(_ => timeProvider.GetUtcNow());
    }
}

public sealed class CreateJobHandler(
    IJobRepository jobs,
    IUnitOfWork unitOfWork,
    IValidator<CreateJobCommand> validator,
    TimeProvider timeProvider)
{
    public async Task<Result<JobDto>> HandleAsync(CreateJobCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (await jobs.ExistsByUrlAsync(command.Url, cancellationToken))
        {
            return Error.Conflict("job_duplicate_url", "A job with this URL already exists.");
        }

        var job = Job.Create(
            command.Title,
            command.Company,
            command.Location,
            command.RemotePolicy,
            command.Url,
            command.PostedAt,
            timeProvider.GetUtcNow());

        jobs.Add(job);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return job.ToDto();
    }
}

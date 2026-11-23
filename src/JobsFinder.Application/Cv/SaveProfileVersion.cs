using FluentValidation;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <param name="CvDocumentId">The CV whose profile is corrected.</param>
/// <param name="BaseVersion">The version the candidate edited; must still be the latest (optimistic concurrency).</param>
/// <param name="Data">The full corrected profile.</param>
public sealed record SaveProfileVersionCommand(Guid CvDocumentId, int BaseVersion, CandidateProfileDataDto Data);

/// <summary>Same field rules as model output (F0.1-BR2), so a saved profile always meets the schema rules.</summary>
public sealed class SaveProfileVersionValidator : AbstractValidator<SaveProfileVersionCommand>
{
    public SaveProfileVersionValidator()
    {
        RuleFor(x => x.CvDocumentId).NotEmpty();
        RuleFor(x => x.BaseVersion).GreaterThanOrEqualTo(CandidateProfile.FirstVersion);
        RuleFor(x => x.Data).NotNull().SetValidator(new CandidateProfileDataValidator());
    }
}

/// <summary>
/// F0.1-AC4, AC5, BR3: every save stores a new profile version (even an unchanged one). The new version is
/// the newest profile row, so it becomes the active profile. Earlier versions are never modified.
/// </summary>
public sealed class SaveProfileVersionHandler(
    ICvRepository cvs,
    IUnitOfWork unitOfWork,
    IValidator<SaveProfileVersionCommand> validator,
    TimeProvider timeProvider)
{
    public async Task<Result<CvDto>> HandleAsync(SaveProfileVersionCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var latest = await cvs.GetLatestProfileVersionAsync(command.CvDocumentId, cancellationToken);
        if (latest is null)
        {
            return CvErrors.NotFound(command.CvDocumentId);
        }

        if (command.BaseVersion != latest.Value)
        {
            return CvErrors.ProfileVersionConflict;
        }

        var profile = CandidateProfile.CreateNextVersion(
            command.CvDocumentId, latest.Value, command.Data.ToDomain(), timeProvider.GetUtcNow());
        cvs.Add(profile);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Another save from the same base version won the race for this version number.
            return CvErrors.ProfileVersionConflict;
        }

        var cv = await cvs.GetAsync(command.CvDocumentId, cancellationToken);
        return cv is null ? CvErrors.NotFound(command.CvDocumentId) : cv;
    }
}

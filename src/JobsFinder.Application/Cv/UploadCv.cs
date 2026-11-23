using System.Text;
using FluentValidation;
using JobsFinder.Application.Abstractions;
using JobsFinder.Application.Common;
using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <param name="FileName">Original file name as sent by the client (used for download only, never for type detection).</param>
/// <param name="Content">The uploaded bytes.</param>
public sealed record UploadCvCommand(string FileName, byte[] Content);

public sealed class UploadCvValidator : AbstractValidator<UploadCvCommand>
{
    public UploadCvValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("A CV file is required.")
            .MaximumLength(CvDocument.FileNameMaxLength);
        RuleFor(x => x.Content)
            .NotNull()
            .Must(c => c.Length > 0).WithMessage("The CV file is empty.");
    }
}

/// <summary>Tunables for CV parsing. Registered with defaults; tests shorten the budget.</summary>
public sealed class CvParsingSettings
{
    /// <summary>Whole-parse budget, kept under the 60 s NFR (F0.1-AC1).</summary>
    public TimeSpan ParseBudget { get; init; } = TimeSpan.FromSeconds(55);

    /// <summary>Retries after an invalid model response (F0.1-BR4): at most 2, so at most 3 attempts.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Extracted text beyond this is cut before it is sent to the model.</summary>
    public int MaxTextLength { get; init; } = 60_000;
}

/// <summary>
/// F0.1-AC1/AC2/AC3/AC6, BR1, BR4: check the file, extract its text, ask the model for a profile,
/// validate it, and store the raw file plus profile in one save. On any failure nothing is stored.
/// </summary>
public sealed class UploadCvHandler(
    ICvRepository cvs,
    IUnitOfWork unitOfWork,
    IValidator<UploadCvCommand> validator,
    ICvTextExtractor extractor,
    IAIProvider ai,
    CvParsingSettings settings,
    TimeProvider timeProvider)
{
    public const string SystemPrompt =
        """
        You extract a structured candidate profile from the text of a CV (resume).
        Rules:
        - Use only facts stated in the CV. Never invent, infer or embellish anything.
        - If something is not stated, use null for single values and an empty array for lists.
        - Field names are fixed English keys from the schema. If the CV is not in English, still fill the same
          English fields; keep names, companies, titles and achievements as written in the CV.
        - Dates are "YYYY-MM" when the month is known, otherwise "YYYY". Set isCurrent to true for an ongoing role.
        - currentEmployer is the company of the ongoing role, or null if no role is ongoing.
        - Links must be absolute URLs starting with https:// or http://.
        - Skill years and proficiency only when the CV states or clearly shows them; otherwise null.
        - Preferences (target roles, locations, remote, salary, notice period) only when the CV states them.
        Answer only with the JSON object that matches the schema.
        """;

    public async Task<Result<CvDto>> HandleAsync(UploadCvCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        if (command.Content.Length > CvDocument.MaxSizeBytes)
        {
            return CvErrors.TooLarge;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(settings.ParseBudget);

        try
        {
            var extraction = await extractor.ExtractAsync(command.Content, budget.Token);
            if (!extraction.IsSuccess)
            {
                return CvErrors.FromRejection(extraction.Rejection!.Value);
            }

            var profile = await ParseProfileAsync(extraction.Text!, budget.Token);
            if (profile is null)
            {
                return CvErrors.Unreadable;
            }

            var now = timeProvider.GetUtcNow();
            var document = CvDocument.Create(command.FileName, extraction.Format!.Value, command.Content, now);
            var candidateProfile = CandidateProfile.CreateFromParse(document.Id, profile.ToDomain(), now);

            cvs.Add(document);
            cvs.Add(candidateProfile);

            // Save with the caller's token: once parsing succeeded, the budget no longer applies.
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new CvDto(document.ToDto(), candidateProfile.ToDto());
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return CvErrors.ParseTimeout;
        }
        catch (AIProviderException)
        {
            return CvErrors.AIUnavailable;
        }
    }

    /// <summary>One attempt plus at most <see cref="CvParsingSettings.MaxRetries"/> retries; null when all are invalid.</summary>
    private async Task<CandidateProfileDataDto?> ParseProfileAsync(string text, CancellationToken cancellationToken)
    {
        var cvText = text.Length > settings.MaxTextLength ? text[..settings.MaxTextLength] : text;
        IReadOnlyList<string> previousErrors = [];

        for (var attempt = 0; attempt <= settings.MaxRetries; attempt++)
        {
            var request = new AIJsonRequest(
                SystemPrompt,
                BuildUserContent(cvText, previousErrors),
                CandidateProfileSchema.Json,
                CandidateProfileSchema.Name);

            var json = await ai.CompleteJsonAsync(request, cancellationToken);
            var parsed = CandidateProfileSchema.Parse(json);
            if (parsed.IsValid)
            {
                return parsed.Profile;
            }

            previousErrors = parsed.Errors;
        }

        return null;
    }

    private static string BuildUserContent(string cvText, IReadOnlyList<string> previousErrors)
    {
        if (previousErrors.Count == 0)
        {
            return cvText;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Your previous answer was rejected for these reasons; return a corrected JSON object:");
        foreach (var error in previousErrors.Take(10))
        {
            builder.Append("- ").AppendLine(error);
        }

        builder.AppendLine().AppendLine("CV text:").Append(cvText);
        return builder.ToString();
    }
}

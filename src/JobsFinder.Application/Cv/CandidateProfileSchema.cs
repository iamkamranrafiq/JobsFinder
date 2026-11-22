using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentValidation;
using JobsFinder.Domain.Cv;

namespace JobsFinder.Application.Cv;

/// <summary>
/// The JSON contract between the CV parser and the model (F0.1-BR4). The schema is generated from
/// <see cref="CandidateProfileDataDto"/>, and <see cref="Parse"/> enforces it: strict deserialization
/// (required fields, nullability, no unknown properties) followed by <see cref="CandidateProfileDataValidator"/>.
/// </summary>
public static class CandidateProfileSchema
{
    public const string Name = "candidate_profile";

    /// <summary>Serializer options for model output: camelCase, string enums, strict.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>The schema sent to the model, as JSON text.</summary>
    public static string Json { get; } = BuildSchema().ToJsonString();

    private static readonly CandidateProfileDataValidator Validator = new();

    public static ProfileParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ProfileParseResult.Invalid(["The response was empty."]);
        }

        CandidateProfileDataDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CandidateProfileDataDto>(json, SerializerOptions);
        }
        catch (JsonException e)
        {
            return ProfileParseResult.Invalid([$"The response does not match the schema: {e.Message}"]);
        }

        if (dto is null)
        {
            return ProfileParseResult.Invalid(["The response was null."]);
        }

        var validation = Validator.Validate(dto);
        return validation.IsValid
            ? ProfileParseResult.Valid(dto)
            : ProfileParseResult.Invalid([.. validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")]);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { new JsonStringEnumConverter() },
        };
        options.MakeReadOnly();
        return options;
    }

    private static JsonNode BuildSchema()
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (_, node) =>
            {
                // The model must not invent extra fields: close every object.
                if (node is JsonObject obj && obj.ContainsKey("properties"))
                {
                    obj["additionalProperties"] = false;
                }

                return node;
            },
        };

        return SerializerOptions.GetJsonSchemaAsNode(typeof(CandidateProfileDataDto), exporterOptions);
    }
}

public sealed record ProfileParseResult(CandidateProfileDataDto? Profile, IReadOnlyList<string> Errors)
{
    public bool IsValid => Profile is not null;

    public static ProfileParseResult Valid(CandidateProfileDataDto profile) => new(profile, []);

    public static ProfileParseResult Invalid(IReadOnlyList<string> errors) => new(null, errors);
}

/// <summary>Business-level checks on model output that a JSON schema cannot express well.</summary>
public sealed class CandidateProfileDataValidator : AbstractValidator<CandidateProfileDataDto>
{
    private const int Short = CandidateProfileData.ShortTextMaxLength;
    private const int Long = CandidateProfileData.LongTextMaxLength;
    private const int MaxItems = CandidateProfileData.MaxListItems;
    private const string DatePattern = @"^\d{4}(-(0[1-9]|1[0-2]))?$";

    public CandidateProfileDataValidator()
    {
        RuleFor(x => x.Identity).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.FullName).MaximumLength(Short);
            i.RuleFor(x => x.Headline).MaximumLength(Short);
        });

        RuleFor(x => x.Contact).NotNull().ChildRules(c =>
        {
            c.RuleFor(x => x.Email).MaximumLength(Short);
            c.RuleFor(x => x.Phone).MaximumLength(Short);
            c.RuleFor(x => x.Location).MaximumLength(Short);
        });

        RuleFor(x => x.Summary).MaximumLength(Long);
        RuleFor(x => x.WorkAuthorisation).MaximumLength(Short);
        RuleFor(x => x.CurrentEmployer).MaximumLength(Short);

        RuleFor(x => x.Skills).NotNull().Must(l => l.Count <= MaxItems);
        RuleForEach(x => x.Skills).NotNull().ChildRules(s =>
        {
            s.RuleFor(x => x.Name).NotEmpty().MaximumLength(Short);
            s.RuleFor(x => x.Years).InclusiveBetween(0, 70);
        });

        RuleFor(x => x.Experience).NotNull().Must(l => l.Count <= MaxItems);
        RuleForEach(x => x.Experience).NotNull().ChildRules(e =>
        {
            e.RuleFor(x => x.Role).NotEmpty().MaximumLength(Short);
            e.RuleFor(x => x.Company).NotEmpty().MaximumLength(Short);
            e.RuleFor(x => x.StartDate).Matches(DatePattern);
            e.RuleFor(x => x.EndDate).Matches(DatePattern);
            e.RuleFor(x => x.Achievements).NotNull().Must(l => l.Count <= MaxItems);
            e.RuleForEach(x => x.Achievements).NotNull().MaximumLength(Long);
        });

        RuleFor(x => x.Education).NotNull().Must(l => l.Count <= MaxItems);
        RuleForEach(x => x.Education).NotNull().ChildRules(e =>
        {
            e.RuleFor(x => x.Institution).NotEmpty().MaximumLength(Short);
            e.RuleFor(x => x.Qualification).MaximumLength(Short);
            e.RuleFor(x => x.Field).MaximumLength(Short);
            e.RuleFor(x => x.StartDate).Matches(DatePattern);
            e.RuleFor(x => x.EndDate).Matches(DatePattern);
        });

        RuleFor(x => x.Certifications).NotNull().Must(l => l.Count <= MaxItems);
        RuleForEach(x => x.Certifications).NotNull().ChildRules(c =>
        {
            c.RuleFor(x => x.Name).NotEmpty().MaximumLength(Short);
            c.RuleFor(x => x.Issuer).MaximumLength(Short);
            c.RuleFor(x => x.Year).InclusiveBetween(1900, 2100);
        });

        RuleFor(x => x.Links).NotNull().Must(l => l.Count <= MaxItems);
        RuleForEach(x => x.Links).NotNull().ChildRules(l =>
        {
            l.RuleFor(x => x.Label).MaximumLength(Short);
            l.RuleFor(x => x.Url)
                .NotEmpty()
                .MaximumLength(CandidateProfileData.UrlMaxLength)
                .Must(BeAbsoluteHttpUrl)
                .WithMessage("Url must be an absolute http(s) URL.");
        });

        RuleFor(x => x.Preferences).NotNull().ChildRules(p =>
        {
            p.RuleFor(x => x.TargetRoles).NotNull().Must(l => l.Count <= MaxItems);
            p.RuleForEach(x => x.TargetRoles).NotNull().MaximumLength(Short);
            p.RuleFor(x => x.Locations).NotNull().Must(l => l.Count <= MaxItems);
            p.RuleForEach(x => x.Locations).NotNull().MaximumLength(Short);
            p.RuleFor(x => x.SalaryMin).GreaterThanOrEqualTo(0);
            p.RuleFor(x => x.SalaryMax).GreaterThanOrEqualTo(0);
            p.RuleFor(x => x.SalaryMax)
                .GreaterThanOrEqualTo(x => x.SalaryMin)
                .When(x => x.SalaryMin is not null && x.SalaryMax is not null)
                .WithMessage("SalaryMax must not be less than SalaryMin.");
            p.RuleFor(x => x.SalaryCurrency).MaximumLength(10);
            p.RuleFor(x => x.NoticePeriod).MaximumLength(Short);
        });
    }

    private static bool BeAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

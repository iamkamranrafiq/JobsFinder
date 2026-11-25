using System.Reflection;
using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Applying;
using JobsFinder.UnitTests.Application.Cv;

namespace JobsFinder.UnitTests.Application.Applying;

[Trait("REQ", "F0.3-BR2")]
public sealed class FieldMapperTests
{
    private static readonly CandidateProfileDataDto Profile = CvSamples.Profile() with
    {
        Links =
        [
            new ProfileLinkDto("GitHub", "https://github.com/ada"),
            new ProfileLinkDto(null, "https://uk.linkedin.com/in/ada"),
            new ProfileLinkDto("Portfolio", "https://ada.example.com"),
        ],
    };

    private static readonly HashSet<string> NothingAsked = [];

    [Theory]
    [InlineData("Full Name", "Ada Lovelace", "identity.fullName")]
    [InlineData("First Name *", "Ada", "identity.fullName")]
    [InlineData("Last name (required)", "Lovelace", "identity.fullName")]
    [InlineData("E-mail", "ada@example.com", "contact.email")]
    [InlineData("  PHONE   number ", "+44 20 7946 0000", "contact.phone")]
    [InlineData("Location", "London, UK", "contact.location")]
    [InlineData("LinkedIn Profile", "https://uk.linkedin.com/in/ada", "links[1].url")]
    [InlineData("GitHub URL", "https://github.com/ada", "links[0].url")]
    [InlineData("Website", "https://ada.example.com", "links[2].url")]
    [InlineData("Current Company", "Analytical Engines Ltd", "currentEmployer")]
    [InlineData("Current title", "Senior Engineer", "experience[0].role")]
    [InlineData("Notice period", "3 months", "preferences.noticePeriod")]
    public void Map_KnownLabel_UsesTheMatchingFact_F0_3_AC1(string label, string value, string reference)
    {
        var plan = MapOne(Form.Field(label));

        Assert.Equal((value, reference), (plan.Evidence?.Value, plan.Evidence?.Reference));
    }

    [Theory]
    [InlineData("Resume/CV")]
    [InlineData("Résumé")]
    [InlineData("Curriculum Vitae *")]
    public void Map_CvField_AttachesTheCv_F0_3_AC1(string label) =>
        Assert.True(MapOne(Form.Field(label, FormFieldKind.File)).AttachCv);

    [Theory]
    [InlineData("Cover letter", NeedsHumanReason.NeverAnswered)]
    [InlineData("Portfolio", NeedsHumanReason.UnknownQuestion)]
    [InlineData("Attach", NeedsHumanReason.UnknownQuestion)]
    public void Map_OtherFileField_IsLeft_F0_3_BR2(string label, NeedsHumanReason reason) =>
        Assert.Equal(reason, MapOne(Form.Field(label, FormFieldKind.File)).Reason);

    [Theory]
    [InlineData("Preferred first name")]
    [InlineData("Legal name")]
    [InlineData("Email of your current manager's assistant")]
    [InlineData("Name of your university")]
    [InlineData("Years of C# experience")]
    [InlineData("What is your LinkedIn profile?")]
    [InlineData("Prénom")]
    [InlineData("")]
    [InlineData("!!!")]
    public void Map_AnyOtherLabel_IsNeverFilled_F0_3_BR2(string label)
    {
        var plan = MapOne(Form.Field(label));

        Assert.Null(plan.Evidence);
        Assert.NotNull(plan.Reason);
    }

    [Theory]
    [InlineData("Gender")]
    [InlineData("What are your pronouns?")]
    [InlineData("Are you a protected veteran?")]
    [InlineData("Disability status")]
    [InlineData("Salary expectations")]
    [InlineData("Are you legally authorized to work in the US?")]
    [InlineData("Will you require visa sponsorship?")]
    [InlineData("Why do you want to join us?")]
    [InlineData("How did you hear about us?")]
    [InlineData("I agree to the terms")]
    [InlineData("Date of birth")]
    [InlineData("Password")]
    [InlineData("Emergency contact phone")]
    public void Map_SensitiveScreeningOrConsentQuestion_IsNeverAnswered_F0_3_BR2(string label) =>
        Assert.Equal(NeedsHumanReason.NeverAnswered, MapOne(Form.Field(label)).Reason);

    [Fact]
    public void Map_KnownLabelInSomebodyElsesGroup_IsNeverAnswered_F0_3_BR2() =>
        Assert.Equal(NeedsHumanReason.NeverAnswered, MapOne(Form.Field("Full name", group: "Reference 1")).Reason);

    [Fact]
    public void Map_CheckboxWithAKnownLabel_IsNeverAnswered_F0_3_BR2() =>
        Assert.Equal(NeedsHumanReason.NeverAnswered, MapOne(Form.Field("Email", FormFieldKind.Choice)).Reason);

    [Theory]
    [InlineData("Phone", "email", "")]
    [InlineData("Email", "tel", "")]
    [InlineData("Full name", "", "email")]
    [InlineData("Phone", "number", "")]
    [InlineData("Email", "password", "")]
    [InlineData("Notice period", "date", "")]
    public void Map_LabelAndFieldDisagree_IsAmbiguous_F0_3_BR2(string label, string inputType, string autocomplete) =>
        Assert.Equal(
            NeedsHumanReason.Ambiguous,
            MapOne(Form.Field(label, inputType: inputType, autocomplete: autocomplete)).Reason);

    [Fact]
    public void Map_SameQuestionTwice_AnswersNeither_F0_3_BR2()
    {
        var plans = FieldMapper.Map([Form.Field("Email"), Form.Field("Email address")], Facts(Profile), NothingAsked);

        Assert.All(plans, p => Assert.Equal(NeedsHumanReason.Ambiguous, p.Reason));
    }

    [Fact]
    public void Map_QuestionOfAnEarlierStep_IsAmbiguous_F0_3_BR2()
    {
        var plan = FieldMapper.Map([Form.Field("Email")], Facts(Profile), new HashSet<string> { nameof(FactKind.Email) });

        Assert.Equal(NeedsHumanReason.Ambiguous, plan[0].Reason);
    }

    [Fact]
    public void Map_FactIsMissing_HasNoEvidence_F0_3_BR2()
    {
        var plan = FieldMapper.Map([Form.Field("Email")], Facts(CandidateProfileDataDto.Empty), NothingAsked);

        Assert.Equal(NeedsHumanReason.NoEvidence, plan[0].Reason);
    }

    [Theory]
    [InlineData(14, null)]
    [InlineData(null, @"\d+")]
    [InlineData(null, "(")]
    [InlineData(null, @"(?=a)ada@example\.com")]
    public void Map_ValueDoesNotFit_IsNotChanged_F0_3_BR2(int? maxLength, string? pattern) =>
        Assert.Equal(
            NeedsHumanReason.DoesNotFit,
            MapOne(Form.Field("Email", maxLength: maxLength, pattern: pattern)).Reason);

    [Fact]
    public void Map_ValueFits_IsFilled_F0_3_AC1() =>
        Assert.NotNull(MapOne(Form.Field("Email", maxLength: 15, pattern: @"[^@]+@[^@]+")).Evidence);

    [Theory]
    [InlineData(true, "1 month", " 3  MONTHS ", "Other")]
    [InlineData(false, "1 month", "3 months or more")]
    [InlineData(false, "3 months", "3 Months")]
    [InlineData(false, "Three months")]
    [InlineData(false, "3-months")]
    [InlineData(false)]
    public void Map_Select_NeedsExactlyOneEqualOption_F0_3_BR2(bool matches, params string[] options)
    {
        var plan = MapOne(Form.Field("Notice period", FormFieldKind.Select, options: options));

        Assert.Equal(matches ? null : NeedsHumanReason.NoMatchingOption, plan.Reason);
    }

    [Fact]
    public void Map_CountryForALocation_IsNotInferred_F0_3_BR2() =>
        Assert.Equal(
            NeedsHumanReason.NoMatchingOption,
            MapOne(Form.Field("Location", FormFieldKind.Select, options: ["United Kingdom", "London"])).Reason);

    [Theory]
    [InlineData("Ada")]
    [InlineData("Ada King Lovelace")]
    [InlineData("Maria del Carmen")]
    public void FromProfile_NameThatIsNotTwoWords_IsNotSplit_F0_3_BR2(string name)
    {
        var facts = Facts(Profile with { Identity = new ProfileIdentityDto(name, null) });

        Assert.Contains(facts, f => f.Kind == FactKind.FullName);
        Assert.DoesNotContain(facts, f => f.Kind is FactKind.FirstName or FactKind.LastName);
    }

    [Theory]
    [InlineData("Ada Lovelaceada@example.com")]
    [InlineData("Ada Lovelace 1815")]
    [InlineData("Ada\nLovelace")]
    [InlineData("   ")]
    [InlineData(null)]
    public void FromProfile_ImplausibleName_IsNoEvidence_F0_3_BR2(string? name) =>
        Assert.DoesNotContain(
            Facts(Profile with { Identity = new ProfileIdentityDto(name, null) }),
            f => f.Kind is FactKind.FullName or FactKind.FirstName or FactKind.LastName);

    [Theory]
    [InlineData("ada@example.com+44 20", "+44 20 7946 0000")]
    [InlineData("ada", "+44 20 7946 0000")]
    [InlineData("ada@example", "+44 20 7946 0000")]
    [InlineData("ada@example.com\nbcc@example.com", "+44 20 7946 0000")]
    public void FromProfile_ImplausibleEmail_IsNoEvidence_F0_3_BR2(string email, string phone)
    {
        var facts = Facts(Profile with { Contact = new ProfileContactDto(email, phone, null) });

        Assert.DoesNotContain(facts, f => f.Kind == FactKind.Email);
        Assert.Contains(facts, f => f.Kind == FactKind.Phone);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+44 20 7946 0000 ext. 5")]
    [InlineData("2019 - 2021 - 2022 - 2023")]
    [InlineData("call me")]
    public void FromProfile_ImplausiblePhone_IsNoEvidence_F0_3_BR2(string phone) =>
        Assert.DoesNotContain(
            Facts(Profile with { Contact = new ProfileContactDto(null, phone, null) }),
            f => f.Kind == FactKind.Phone);

    [Fact]
    public void FromProfile_TwoCandidatesForAFact_IsNoEvidence_F0_3_BR2()
    {
        var facts = Facts(Profile with
        {
            Links = [new ProfileLinkDto(null, "https://github.com/ada"), new ProfileLinkDto(null, "https://github.com/analytical")],
            Experience =
            [
                new ProfileExperienceDto("Engineer", "A", null, null, true, []),
                new ProfileExperienceDto("Teacher", "B", null, null, true, []),
            ],
        });

        Assert.DoesNotContain(facts, f => f.Kind is FactKind.GitHub or FactKind.CurrentTitle);
    }

    [Theory]
    [InlineData("https://evilgithub.com/ada")]
    [InlineData("https://github.com.evil.com/ada")]
    [InlineData("https://user:pass@github.com/ada")]
    [InlineData("ftp://github.com/ada")]
    public void FromProfile_LookAlikeOrUnsafeLink_IsNoEvidence_F0_3_BR2(string url) =>
        Assert.DoesNotContain(
            Facts(Profile with { Links = [new ProfileLinkDto("GitHub", url)] }),
            f => f.Kind == FactKind.GitHub);

    [Fact]
    public void EvidenceValue_CanOnlyComeFromAProfile_F0_3_BR2()
    {
        // The whole point of the type: no constructor, setter or other factory that takes a text.
        Assert.Empty(typeof(EvidenceValue).GetConstructors());
        Assert.All(typeof(EvidenceValue).GetProperties(), p => Assert.Null(p.SetMethod));
        var factories = typeof(EvidenceValue).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(EvidenceValue)
                || m.ReturnType == typeof(IReadOnlyList<EvidenceValue>));
        Assert.Equal([nameof(EvidenceValue.FromProfile)], factories.Select(m => m.Name));

        var parameters = typeof(IFormSession).GetMethods().SelectMany(m => m.GetParameters()).Where(p => p.ParameterType == typeof(string));
        Assert.All(parameters, p => Assert.Equal("fieldId", p.Name));
    }

    [Fact]
    public void EvidenceValue_ToString_HidesTheValue_F0_3_BR2() =>
        Assert.All(Facts(Profile), f => Assert.Equal(f.Reference, f.ToString()));

    [Fact]
    public void FormSession_HasNoWayToSubmitPressClickOrNavigate_F0_3_AC1() =>
        Assert.Equal(
            ["AttachCvAsync", "CaptureAsync", "FillAsync", "GoToNextStepAsync", "ReadStepAsync", "SelectOptionAsync"],
            typeof(IFormSession).GetMethods().Select(m => m.Name).Order());

    [Theory]
    [InlineData("a\u0000b\u202Ec\u200Bd", 10, "abcd")]
    [InlineData("  a \r\n\t b  ", 10, "a b")]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("ab\U0001F600", 3, "ab")]
    [InlineData(null, 3, "")]
    public void Clean_UntrustedText_IsOneCappedLine_F0_3_AC1(string? text, int max, string expected) =>
        Assert.Equal(expected, FormText.Clean(text, max));

    private static FieldPlan MapOne(FormField field) => FieldMapper.Map([field], Facts(Profile), NothingAsked)[0];

    private static IReadOnlyList<EvidenceValue> Facts(CandidateProfileDataDto profile) => EvidenceValue.FromProfile(profile);
}

using System.Text.Json.Nodes;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Cv;

namespace JobsFinder.UnitTests.Application.Cv;

public class CandidateProfileSchemaTests
{
    private static JsonObject Schema() => JsonNode.Parse(CandidateProfileSchema.Json)!.AsObject();

    [Fact]
    public void Schema_ContainsAllRequiredSections_F0_1_BR2()
    {
        var schema = Schema();

        string[] sections =
        [
            "identity", "contact", "summary", "skills", "experience", "education",
            "certifications", "links", "workAuthorisation", "currentEmployer", "preferences",
        ];
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        foreach (var section in sections)
        {
            Assert.True(properties.ContainsKey(section), $"Missing property '{section}'.");
            Assert.Contains(section, required);
        }

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());

        var skill = properties["skills"]!["items"]!["properties"]!.AsObject();
        Assert.True(skill.ContainsKey("proficiency"));
        Assert.True(skill.ContainsKey("years"));

        var experience = properties["experience"]!["items"]!["properties"]!.AsObject();
        foreach (var field in new[] { "role", "company", "startDate", "endDate", "achievements" })
        {
            Assert.True(experience.ContainsKey(field), $"Missing experience field '{field}'.");
        }

        var preferences = properties["preferences"]!["properties"]!.AsObject();
        foreach (var field in new[] { "targetRoles", "locations", "remote", "salaryMin", "salaryMax", "noticePeriod" })
        {
            Assert.True(preferences.ContainsKey(field), $"Missing preference field '{field}'.");
        }
    }

    [Fact]
    public void Parse_ValidJson_MapsAllSections_F0_1_BR2()
    {
        var result = CandidateProfileSchema.Parse(CvSamples.ProfileJson());

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var profile = result.Profile!;
        Assert.Equal("Ada Lovelace", profile.Identity.FullName);
        Assert.Equal("ada@example.com", profile.Contact.Email);
        Assert.Equal(SkillProficiency.Expert, profile.Skills[0].Proficiency);
        Assert.Equal(10, profile.Skills[0].Years);
        Assert.True(profile.Experience[0].IsCurrent);
        Assert.Equal("Cut build times by 40%", profile.Experience[0].Achievements[0]);
        Assert.Equal("BSc", profile.Education[0].Qualification);
        Assert.Equal("AZ-204", profile.Certifications[0].Name);
        Assert.Equal("https://github.com/ada", profile.Links[0].Url);
        Assert.Equal("UK citizen", profile.WorkAuthorisation);
        Assert.Equal("Analytical Engines Ltd", profile.CurrentEmployer);
        Assert.Equal(RemotePreference.Hybrid, profile.Preferences.Remote);
        Assert.Equal(90000, profile.Preferences.SalaryMin);
        Assert.Equal("3 months", profile.Preferences.NoticePeriod);
    }

    [Fact]
    public void Parse_EmptyProfile_IsValid_F0_1_BR2()
    {
        var result = CandidateProfileSchema.Parse(CvSamples.ProfileJson(CandidateProfileDataDto.Empty));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public void Parse_MalformedOrIncomplete_Fails_F0_1_BR4(string json)
    {
        var result = CandidateProfileSchema.Parse(json);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Parse_UnknownProperty_Fails_F0_1_BR4()
    {
        var json = JsonNode.Parse(CvSamples.ProfileJson())!.AsObject();
        json["favouriteColour"] = "blue";

        var result = CandidateProfileSchema.Parse(json.ToJsonString());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_NullForRequiredList_Fails_F0_1_BR4()
    {
        var json = JsonNode.Parse(CvSamples.ProfileJson())!.AsObject();
        json["skills"] = null;

        var result = CandidateProfileSchema.Parse(json.ToJsonString());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_BadDate_Fails_F0_1_BR4()
    {
        var profile = CvSamples.Profile();
        var bad = profile with { Experience = [profile.Experience[0] with { StartDate = "March 2019" }] };

        var result = CandidateProfileSchema.Parse(CvSamples.ProfileJson(bad));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("StartDate", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnknownEnumValue_Fails_F0_1_BR4()
    {
        var json = JsonNode.Parse(CvSamples.ProfileJson())!.AsObject();
        json["skills"]![0]!["proficiency"] = "Guru";

        var result = CandidateProfileSchema.Parse(json.ToJsonString());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_RelativeLink_Fails_F0_1_BR4()
    {
        var bad = CvSamples.Profile() with { Links = [new ProfileLinkDto("LinkedIn", "linkedin.com/in/ada")] };

        var result = CandidateProfileSchema.Parse(CvSamples.ProfileJson(bad));

        Assert.False(result.IsValid);
    }
}

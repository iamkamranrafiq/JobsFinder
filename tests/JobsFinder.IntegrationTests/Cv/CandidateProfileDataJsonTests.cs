using JobsFinder.Domain.Cv;
using JobsFinder.Infrastructure.Persistence.Converters;

namespace JobsFinder.IntegrationTests.Cv;

/// <summary>The jsonb column round-trip, checked without a database. No Docker needed.</summary>
public class CandidateProfileDataJsonTests
{
    [Fact]
    public void SerializeThenDeserialize_RoundTripsEverySection_F0_1_AC6()
    {
        var data = new CandidateProfileData(
            new ProfileIdentity("Ada Lovelace", "Engineer"),
            new ProfileContact("ada@example.com", "+44 1", "London"),
            "Summary",
            [new ProfileSkill("C#", SkillProficiency.Expert, 10.5m)],
            [new ProfileExperience("Engineer", "Analytical Engines Ltd", "2019-03", null, true, ["Shipped it"])],
            [new ProfileEducation("University of London", "BSc", "Maths", "2010", "2013")],
            [new ProfileCertification("AZ-204", "Microsoft", 2022)],
            [new ProfileLink("GitHub", "https://github.com/ada")],
            "UK citizen",
            "Analytical Engines Ltd",
            new ProfilePreferences(["Staff Engineer"], ["Remote"], RemotePreference.Remote, 1, 2, "GBP", "1 month"));

        var json = CandidateProfileDataJson.Serialize(data);
        var back = CandidateProfileDataJson.Deserialize(json);

        Assert.Contains("\"proficiency\":\"Expert\"", json, StringComparison.Ordinal);
        Assert.Equal(json, CandidateProfileDataJson.Serialize(back));
        Assert.Equal("Analytical Engines Ltd", back.CurrentEmployer);
        Assert.Equal(10.5m, back.Skills[0].Years);
        Assert.Equal("Shipped it", back.Experience[0].Achievements[0]);
        Assert.True(CandidateProfileDataJson.Comparer.Equals(data, back));
    }
}

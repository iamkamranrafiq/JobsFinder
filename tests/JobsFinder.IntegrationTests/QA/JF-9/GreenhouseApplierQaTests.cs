using System.Diagnostics;
using System.Text.Json;
using JobsFinder.Application.Applying;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Applying;
using JobsFinder.IntegrationTests.Applying;
using Microsoft.Extensions.DependencyInjection;

namespace JobsFinder.IntegrationTests.QA.JF9;

/// <summary>
/// Independent QA of JF-9 (part A). Real handler, database and Chromium against the local FixtureSite only: every
/// connection of the browser is dialled to loopback. The pages here are the QA engineer's own, not the developer's.
/// </summary>
[Collection(ApiTestGroup.Name)]
[Trait("Category", "QA")]
public sealed class GreenhouseApplierQaTests(ApiFactory factory)
{
    private const string Email = "zelda.quillfeather@example.com";
    private const string Phone = "+44 20 7946 0123";

    private const string StandardFields = """
        <label for="first_name">First Name *</label><input id="first_name" type="text" required aria-required="true">
        <label for="last_name">Last Name *</label><input id="last_name" type="text" required aria-required="true">
        <label for="email">Email *</label><input id="email" type="email" required aria-required="true">
        <label for="phone">Phone</label><input id="phone" type="tel">
        <label for="loc">Location</label><input id="loc" type="text">
        <label for="li">LinkedIn Profile</label><input id="li" type="text">
        <fieldset><legend>Resume/CV *</legend><label for="resume">Attach</label><input id="resume" type="file"></fieldset>
        """;

    private static string Form(string body, string script = "") => $$"""
        <!doctype html><html lang="en"><head><title>Apply</title>
        <style>label, input, select, textarea { display:block; margin:4px 0 } .tall { height: 2400px }</style></head>
        <body><h1>Engineer at Acme</h1><div class="tall">Job description.</div>
        <form id="application_form" method="post" action="/submit">
        {{body}}
        <input type="hidden" id="submit_clicked" value="">
        <button type="submit" id="go" onclick="document.getElementById('submit_clicked').value='yes'">Submit Application</button>
        </form><script>{{script}}</script></body></html>
        """;

    private static (int Width, int Height) JpegSize(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 4 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "Not a JPEG (missing SOI marker).");
        var i = 2;
        while (i + 9 < jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                i++;
                continue;
            }

            var marker = jpeg[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                return ((jpeg[i + 7] << 8) | jpeg[i + 8], (jpeg[i + 5] << 8) | jpeg[i + 6]);
            }

            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }

        throw new InvalidOperationException("No SOF marker.");
    }

    private static CandidateProfileDataDto WithContact(string? email, string? phone) =>
        ApplyHost.Profile with { Contact = new ProfileContactDto(email, phone, "Leeds, UK") };

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task TC01_StandardForm_AllAnswerableFieldsFilledAndCvAttached()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Form(StandardFields));

        Assert.Equal((ApplyRunStatus.Prepared, (ApplyReason?)null), (run.Status, run.Reason));
        Assert.Equal("Zelda", host.Spy.Values["first_name"]);
        Assert.Equal("Quillfeather", host.Spy.Values["last_name"]);
        Assert.Equal(Email, host.Spy.Values["email"]);
        Assert.Equal(Phone, host.Spy.Values["phone"]);
        Assert.Equal("Leeds, UK", host.Spy.Values["loc"]);
        Assert.Equal("https://www.linkedin.com/in/zelda-quillfeather", host.Spy.Values["li"]);
        Assert.True(run.CvAttached);
        Assert.Equal(7, run.Fields.Count(f => f.Outcome == ApplyFieldOutcome.Filled));
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task TC02_FullPageScreenshotStoredAsJpeg()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Form(StandardFields));

        var shots = await host.ScreenshotsAsync(run.Id);
        var shot = Assert.Single(shots);
        Assert.Equal("image/jpeg", shot.ContentType);
        Assert.InRange(shot.Content.Length, 1, ApplyScreenshot.MaxSizeBytes);
        var (_, height) = JpegSize(shot.Content);
        Assert.True(height > 2400, $"Screenshot is {height}px high; the page is taller than 2400px, so it is not full page.");
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task TC03_CvAttachedUnderItsNameAndSize()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Form(StandardFields));

        Assert.True(run.CvAttached);
        var parts = host.Spy.Values["resume"].Split('|');
        Assert.Equal("zelda-cv.pdf", parts[0]);
        Assert.Equal(ApplyHost.CvBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), parts[1]);
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task TC04_RequiredQuestionWithoutEvidence_LeftEmptyAndNeedsYou()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string extra = """<label for="why">Why do you want to work here? *</label><textarea id="why" required aria-required="true"></textarea>""";

        var run = await host.RunAsync(Form(StandardFields + extra));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        Assert.Equal(string.Empty, host.Spy.Values["why"]);
        var field = Assert.Single(run.Fields, f => f.Label.Contains("Why do you want", StringComparison.Ordinal));
        Assert.Equal(ApplyFieldOutcome.NeedsHuman, field.Outcome);
        Assert.True(field.Required);
        Assert.Null(field.Evidence);
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task TC05_RequiredPhoneButProfileHasNone_LeftEmptyAndNeedsYou()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string fields = """
            <label for="first_name">First Name *</label><input id="first_name" required>
            <label for="email">Email *</label><input id="email" type="email" required>
            <label for="phone">Phone *</label><input id="phone" type="tel" required aria-required="true">
            <fieldset><legend>Resume/CV *</legend><label for="resume">Attach</label><input id="resume" type="file"></fieldset>
            """;

        var run = await host.RunAsync(Form(fields), WithContact(Email, null));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        Assert.Equal(string.Empty, host.Spy.Values["phone"]);
        var phone = Assert.Single(run.Fields, f => f.Label.StartsWith("Phone", StringComparison.Ordinal));
        Assert.Equal(NeedsHumanReason.NoEvidence, phone.Reason);
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task TC06_SensitiveAndOptionalUnknownFields_NeverFilledOrTicked()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string extra = """
            <label for="gender">Gender</label><select id="gender"><option value="">Select</option><option>Female</option><option>Male</option></select>
            <label for="salary">Salary expectations</label><input id="salary" type="text">
            <label for="consent"><input id="consent" type="checkbox"> I agree to the privacy policy</label>
            """;

        var run = await host.RunAsync(Form(StandardFields + extra));

        Assert.Equal(string.Empty, host.Spy.Values["gender"]);
        Assert.Equal(string.Empty, host.Spy.Values["salary"]);
        Assert.Equal(string.Empty, host.Spy.Values["consent"]);
        Assert.Contains(run.Fields, f => f.Label.Contains("Salary", StringComparison.Ordinal) && f.Outcome == ApplyFieldOutcome.NeedsHuman);
        Assert.Contains(run.Fields, f => f.Label.Contains("Gender", StringComparison.Ordinal) && f.Outcome == ApplyFieldOutcome.NeedsHuman);
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task TC07_AmbiguousDuplicateEmail_NeitherFilled()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string fields = """
            <label for="email">Email *</label><input id="email" type="email" required>
            <label for="email2">Email *</label><input id="email2" type="email" required>
            <fieldset><legend>Resume/CV *</legend><label for="resume">Attach</label><input id="resume" type="file"></fieldset>
            """;

        var run = await host.RunAsync(Form(fields));

        Assert.Equal(string.Empty, host.Spy.Values["email"]);
        Assert.Equal(string.Empty, host.Spy.Values["email2"]);
        Assert.Equal(ApplyRunStatus.NeedsYou, run.Status);
        Assert.Equal(2, run.Fields.Count(f => f.Reason == NeedsHumanReason.Ambiguous));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task TC08_ValueLongerThanMaxLength_NotTruncated()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string fields = """
            <label for="email">Email *</label><input id="email" type="email" maxlength="5" required>
            <fieldset><legend>Resume/CV *</legend><label for="resume">Attach</label><input id="resume" type="file"></fieldset>
            """;

        var run = await host.RunAsync(Form(fields));

        Assert.Equal(string.Empty, host.Spy.Values["email"]);
        var field = Assert.Single(run.Fields, f => f.Label.StartsWith("Email", StringComparison.Ordinal));
        Assert.Equal(NeedsHumanReason.DoesNotFit, field.Reason);
        Assert.Equal(ApplyRunStatus.NeedsYou, run.Status);
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task TC09_PageAlways503_FailedPageLoadAfterThreeRetries()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Failures[ApplyHost.JobPath] = (503, int.MaxValue);

        var run = await host.RunUrlAsync(FixtureSite.Host + ApplyHost.JobPath);

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.PageLoad), (run.Status, run.Reason));
        Assert.Equal(4, host.Site.Hits.Count(h => h.Path == ApplyHost.JobPath));
        var stored = await host.StoredAsync(run.Id);
        Assert.NotNull(stored);
        Assert.Equal(ApplyRunStatus.Failed, stored.Status);
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task TC10_PageFailsTwiceThenLoads_Prepared()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Failures[ApplyHost.JobPath] = (503, 2);

        var run = await host.RunAsync(Form(StandardFields));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.Equal(3, host.Site.Hits.Count(h => h.Path == ApplyHost.JobPath));
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task TC11_Page404_FailedPageLoadWithoutRetries()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        var path = $"/acme/jobs/{Guid.NewGuid():N}";

        var run = await host.RunUrlAsync(FixtureSite.Host + path);

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.PageLoad), (run.Status, run.Reason));
        Assert.Equal(1, host.Site.Hits.Count(h => h.Path == path));
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgeMultiStep")]
    public async Task TC12_TwoStepForm_BothStepsFilledStopsOnFinalStep()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string html = """
            <!doctype html><html><head><title>Apply</title></head><body>
            <form id="application_form" method="post" action="/submit">
              <section id="s1">
                <label for="first_name">First Name *</label><input id="first_name" required>
                <label for="last_name">Last Name *</label><input id="last_name" required>
                <button type="button" id="nextbtn" onclick="document.getElementById('s1').hidden=true;document.getElementById('s2').hidden=false">Next</button>
              </section>
              <section id="s2" hidden>
                <label for="email">Email *</label><input id="email" type="email" required>
                <fieldset><legend>Resume/CV *</legend><label for="resume">Attach</label><input id="resume" type="file"></fieldset>
                <input type="hidden" id="submit_clicked" value="">
                <button type="submit" onclick="document.getElementById('submit_clicked').value='yes'">Submit Application</button>
              </section>
            </form></body></html>
            """;

        var run = await host.RunAsync(html);

        Assert.Equal((ApplyRunStatus.Prepared, (ApplyReason?)null), (run.Status, run.Reason));
        Assert.Equal(2, run.StepCount);
        Assert.Equal(2, (await host.ScreenshotsAsync(run.Id)).Count);
        Assert.Equal("Zelda", host.Spy.Values["first_name"]);
        Assert.Equal(Email, host.Spy.Values["email"]);
        Assert.StartsWith("zelda-cv.pdf|", host.Spy.Values["resume"], StringComparison.Ordinal);
        Assert.Equal(string.Empty, host.Spy.Values["submit_clicked"]);
        Assert.True(run.CvAttached);
        Assert.Equal(2, run.Fields.Count(f => f.Step == 1));
        Assert.Equal(2, run.Fields.Count(f => f.Step == 2));
    }

    [Fact]
    [Trait("REQ", "F0.3-AC4")]
    public async Task TC13_NeverSubmits_NoNonGetRequestAndSubmitNeverClicked()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Form(StandardFields));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.Equal(string.Empty, host.Spy.Values["submit_clicked"]);
        Assert.DoesNotContain(host.Site.Hits, h => h.Method is not ("GET" or "HEAD"));
        Assert.DoesNotContain(host.Site.Hits, h => h.Path.StartsWith("/submit", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("REQ", "F0.3-NFR")]
    public async Task TC14_HangingPage_FailedTimedOutWithinBudget()
    {
        var settings = new ApplySettings
        {
            Budget = TimeSpan.FromSeconds(3),
            RetryDelays = [TimeSpan.FromMilliseconds(20)],
            QuietTime = TimeSpan.FromMilliseconds(600),
            MaxSettleTime = TimeSpan.FromSeconds(4),
        };
        await using var host = await ApplyHost.StartAsync(factory, settings);
        var watch = Stopwatch.StartNew();

        var run = await host.RunUrlAsync(FixtureSite.Host + "/hang");

        watch.Stop();
        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.TimedOut), (run.Status, run.Reason));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(12), $"Took {watch.Elapsed}.");
        Assert.NotNull(await host.StoredAsync(run.Id));
    }

    [Fact]
    [Trait("REQ", "F0.3-NFR")]
    public async Task TC15_DefaultSettings_CompletesUnderTwoMinutes()
    {
        await using var host = await ApplyHost.StartAsync(factory, new ApplySettings());
        var watch = Stopwatch.StartNew();

        var run = await host.RunAsync(Form(StandardFields));

        watch.Stop();
        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(120), $"Took {watch.Elapsed}.");
        Assert.True(run.CompletedAt - run.StartedAt < TimeSpan.FromSeconds(120));
    }

    [Theory]
    [Trait("REQ", "F0.3-BR1")]
    [InlineData("http://job-boards.greenhouse.io/acme/jobs/1")]
    [InlineData("https://evil.example.com/acme/jobs/1")]
    [InlineData("https://job-boards.greenhouse.io:8443/acme/jobs/1")]
    [InlineData("https://user:pw@job-boards.greenhouse.io/acme/jobs/1")]
    [InlineData("https://job-boards.greenhouse.io.evil.example.com/acme/jobs/1")]
    [InlineData("https://greenhouse.io/acme/jobs/1")]
    public async Task TC16_UrlNotAGreenhouseBoard_FailedUrlNotAllowedBrowserNeverOpened(string url)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunUrlAsync(url);

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.UrlNotAllowed), (run.Status, run.Reason));
        Assert.Equal(0, host.Spy.OpenCalls);
        Assert.Empty(host.Site.DialledHosts);
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task TC17_UnknownOrEmptyPostingId_ErrorAndNothingStored()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PrepareApplicationHandler>();

        var unknown = await handler.HandleAsync(new PrepareApplicationCommand(Guid.NewGuid()), CancellationToken.None);
        var empty = await handler.HandleAsync(new PrepareApplicationCommand(Guid.Empty), CancellationToken.None);

        Assert.False(unknown.IsSuccess);
        Assert.Equal("posting_not_found", unknown.Error!.Code);
        Assert.False(empty.IsSuccess);
        Assert.Equal(0, host.Spy.OpenCalls);
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task TC18_StoredRunAndLogs_HoldReferencesNotProfileValues()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Form(StandardFields));

        var stored = await host.StoredAsync(run.Id);
        Assert.NotNull(stored);
        var json = JsonSerializer.Serialize(stored.Fields);
        Assert.DoesNotContain(Email, json, StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Quillfeather", json, StringComparison.Ordinal);
        Assert.Contains("contact.email", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, host.Logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, host.Logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("REQ", "F0.3-AC4")]
    public async Task TC19_PageTriesToPostAndLeak_NothingReachesTheServer()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        const string script = """
            fetch('/leak?early', { method: 'POST', body: 'x' }).catch(() => {});
            setTimeout(() => { fetch('/leak?late', { method: 'POST', body: document.title }).catch(() => {}); }, 1500);
            setTimeout(() => { navigator.sendBeacon('/leak?beacon', 'x'); }, 2500);
            """;

        var run = await host.RunAsync(Form(StandardFields, script));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.DoesNotContain(host.Site.Hits, h => h.Path.StartsWith("/leak", StringComparison.Ordinal));
        Assert.DoesNotContain(host.Site.Hits, h => h.Method is not ("GET" or "HEAD"));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR1")]
    public async Task TC20_RedirectToAnotherHost_FailedUrlNotAllowed()
    {
        await using var host = await ApplyHost.StartAsync(factory);
        host.Site.Redirects[ApplyHost.JobPath] = "https://evil.example.com/landing";

        var run = await host.RunUrlAsync(FixtureSite.Host + ApplyHost.JobPath);

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.UrlNotAllowed), (run.Status, run.Reason));
        Assert.Empty(await host.ScreenshotsAsync(run.Id));
    }
}

using JobsFinder.Application.Applying;
using JobsFinder.Application.Common;
using JobsFinder.Application.Cv;
using JobsFinder.Domain.Applying;
using JobsFinder.Domain.Cv;
using JobsFinder.Domain.Sourcing;
using JobsFinder.UnitTests.Application.Cv;
using JobsFinder.UnitTests.Application.Sourcing;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobsFinder.UnitTests.Application.Applying;

[Trait("REQ", "F0.3-AC1")]
public sealed class PrepareApplicationHandlerTests
{
    private const string ApplyUrl = "https://job-boards.greenhouse.io/acme/jobs/1";

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly FormField Email = Form.Field("Email", required: true);
    private static readonly FormField Cv = Form.Field("Resume", FormFieldKind.File, required: true);

    private readonly FakeSourcingStore _postings = new();
    private readonly FakeCvRepository _cvs = new();
    private readonly FakeApplyRunRepository _runs = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleAsync_AnswerableForm_StoresOnePreparedRun_F0_3_AC1()
    {
        var session = new FakeFormSession(Form.Final(Email, Cv));
        var posting = Seed();

        var result = await HandleAsync(new FakeBrowser(session), posting.Id);

        var run = Assert.Single(_runs.Runs);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal((ApplyRunStatus.Prepared, (ApplyReason?)null, true), (run.Status, run.Reason, run.CvAttached));
        Assert.Equal((posting.Id, _cvs.Profiles[0].Id), (run.JobPostingId, run.CandidateProfileId));
        Assert.Equal(run.Id, result.Value!.Id);
        Assert.Equal("ada@example.com", session.Filled["Email"]);
        Assert.Equal("ada.pdf", session.Attached!.FileName);
        Assert.Equal((run.Id, 1), (_runs.Screenshots.Single().ApplyRunId, _runs.Screenshots.Single().Step));
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task HandleAsync_SecondProfileVersion_UsesTheActiveOne_F0_3_AC1()
    {
        var session = new FakeFormSession(Form.Final(Email, Cv));
        var posting = Seed();
        var data = (CvSamples.Profile() with { Contact = new ProfileContactDto("new@example.com", null, null) }).ToDomain();
        _cvs.Add(CandidateProfile.CreateNextVersion(_cvs.Documents[0].Id, 1, data, Now.AddMinutes(1)));

        await HandleAsync(new FakeBrowser(session), posting.Id);

        Assert.Equal("new@example.com", session.Filled["Email"]);
        Assert.Equal(_cvs.Profiles[1].Id, _runs.Runs.Single().CandidateProfileId);
    }

    [Fact]
    public async Task HandleAsync_UnknownPosting_IsNotFoundAndStoresNothing_F0_3_AC1()
    {
        Seed();

        var result = await HandleAsync(new FakeBrowser(new FakeFormSession()), Guid.NewGuid());

        Assert.Equal(("posting_not_found", ErrorType.NotFound), (result.Error!.Code, result.Error.Type));
        Assert.Empty(_runs.Runs);
    }

    [Fact]
    public async Task HandleAsync_NoProfile_IsAConflictAndStoresNothing_F0_3_AC1()
    {
        var posting = Seed(withProfile: false);

        var result = await HandleAsync(new FakeBrowser(new FakeFormSession()), posting.Id);

        Assert.Equal(("profile_missing", ErrorType.Conflict), (result.Error!.Code, result.Error.Type));
        Assert.Empty(_runs.Runs);
    }

    [Fact]
    public async Task HandleAsync_EmptyId_IsAValidationError_F0_3_AC1()
    {
        var result = await HandleAsync(new FakeBrowser(new FakeFormSession()), Guid.Empty);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Theory]
    [InlineData("https://jobs.lever.co/acme/1")]
    [InlineData("http://job-boards.greenhouse.io/acme/jobs/1")]
    [InlineData("jobs@example.com")]
    [InlineData(null)]
    public async Task HandleAsync_UrlNotAllowed_IsAFailedRunWithoutABrowser_F0_3_AC1(string? url)
    {
        var browser = new FakeBrowser(new FakeFormSession());
        var posting = Seed(url);

        await HandleAsync(browser, posting.Id);

        Assert.Empty(browser.Opened);
        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.UrlNotAllowed), (_runs.Runs[0].Status, _runs.Runs[0].Reason));
    }

    [Theory]
    [InlineData(ApplyPath.Unknown)]
    [InlineData(ApplyPath.Email)]
    [InlineData(ApplyPath.NativeApply)]
    public async Task HandleAsync_ApplyPathIsNotAts_IsAFailedRunWithoutABrowser_F0_3_AC1(ApplyPath path)
    {
        var browser = new FakeBrowser(new FakeFormSession());
        var posting = Seed();
        _postings.ApplyPathOverride = path;

        await HandleAsync(browser, posting.Id);

        Assert.Empty(browser.Opened);
        Assert.Equal(ApplyReason.UrlNotAllowed, _runs.Runs[0].Reason);
    }

    [Theory]
    [InlineData(3, 4, null)]
    [InlineData(4, 4, ApplyReason.PageLoad)]
    [InlineData(9, 4, ApplyReason.PageLoad)]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_PageDoesNotLoad_IsRetriedThreeTimes_F0_3_EdgePageLoad(int failures, int attempts, ApplyReason? reason)
    {
        var browser = new FakeBrowser(new FakeFormSession(Form.Final(Email, Cv)));
        for (var i = 0; i < failures; i++)
        {
            browser.Failures.Enqueue(new BrowserAutomationException(ApplyReason.PageLoad, isTransient: true));
        }

        await HandleAsync(browser, Seed().Id);

        Assert.Equal(attempts, browser.Opened.Count);
        Assert.Equal(reason, _runs.Runs.Single().Reason);
    }

    [Theory]
    [InlineData(ApplyReason.PageLoad)]
    [InlineData(ApplyReason.UrlNotAllowed)]
    [InlineData(ApplyReason.FormNotRecognised)]
    [InlineData(ApplyReason.BrowserUnavailable)]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public async Task HandleAsync_FailureThatWillNotPass_IsNotRetried_F0_3_EdgePageLoad(ApplyReason reason)
    {
        var browser = new FakeBrowser(new FakeFormSession());
        browser.Failures.Enqueue(new BrowserAutomationException(reason));

        await HandleAsync(browser, Seed().Id);

        Assert.Single(browser.Opened);
        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)reason), (_runs.Runs[0].Status, _runs.Runs[0].Reason));
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgePageLoad")]
    public void ApplySettings_Default_BacksOffThreeTimesInsideTheBudget_F0_3_EdgePageLoad()
    {
        var settings = new ApplySettings();

        Assert.Equal([1, 2, 4], settings.RetryDelays.Select(d => d.TotalSeconds));
        Assert.True(settings.Budget < TimeSpan.FromMinutes(2));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_RequiredFieldOpen_FillsTheRestAndNeedsYou_F0_3_BR2()
    {
        var session = new FakeFormSession(Form.Final(Form.Field("Favourite colour", required: true), Email, Cv));

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        var run = _runs.Runs.Single();
        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        Assert.Equal("ada@example.com", session.Filled["Email"]);
        Assert.Equal(["Favourite colour"], run.Fields.Where(f => f.Outcome == ApplyFieldOutcome.NeedsHuman).Select(f => f.Label));
        Assert.Single(_runs.Screenshots);
    }

    [Theory]
    [InlineData(FieldApplyResult.NotApplied, NeedsHumanReason.NotApplied)]
    [InlineData(FieldApplyResult.NoMatchingOption, NeedsHumanReason.NoMatchingOption)]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_PageDoesNotTakeTheValue_IsNotReportedAsFilled_F0_3_BR2(FieldApplyResult result, NeedsHumanReason reason)
    {
        var session = new FakeFormSession(Form.Final(Email, Cv)) { Results = { ["Email"] = result } };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        var run = _runs.Runs.Single();
        Assert.Equal((reason, (string?)null), (run.Fields[0].Reason!.Value, run.Fields[0].Evidence));
        Assert.Equal(ApplyRunStatus.NeedsYou, run.Status);
    }

    [Fact]
    public async Task HandleAsync_OptionalCvFieldFails_IsNotPrepared_F0_3_AC1()
    {
        var session = new FakeFormSession(Form.Final(Email, Form.Field("CV", FormFieldKind.File)))
        {
            Results = { ["CV"] = FieldApplyResult.NotApplied },
        };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        Assert.Equal(ApplyReason.CvNotAttached, _runs.Runs.Single().Reason);
    }

    [Fact]
    [Trait("REQ", "F0.3-EdgeMultiStep")]
    public async Task HandleAsync_ThreeSteps_StoresAScreenshotOfEach_F0_3_EdgeMultiStep()
    {
        var session = new FakeFormSession(Form.WithNext(Email), Form.WithNext(Cv), Form.Final(Form.Field("Phone")));

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        var run = _runs.Runs.Single();
        Assert.Equal((ApplyRunStatus.Prepared, 3), (run.Status, run.StepCount));
        Assert.Equal([1, 2, 3], run.Fields.Select(f => f.Step));
        Assert.Equal([1, 2, 3], _runs.Screenshots.Select(s => s.Step));
    }

    public static TheoryData<string, FormField[], NeedsHumanReason, string> Changes => new()
    {
        { "new field", [Email, Cv, Form.Field("Phone")], NeedsHumanReason.AppearedLater, "Phone" },
        { "new optional question", [Email, Cv, Form.Field("Colour")], NeedsHumanReason.AppearedLater, "Colour" },
        { "answered field gone", [Cv], NeedsHumanReason.NotKept, "Email" },
        { "CV field gone", [Email], NeedsHumanReason.NotKept, "Resume" },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_StepReadsDifferentlyAfterFilling_IsJudgedAsItIsNow_F0_3_BR2(
        string name, FormField[] later, NeedsHumanReason reason, string label)
    {
        Assert.NotEmpty(name);
        var session = new FakeFormSession(Form.Final(Email, Cv)) { Later = { [0] = () => Form.Final(later) } };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        var run = _runs.Runs.Single();
        Assert.Equal(ApplyRunStatus.NeedsYou, run.Status);
        Assert.Equal(reason, run.Fields.Single(f => f.Label == label).Reason);
        Assert.DoesNotContain(session.Filled.Keys, k => k == "Phone");
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_OptionalFieldBecomesRequiredOrIsFilledByThePage_NeedsYou_F0_3_BR2()
    {
        var colour = Form.Field("Colour");
        foreach (var later in new[] { colour with { Required = true }, colour with { State = "blue" } })
        {
            _runs.Runs.Clear();
            var session = new FakeFormSession(Form.Final(Email, Cv, colour)) { Later = { [0] = () => Form.Final(Email, Cv, later) } };

            await HandleAsync(new FakeBrowser(session), Seed().Id);

            Assert.Equal(ApplyReason.UnknownField, _runs.Runs.Single().Reason);
        }
    }

    [Theory]
    [InlineData(FormStepEnd.Unclear)]
    [InlineData(FormStepEnd.None)]
    [Trait("REQ", "F0.3-EdgeMultiStep")]
    public async Task HandleAsync_StepIsNotFinalAnyMore_IsNotCompleted_F0_3_EdgeMultiStep(FormStepEnd end)
    {
        var session = new FakeFormSession(Form.Final(Email, Cv)) { Later = { [0] = () => new FormStep([Email, Cv], end) } };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        Assert.Equal(ApplyReason.StepNotReached, _runs.Runs.Single().Reason);
    }

    [Theory]
    [InlineData(0, "Email", ApplyReason.UnknownField, true)]
    [InlineData(1, "Resume", ApplyReason.UnknownField, false)]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_AnswerIsLost_IsNotReportedAsFilled_F0_3_BR2(int lost, string label, ApplyReason reason, bool cvAttached)
    {
        var session = new FakeFormSession(Form.Final(Email, Cv)) { Lost = { lost } };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        var run = _runs.Runs.Single();
        Assert.Equal((reason, cvAttached), (run.Reason!.Value, run.CvAttached));
        var field = run.Fields.Single(f => f.Label == label);
        Assert.Equal((NeedsHumanReason.NotKept, (string?)null), (field.Reason!.Value, field.Evidence));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_PageNeverComesToRest_IsNotCompletedAndHasNoScreenshot_F0_3_BR2()
    {
        var i = 0;
        var session = new FakeFormSession(Form.Final(Email, Cv)) { Later = { [0] = () => Form.Final(Email, Cv with { State = $"{i++}" }) } };

        await HandleAsync(new FakeBrowser(session), Seed().Id, quiet: TimeSpan.FromMilliseconds(150));

        Assert.Equal(ApplyReason.StepNotReached, _runs.Runs.Single().Reason);
        Assert.Empty(_runs.Screenshots);
    }

    [Fact]
    [Trait("REQ", "F0.3-NFR")]
    public void ApplySettings_Default_LeavesRoomToSettleInsideTheBudget_F0_3_NFR()
    {
        var settings = new ApplySettings();

        Assert.True(settings.QuietTime < settings.MaxSettleTime);
        Assert.True(settings.MaxSettleTime * 3 < settings.Budget);
    }

    [Theory]
    [InlineData(true, ApplyRunStatus.NeedsYou, ApplyReason.UnknownField)]
    [InlineData(false, ApplyRunStatus.Failed, ApplyReason.StepNotReached)]
    [Trait("REQ", "F0.3-EdgeMultiStep")]
    public async Task HandleAsync_StepDoesNotMove_SaysWhy_F0_3_EdgeMultiStep(bool openField, ApplyRunStatus status, ApplyReason reason)
    {
        var session = new FakeFormSession(Form.WithNext(Form.Field("Favourite colour", required: openField)), Form.Final(Email)) { Moves = false };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        Assert.Equal((status, (ApplyReason?)reason, 1), (_runs.Runs[0].Status, _runs.Runs[0].Reason, _runs.Runs[0].StepCount));
    }

    [Fact]
    public async Task HandleAsync_ScreenshotFails_KeepsWhatWasDone_F0_3_AC1()
    {
        var session = new FakeFormSession(Form.Final(Email, Cv)) { CaptureError = new BrowserAutomationException(ApplyReason.Screenshot) };

        await HandleAsync(new FakeBrowser(session), Seed().Id);

        var run = _runs.Runs.Single();
        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.Screenshot), (run.Status, run.Reason));
        Assert.Equal(2, run.Fields.Count);
        Assert.True(session.Disposed);
    }

    [Fact]
    [Trait("REQ", "F0.3-NFR")]
    public async Task HandleAsync_OverBudget_FailsAsTimedOut_F0_3_NFR()
    {
        var session = new FakeFormSession(Form.Final(Email)) { Hangs = true };

        await HandleAsync(new FakeBrowser(session), Seed().Id, TimeSpan.FromMilliseconds(50));

        Assert.Equal(ApplyReason.TimedOut, _runs.Runs.Single().Reason);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task HandleAsync_Cancelled_StoresNothing_F0_3_AC1()
    {
        var session = new FakeFormSession(Form.Final(Email)) { Hangs = true };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HandleAsync(new FakeBrowser(session), Seed().Id, cancellationToken: cancel.Token));

        Assert.Empty(_runs.Runs);
        Assert.True(session.Disposed);
    }

    private JobPosting Seed(string? url = ApplyUrl, bool withProfile = true)
    {
        if (withProfile)
        {
            var document = CvDocument.Create("ada.pdf", CvFormat.Pdf, CvSamples.Bytes(), Now);
            _cvs.Add(document);
            _cvs.Add(CandidateProfile.CreateFromParse(document.Id, CvSamples.Profile().ToDomain(), Now));
        }

        var posting = JobPosting.Create("test", "1", "Acme", "Engineer", null, false, "Description", url, null, Now);
        _postings.Postings.Add(posting);
        return posting;
    }

    private Task<Result<ApplyRunDto>> HandleAsync(
        FakeBrowser browser, Guid postingId, TimeSpan? budget = null, TimeSpan? quiet = null, CancellationToken cancellationToken = default) =>
        new PrepareApplicationHandler(
                _postings,
                _cvs,
                _runs,
                _unitOfWork,
                browser,
                new PrepareApplicationValidator(),
                new ApplySettings { Budget = budget ?? TimeSpan.FromSeconds(30), RetryDelays = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero], QuietTime = quiet ?? TimeSpan.Zero, MaxSettleTime = TimeSpan.FromMilliseconds(500) },
                new FixedTimeProvider(Now),
                NullLogger<PrepareApplicationHandler>.Instance)
            .HandleAsync(new PrepareApplicationCommand(postingId), cancellationToken);
}

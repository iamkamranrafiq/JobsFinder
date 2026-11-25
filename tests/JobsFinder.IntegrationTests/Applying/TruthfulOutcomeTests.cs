using JobsFinder.Domain.Applying;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// A run is Prepared only if that is true (F0.3-BR2, multi-step edge case): nothing a person can see as required was
/// skipped, the last step was really the last, and a field counts as filled only if the page took the value.
/// </summary>
[Collection(ApiTestGroup.Name)]
[Trait("REQ", "F0.3-BR2")]
public sealed class TruthfulOutcomeTests(ApiFactory factory)
{
    public static TheoryData<string, string> NotPlainlyFinalNorNext => new()
    {
        { "two candidates", """<button type="button" class="next">Next</button><button type="button">Continue</button>""" },
        { "submit button named next", """<button type="submit" class="next">Next</button>""" },
        { "button without type", """<button class="next">Next</button>""" },
        { "input submit named continue", """<input type="submit" class="next" value="Save and continue">""" },
        { "next beside submit", """<button type="button" class="next">Next</button><button type="submit">Submit application</button>""" },
        { "next outside the form beside submit", """<button type="submit">Submit application</button></form><button type="button" class="next">Next</button><form id="other">""" },
        { "next that applies", """<button type="button" class="next">Next: submit application</button>""" },
        { "disabled", """<button type="button" class="next" disabled>Next</button>""" },
        { "link", """<a href="/leak" class="next">Next</a>""" },
        { "other ways to apply", """<button type="button">Apply with LinkedIn</button>""" },
        { "next beside other ways to apply", """<button type="button" class="next">Next</button><button type="button">Apply with LinkedIn</button>""" },
        { "another language", """<button type="submit" class="next">Weiter</button>""" },

        // The name a person sees and the name a script sees differ (review M1).
        { "aria-label", """<button type="button" class="next" aria-label="Next">Submit application</button>""" },
        { "value", """<button type="button" class="next" value="next">Submit application</button>""" },
        { "title", """<button type="button" class="next" title="Submit application">Next</button>""" },
        { "aria-labelledby", """<span id="n">Next</span><button type="button" class="next" aria-labelledby="n">Submit application</button>""" },
        { "in a link", """<a href="/leak"><button type="button" class="next">Next</button></a>""" },
    };

    [Theory]
    [MemberData(nameof(NotPlainlyFinalNorNext))]
    [Trait("REQ", "F0.3-EdgeMultiStep")]
    public async Task HandleAsync_StepWhoseEndIsUnclear_IsNotClickedAndNotPrepared_F0_3_EdgeMultiStep(string name, string control)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.CvOnStepOne(control));

        Assert.Equal((ApplyRunStatus.Failed, (ApplyReason?)ApplyReason.StepNotReached, 1), (run.Status, run.Reason, run.StepCount));
        Assert.Equal(string.Empty, host.Spy.Values["clicked"]);

        // What could be done is done and shown.
        Assert.True(run.CvAttached);
        Assert.Single(await host.ScreenshotsAsync(run.Id));
    }

    [Theory]
    [InlineData("""<button type="submit">Submit application</button>""")]
    [InlineData("""<button>Submit</button>""")]
    [InlineData("""<input type="submit" value="Submit Application">""")]
    [InlineData("""<button type="submit" aria-label="Submit application" disabled>Submit</button>""")]
    [InlineData("""<button type="button">Attach</button><button type="submit">Apply now</button>""")]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_PlainlyFinalStep_IsPrepared_F0_3_AC1(string control)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(submit: control));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
    }

    public static TheoryData<string, string> RequiredInDisguise => new()
    {
        { "read-only", """<label for="x">Start date*</label><input id="x" readonly>""" },
        { "not focusable", """<label for="x">Colour*</label><input id="x" tabindex="-1">""" },
        { "disabled", """<label for="x">Colour*</label><input id="x" disabled>""" },
        { "in a disabled fieldset", """<fieldset disabled><label for="x">Colour</label><input id="x" required></fieldset>""" },
        { "native select behind a widget", """<label for="x">Country*</label><select id="x" style="display:none"><option></option><option>UK</option></select><div>Choose</div>""" },
        { "native select without a label element", """<div>Country <select id="x" required style="display:none"><option></option><option>UK</option></select><span>Choose</span></div>""" },
        { "tiny control", """<label for="x">Colour*</label><input id="x" style="width:1px;height:1px;border:0;padding:0">""" },
        { "word in brackets", """<label for="x">Colour (required)</label><input id="x">""" },
        { "word at the end", """<label for="x">Colour - Required</label><input id="x">""" },
        { "asterisk hidden from readers", """<label for="x">Colour<span aria-hidden="true">*</span></label><input id="x">""" },
        { "asterisk drawn by style", """<style>.req::after { content: " *"; }</style><label for="x" class="req">Colour</label><input id="x">""" },
        { "described as required", """<label for="x">Colour</label><input id="x" aria-describedby="d"><span id="d">This field is required</span>""" },
        { "second radio", """<fieldset><legend>Colour</legend><label><input type="radio" name="c" value="r" id="x"> Red</label><label><input type="radio" name="c" value="b" required> Blue</label></fieldset>""" },
        { "radio group", """<div role="radiogroup" aria-label="Colour" aria-required="true"><label><input type="radio" name="c" id="x"> Red</label></div>""" },
        { "custom radios", """<div role="radiogroup" aria-label="Colour" aria-required="true"><span role="radio" tabindex="0">Red</span></div><input type="hidden" id="x">""" },
        { "rich text", """<div id="l">Colour*</div><div contenteditable="" aria-labelledby="l" style="height:20px"></div><input type="hidden" id="x">""" },
        { "custom text box", """<div id="l">Colour*</div><div role="textbox" tabindex="0" aria-labelledby="l" style="height:20px"></div><input type="hidden" id="x">""" },
        { "outside the form", """</form><label for="x">Colour*</label><input id="x" form="application-form"><form id="other">""" },
    };

    [Theory]
    [MemberData(nameof(RequiredInDisguise))]
    public async Task HandleAsync_RequiredFieldItCannotFill_IsRecordedAndNotPrepared_F0_3_BR2(string name, string field)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);

        // The submit button comes first, so that a field after </form> stays after the form.
        var run = await host.RunAsync(FixturePages.NewBoard(FixturePages.Submit + field, submit: string.Empty));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        var open = Assert.Single(run.Fields, f => f is { Required: true, Outcome: ApplyFieldOutcome.NeedsHuman });
        Assert.NotEmpty(open.Label);
        Assert.Equal(string.Empty, host.Spy.Values["x"]);
    }

    [Fact]
    public async Task HandleAsync_FieldsNobodyCanSee_AreNotRecorded_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(
            """
            <div style="display:none"><label for="h1">Colour*</label><input id="h1" required></div>
            <div style="position:absolute;left:-9999px"><label for="h2">Animal*</label><input id="h2" required></div>
            <div aria-hidden="true"><label for="h3">Planet*</label><input id="h3" required></div>
            <input type="hidden" id="h4" required>
            """));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        Assert.DoesNotContain(run.Fields, f => f.Label is "Colour*" or "Animal*" or "Planet*");
    }

    private const string ListField =
        """
        <label for="notice">Notice period</label>
        <div><input id="notice" role="combobox" aria-controls="list"><div id="list" role="listbox" hidden><div role="option">3 months</div></div></div>
        """;

    private const string OpenList = "const n = document.getElementById('notice'); const l = document.getElementById('list'); n.addEventListener('input', () => { l.hidden = !n.value; }); ";

    [Theory]
    [InlineData("the click does nothing", "")]
    [InlineData("the list closes without choosing", "l.addEventListener('click', () => { l.hidden = true; });")]
    [InlineData("leaving closes the list", "n.addEventListener('blur', () => { l.hidden = true; });")]
    [InlineData("something else is chosen", "l.addEventListener('click', () => { l.hidden = true; n.value = '1 month'; });")]
    public async Task HandleAsync_ListThatDoesNotTakeTheOption_IsNotFilledAndIsCleared_F0_3_BR2(string name, string script)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(ListField, OpenList + script));

        var field = run.Fields.Single(f => f.Label == "Notice period");
        Assert.Equal((ApplyFieldOutcome.NeedsHuman, (NeedsHumanReason?)NeedsHumanReason.NotApplied), (field.Outcome, field.Reason));
        Assert.Equal(string.Empty, host.Spy.Values["notice"]);
        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_ListThatWritesTheOptionIntoItsInput_IsFilled_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(
            ListField, OpenList + "l.addEventListener('click', (e) => { n.value = e.target.textContent; l.hidden = true; });"));

        Assert.Equal(ApplyFieldOutcome.Filled, run.Fields.Single(f => f.Label == "Notice period").Outcome);
        Assert.Equal("3 months", host.Spy.Values["notice"]);
    }

    private const string Select =
        """<label for="s">Notice period</label><select id="s"><option value="">Select</option><option>1 month</option><option>3 months</option></select>""";

    [Fact]
    public async Task HandleAsync_SelectThatChangesItself_IsPutBackAndNotFilled_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(
            Select, "const s = document.getElementById('s'); s.addEventListener('change', () => { if (s.value) s.value = '1 month'; });"));

        Assert.Equal(NeedsHumanReason.NotApplied, run.Fields.Single(f => f.Label == "Notice period").Reason);
        Assert.Equal(string.Empty, host.Spy.Values["s"]);
        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
    }

    [Theory]
    [InlineData(Select, "const s = document.getElementById('s'); s.addEventListener('change', () => { s.value = '1 month'; });")]
    [InlineData("""<label for="s">GitHub</label><input id="s">""", "const s = document.getElementById('s'); s.addEventListener('input', () => { s.value = 'https://github.com/other'; });")]
    public async Task HandleAsync_ValueThatCannotBeTakenBack_IsNeverPrepared_F0_3_BR2(string field, string script)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(field, script));

        // Optional, but it now holds something nobody chose.
        Assert.NotEqual(string.Empty, host.Spy.Values["s"]);
        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
    }

    [Theory]
    [InlineData("""<fieldset><legend>Previous employment</legend><label for="x">Location</label><input id="x"></fieldset>""")]
    [InlineData("""<fieldset><legend>Education</legend><div role="group" aria-label="Contact information"><label for="x">Location</label><input id="x"></div></fieldset>""")]
    [InlineData("""<section aria-label="School"><label for="x">Location</label><input id="x"></section>""")]
    [InlineData("""<h3>Previous employment</h3><label for="x">Location</label><input id="x">""")]
    public async Task HandleAsync_KnownLabelUnderAnotherHeading_IsNotFilled_F0_3_BR2(string field)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(field));

        Assert.Equal(string.Empty, host.Spy.Values["x"]);
        Assert.Equal(NeedsHumanReason.Ambiguous, run.Fields.Single(f => f.Label == "Location").Reason);
    }

    [Theory]
    [InlineData("""<fieldset><legend>Contact information</legend><label for="x">Location</label><input id="x"></fieldset>""")]
    [InlineData("""<h3>Personal information</h3><label for="x">Location</label><input id="x">""")]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_KnownLabelUnderTheCandidatesOwnHeading_IsFilled_F0_3_AC1(string field)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        await host.RunAsync(FixturePages.NewBoard(field));

        Assert.Equal(ApplyHost.Profile.Contact.Location, host.Spy.Values["x"]);
    }

    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_CvFieldUnderTheCandidatesOwnHeading_IsAttached_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.Page(
            "<form id=\"application-form\"><fieldset><legend>Personal information</legend><label for=\"cv\">CV*</label><input id=\"cv\" type=\"file\"></fieldset><button>Submit</button></form>"));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
    }
}

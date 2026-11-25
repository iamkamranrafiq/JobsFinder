using JobsFinder.Application.Applying;
using JobsFinder.Domain.Applying;

namespace JobsFinder.IntegrationTests.Applying;

/// <summary>
/// Review loop 2: what a person is asked is recorded whatever shape it has (B2, M8), a field is either as it was or
/// reported (M3), and the run is judged by the page as it is at the end, not as it was before filling (M9). The rows are
/// inside a shared wrapper beside a searchable list, as on real pages.
/// </summary>
[Collection(ApiTestGroup.Name)]
[Trait("REQ", "F0.3-BR2")]
public sealed class SeenByAPersonTests(ApiFactory factory)
{
    private static string Wrapped(string row, string script = "") => FixturePages.NewBoard(
        $"""<div class="rows"><div class="row">{FixturePages.Combobox}</div>{row}</div>""",
        FixturePages.ComboboxScript + script);

    /// <summary>Everything is answerable except "x", which a person sees and must answer.</summary>
    public static TheoryData<string, string> Asked => new()
    {
        { "native select behind a widget", """<div class="row"><label for="x">Country*</label><select id="x" style="display:none"><option></option><option>UK</option></select><div>Choose</div></div>""" },
        { "native select behind a widget, no label", """<div class="row">Country* <select id="x" style="display:none"><option></option><option>UK</option></select><span>Choose</span></div>""" },
        { "required native select, flat", """Country <select id="x" required style="display:none"><option></option><option>UK</option></select><span>Choose</span>""" },
        { "styled checkbox", """<div class="row"><input type="checkbox" id="x" required style="opacity:0;position:absolute"><label for="x">I have read the notice</label></div>""" },
        { "styled checkbox, label around", """<div class="row"><label>I have read the notice* <input type="checkbox" id="x" style="opacity:0;position:absolute"></label></div>""" },
        { "hidden file, no label", """<div class="row">Transcript* <button type="button">Attach</button><input id="x" type="file" required hidden></div>""" },
        { "hidden file, required by attribute only", """<div class="row">Transcript <button type="button">Attach</button><input id="x" type="file" required hidden></div>""" },
        { "hidden file, flat", """<div>Transcript*</div><button type="button">Attach</button><input id="x" type="file" hidden>""" },
        { "hidden file in a named group", """<div class="row"><div role="group" aria-label="Transcript"><input id="x" type="file" required style="display:none"><button type="button">Attach</button></div></div>""" },
        { "widget without any role", """<div class="row"><div>Colour*</div><div tabindex="0">Choose</div><input type="hidden" id="x"></div>""" },
        { "widget without any role, marker drawn by style", """<style>.req::after { content: "*"; }</style><div class="row"><div class="req">Colour</div><div tabindex="0">Choose</div><input type="hidden" id="x"></div>""" },
        { "widget without any role, marker apart", """<div class="row"><div>Colour</div><span>*</span><div tabindex="0">Choose</div><input type="hidden" id="x"></div>""" },
        { "widget without any role, no star on its neighbour", """<div class="row"><label for="y">Nickname</label><input id="y"><div><div>Colour (required)</div><div tabindex="0">Choose</div></div><input type="hidden" id="x"></div>""" },
        { "widget without any role, worded like a legend", """<div class="row"><div>Please indicate your colour*</div><div tabindex="0">Choose</div><input type="hidden" id="x"></div>""" },
        { "widget without any role, marker in the text", """<div class="row"><div>Colour * (choose one)</div><div tabindex="0">Choose</div><input type="hidden" id="x"></div>""" },
        { "listbox", """<div class="row"><div id="l">Colour*</div><div role="listbox" tabindex="0" aria-labelledby="l"><div role="option">Red</div></div><input type="hidden" id="x"></div>""" },
        { "date button", """<div class="row"><div id="l">Start date*</div><button type="button" aria-haspopup="dialog" aria-labelledby="l">Pick</button><input type="hidden" id="x"></div>""" },
        { "visible but hidden from readers", """<div class="row" aria-hidden="true"><label for="x">Planet*</label><input id="x"></div>""" },
        { "inert", """<div class="row" inert><label for="x">Planet*</label><input id="x"></div>""" },
        { "in closed details", """<details><summary>More about you</summary><label for="x">Planet*</label><input id="x"></details>""" },
        { "in a collapsed section", """<button type="button" aria-expanded="false" aria-controls="sec">More about you</button><div id="sec" style="display:none"><label for="x">Planet*</label><input id="x"></div>""" },
        { "embedded frame", """<div class="row"><iframe srcdoc="<label>Planet* <input></label>" style="width:300px;height:60px"></iframe><input type="hidden" id="x"></div>""" },
        { "marker beside the label", """<label for="x">Colour</label><span>*</span><input id="x">""" },
        { "marker beside the label in its row", """<div class="row"><label for="x">Colour</label><span>*</span><input id="x"></div>""" },
        { "marker after the field", """<div class="row"><label for="x">Colour</label><input id="x"><em>(required)</em></div>""" },
        { "no label, text beside it", """<div class="row"><div>Colour *</div><input id="x"></div>""" },
        { "no label at all", """<div class="row"><input id="x"></div>""" },
        { "select without a label", """<div class="row"><select id="x"><option value=""></option><option>Red</option></select></div>""" },
        { "outside every form", """</form><label for="x">Planet*</label><input id="x"><form id="other">""" },
    };

    [Theory]
    [MemberData(nameof(Asked))]
    public async Task HandleAsync_PersonIsAskedSomethingItCannotAnswer_IsNeverPrepared_F0_3_BR2(string name, string row)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);

        // Submit first, so that what closes the form stays after it.
        var html = row.StartsWith("</form>", StringComparison.Ordinal)
            ? FixturePages.NewBoard(FixturePages.Submit + row, submit: string.Empty)
            : Wrapped(row);
        var run = await host.RunAsync(html);

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        Assert.Contains(run.Fields, f => f is { Required: true, Outcome: ApplyFieldOutcome.NeedsHuman });
        Assert.Equal(string.Empty, host.Spy.Values["x"]);
        Assert.True(run.CvAttached);
    }

    [Fact]
    public async Task HandleAsync_FieldInShadowDom_IsNeverPrepared_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(
            "<div class=\"row\" id=\"shadow\"></div>",
            "document.getElementById('shadow').attachShadow({ mode: 'open' }).innerHTML = '<label>Planet* <input></label>';"));

        Assert.Equal(ApplyReason.UnknownField, run.Reason);
    }

    /// <summary>Too strict would be no use either: none of these asks a person anything.</summary>
    [Theory]
    [InlineData("""<p>* Indicates a required field</p>""")]
    [InlineData("""<p>Fields marked with * are required.</p>""")]
    [InlineData("""<p>We process data as required by law.</p>""")]
    [InlineData("""<div class="row"><input required aria-hidden="true" tabindex="-1" style="opacity:0;position:absolute;width:100%"></div>""")]
    [InlineData("""<div style="display:none"><label for="h">Colour*</label><input id="h" required></div>""")]
    [InlineData("""<div class="row"><label for="c">Favourite colour</label><input id="c"></div>""")]
    [InlineData("""<div class="row"><label for="p">Phone type</label><select id="p"><option>Mobile</option></select></div>""")]
    [InlineData("""<div style="display:none" id="step2"><label for="s">Planet*</label><input id="s" required></div>""")]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_NothingMoreIsAsked_IsPrepared_F0_3_AC1(string row)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(row));

        Assert.Equal((ApplyRunStatus.Prepared, (ApplyReason?)null), (run.Status, run.Reason));
        Assert.Equal("3 months", host.Spy.Values["single-value"]);
    }

    /// <summary>React-select's own hidden required input is part of the list beside it, not a second question.</summary>
    [Fact]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_HiddenRequiredInputOfTheListItself_IsPrepared_F0_3_AC1()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(
            FixturePages.Combobox.Replace(
                "<div id=\"notice-menu\">",
                "<input required aria-hidden=\"true\" tabindex=\"-1\" style=\"opacity:0;position:absolute;width:100%\"><div id=\"notice-menu\">",
                StringComparison.Ordinal),
            FixturePages.ComboboxScript));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
    }

    [Theory]
    [InlineData("""<fieldset><legend>Location</legend><input id="x"></fieldset>""")]
    [InlineData("""<div role="group" aria-label="Location"><input id="x"></div>""")]
    [InlineData("""<fieldset><legend id="l">Location</legend><input id="x" aria-labelledby="l"></fieldset>""")]
    [InlineData("""<h4>Location</h4><input id="x">""")]
    [InlineData("""<h4 id="l">Location</h4><input id="x" aria-labelledby="l"><input id="y" aria-labelledby="l">""")]
    [InlineData("""<div class="row">Location <input id="x"></div>""")]
    [InlineData("""<div class="row"><input id="x" placeholder="Location" title="Location"></div>""")]
    [InlineData("""<fieldset><legend>Location</legend><select id="x"><option value=""></option><option>Leeds, UK</option></select></fieldset>""")]
    public async Task HandleAsync_NameThatIsNotTheFieldsOwn_IsNotUsedToFillIt_F0_3_BR2(string row)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(row));

        Assert.Equal(string.Empty, host.Spy.Values["x"]);
        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
    }

    [Fact]
    [Trait("REQ", "F0.3-BR2")]
    public async Task HandleAsync_TwoFilesInAGroupNamedResume_NeitherGetsTheCvByTheGroupName_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.Page(
            "<form id=\"application-form\"><fieldset><legend>Resume</legend><label>Attach <input type=\"file\" id=\"a\"></label><label>Attach <input type=\"file\" id=\"b\"></label></fieldset><button>Submit</button></form>"));

        Assert.False(run.CvAttached);
        Assert.Equal((string.Empty, string.Empty), (host.Spy.Values["a"], host.Spy.Values["b"]));
    }

    // ---- M3: an optional field that is neither filled nor as it was must not go unnoticed ----

    private const string List =
        """<div class="row"><label for="s">Current company</label><div style="position:relative"><input id="s" role="combobox" aria-controls="list"><div id="list" role="listbox" hidden><div role="option">Quill Systems Ltd</div></div></div></div>""";

    private const string OpenList = "const s = document.getElementById('s'); const l = document.getElementById('list'); s.addEventListener('input', () => { l.hidden = !s.value; }); ";

    private const string Placeholder =
        """<div class="row"><label for="s">Current company</label><select id="s"><option disabled selected>Select</option><option>Other Ltd</option><option>Quill Systems Ltd</option></select></div>""";

    private const string Text = """<div class="row"><label for="s">Current company</label><input id="s"></div>""";

    public static TheoryData<string, string, string> LeftChanged => new()
    {
        { "option covered, input locked", List, OpenList + "s.addEventListener('input', () => { const o = document.createElement('div'); o.style = 'position:fixed;inset:0;z-index:9'; document.body.append(o); s.readOnly = true; });" },
        { "disabled placeholder", Placeholder, "const s = document.getElementById('s'); s.addEventListener('change', () => { s.selectedIndex = 1; });" },
        { "nothing selected before", Placeholder, "const s = document.getElementById('s'); s.selectedIndex = -1; s.addEventListener('change', () => { s.selectedIndex = 1; });" },
        { "select locked after the change", Placeholder, "const s = document.getElementById('s'); s.addEventListener('change', () => { s.selectedIndex = 1; s.disabled = true; });" },
        { "text locked after the change", Text, "const s = document.getElementById('s'); s.addEventListener('input', () => { s.value = 'Other Ltd'; s.readOnly = true; });" },
        { "prefilled text changed", Text, "const s = document.getElementById('s'); s.value = 'Given'; s.addEventListener('blur', () => { s.value = 'Other Ltd'; });" },
        { "field replaced while typing", Text, "const s = document.getElementById('s'); s.addEventListener('input', () => { s.outerHTML = '<input id=\"s\" value=\"Other Ltd\">'; });" },
    };

    [Theory]
    [MemberData(nameof(LeftChanged))]
    public async Task HandleAsync_OptionalFieldIsNeitherFilledNorAsItWas_IsNeverPrepared_F0_3_BR2(string name, string field, string script)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(field, script));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        Assert.DoesNotContain(run.Fields, f => f is { Label: "Current company", Outcome: ApplyFieldOutcome.Filled });
    }

    [Theory]
    [InlineData(List, OpenList + "const o = document.createElement('div'); o.style = 'position:absolute;inset:0;z-index:9'; l.style.position = 'relative'; l.append(o);")]
    [InlineData(Text, "const s = document.getElementById('s'); s.addEventListener('input', () => { if (s.value) s.value = ''; });")]
    [Trait("REQ", "F0.3-AC1")]
    public async Task HandleAsync_OptionalFieldIsVerifiedToBeAsItWas_IsPrepared_F0_3_AC1(string field, string script)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(field, script));

        Assert.Equal(string.Empty, host.Spy.Values["s"]);
        Assert.Equal(NeedsHumanReason.NotApplied, run.Fields.Single(f => f.Label == "Current company").Reason);
        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
    }

    [Fact]
    public async Task HandleAsync_PagePutsAnotherFileInTheOptionalCvField_IsNotPrepared_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.NewBoard(
            script: "const r = document.getElementById('resume'); r.addEventListener('change', () => { const d = new DataTransfer(); d.items.add(new File(['x'], 'other.pdf')); r.files = d.files; });"));

        Assert.False(run.CvAttached);
        Assert.Equal(ApplyRunStatus.NeedsYou, run.Status);
        Assert.Contains(run.Fields, f => f is { Label: "Resume/CV*", Reason: NeedsHumanReason.NotCleared });
    }

    // ---- M9: the page changes because of what was filled in ----

    private const string Extra = """<div class="row" id="more"></div><div class="row"><label for="o">Favourite colour</label><input id="o"></div>""";

    public static TheoryData<string, string> Changes => new()
    {
        { "conditional required field", "document.getElementById('email').addEventListener('blur', () => { document.getElementById('more').innerHTML = '<label for=n>Why not*</label><input id=n>'; });" },
        { "conditional field after the CV", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('more').innerHTML = '<label for=n>Why not</label><input id=n>'; });" },
        { "conditional field a little later", "document.getElementById('resume').addEventListener('change', () => setTimeout(() => { document.getElementById('more').innerHTML = '<label for=n>Why not*</label><input id=n>'; }, 250));" },
        { "optional field becomes required", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('o').required = true; });" },
        { "value cleared after the CV", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('email').value = ''; });" },
        { "value changed after the CV", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('email').value = 'other@example.com'; });" },
        { "optional value changed after the CV", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('phone').value = '123'; });" },
        { "page fills another field itself", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('o').value = 'Blue'; });" },
        { "answered field disappears", "document.getElementById('resume').addEventListener('change', () => { document.getElementById('phone').parentElement.remove(); });" },
        { "answered field is drawn again, empty", "document.getElementById('resume').addEventListener('change', () => { const p = document.getElementById('email').parentElement; p.innerHTML = p.innerHTML; });" },
        { "CV dropped later", "document.getElementById('linkedin').addEventListener('blur', () => setTimeout(() => { document.getElementById('resume').value = ''; }, 150));" },
        { "list choice dropped later", "document.getElementById('resume').addEventListener('change', () => setTimeout(() => { document.querySelector('.select__single-value').textContent = ''; }, 300));" },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task HandleAsync_PageChangesBecauseOfTheAnswers_IsNeverPrepared_F0_3_BR2(string name, string script)
    {
        Assert.NotEmpty(name);
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(Extra, script));

        Assert.Equal(ApplyRunStatus.NeedsYou, run.Status);
        Assert.Contains(run.Reason!.Value, new[] { ApplyReason.UnknownField, ApplyReason.CvNotAttached });
        Assert.NotEmpty(await host.ScreenshotsAsync(run.Id));
    }

    [Theory]
    [InlineData("document.querySelector('[type=submit]').remove();")]
    [InlineData("const b = document.createElement('button'); b.type = 'button'; b.textContent = 'Next'; document.querySelector('form').append(b);")]
    [InlineData("document.querySelector('[type=submit]').textContent = 'Continue';")]
    [Trait("REQ", "F0.3-EdgeMultiStep")]
    public async Task HandleAsync_FinalStepIsNotFinalAnyMore_IsNeverPrepared_F0_3_EdgeMultiStep(string change)
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(
            Extra, $"document.getElementById('resume').addEventListener('change', () => {{ {change} }});"));

        Assert.Equal(ApplyRunStatus.Failed, run.Status);
        Assert.Contains(run.Reason!.Value, new[] { ApplyReason.StepNotReached, ApplyReason.FormNotRecognised });
    }

    [Fact]
    public async Task HandleAsync_LaterStepDropsAnEarlierAnswer_IsNeverPrepared_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(FixturePages.MultiStep(
            script: FixturePages.StepScript + "document.getElementById('resume').addEventListener('change', () => { document.getElementById('first_name').value = ''; });"));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
        Assert.Equal(NeedsHumanReason.NotKept, run.Fields.Single(f => f.Label == "First Name*").Reason);
    }

    [Fact]
    public async Task HandleAsync_PageNeverComesToRest_IsNeverPrepared_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(Extra, "let i = 0; setInterval(() => { document.getElementById('o').value = `${i++}`; }, 60);"));

        Assert.NotEqual(ApplyRunStatus.Prepared, run.Status);
    }

    /// <summary>With the product's own waiting times: a change 1.5 s after the last answer is still noticed.</summary>
    [Fact]
    public async Task HandleAsync_PageChangesLate_IsNeverPrepared_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory, new ApplySettings());

        var run = await host.RunAsync(Wrapped(
            Extra, "document.getElementById('resume').addEventListener('change', () => setTimeout(() => { document.getElementById('email').value = ''; }, 1500));"));

        Assert.Equal((ApplyRunStatus.NeedsYou, (ApplyReason?)ApplyReason.UnknownField), (run.Status, run.Reason));
    }

    /// <summary>Reading again must stay reading: nothing that appeared is typed into or clicked.</summary>
    [Fact]
    public async Task HandleAsync_FieldAndNextAppearLater_AreNeitherFilledNorClicked_F0_3_BR2()
    {
        await using var host = await ApplyHost.StartAsync(factory);

        var run = await host.RunAsync(Wrapped(
            Extra + "<input type=\"hidden\" id=\"touched\">",
            """
            document.getElementById('resume').addEventListener('change', () => {
              document.getElementById('more').innerHTML = '<label for="n">Website</label><input id="n"><label for="g">GitHub</label><input id="g"><button type="button" id="nx">Next</button>';
              for (const id of ['n', 'g', 'nx']) for (const type of ['focus', 'input', 'click', 'keydown', 'change'])
                document.getElementById(id).addEventListener(type, () => { document.getElementById('touched').value += `${id}:${type} `; });
            });
            """));

        Assert.Equal(string.Empty, host.Spy.Values["touched"]);
        Assert.Equal((string.Empty, string.Empty), (host.Spy.Values["n"], host.Spy.Values["g"]));
        Assert.NotEqual(ApplyRunStatus.Prepared, run.Status);
        Assert.All(
            run.Fields.Where(f => f.Label is "Website" or "GitHub"),
            f => Assert.Equal(NeedsHumanReason.AppearedLater, f.Reason));
    }

    /// <summary>Reading a step is quadratic in places; a long form must still fit the budget with the product's own times.</summary>
    [Fact]
    [Trait("REQ", "F0.3-NFR")]
    public async Task HandleAsync_LongFormWithProductTimes_StaysFarBelowTheBudget_F0_3_NFR()
    {
        await using var host = await ApplyHost.StartAsync(factory, new ApplySettings());
        var rows = string.Concat(Enumerable.Range(0, 85).Select(i =>
            $"""<div class="row"><div><label for="l{i}">Question {i}</label></div><div><div><input id="l{i}"></div><p>Some help for question {i}.</p></div></div>"""));
        var started = TimeProvider.System.GetTimestamp();

        var run = await host.RunAsync(Wrapped(rows));

        Assert.Equal(ApplyRunStatus.Prepared, run.Status);
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        Assert.True(elapsed < TimeSpan.FromSeconds(45), $"Took {elapsed}.");
    }
}

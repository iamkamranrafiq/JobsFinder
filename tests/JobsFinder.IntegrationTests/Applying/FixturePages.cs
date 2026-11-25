namespace JobsFinder.IntegrationTests.Applying;

/// <summary>My imitation of Greenhouse forms. Not copied from, and not checked against, the real site.</summary>
internal static class FixturePages
{
    /// <summary>Records what a page script sees when the form is handled, so tests can read the filled page back.</summary>
    public const string Submit = """<button type="submit">Submit application</button>""";

    /// <summary>Layout of the newer boards: React-like markup, hidden file input, drawn by script after load.</summary>
    public static string NewBoard(string extraFields = "", string script = "", string submit = Submit) => Page(
        $$"""
        <form id="application-form" class="application--form" method="post" action="/submit">
          <div><label for="first_name">First Name*</label><input id="first_name" type="text" autocomplete="given-name" aria-required="true"></div>
          <div><label for="last_name">Last Name*</label><input id="last_name" type="text" autocomplete="family-name" aria-required="true"></div>
          <div><label for="email">Email*</label><input id="email" type="text" autocomplete="email" aria-required="true" maxlength="255"></div>
          <div><label for="phone">Phone</label><input id="phone" type="tel" autocomplete="tel"></div>
          <div role="group" aria-labelledby="upload-label-resume">
            <div id="upload-label-resume">Resume/CV*</div>
            <button type="button">Attach</button>
            <label for="resume" class="visually-hidden">Attach</label>
            <input id="resume" type="file" class="visually-hidden" accept=".pdf,.doc,.docx,.txt,.rtf">
          </div>
          <div role="group" aria-labelledby="upload-label-cover">
            <div id="upload-label-cover">Cover Letter</div>
            <label for="cover" class="visually-hidden">Attach</label>
            <input id="cover" type="file" class="visually-hidden" accept=".pdf,.doc,.docx">
          </div>
          <div><label for="linkedin">LinkedIn Profile</label><input id="linkedin" type="text"></div>
          {{extraFields}}
          {{submit}}
        </form>
        """,
        script);

    /// <summary>Layout of the older boards: everything in one form, native controls.</summary>
    public static string LegacyBoard() => Page(
        $$"""
        <h1>Engineer at Acme</h1><p style="height: 1500px">A long job description.</p>
        <form id="application_form" method="post" action="/submit">
          <label>First Name <span class="asterisk">*</span> <input type="text" name="job_application[first_name]" required></label>
          <label>Last Name <span class="asterisk">*</span> <input type="text" name="job_application[last_name]" required></label>
          <label>Email <span class="asterisk">*</span> <input type="email" name="job_application[email]" required></label>
          <label>Location <input type="text" name="job_application[location]"></label>
          <fieldset><legend>Resume/CV *</legend><label>Attach <input type="file" name="job_application[resume]"></label></fieldset>
          <label>GitHub <input type="url" name="github"></label>
          <label>Current Company <input type="text" name="company"></label>
          <label>Notice period <select name="notice"><option value="">Select</option><option>1 month</option><option>3 Months</option></select></label>
          <input type="submit" value="Submit Application">
        </form>
        """);

    /// <summary>Three steps in one form; only the current one is visible, as client-side wizards do it.</summary>
    public static string MultiStep(string nextControl = """<button type="button" class="next">Next</button>""", string script = StepScript) => Page(
        $$"""
        <form id="application-form" method="post" action="/submit">
          <section class="step">
            <label for="first_name">First Name*</label><input id="first_name" required>
            <label for="last_name">Last Name*</label><input id="last_name" required>
            {{nextControl}}
          </section>
          <section class="step" hidden>
            <label for="email">Email*</label><input id="email" type="email" required>
            <label for="resume">Resume/CV*</label><input id="resume" type="file" required>
            {{nextControl}}
          </section>
          <section class="step" hidden>
            <label for="github">GitHub</label><input id="github">
            <button type="submit">Submit application</button>
          </section>
        </form>
        """,
        script);

    public const string StepScript =
        """
        for (const next of document.querySelectorAll('.next')) {
          next.addEventListener('click', () => {
            const step = next.closest('.step');
            step.hidden = true;
            step.nextElementSibling.hidden = false;
          });
        }
        """;

    /// <summary>A searchable list as React-select draws it: the input is tiny and emptied after a choice.</summary>
    public const string Combobox =
        """
        <div><label id="notice-label" for="notice">Notice period*</label>
          <div class="select__control"><div class="select__single-value"></div>
            <input id="notice" role="combobox" aria-autocomplete="list" aria-required="true" style="width: 2px; border: 0; padding: 0">
          </div><div id="notice-menu"></div></div>
        """;

    public const string ComboboxScript =
        """
        const input = document.getElementById('notice');
        const menu = document.getElementById('notice-menu');
        input.addEventListener('input', () => {
          menu.innerHTML = '';
          input.removeAttribute('aria-controls');
          if (!input.value) return;
          const list = document.createElement('div');
          list.id = 'notice-list';
          list.setAttribute('role', 'listbox');
          for (const text of ['1 month', '3 months', '3 months or more']) {
            const option = document.createElement('div');
            option.setAttribute('role', 'option');
            option.textContent = text;
            option.addEventListener('mousedown', (e) => e.preventDefault());
            option.addEventListener('click', () => {
              document.querySelector('.select__single-value').textContent = text;
              input.value = '';
              menu.innerHTML = '';
            });
            list.append(option);
          }
          menu.append(list);
          input.setAttribute('aria-controls', 'notice-list');
        });
        """;

    public static string Page(string body, string script = "") =>
        $$"""
        <!doctype html><html lang="en"><head><title>Job application</title>
        <style>
          .visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); opacity: 0; }
          label, input, select, textarea { display: block; margin: 4px 0; }
        </style></head>
        <body>{{body}}<script>{{script}}</script></body></html>
        """;
}

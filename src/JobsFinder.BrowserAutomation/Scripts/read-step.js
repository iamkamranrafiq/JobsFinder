// Reads the current step of a Greenhouse application form. Read-only: it changes nothing on the page.
// Returns { meta, elements, next }; meta.fields[i] describes elements[i]. The caller treats all of it as untrusted.
() => {
  const forms = [...document.querySelectorAll('form')];
  const known = forms.filter(
    (f) => f.id === 'application-form' || f.id === 'application_form' || f.classList.contains('application--form'),
  );
  const withFile = forms.filter((f) => f.querySelector('input[type="file"]'));
  const form = known.length === 1 ? known[0] : known.length === 0 && withFile.length === 1 ? withFile[0] : null;
  if (!form) {
    return { meta: { found: false, end: 'None', fields: [] }, elements: [], next: null };
  }

  // What a person cannot see is either not for people (honeypots) or belongs to another step.
  const shown = (el, minSize = 4) => {
    if (!el.isConnected || el.closest('[aria-hidden="true"], [hidden], [inert]')) {
      return false;
    }
    const box = el.getBoundingClientRect();
    if (box.width < minSize || box.height < minSize) {
      return false;
    }
    if (box.right + scrollX <= 0 || box.bottom + scrollY <= 0 || box.left + scrollX >= document.documentElement.scrollWidth) {
      return false;
    }
    if (getComputedStyle(el).visibility !== 'visible') {
      return false;
    }
    for (let e = el; e; e = e.parentElement) {
      const style = getComputedStyle(e);
      const area = e.getBoundingClientRect();
      if (style.display === 'none' || Number(style.opacity) < 0.1) {
        return false;
      }
      if (e !== el && style.overflow !== 'visible' && (area.width < 2 || area.height < 2)) {
        return false;
      }
    }
    return true;
  };

  const textOf = (el) => {
    const copy = el.cloneNode(true);
    copy.querySelectorAll('input, select, textarea, button, script, style, [aria-hidden="true"]').forEach((e) => e.remove());
    return copy.textContent || '';
  };

  const referenced = (el, attribute) =>
    (el.getAttribute(attribute) || '')
      .split(/\s+/)
      .filter(Boolean)
      .map((id) => document.getElementById(id))
      .filter(Boolean);

  // ---- Labels, and the headings a field is under ----

  const labelElementsOf = (el) => [...new Set([...referenced(el, 'aria-labelledby'), ...(el.labels || [])])];

  const labelOf = (el) =>
    referenced(el, 'aria-labelledby').map(textOf).join(' ').trim() ||
    [...(el.labels || [])].map(textOf).join(' ').trim() ||
    el.getAttribute('aria-label') ||
    '';

  const groupSelector = 'fieldset, [role="group"], [role="radiogroup"], section, [role="region"]';
  const groupElementsOf = (el) => {
    const groups = [];
    for (let g = el.parentElement?.closest(groupSelector); g && form.contains(g); g = g.parentElement?.closest(groupSelector)) {
      groups.push(g);
    }
    return groups;
  };

  const groupLabelElementsOf = (group) => {
    const legend = group.querySelector(':scope > legend');
    return [...(legend ? [legend] : []), ...referenced(group, 'aria-labelledby')];
  };

  const groupNameOf = (group) =>
    groupLabelElementsOf(group).map(textOf).join(' ').trim() || (group.getAttribute('aria-label') || '').trim();

  const headings = [...form.querySelectorAll('h1, h2, h3, h4, h5, h6, [role="heading"]')].filter((h) => shown(h, 1));
  const headingOf = (el) => {
    const before = headings.filter((h) => h.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING);
    return before.length > 0 ? textOf(before[before.length - 1]).trim() : '';
  };

  // ---- Required: whatever tells a person so counts. Too strict only means that the candidate is asked. ----

  const saysRequired = (text) => /[*＊∗✱]/.test(text) || /\brequired\b/i.test(text.replace(/\bnot required\b/gi, ''));
  const drawnBy = (el) => ['::before', '::after'].map((part) => getComputedStyle(el, part).content).join(' ');
  const marks = (el) =>
    [el, ...el.querySelectorAll('*')].some((e) => saysRequired(drawnBy(e))) || saysRequired(el.textContent || '');

  const isRequired = (el) => {
    const groups = groupElementsOf(el);
    const sameName =
      el.tagName === 'INPUT' && ['radio', 'checkbox'].includes(el.type) && el.name
        ? [...form.elements].filter((e) => e.type === el.type && e.name === el.name)
        : [el];
    return (
      sameName.some(
        (e) =>
          e.required === true ||
          e.getAttribute('aria-required') === 'true' ||
          saysRequired(e.getAttribute('aria-label') || '') ||
          [...labelElementsOf(e), ...referenced(e, 'aria-describedby')].some(marks),
      ) ||
      groups.some(
        (g) =>
          g.getAttribute('aria-required') === 'true' ||
          saysRequired(g.getAttribute('aria-label') || '') ||
          groupLabelElementsOf(g).some(marks),
      )
    );
  };

  // ---- Fields ----

  const fields = [];
  const elements = [];
  const choices = new Set();
  const add = (el, kind, fallbackLabel = '') => {
    const groups = groupElementsOf(el).map(groupNameOf).filter(Boolean);
    const heading = headingOf(el);
    const label = labelOf(el);
    elements.push(el);
    fields.push({
      kind,
      label: ((kind === 'Choice' && groups[0]) || label || groups[0] || fallbackLabel).slice(0, 2000),
      groups: [...groups, ...(heading ? [heading] : [])].slice(0, 20).map((g) => g.slice(0, 2000)),
      required: isRequired(el),
      inputType: kind === 'Text' ? (el.type || '').slice(0, 50) : '',
      autocomplete: (el.getAttribute('autocomplete') || '').slice(0, 100),
      maxLength: typeof el.maxLength === 'number' && el.maxLength >= 0 ? el.maxLength : null,
      pattern: (el.getAttribute('pattern') || '').slice(0, 1000),
      accept: (el.getAttribute('accept') || '').slice(0, 500),
      name: `${el.id}|${el.name || ''}`.slice(0, 200),
      // A disabled option keeps its place so that the index stays right, but can never match.
      options: kind === 'Select' ? [...el.options].slice(0, 101).map((o) => (o.disabled ? '' : o.text.slice(0, 500))) : [],
    });
  };

  // A control that is not visible itself but stands behind something visible: a person is asked, the code cannot answer.
  const standsBehindSomethingVisible = (el) =>
    labelElementsOf(el).some((l) => shown(l, 1)) ||
    ((el.required === true || el.getAttribute('aria-required') === 'true') && !!el.parentElement && shown(el.parentElement));

  // React-select adds a hidden required input beside its own search box, which is the field that counts.
  const isShadowOfCombobox = (el) => {
    let parent = el.parentElement;
    for (let i = 0; parent && parent !== form && i < 3; i++, parent = parent.parentElement) {
      if ([...parent.querySelectorAll('input[role="combobox"]')].some((c) => c !== el && shown(c, 1))) {
        return true;
      }
    }
    return false;
  };

  const controlSelector =
    'input, select, textarea, [contenteditable]:not([contenteditable="false"]), [role="textbox"]:not(input, textarea), ' +
    '[role="combobox"]:not(input), [role="checkbox"]:not(input), [role="radio"]:not(input), [role="switch"]:not(input), ' +
    '[role="slider"]:not(input), [role="spinbutton"]:not(input), button[aria-haspopup="listbox"]';
  const controls = [...document.querySelectorAll(controlSelector)].filter((el) => form.contains(el) || el.form === form);

  for (const el of controls) {
    const tag = el.tagName;
    const type = tag === 'INPUT' ? el.type : '';
    if (['hidden', 'submit', 'button', 'reset', 'image'].includes(type)) {
      continue;
    }

    const isNative = tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA';
    const isCombobox = tag === 'INPUT' && el.getAttribute('role') === 'combobox';
    const isChoice = !isNative || ['checkbox', 'radio'].includes(type);

    if (type === 'file') {
      // Often hidden behind a button; what counts is whether a person can see what it is for.
      const owner = groupElementsOf(el)[0];
      if (shown(el) || [...(el.labels || [])].some((l) => shown(l)) || (owner && shown(owner))) {
        add(el, el.matches(':disabled') ? 'Choice' : 'File');
      }
      continue;
    }

    const visible = shown(el, isCombobox || isChoice ? 1 : 4);
    if (!visible && (isShadowOfCombobox(el) || !standsBehindSomethingVisible(el))) {
      continue;
    }

    if (isChoice) {
      const key = `${type}|${el.name || ''}`;
      if (isNative && el.name) {
        if (choices.has(key)) {
          continue;
        }
        choices.add(key);
      }
      add(el, 'Choice');
      continue;
    }

    const usable = visible && !el.matches(':disabled') && !el.readOnly && (isCombobox || el.tabIndex >= 0);
    if (!usable) {
      add(el, 'Choice', el.parentElement ? textOf(el.parentElement).trim() : '');
    } else if (isCombobox) {
      add(el, 'Combobox');
    } else if (tag === 'SELECT') {
      add(el, el.multiple ? 'Choice' : 'Select');
    } else {
      add(el, tag === 'TEXTAREA' ? 'TextArea' : 'Text');
    }
  }

  // ---- How the step ends. Only what is plain counts; everything else is "Unclear". ----

  const wordsOf = (text) => (text || '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();

  // Every name the control has: the one a person sees and the ones only a script sees must agree.
  const namesOf = (b) =>
    [
      b.getAttribute('aria-label'),
      ...referenced(b, 'aria-labelledby').map((e) => e.textContent),
      b.getAttribute('value'),
      b.getAttribute('title'),
      b.textContent,
    ]
      .map(wordsOf)
      .filter(Boolean);

  const finalNames = [
    'submit', 'submit application', 'submit your application', 'submit my application', 'apply', 'apply now', 'apply for this job',
    'send', 'send application', 'send my application',
  ];
  const nextNames = ['next', 'next step', 'continue'];
  const isSubmitType = (b) => (b.tagName === 'BUTTON' || b.tagName === 'INPUT') && (b.type === 'submit' || b.type === 'image');
  const soundsLikeSubmit = (b) => namesOf(b).some((n) => /\b(submit|apply|send|finish|complete)\b/.test(n));
  const soundsLikeNext = (b) => namesOf(b).some((n) => /\b(next|continue|proceed)\b/.test(n));

  const allButtons = [
    ...document.querySelectorAll('button, input[type="submit"], input[type="image"], input[type="button"], [role="button"], a'),
  ].filter((b) => shown(b));
  const buttons = allButtons.filter((b) => form.contains(b) || b.form === form);

  // Looked for on the whole page: a wizard may keep its buttons outside the form.
  const nextLike = allButtons.filter(soundsLikeNext);
  const finals = buttons.filter((b) => isSubmitType(b) && namesOf(b).length > 0 && namesOf(b).every((n) => finalNames.includes(n)));
  const submitLike = buttons.filter((b) => isSubmitType(b) || soundsLikeSubmit(b));

  const isPlainNext = (b) =>
    buttons.includes(b) &&
    (b.tagName === 'BUTTON' || b.tagName === 'INPUT') &&
    b.type === 'button' &&
    !b.matches(':disabled') &&
    b.getAttribute('aria-disabled') !== 'true' &&
    !b.parentElement.closest('a, button, label, summary, [role="button"], [role="link"]') &&
    namesOf(b).every((n) => nextNames.includes(n));

  const next = submitLike.length === 0 && nextLike.length === 1 && isPlainNext(nextLike[0]) ? nextLike[0] : null;
  const end = next ? 'Next'
    : finals.length > 0 && nextLike.length === 0 ? 'Final'
    : submitLike.length > 0 || nextLike.length > 0 ? 'Unclear'
    : 'None';

  return { meta: { found: true, end, fields }, elements, next };
}

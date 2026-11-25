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

  // What a sighted person can see. Being hidden from screen readers (aria-hidden) or switched off (inert, disabled) does
  // not make a question go away, so it does not count here.
  const seen = (el, minSize = 4) => {
    if (!el.isConnected || !el.checkVisibility({ visibilityProperty: true, contentVisibilityAuto: true })) {
      return false;
    }
    const box = el.getBoundingClientRect();
    if (box.width < minSize || box.height < minSize) {
      return false;
    }
    if (box.right + scrollX <= 0 || box.bottom + scrollY <= 0 || box.left + scrollX >= document.documentElement.scrollWidth) {
      return false;
    }
    for (let e = el; e; e = e.parentElement) {
      const style = getComputedStyle(e);
      const area = e.getBoundingClientRect();
      if (Number(style.opacity) < 0.1) {
        return false;
      }
      if (e !== el && style.overflow !== 'visible' && (area.width < 2 || area.height < 2)) {
        return false;
      }
    }
    return true;
  };

  // What the code may use: seen, and neither hidden from readers nor switched off.
  const operable = (el, minSize = 4) => seen(el, minSize) && !el.closest('[aria-hidden="true"], [inert]') && !el.matches(':disabled');

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

  // ---- Controls: everything a question can be answered with, seen or not ----

  const controlSelector =
    'input, select, textarea, [contenteditable]:not([contenteditable="false"]), ' +
    '[role="textbox"]:not(input, textarea), [role="searchbox"]:not(input), [role="combobox"]:not(input), ' +
    '[role="checkbox"]:not(input), [role="radio"]:not(input), [role="switch"]:not(input), [role="slider"]:not(input), ' +
    '[role="spinbutton"]:not(input), [role="listbox"]:not(select), button[aria-haspopup]';

  // Not a question: buttons, and type=hidden, which nothing shows (a widget in front of it is found by its own role, or
  // by the required marker a person sees).
  const isQuestion = (el) => el.tagName !== 'INPUT' || !['hidden', 'submit', 'button', 'reset', 'image'].includes(el.type);

  // The list that a searchable input opens is part of that input.
  const isPopup = (el) =>
    el.getAttribute('role') === 'listbox' &&
    !!el.id &&
    !!document.querySelector(`[aria-controls~="${CSS.escape(el.id)}"], [aria-owns~="${CSS.escape(el.id)}"]`);

  const everywhere = [...document.querySelectorAll(controlSelector)].filter((el) => isQuestion(el) && !isPopup(el));
  const controls = everywhere.filter((el) => form.contains(el) || el.form === form);

  const isSameQuestion = (a, b) =>
    a === b || (a.tagName === 'INPUT' && ['radio', 'checkbox'].includes(a.type) && a.type === b.type && !!a.name && a.name === b.name);

  // The largest element around a control that holds no other question: what is written there is about this control.
  const rowOf = (el) => {
    let row = el;
    for (let p = el.parentElement; p && p !== form && p !== document.body; p = p.parentElement) {
      if (controls.some((c) => p.contains(c) && !isSameQuestion(c, el))) {
        break;
      }
      row = p;
    }
    return row;
  };

  const seenTextIn = (root) => {
    const texts = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      const parent = node.parentElement;
      if (node.data.trim() && parent && !parent.closest('select, textarea, script, style, [contenteditable]') && seen(parent, 1)) {
        texts.push(node.data.trim());
      }
    }
    return texts.join(' ');
  };

  // ---- Labels, and the headings a field is under ----

  const labelElementsOf = (el) => [...new Set([...referenced(el, 'aria-labelledby'), ...(el.labels || [])])];

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

  // A label is a field's own only if it names nothing else: not a group, and not a second field.
  const groupLabels = new Set([...form.querySelectorAll(groupSelector)].flatMap(groupLabelElementsOf));
  const labelUses = new Map();
  for (const el of controls) {
    for (const label of labelElementsOf(el)) {
      labelUses.set(label, (labelUses.get(label) || 0) + 1);
    }
  }
  const isOwn = (label) => labelUses.get(label) === 1 && !groupLabels.has(label);

  const wordsOf = (text) => (text || '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();

  // The name a person reads. One that only a script can read counts only if a person reads nothing else there.
  const ownLabelOf = (el) => {
    const own = labelElementsOf(el).filter(isOwn);
    const read = own.filter((l) => seen(l, 1));
    if (read.length > 0) {
      return read.map(textOf).join(' ').trim();
    }
    const named = own.map(textOf).join(' ').trim() || (el.getAttribute('aria-label') || '').trim();
    const isWidget = !['INPUT', 'SELECT', 'TEXTAREA'].includes(el.tagName);
    const name = named || (isWidget && !el.isContentEditable ? (el.textContent || '').trim() : '');
    const beside = wordsOf(seenTextIn(rowOf(el)));
    return name && (isWidget || beside === '' || beside === wordsOf(name)) ? name : '';
  };

  const headings = [...form.querySelectorAll('h1, h2, h3, h4, h5, h6, [role="heading"]')].filter((h) => seen(h, 1));
  const headingOf = (el) => {
    const before = headings.filter((h) => h.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING);
    return before.length > 0 ? textOf(before[before.length - 1]).trim() : '';
  };

  // ---- Required: whatever tells a person so counts. Too strict only means that the candidate is asked. ----

  const asterisk = /[*\uFF0A\u2217\u2731]/;

  // "* indicates a required field" asks nothing; "Please indicate your colour*" does.
  const explainsMarkers = (text) => /\b(required|mandatory)\b/i.test(text) && /\b(indicates|denotes|marked|fields|required field)\b/i.test(text);
  const saysRequired = (text) => asterisk.test(text) || /\brequired\b/i.test(text.replace(/\bnot required\b/gi, ''));
  const drawnBy = (el) => ['::before', '::after'].map((part) => getComputedStyle(el, part).content).join(' ');
  const isDrawn = (el) => asterisk.test(drawnBy(el));
  const marks = (el) => [el, ...el.querySelectorAll('*')].some(isDrawn) || saysRequired(el.textContent || '');

  const isRequired = (el) => {
    const same = controls.filter((c) => isSameQuestion(c, el));
    const row = rowOf(el);
    return (
      [el, ...same].some(
        (e) =>
          e.required === true ||
          e.getAttribute('aria-required') === 'true' ||
          saysRequired(e.getAttribute('aria-label') || '') ||
          [...labelElementsOf(e), ...referenced(e, 'aria-describedby')].some(marks),
      ) ||
      groupElementsOf(el).some(
        (g) =>
          g.getAttribute('aria-required') === 'true' ||
          saysRequired(g.getAttribute('aria-label') || '') ||
          groupLabelElementsOf(g).some(marks),
      ) ||
      (row !== el &&
        ([row, ...row.querySelectorAll('*')].some((e) => isDrawn(e) && seen(e, 1)) ||
          (saysRequired(seenTextIn(row)) && !explainsMarkers(seenTextIn(row)))))
    );
  };

  // ---- What each control holds, to notice a change. Hashed by the caller; never stored or logged. ----

  const stateOf = (el) => {
    const same = controls.filter((c) => isSameQuestion(c, el));
    const isNative = ['INPUT', 'SELECT', 'TEXTAREA'].includes(el.tagName);
    return JSON.stringify([
      (same.length > 0 ? same : [el]).map((c) => [c.value ?? '', c.checked ?? '', c.selectedIndex ?? '', [...(c.files || [])].map((f) => [f.name, f.size])]),
      ['aria-checked', 'aria-selected', 'aria-valuenow', 'aria-valuetext', 'aria-pressed'].map((n) => el.getAttribute(n)),
      isNative && el.getAttribute('role') !== 'combobox' ? '' : el.textContent + '|' + (isNative ? seenTextIn(rowOf(el)) : ''),
    ]).slice(0, 20000);
  };

  // ---- Fields ----

  const fields = [];
  const elements = [];
  const add = (el, kind, text = null) => {
    const groups = groupElementsOf(el).map(groupNameOf).filter(Boolean);
    const shared = labelElementsOf(el).filter((l) => !isOwn(l)).map((l) => textOf(l).trim()).filter(Boolean);
    const heading = headingOf(el);
    const own = text === null ? ownLabelOf(el) : text;

    // A group with a single file input in it is named after what the file is for ("Resume/CV" around "Attach").
    const fileGroup = kind === 'File' ? groupElementsOf(el).find((g) => groupNameOf(g) && g.querySelectorAll('input[type="file"]').length === 1) : null;
    const isNamed = !!own || !!fileGroup;
    elements.push(el);
    fields.push({
      kind,
      label: ((kind === 'Choice' && groups[0]) || own || groups[0] || (seen(rowOf(el), 1) ? seenTextIn(rowOf(el)) : '')).slice(0, 2000),
      isNamed,
      fileGroup: fileGroup ? groupNameOf(fileGroup).slice(0, 2000) : null,
      groups: [...groups, ...shared, ...(heading ? [heading] : [])].slice(0, 20).map((g) => g.slice(0, 2000)),

      // Nobody can tell from the page that a field without a name may be left out.
      required: text !== null || !isNamed || isRequired(el),
      inputType: kind === 'Text' ? (el.type || '').slice(0, 50) : '',
      autocomplete: (el.getAttribute('autocomplete') || '').slice(0, 100),
      maxLength: typeof el.maxLength === 'number' && el.maxLength >= 0 ? el.maxLength : null,
      pattern: (el.getAttribute('pattern') || '').slice(0, 1000),
      accept: (el.getAttribute('accept') || '').slice(0, 500),
      name: `${el.id}|${el.name || ''}`.slice(0, 200),
      state: text === null ? stateOf(el) : '',

      // A disabled option keeps its place so that the index stays right, but can never match.
      options: kind === 'Select' ? [...el.options].slice(0, 101).map((o) => (o.disabled ? '' : o.text.slice(0, 500))) : [],
    });
  };

  // A control that is not seen itself. A person is asked through it if anything about it alone is seen: its own label, text
  // in its row, or the name of its group; if the page itself requires it in a place that is seen; or if a seen control opens
  // the part it is in. Otherwise there is nothing on the page that a person could answer.
  const isOpenedBySomethingSeen = (el) => {
    const details = el.closest('details:not([open])');
    const summary = details?.querySelector(':scope > summary');
    if (summary && seen(summary, 1)) {
      return true;
    }
    for (let p = el.parentElement; p; p = p.parentElement) {
      if (p.id && [...document.querySelectorAll(`[aria-controls~="${CSS.escape(p.id)}"]`)].some(
        (o) => seen(o, 1) && (o.getAttribute('aria-expanded') === 'false' || o.getAttribute('role') === 'tab'),
      )) {
        return true;
      }
    }
    return false;
  };

  const isAskedThrough = (el) => {
    const row = rowOf(el);
    const group = groupElementsOf(el)[0];
    return (
      labelElementsOf(el).some((l) => seen(l, 1)) ||
      (row !== el && seen(row, 1) && seenTextIn(row) !== '') ||
      (!!group && seen(group, 1) && groupNameOf(group) !== '' && controls.filter((c) => group.contains(c) && !isSameQuestion(c, el)).length === 0) ||
      ((el.required === true || el.getAttribute('aria-required') === 'true') &&
        !el.closest('[aria-hidden="true"]') &&
        !!el.parentElement &&
        seen(el.parentElement)) ||
      isOpenedBySomethingSeen(el)
    );
  };

  const recorded = [];
  for (const el of controls) {
    const tag = el.tagName;
    const type = tag === 'INPUT' ? el.type : '';
    const isNative = tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA';
    const isCombobox = tag === 'INPUT' && el.getAttribute('role') === 'combobox';
    const isChoice = !isNative || ['checkbox', 'radio'].includes(type);
    const isSeen = seen(el, isCombobox || isChoice ? 1 : 4);

    // The other buttons of a radio or checkbox group: the group is one question and is already recorded.
    if (recorded.some((r) => isSameQuestion(r, el))) {
      continue;
    }
    if (!isSeen && !isAskedThrough(el)) {
      continue;
    }

    recorded.push(el);
    if (type === 'file') {
      add(el, el.matches(':disabled') || el.closest('[aria-hidden="true"], [inert]') ? 'Choice' : 'File');
    } else if (isChoice || !operable(el, isCombobox ? 1 : 4) || el.readOnly || (!isCombobox && el.tabIndex < 0)) {
      add(el, 'Choice');
    } else if (isCombobox) {
      add(el, 'Combobox');
    } else if (tag === 'SELECT') {
      add(el, el.multiple ? 'Choice' : 'Select');
    } else {
      add(el, tag === 'TEXTAREA' ? 'TextArea' : 'Text');
    }
  }

  // Outside every form: a script may still send it. Optional ones are the page's own search boxes and the like.
  for (const el of everywhere.filter((e) => !controls.includes(e) && !e.form && !e.closest('form'))) {
    if (seen(el, 1) && (el.required === true || el.getAttribute('aria-required') === 'true' || labelElementsOf(el).some(marks))) {
      recorded.push(el);
      add(el, 'Choice', labelElementsOf(el).map(textOf).join(' ').trim() || (el.getAttribute('aria-label') || ''));
    }
  }

  // Documents of their own, which this script cannot look into or does not.
  for (const el of form.querySelectorAll('*')) {
    const embeds = ['IFRAME', 'OBJECT', 'EMBED'].includes(el.tagName) || !!el.shadowRoot?.querySelector(controlSelector);
    if (embeds && seen(el)) {
      add(el, 'Choice', el.getAttribute('title') || el.getAttribute('aria-label') || 'Embedded content');
    }
  }

  // The net under all of this: a required marker that a person sees, and that belongs to no field recorded above.
  const isNameOf = (marker, el) =>
    [...labelElementsOf(el), ...referenced(el, 'aria-describedby'), ...groupElementsOf(el).flatMap(groupLabelElementsOf)].some((l) =>
      l.contains(marker),
    );
  const fieldOf = (r) => fields[elements.indexOf(r)];

  // What a person reads, in order: texts and recorded fields. A text stands for the field it is a label of.
  const reading = [];
  const readings = document.createTreeWalker(form, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT);
  for (let node = readings.nextNode(); node; node = readings.nextNode()) {
    if (node.nodeType === Node.TEXT_NODE) {
      const parent = node.parentElement;
      if (node.data.trim() && !parent.closest('select, textarea, script, style') && seen(parent, 1)) {
        reading.push({ node, field: recorded.find((r) => labelElementsOf(r).some((l) => l.contains(node))) });
      }
    } else if (recorded.includes(node)) {
      reading.push({ node, field: node });
    }
  }
  const before = (el) => reading.findLast((i) => i.node.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING && !el.contains(i.node))?.field;
  const after = (el) => reading.find((i) => el.compareDocumentPosition(i.node) & Node.DOCUMENT_POSITION_FOLLOWING && !el.contains(i.node))?.field;

  const ownText = (el) => [...el.childNodes].filter((n) => n.nodeType === Node.TEXT_NODE).map((n) => n.data).join(' ').trim();
  for (const el of [form, ...form.querySelectorAll('*')]) {
    if (el.closest('select, textarea, script, style, button, a, [contenteditable]')) {
      continue;
    }
    const text = ownText(el);
    const isMarker =
      (text.length < 300 && (asterisk.test(text) || /\(\s*required\s*\)/i.test(text))) || isDrawn(el);
    if (!isMarker || explainsMarkers(textOf(el)) || !seen(el, 1)) {
      continue;
    }
    if (recorded.some((r) => isNameOf(el, r) || (!fieldOf(r).isNamed && rowOf(r) !== r && rowOf(r).contains(el)))) {
      continue;
    }

    // A marker on its own belongs to what it stands next to. One with words is the name of a question, and as no field has
    // that name it is a question of its own.
    const isBare = wordsOf(text).replace(/\brequired\b/g, '').trim() === '' && wordsOf(textOf(el)).replace(/\brequired\b/g, '').trim() === '';
    const neighbours = isBare ? [before(el), after(el)].filter(Boolean) : [];
    for (const r of neighbours) {
      fieldOf(r).required = true;
    }
    if (neighbours.length === 0) {
      add(el, 'Choice', textOf(el).trim() || text);
    }
  }

  // ---- How the step ends. Only what is plain counts; everything else is "Unclear". ----


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
  ].filter((b) => seen(b));
  const buttons = allButtons.filter((b) => form.contains(b) || b.form === form);

  // Looked for on the whole page: a wizard may keep its buttons outside the form.
  const nextLike = allButtons.filter(soundsLikeNext);
  const finals = buttons.filter((b) => isSubmitType(b) && !b.closest('[aria-hidden="true"], [inert]') && namesOf(b).length > 0 && namesOf(b).every((n) => finalNames.includes(n)));
  const submitLike = buttons.filter((b) => isSubmitType(b) || soundsLikeSubmit(b));

  const isPlainNext = (b) =>
    buttons.includes(b) &&
    (b.tagName === 'BUTTON' || b.tagName === 'INPUT') &&
    b.type === 'button' &&
    operable(b) &&
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

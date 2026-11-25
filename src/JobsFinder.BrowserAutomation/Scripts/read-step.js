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
  const isOnPage = (box) =>
    box.right + scrollX > 0 && box.bottom + scrollY > 0 && box.left + scrollX < document.documentElement.scrollWidth;

  // Hidden by an element around it (or itself): transparent, or cut down to nothing, as screen-reader-only classes do.
  const isCutAway = (el) => {
    for (let e = el; e; e = e.parentElement) {
      const style = getComputedStyle(e);
      const area = e.getBoundingClientRect();
      const isClipped = ['absolute', 'fixed'].includes(style.position) && /^rect\((0|1)px,? (0|1)px,? (0|1)px,? (0|1)px\)$/.test(style.clip);
      if (Number(style.opacity) < 0.1 || isClipped || /^inset\((50|100)%/.test(style.clipPath)) {
        return true;
      }
      if (style.display !== 'contents' && style.overflow !== 'visible' && (area.width < 2 || area.height < 2)) {
        return true;
      }
    }
    return false;
  };

  // A text is judged by where it is drawn, not by the box of its parent, which may have none or no size.
  const textSeen = (node) => {
    const parent = node.parentElement;
    if (!node.data.trim() || !parent?.isConnected || getComputedStyle(parent).visibility !== 'visible') {
      return false;
    }
    let box = parent;
    while (box && getComputedStyle(box).display === 'contents') {
      box = box.parentElement;
    }
    if (!box?.checkVisibility()) {
      return false;
    }
    const range = document.createRange();
    range.selectNodeContents(node);
    return [...range.getClientRects()].some((r) => r.width >= 1 && r.height >= 1 && isOnPage(r)) && !isCutAway(parent);
  };

  const seen = (el, minSize = 4) => {
    if (!el.isConnected) {
      return false;
    }
    if (getComputedStyle(el).display === 'contents') {
      return [...el.childNodes].some((n) => (n.nodeType === Node.TEXT_NODE ? textSeen(n) : n.nodeType === Node.ELEMENT_NODE && seen(n, minSize)));
    }

    // Not contentVisibilityAuto: a part that is not rendered yet is seen on scrolling to it.
    if (!el.checkVisibility({ visibilityProperty: true })) {
      return false;
    }
    const box = el.getBoundingClientRect();
    return box.width >= minSize && box.height >= minSize && isOnPage(box) && !isCutAway(el);
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

  // The list that a seen control of this form opens is part of that control, which is recorded itself.
  const isPopup = (el) =>
    el.getAttribute('role') === 'listbox' &&
    !!el.id &&
    [...document.querySelectorAll(`[aria-controls~="${CSS.escape(el.id)}"], [aria-owns~="${CSS.escape(el.id)}"]`)].some(
      (c) => c !== el && c.matches(controlSelector) && isQuestion(c) && (form.contains(c) || c.form === form) && seen(c, 1),
    );

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

  const seenTextIn = (root, except = 'select, textarea, script, style, [contenteditable]') => {
    const texts = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
      if (node.data.trim() && !node.parentElement?.closest(except) && textSeen(node)) {
        texts.push(node.data.trim());
      }
    }
    return texts.join(' ');
  };

  // ---- Labels, and the headings a field is under ----

  // What a person reads there, without the texts of controls in it; what a script reads only if a person reads nothing.
  const readIn = (el) => seenTextIn(el, 'select, textarea, script, style, button, [contenteditable]');
  const nameIn = (el) => readIn(el) || textOf(el).trim();

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
    groupLabelElementsOf(group).map(nameIn).join(' ').trim() || (group.getAttribute('aria-label') || '').trim();

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
    const read = own.map(readIn).filter(Boolean);
    if (read.length > 0) {
      return read.join(' ');
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
    return before.length > 0 ? nameIn(before[before.length - 1]) : '';
  };

  // ---- Required: whatever tells a person so counts. Too strict only means that the candidate is asked. ----

  const asterisk = /[*\uFF0A\u2217\u2731]/;

  // Speaks of fields in general: "* indicates a required field", "Fields marked with * are required". Not "Required field",
  // which speaks of the one it stands at.
  const explainsMarkers = (text) =>
    /\b(required|mandatory)\b/i.test(text) && /\b(indicates|denotes|marked|(required|mandatory) (fields|questions)|fields\b.*\b(are|is) (required|mandatory))\b/i.test(text);
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
    const shared = labelElementsOf(el).filter((l) => !isOwn(l)).map(nameIn).filter(Boolean);
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

  // ---- Buttons ----

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

  const isSubmitType = (b) => (b.tagName === 'BUTTON' || b.tagName === 'INPUT') && (b.type === 'submit' || b.type === 'image');
  const soundsLikeSubmit = (b) => namesOf(b).some((n) => /\b(submit|apply|send|finish|complete)\b/.test(n));
  const soundsLikeNext = (b) => namesOf(b).some((n) => /\b(next|continue|proceed)\b/.test(n));

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
      if (node.data.trim() && !parent.closest('select, textarea, script, style') && textSeen(node)) {
        reading.push({ node, field: recorded.find((r) => labelElementsOf(r).some((l) => l.contains(node))) });
      }
    } else if (recorded.includes(node)) {
      reading.push({ node, field: node });
    }
  }
  // A name written beside a field instead of in a label ("GitHub *" over an input that only a script calls GitHub).
  const nodeNames = (node, r) =>
    fieldOf(r).isNamed && wordsOf(node.data) === wordsOf(fieldOf(r).label) && (r.contains(node) || (rowOf(r) !== r && rowOf(r).contains(node)));
  const named = (item) => item.field || (item.node.nodeType === Node.TEXT_NODE ? recorded.find((r) => nodeNames(item.node, r)) : undefined);

  const isNear = (el, r) => rowOf(r).contains(el) || el.parentElement.contains(r);
  const follows = (el, node) => !!(el.compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING);

  // What a marker without words belongs to: what is right before it, or, at the start of something, right after it. A text
  // before it that names no field is a question that was not recorded; "unknown" then, whatever comes next.
  const ownersOf = (el) => {
    const prev = reading.findLast((i) => follows(i.node, el) && !el.contains(i.node));
    const next = reading.find((i) => follows(el, i.node) && !el.contains(i.node));
    const nextOwner = next && named(next) && isNear(el, named(next)) ? [named(next)] : [];
    if (!prev) {
      return nextOwner;
    }
    if (prev.node.nodeType === Node.TEXT_NODE) {
      return named(prev) ? [named(prev)] : [];
    }
    return [...(isNear(el, prev.field) ? [prev.field] : []), ...nextOwner];
  };

  // "* Required" before the first field, under nothing but a heading, is a legend.
  const isLegend = (el, text) => {
    const prev = reading.findLast((i) => follows(i.node, el) && !el.contains(i.node));
    return (
      /\b(required|mandatory)\b/i.test(text) &&
      !recorded.some((r) => follows(r, el)) &&
      (!prev || !!prev.node.parentElement?.closest('h1, h2, h3, h4, h5, h6, [role="heading"]'))
    );
  };

  // Only these buttons ask nothing: sending, going on, and choosing a file for a file field, which is recorded itself.
  const isOtherButton = (b) =>
    isSubmitType(b) ||
    soundsLikeSubmit(b) ||
    soundsLikeNext(b) ||
    recorded.some((r) => r.type === 'file' && (rowOf(r).contains(b) || groupElementsOf(r)[0]?.contains(b)));

  const withoutMarkers = (text) => wordsOf(text).replace(/\b(required|mandatory|field|this|is)\b/g, ' ').trim();
  const ownTexts = (el) => [...el.childNodes].filter((n) => n.nodeType === Node.TEXT_NODE && n.data.trim());
  for (const el of [form, ...form.querySelectorAll('*')]) {
    // Texts of controls, which are recorded themselves, and of links that lead elsewhere.
    if (el.closest('select, textarea, script, style, [contenteditable], a[href]:not([role="button"], [href=""], [href="#"], [href^="javascript:"])')) {
      continue;
    }
    const button = el.closest('button, [role="button"]');
    if (button && isOtherButton(button)) {
      continue;
    }

    const text = ownTexts(el).map((n) => n.data.trim()).join(' ');
    const isWords = withoutMarkers(text) === '' && /\b(required|mandatory)\b/i.test(text);
    const isMarked = asterisk.test(text) || /\(\s*(required|mandatory)\s*\)/i.test(text);
    const isMarkedAtEnd = /([*\uFF0A\u2217\u2731]|\(\s*(required|mandatory)\s*\))\s*$/i.test(text);
    const isSeenMarker = ((text.length < 300 ? isWords || isMarked : isMarkedAtEnd) && ownTexts(el).some(textSeen)) || (isDrawn(el) && seen(el, 1));
    const whole = button === el ? wordsOf(el.textContent) : wordsOf(textOf(el));
    if (!isSeenMarker || explainsMarkers(textOf(el))) {
      continue;
    }
    if (recorded.some((r) => isNameOf(el, r) || (!fieldOf(r).isNamed && rowOf(r) !== r && rowOf(r).contains(el)))) {
      continue;
    }

    // With words it is the name of a question: of the field called that, or else of one that was not recorded.
    const words = withoutMarkers(whole) || withoutMarkers(text);
    if (words !== '') {
      const r = recorded.find((f) => fieldOf(f).isNamed && withoutMarkers(fieldOf(f).label) === words && (f.contains(el) || (rowOf(f) !== f && rowOf(f).contains(el))));
      if (r) {
        fieldOf(r).required = true;
      } else {
        add(el, 'Choice', (button === el ? (el.textContent || '').trim() : textOf(el).trim()) || text);
      }
      continue;
    }

    const owners = ownersOf(el);
    for (const r of owners) {
      fieldOf(r).required = true;
    }
    if (owners.length === 0 && !isLegend(el, el.parentElement ? text + ' ' + textOf(el.parentElement) : text)) {
      const prev = reading.findLast((i) => follows(i.node, el) && !el.contains(i.node));
      add(el, 'Choice', `${prev?.node.data?.trim() ?? ''} ${textOf(el).trim() || text}`.trim());
    }
  }

  // ---- How the step ends. Only what is plain counts; everything else is "Unclear". ----

  const finalNames = [
    'submit', 'submit application', 'submit your application', 'submit my application', 'apply', 'apply now', 'apply for this job',
    'send', 'send application', 'send my application',
  ];
  const nextNames = ['next', 'next step', 'continue'];
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

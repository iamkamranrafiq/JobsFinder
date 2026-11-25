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
    return { meta: { found: false, hasSubmit: false, hasNext: false, fields: [] }, elements: [], next: null };
  }

  // Invisible fields are either not for people (honeypots) or belong to another step.
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

  const byIds = (value) =>
    (value || '')
      .split(/\s+/)
      .filter(Boolean)
      .map((id) => document.getElementById(id))
      .filter(Boolean)
      .map(textOf)
      .join(' ')
      .trim();

  const labelOf = (el) =>
    byIds(el.getAttribute('aria-labelledby')) ||
    [...(el.labels || [])].map(textOf).join(' ').trim() ||
    el.getAttribute('aria-label') ||
    '';

  const groupElementOf = (el) => el.parentElement?.closest('fieldset, [role="group"], [role="radiogroup"]') || null;

  const groupOf = (el) => {
    const group = groupElementOf(el);
    if (!group || !form.contains(group)) {
      return '';
    }
    const legend = group.querySelector(':scope > legend');
    return (legend && textOf(legend)) || byIds(group.getAttribute('aria-labelledby')) || group.getAttribute('aria-label') || '';
  };

  const marked = (text) => text.includes('*');
  const isRequired = (el, ...texts) =>
    el.required === true || el.getAttribute('aria-required') === 'true' || texts.some(marked);

  const fields = [];
  const elements = [];
  const choices = new Set();
  const add = (el, kind, label, group, required) => {
    elements.push(el);
    fields.push({
      kind,
      label: label.slice(0, 2000),
      group: group.slice(0, 2000),
      required,
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

  const controls = form.querySelectorAll(
    'input, select, textarea, [contenteditable="true"], [role="combobox"]:not(input), [role="checkbox"]:not(input), ' +
      '[role="radio"]:not(input), [role="switch"]:not(input)',
  );
  for (const el of controls) {
    const tag = el.tagName;
    const type = tag === 'INPUT' ? el.type : '';
    if (el.disabled || ['hidden', 'submit', 'button', 'reset', 'image'].includes(type)) {
      continue;
    }

    const label = labelOf(el);
    const group = groupOf(el);

    if (type === 'file') {
      // Often hidden behind a button; what counts is whether a person can see what it is for.
      const owner = groupElementOf(el);
      const visible = shown(el) || [...(el.labels || [])].some((l) => shown(l)) || (owner && form.contains(owner) && shown(owner));
      if (visible) {
        add(el, 'File', group || label, '', isRequired(el, label, group) || owner?.getAttribute('aria-required') === 'true');
      }
      continue;
    }

    if (['checkbox', 'radio'].includes(type) || (tag !== 'INPUT' && tag !== 'SELECT' && tag !== 'TEXTAREA')) {
      const key = `${type}|${el.name || ''}`;
      const grouped = type !== '' && el.name;
      if (!shown(el, 1) || (grouped && choices.has(key))) {
        continue;
      }
      if (grouped) {
        choices.add(key);
      }
      add(el, 'Choice', group || label, group, isRequired(el, label, group));
      continue;
    }

    if (tag === 'INPUT' && el.getAttribute('role') === 'combobox') {
      if (shown(el, 1) && !el.readOnly) {
        add(el, 'Combobox', label, group, isRequired(el, label));
      }
      continue;
    }

    if (!shown(el) || el.readOnly || el.tabIndex < 0) {
      continue;
    }

    const kind = tag === 'SELECT' ? (el.multiple ? 'Choice' : 'Select') : tag === 'TEXTAREA' ? 'TextArea' : 'Text';
    add(el, kind, label, group, isRequired(el, label));
  }

  const nameOf = (el) => (el.getAttribute('aria-label') || el.value || el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
  const buttons = [...form.querySelectorAll('button, input[type="submit"], input[type="image"], input[type="button"], [role="button"], a')].filter(
    (b) => shown(b),
  );
  const hasSubmit = buttons.some(
    (b) => b.type === 'submit' || b.type === 'image' || /\b(submit|apply|send|finish)\b/.test(nameOf(b)),
  );
  const nextControls = buttons.filter(
    (b) =>
      (b.tagName === 'BUTTON' || b.tagName === 'INPUT') &&
      b.type === 'button' &&
      !b.disabled &&
      b.getAttribute('aria-disabled') !== 'true' &&
      ['next', 'next step', 'continue'].includes(nameOf(b)),
  );
  const next = !hasSubmit && nextControls.length === 1 ? nextControls[0] : null;

  return { meta: { found: true, hasSubmit, hasNext: next !== null, fields }, elements, next };
}

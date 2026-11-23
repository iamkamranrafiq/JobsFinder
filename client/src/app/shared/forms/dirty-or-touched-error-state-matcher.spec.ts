import { FormControl, Validators } from '@angular/forms';

import { DirtyOrTouchedErrorStateMatcher } from './dirty-or-touched-error-state-matcher';

describe('DirtyOrTouchedErrorStateMatcher', () => {
  const matcher = new DirtyOrTouchedErrorStateMatcher();

  it('D4-AC4 shows no error for a pristine, untouched invalid control', () => {
    expect(matcher.isErrorState(new FormControl('', Validators.required))).toBe(false);
  });

  it('D4-AC4 shows an error for an invalid control once dirty or touched', () => {
    const dirty = new FormControl('', Validators.required);
    dirty.markAsDirty();
    const touched = new FormControl('', Validators.required);
    touched.markAsTouched();

    expect(matcher.isErrorState(dirty)).toBe(true);
    expect(matcher.isErrorState(touched)).toBe(true);
  });

  it('D4-AC4 shows no error for a valid control or no control', () => {
    const valid = new FormControl('x', Validators.required);
    valid.markAsTouched();

    expect(matcher.isErrorState(valid)).toBe(false);
    expect(matcher.isErrorState(null)).toBe(false);
  });
});

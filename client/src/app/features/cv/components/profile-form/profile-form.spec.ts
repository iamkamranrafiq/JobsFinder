import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CandidateProfileData } from '../../data/cv.model';
import { ProfileForm } from './profile-form';

const full: CandidateProfileData = {
  identity: { fullName: 'Ada Lovelace', headline: 'Senior .NET Engineer' },
  contact: { email: 'ada@example.com', phone: '+44 20 7946 0000', location: 'London, UK' },
  summary: 'Backend engineer with 10 years of .NET.',
  skills: [
    { name: 'C#', proficiency: 'Expert', years: 10 },
    { name: 'Angular', proficiency: null, years: 2.5 },
  ],
  experience: [
    {
      role: 'Senior Engineer',
      company: 'Analytical Engines Ltd',
      startDate: '2019-03',
      endDate: null,
      isCurrent: true,
      achievements: ['Cut build times by 40%', 'Led the API team'],
    },
  ],
  education: [{ institution: 'University of London', qualification: 'BSc', field: 'Mathematics', startDate: '2010', endDate: '2013' }],
  certifications: [{ name: 'AZ-204', issuer: 'Microsoft', year: 2022 }],
  links: [{ label: 'GitHub', url: 'https://github.com/ada' }],
  workAuthorisation: 'UK citizen',
  currentEmployer: 'Analytical Engines Ltd',
  preferences: {
    targetRoles: ['Staff Engineer', 'Tech Lead'],
    locations: ['London'],
    remote: 'Hybrid',
    salaryMin: 90000,
    salaryMax: 120000,
    salaryCurrency: 'GBP',
    noticePeriod: '3 months',
  },
};

interface FormInternals {
  toData(): CandidateProfileData;
}

async function render(profile: CandidateProfileData) {
  await TestBed.configureTestingModule({ imports: [ProfileForm] }).compileComponents();
  const fixture: ComponentFixture<ProfileForm> = TestBed.createComponent(ProfileForm);
  fixture.componentRef.setInput('profile', profile);
  fixture.detectChanges();
  const emitted: CandidateProfileData[] = [];
  fixture.componentInstance.save.subscribe((data) => emitted.push(data));
  const el = fixture.nativeElement as HTMLElement;
  return { fixture, el, emitted };
}

function input(el: HTMLElement, testIdOrLabel: string): HTMLInputElement {
  const byTestId = el.querySelector<HTMLInputElement>(`[data-testid="${testIdOrLabel}"]`);
  if (byTestId) {
    return byTestId;
  }
  const label = [...el.querySelectorAll('label')].find((l) => l.textContent?.trim().startsWith(testIdOrLabel));
  const control = label?.querySelector<HTMLInputElement>('input, textarea, select');
  if (!control) {
    throw new Error(`No control for ${testIdOrLabel}`);
  }
  return control;
}

function type(control: HTMLInputElement, value: string) {
  control.value = value;
  control.dispatchEvent(new Event('input'));
}

function submit(fixture: ComponentFixture<ProfileForm>, el: HTMLElement) {
  el.querySelector('form')!.dispatchEvent(new Event('submit'));
  fixture.detectChanges();
}

describe('ProfileForm', () => {
  it('F0.1-AC5 prefills the current employer from the parse', async () => {
    const { el } = await render(full);

    expect(input(el, 'current-employer-input').value).toBe('Analytical Engines Ltd');
    expect(el.querySelector('[data-testid="current-employer-hint"]')?.textContent).toContain('Taken from your CV');
  });

  it('F0.1-AC5 shows an empty employer field with a completion hint', async () => {
    const { el } = await render({ ...full, currentEmployer: null });

    expect(input(el, 'current-employer-input').value).toBe('');
    expect(el.querySelector('[data-testid="current-employer-hint"]')?.textContent).toContain(
      'Not found in your CV. Add it if you are currently employed.',
    );
  });

  it('F0.1-AC5 emits the employer the candidate typed', async () => {
    const { fixture, el, emitted } = await render({ ...full, currentEmployer: null });

    type(input(el, 'current-employer-input'), '  Difference Engines plc ');
    submit(fixture, el);

    expect(emitted).toHaveLength(1);
    expect(emitted[0].currentEmployer).toBe('Difference Engines plc');
  });

  it('F0.1-BR2 an untouched form maps back to exactly the input profile', async () => {
    const { fixture } = await render(full);

    expect((fixture.componentInstance as unknown as FormInternals).toData()).toEqual(full);
  });

  it('F0.1-AC4 emits the edited field and keeps the rest of the profile', async () => {
    const { fixture, el, emitted } = await render(full);

    type(input(el, 'Headline'), 'Staff Engineer');
    submit(fixture, el);

    expect(emitted).toEqual([{ ...full, identity: { ...full.identity, headline: 'Staff Engineer' } }]);
  });

  it('F0.1-BR2 blank text becomes null and blank list lines are dropped', async () => {
    const { fixture, el, emitted } = await render(full);

    type(input(el, 'Headline'), '   ');
    type(input(el, 'Target roles'), 'Staff Engineer\n\n  \n Tech Lead ');
    submit(fixture, el);

    expect(emitted[0].identity.headline).toBeNull();
    expect(emitted[0].preferences.targetRoles).toEqual(['Staff Engineer', 'Tech Lead']);
  });

  it('F0.1-BR2 blocks save on an invalid date or URL', async () => {
    const { fixture, el, emitted } = await render(full);

    type(input(el, 'Start (YYYY or YYYY-MM)'), 'March 2019');
    type(input(el, 'Link 1 URL'), 'javascript:alert(1)');
    submit(fixture, el);

    expect(emitted).toHaveLength(0);
    expect(el.querySelector('[data-testid="profile-form-invalid"]')).not.toBeNull();
  });

  it('F0.1-BR2 adds and removes list rows', async () => {
    const { fixture, el, emitted } = await render(full);

    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Remove skill 2')!.click();
    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Add link')!.click();
    fixture.detectChanges();
    type(input(el, 'Link 2 URL'), 'https://ada.dev');
    submit(fixture, el);

    expect(emitted[0].skills.map((s) => s.name)).toEqual(['C#']);
    expect(emitted[0].links).toEqual([...full.links, { label: null, url: 'https://ada.dev' }]);
  });

  it('F0.1-BR3 does not emit when nothing was changed', async () => {
    const { fixture, el, emitted } = await render(full);

    submit(fixture, el);

    expect(emitted).toHaveLength(0);
  });
});

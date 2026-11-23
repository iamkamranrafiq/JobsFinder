import { ChangeDetectionStrategy, Component, OnInit, inject, input, output } from '@angular/core';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';

import {
  CandidateProfileData,
  PROFILE_LONG_TEXT_MAX,
  PROFILE_SHORT_TEXT_MAX,
  PROFILE_URL_MAX,
  ProfileCertification,
  ProfileEducation,
  ProfileExperience,
  ProfileLink,
  ProfileSkill,
  RemotePreference,
  SkillProficiency,
} from '../../data/cv.model';

// Client-side mirrors of CandidateProfileDataValidator (F0.1-BR2). The API stays authoritative.
const DATE_PATTERN = /^\d{4}(-(0[1-9]|1[0-2]))?$/;
const NUMBER_PATTERN = /^\d+(\.\d+)?$/;
const YEAR_PATTERN = /^\d{4}$/;
const short = Validators.maxLength(PROFILE_SHORT_TEXT_MAX);

const httpUrl: ValidatorFn = (control) => {
  const value = (control.value as string).trim();
  if (!value) {
    return null;
  }
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? null : { httpUrl: true };
  } catch {
    return { httpUrl: true };
  }
};

const salaryRange: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const min = toNumber(group.get('salaryMin')?.value as string);
  const max = toNumber(group.get('salaryMax')?.value as string);
  return min !== null && max !== null && max < min ? { salaryRange: true } : null;
};

export const SKILL_PROFICIENCIES: readonly SkillProficiency[] = ['Beginner', 'Intermediate', 'Advanced', 'Expert'];
export const REMOTE_PREFERENCES: readonly RemotePreference[] = ['Remote', 'Hybrid', 'OnSite', 'Any'];

/**
 * Editable form for every profile field (F0.1-AC4, BR2), including the current employer (F0.1-AC5).
 * Presentational: it emits the corrected profile and the store saves it as a new version.
 */
@Component({
  selector: 'app-profile-form',
  imports: [ReactiveFormsModule],
  templateUrl: './profile-form.html',
  styleUrl: './profile-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileForm implements OnInit {
  private readonly fb = inject(NonNullableFormBuilder);

  readonly profile = input.required<CandidateProfileData>();
  readonly saving = input(false);
  readonly save = output<CandidateProfileData>();
  readonly cancelled = output<void>();

  protected readonly proficiencies = SKILL_PROFICIENCIES;
  protected readonly remotePreferences = REMOTE_PREFERENCES;
  protected readonly longMax = PROFILE_LONG_TEXT_MAX;

  protected form!: ReturnType<ProfileForm['buildForm']>;

  ngOnInit(): void {
    this.form = this.buildForm(this.profile());
  }

  protected get skills() {
    return this.form.controls.skills;
  }

  protected get experience() {
    return this.form.controls.experience;
  }

  protected get education() {
    return this.form.controls.education;
  }

  protected get certifications() {
    return this.form.controls.certifications;
  }

  protected get links() {
    return this.form.controls.links;
  }

  protected addSkill(): void {
    this.add(this.skills, this.skillGroup({ name: '', proficiency: null, years: null }));
  }

  protected addExperience(): void {
    this.add(
      this.experience,
      this.experienceGroup({ role: '', company: '', startDate: null, endDate: null, isCurrent: false, achievements: [] }),
    );
  }

  protected addEducation(): void {
    this.add(
      this.education,
      this.educationGroup({ institution: '', qualification: null, field: null, startDate: null, endDate: null }),
    );
  }

  protected addCertification(): void {
    this.add(this.certifications, this.certificationGroup({ name: '', issuer: null, year: null }));
  }

  protected addLink(): void {
    this.add(this.links, this.linkGroup({ label: null, url: '' }));
  }

  protected remove(array: FormArray<FormGroup>, index: number): void {
    array.removeAt(index);
    this.form.markAsDirty();
  }

  protected invalid(control: AbstractControl | null): boolean {
    return !!control && control.invalid && (control.touched || control.dirty);
  }

  protected onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.form.pristine || this.saving()) {
      return;
    }
    this.save.emit(this.toData());
  }

  /** Form values back to the API shape: trimmed, blank text as null, blank list lines dropped. */
  protected toData(): CandidateProfileData {
    const v = this.form.getRawValue();
    return {
      identity: { fullName: text(v.fullName), headline: text(v.headline) },
      contact: { email: text(v.email), phone: text(v.phone), location: text(v.location) },
      summary: text(v.summary),
      skills: v.skills.map(
        (s): ProfileSkill => ({ name: s.name.trim(), proficiency: s.proficiency || null, years: toNumber(s.years) }),
      ),
      experience: v.experience.map(
        (e): ProfileExperience => ({
          role: e.role.trim(),
          company: e.company.trim(),
          startDate: text(e.startDate),
          endDate: text(e.endDate),
          isCurrent: e.isCurrent,
          achievements: lines(e.achievements),
        }),
      ),
      education: v.education.map(
        (e): ProfileEducation => ({
          institution: e.institution.trim(),
          qualification: text(e.qualification),
          field: text(e.field),
          startDate: text(e.startDate),
          endDate: text(e.endDate),
        }),
      ),
      certifications: v.certifications.map(
        (c): ProfileCertification => ({ name: c.name.trim(), issuer: text(c.issuer), year: toNumber(c.year) }),
      ),
      links: v.links.map((l): ProfileLink => ({ label: text(l.label), url: l.url.trim() })),
      workAuthorisation: text(v.workAuthorisation),
      currentEmployer: text(v.currentEmployer),
      preferences: {
        targetRoles: lines(v.preferences.targetRoles),
        locations: lines(v.preferences.locations),
        remote: v.preferences.remote || null,
        salaryMin: toNumber(v.preferences.salaryMin),
        salaryMax: toNumber(v.preferences.salaryMax),
        salaryCurrency: text(v.preferences.salaryCurrency),
        noticePeriod: text(v.preferences.noticePeriod),
      },
    };
  }

  private add(array: FormArray<FormGroup>, group: FormGroup): void {
    array.push(group);
    this.form.markAsDirty();
  }

  private buildForm(p: CandidateProfileData) {
    return this.fb.group({
      fullName: [p.identity.fullName ?? '', short],
      headline: [p.identity.headline ?? '', short],
      email: [p.contact.email ?? '', short],
      phone: [p.contact.phone ?? '', short],
      location: [p.contact.location ?? '', short],
      summary: [p.summary ?? '', Validators.maxLength(PROFILE_LONG_TEXT_MAX)],
      currentEmployer: [p.currentEmployer ?? '', short],
      workAuthorisation: [p.workAuthorisation ?? '', short],
      skills: this.fb.array(p.skills.map((s) => this.skillGroup(s))),
      experience: this.fb.array(p.experience.map((e) => this.experienceGroup(e))),
      education: this.fb.array(p.education.map((e) => this.educationGroup(e))),
      certifications: this.fb.array(p.certifications.map((c) => this.certificationGroup(c))),
      links: this.fb.array(p.links.map((l) => this.linkGroup(l))),
      preferences: this.fb.group(
        {
          targetRoles: [p.preferences.targetRoles.join('\n')],
          locations: [p.preferences.locations.join('\n')],
          remote: new FormControl<RemotePreference | ''>(p.preferences.remote ?? '', { nonNullable: true }),
          salaryMin: [numberText(p.preferences.salaryMin), Validators.pattern(NUMBER_PATTERN)],
          salaryMax: [numberText(p.preferences.salaryMax), Validators.pattern(NUMBER_PATTERN)],
          salaryCurrency: [p.preferences.salaryCurrency ?? '', Validators.maxLength(10)],
          noticePeriod: [p.preferences.noticePeriod ?? '', short],
        },
        { validators: salaryRange },
      ),
    });
  }

  private skillGroup(s: ProfileSkill) {
    return this.fb.group({
      name: [s.name, [Validators.required, short]],
      proficiency: new FormControl<SkillProficiency | ''>(s.proficiency ?? '', { nonNullable: true }),
      years: [numberText(s.years), [Validators.pattern(NUMBER_PATTERN), Validators.max(70)]],
    });
  }

  private experienceGroup(e: ProfileExperience) {
    return this.fb.group({
      role: [e.role, [Validators.required, short]],
      company: [e.company, [Validators.required, short]],
      startDate: [e.startDate ?? '', Validators.pattern(DATE_PATTERN)],
      endDate: [e.endDate ?? '', Validators.pattern(DATE_PATTERN)],
      isCurrent: [e.isCurrent],
      achievements: [e.achievements.join('\n')],
    });
  }

  private educationGroup(e: ProfileEducation) {
    return this.fb.group({
      institution: [e.institution, [Validators.required, short]],
      qualification: [e.qualification ?? '', short],
      field: [e.field ?? '', short],
      startDate: [e.startDate ?? '', Validators.pattern(DATE_PATTERN)],
      endDate: [e.endDate ?? '', Validators.pattern(DATE_PATTERN)],
    });
  }

  private certificationGroup(c: ProfileCertification) {
    return this.fb.group({
      name: [c.name, [Validators.required, short]],
      issuer: [c.issuer ?? '', short],
      year: [numberText(c.year), [Validators.pattern(YEAR_PATTERN), Validators.min(1900), Validators.max(2100)]],
    });
  }

  private linkGroup(l: ProfileLink) {
    return this.fb.group({
      label: [l.label ?? '', short],
      url: [l.url, [Validators.required, Validators.maxLength(PROFILE_URL_MAX), httpUrl]],
    });
  }
}

function text(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function lines(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line);
}

function toNumber(value: string): number | null {
  const trimmed = value.trim();
  return trimmed ? Number(trimmed) : null;
}

function numberText(value: number | null): string {
  return value === null ? '' : String(value);
}

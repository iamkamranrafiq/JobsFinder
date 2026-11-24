import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatListModule } from '@angular/material/list';

import { CandidateProfileData } from '../../data/cv.model';

/** Read-only display of a parsed profile (F0.1-BR2). Editing arrives with F0.1-AC4. */
@Component({
  selector: 'app-profile-view',
  imports: [MatChipsModule, MatListModule],
  templateUrl: './profile-view.html',
  styleUrl: './profile-view.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileView {
  readonly profile = input.required<CandidateProfileData>();

  protected period(start: string | null, end: string | null, isCurrent: boolean): string {
    const to = isCurrent ? 'present' : (end ?? '');
    return [start ?? '', to].filter((p) => p).join(' – ');
  }

  protected salary(): string | null {
    const { salaryMin, salaryMax, salaryCurrency } = this.profile().preferences;
    if (salaryMin === null && salaryMax === null) {
      return null;
    }
    const range = [salaryMin, salaryMax].filter((v) => v !== null).join(' – ');
    return salaryCurrency ? `${range} ${salaryCurrency}` : range;
  }
}

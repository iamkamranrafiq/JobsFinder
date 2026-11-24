import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { FetchFailureReason } from '../../data/source-status.model';

/** Tells the user that the last job fetch failed (F0.2-AC4). The page decides when to show it. */
@Component({
  selector: 'app-source-status-banner',
  templateUrl: './source-status-banner.html',
  styleUrl: './source-status-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SourceStatusBanner {
  readonly reason = input<FetchFailureReason | null>(null);

  protected readonly cause = computed(() => {
    switch (this.reason()) {
      case 'NotConfigured':
        return 'The job source is not set up.';
      case 'Interrupted':
        return 'The last job fetch was interrupted.';
      default:
        return 'The last job fetch failed.';
    }
  });
}

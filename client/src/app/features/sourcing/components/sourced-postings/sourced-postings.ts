import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { ApplyPath } from '../../data/posting.model';
import { SourcingStore } from '../../state/sourcing.store';
import { SourceStatusBanner } from '../source-status-banner/source-status-banner';

/**
 * "Fetch jobs" and the sourced postings (F0.2-AC1). Self-contained, so another page can host it: provide
 * SourcingStore on that page's route.
 */
@Component({
  selector: 'app-sourced-postings',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatListModule,
    MatProgressBarModule,
    SourceStatusBanner,
  ],
  templateUrl: './sourced-postings.html',
  styleUrl: './sourced-postings.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SourcedPostings implements OnInit, OnDestroy {
  protected readonly store = inject(SourcingStore);

  protected readonly applyPathLabels: Record<ApplyPath, string> = {
    AtsUrl: 'ATS link',
    Email: 'Email',
    NativeApply: 'Native apply',
    Unknown: 'Unknown',
  };

  ngOnInit(): void {
    this.store.enter();
  }

  ngOnDestroy(): void {
    this.store.leave();
  }
}

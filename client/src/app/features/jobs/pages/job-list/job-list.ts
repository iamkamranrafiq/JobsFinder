import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';

import { RemotePolicy } from '../../data/job.model';
import { JobsStore } from '../../state/jobs.store';

@Component({
  selector: 'app-job-list',
  imports: [DatePipe],
  templateUrl: './job-list.html',
  styleUrl: './job-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobList implements OnInit {
  protected readonly store = inject(JobsStore);
  protected readonly policies: RemotePolicy[] = ['Remote', 'Hybrid', 'OnSite'];

  ngOnInit(): void {
    this.store.load({});
  }

  protected onSearch(text: string): void {
    this.store.load({ text, page: 1 });
  }

  protected onPolicy(value: string): void {
    this.store.load({ remotePolicy: (value || null) as RemotePolicy | null, page: 1 });
  }

  protected goTo(page: number): void {
    this.store.load({ page });
  }
}

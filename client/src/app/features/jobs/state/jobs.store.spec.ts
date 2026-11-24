import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { Job, JobSearch } from '../data/job.model';
import { JobsApi } from '../data/jobs.api';
import { JobsStore } from './jobs.store';

const job: Job = {
  id: '1',
  title: 'Angular Developer',
  company: 'Acme',
  location: null,
  remotePolicy: 'Remote',
  url: 'https://example.com/1',
  postedAt: '2026-11-01T00:00:00Z',
};

function setup(search: (s: JobSearch) => ReturnType<JobsApi['search']>) {
  TestBed.configureTestingModule({
    providers: [JobsStore, { provide: JobsApi, useValue: { search } }],
  });
  return TestBed.inject(JobsStore);
}

describe('JobsStore', () => {
  it('loads jobs and merges the search change', () => {
    const calls: JobSearch[] = [];
    const store = setup((s) => {
      calls.push(s);
      return of({ items: [job], page: 1, pageSize: 20, totalCount: 41 });
    });

    store.load({ text: 'angular' });

    expect(calls[0].text).toBe('angular');
    expect(store.jobs()).toEqual([job]);
    expect(store.totalPages()).toBe(3);
    expect(store.loading()).toBe(false);
  });

  it('shows the problem-details title on failure', () => {
    const store = setup(() =>
      throwError(() => new HttpErrorResponse({ status: 400, error: { title: 'Bad search' } })),
    );

    store.load({});

    expect(store.error()).toBe('Bad search');
    expect(store.jobs()).toEqual([]);
    expect(store.loading()).toBe(false);
  });
});

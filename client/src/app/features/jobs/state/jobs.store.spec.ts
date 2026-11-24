import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { Job, JobSearch } from '../data/job.model';
import { JobsApi } from '../data/jobs.api';
import { SourceStatus } from '../data/source-status.model';
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

  describe('source status', () => {
    function setupStatus(sourceStatus: () => Observable<SourceStatus>) {
      TestBed.configureTestingModule({
        providers: [JobsStore, { provide: JobsApi, useValue: { sourceStatus } }],
      });
      return TestBed.inject(JobsStore);
    }

    it('F0.2-AC4 Unavailable marks the source unavailable', () => {
      const store = setupStatus(() => of({ state: 'Unavailable', lastRun: null }));

      store.loadSourceStatus();

      expect(store.sourceUnavailable()).toBe(true);
      expect(store.error()).toBeNull();
    });

    for (const state of ['Available', 'NeverRun'] as const) {
      it(`F0.2-AC4 ${state} does not mark the source unavailable`, () => {
        const store = setupStatus(() => of({ state, lastRun: null }));

        store.loadSourceStatus();

        expect(store.sourceUnavailable()).toBe(false);
      });
    }

    it('F0.2-AC4 a failed status call shows nothing and a later success still works', () => {
      const answers: Observable<SourceStatus>[] = [
        throwError(() => new HttpErrorResponse({ status: 500 })),
        of({ state: 'Unavailable', lastRun: null }),
      ];
      const store = setupStatus(() => answers.shift()!);

      store.loadSourceStatus();
      expect(store.sourceUnavailable()).toBe(false);
      expect(store.error()).toBeNull();

      store.loadSourceStatus();
      expect(store.sourceUnavailable()).toBe(true);
    });
  });
});

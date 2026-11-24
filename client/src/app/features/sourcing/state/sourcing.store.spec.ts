import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { NEVER, Observable, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { PagedResult } from '../../../core/http/paged-result';
import { JobPosting } from '../data/posting.model';
import { FetchRun, SourceStatus } from '../data/source-status.model';
import { SourcingApi } from '../data/sourcing.api';
import { MAX_POLL_FAILURES, POLL_INTERVAL_MS, SourcingStore } from './sourcing.store';

const posting: JobPosting = {
  id: 'p1',
  source: 'apify',
  company: 'Acme',
  title: 'Engineer',
  location: null,
  isRemote: false,
  applyUrl: null,
  applyPath: 'Unknown',
  postedAt: null,
  createdAt: '2026-11-24T10:00:00Z',
};

function run(id: string, change: Partial<FetchRun> = {}): FetchRun {
  return {
    id,
    source: 'apify',
    status: 'Succeeded',
    failureReason: null,
    startedAt: '2026-11-24T10:00:00Z',
    completedAt: '2026-11-24T10:00:05Z',
    fetchedCount: 3,
    insertedCount: 1,
    duplicateCount: 2,
    malformedCount: 0,
    ...change,
  };
}

const neverRun: SourceStatus = { state: 'NeverRun', lastRun: null, currentFetchId: null };
const running: SourceStatus = { state: 'NeverRun', lastRun: null, currentFetchId: 'f1' };
const succeeded: SourceStatus = { state: 'Available', lastRun: run('f1'), currentFetchId: null };

function failed(failureReason: FetchRun['failureReason'], id = 'f1'): SourceStatus {
  return {
    state: 'Unavailable',
    lastRun: run(id, { status: 'Failed', failureReason }),
    currentFetchId: null,
  };
}

const serverError = () => throwError(() => new HttpErrorResponse({ status: 500 }));

/** Status answers are used in order; the last one repeats. */
function setup(options: {
  statuses?: Observable<SourceStatus>[];
  start?: Observable<SourceStatus>;
  postings?: () => Observable<PagedResult<JobPosting>>;
}) {
  const statuses = [...(options.statuses ?? [of(neverRun)])];
  const api = {
    sourceStatus: vi.fn(() => (statuses.length > 1 ? statuses.shift()! : statuses[0])),
    startFetch: vi.fn(() => options.start ?? of(running)),
    postings: vi.fn(
      (page: number, pageSize: number) =>
        options.postings?.() ?? of({ items: [posting], page, pageSize, totalCount: 41 }),
    ),
  };
  TestBed.configureTestingModule({
    providers: [SourcingStore, { provide: SourcingApi, useValue: api }],
  });
  return { api, store: TestBed.inject(SourcingStore) };
}

describe('SourcingStore', () => {
  beforeEach(() => void vi.useFakeTimers());
  afterEach(() => void vi.useRealTimers());

  describe('postings', () => {
    it('F0.2-AC1 loads a page of postings', () => {
      const { api, store } = setup({});

      store.loadPostings(2);

      expect(api.postings).toHaveBeenCalledWith(2, 20);
      expect(store.postings()).toEqual([posting]);
      expect([store.page(), store.totalPages()]).toEqual([2, 3]);
      expect(store.loading()).toBe(false);
    });

    it('F0.2-AC1 keeps the shown page when the next one fails', () => {
      const answers = [
        of({ items: [posting], page: 1, pageSize: 20, totalCount: 41 }),
        throwError(() => new HttpErrorResponse({ status: 400, error: { title: 'Bad page' } })),
      ];
      const { store } = setup({ postings: () => answers.shift()! });
      store.loadPostings(1);

      store.loadPostings(2);

      expect(store.postingsError()).toBe('Bad page');
      expect(store.postings()).toEqual([posting]);
      expect(store.page()).toBe(1);
    });
  });

  describe('fetch', () => {
    it('F0.2-AC1 follows a fetch to its end, shows the counts and reloads the list', () => {
      const { api, store } = setup({ statuses: [of(running), of(succeeded)] });

      store.startFetch();
      expect(store.fetching()).toBe(true);
      expect(api.postings).not.toHaveBeenCalled();

      vi.advanceTimersByTime(POLL_INTERVAL_MS);
      expect(store.fetching()).toBe(true);

      vi.advanceTimersByTime(POLL_INTERVAL_MS);
      expect(store.fetching()).toBe(false);
      expect(store.finishedRun()?.insertedCount).toBe(1);
      expect(store.fetchError()).toBeNull();
      expect(api.postings).toHaveBeenCalledWith(1, 20);

      vi.advanceTimersByTime(POLL_INTERVAL_MS * 5);
      expect(api.sourceStatus).toHaveBeenCalledTimes(2);
    });

    it('F0.2-AC1 ignores a second start while one is running', () => {
      const { api, store } = setup({ statuses: [of(running)] });

      store.startFetch();
      store.startFetch();

      expect(api.startFetch).toHaveBeenCalledTimes(1);
    });

    for (const reason of ['NotConfigured', 'Unauthorized', 'Interrupted'] as const) {
      it(`F0.2-AC1 a fetch that fails (${reason}) marks the source unavailable and keeps the list`, () => {
        const { api, store } = setup({ statuses: [of(failed(reason))] });
        store.loadPostings(1);

        store.startFetch();
        vi.advanceTimersByTime(POLL_INTERVAL_MS);

        expect(store.fetching()).toBe(false);
        expect(store.sourceUnavailable()).toBe(true);
        expect(store.failureReason()).toBe(reason);
        expect(store.finishedRun()).toBeNull();
        expect(store.fetchError()).toBeNull();
        expect(store.postings()).toEqual([posting]);
        expect(api.postings).toHaveBeenCalledTimes(1);
      });
    }

    it('F0.2-AC1 a fetch that left no run (the API restarted) is reported as interrupted', () => {
      const { store } = setup({ statuses: [of({ ...succeeded, lastRun: run('older') })] });

      store.startFetch();
      vi.advanceTimersByTime(POLL_INTERVAL_MS);

      expect(store.fetching()).toBe(false);
      expect(store.fetchError()).toBe('The fetch was interrupted. Try again.');
      expect(store.finishedRun()).toBeNull();
    });

    it('F0.2-AC1 a 409 follows the fetch that is already running', () => {
      const { store } = setup({
        start: throwError(
          () => new HttpErrorResponse({ status: 409, error: { code: 'fetch_in_progress' } }),
        ),
        statuses: [of(running), of(succeeded)],
      });

      store.startFetch();
      expect(store.fetching()).toBe(true);
      expect(store.fetchError()).toBeNull();

      vi.advanceTimersByTime(POLL_INTERVAL_MS);
      expect(store.fetching()).toBe(false);
      expect(store.finishedRun()?.id).toBe('f1');
    });

    it('F0.2-AC1 a start that fails shows the error and can be retried', () => {
      const { api, store } = setup({
        start: throwError(
          () => new HttpErrorResponse({ status: 500, error: { title: 'An unexpected error.' } }),
        ),
      });

      store.startFetch();
      expect(store.fetching()).toBe(false);
      expect(store.fetchError()).toBe('An unexpected error.');

      store.startFetch();
      expect(api.startFetch).toHaveBeenCalledTimes(2);
    });

    it('F0.2-AC1 picks up a running fetch when the page is opened', () => {
      const { store } = setup({ statuses: [of(running), of(succeeded)] });

      store.loadSourceStatus();
      expect(store.fetching()).toBe(true);

      vi.advanceTimersByTime(POLL_INTERVAL_MS);
      expect(store.fetching()).toBe(false);
      expect(store.finishedRun()?.id).toBe('f1');
    });

    it('F0.2-AC1 a finished fetch from before the page was opened is not announced', () => {
      const { store } = setup({ statuses: [of(succeeded)] });

      store.loadSourceStatus();

      expect(store.finishedRun()).toBeNull();
      expect(store.fetching()).toBe(false);
    });

    it('F0.2-AC1 survives single failed status requests', () => {
      const { store } = setup({
        statuses: [serverError(), serverError(), of(running), serverError(), of(succeeded)],
      });

      store.startFetch();
      vi.advanceTimersByTime(POLL_INTERVAL_MS * 4);
      expect(store.fetching()).toBe(true);
      expect(store.fetchError()).toBeNull();

      vi.advanceTimersByTime(POLL_INTERVAL_MS);
      expect(store.finishedRun()?.id).toBe('f1');
    });

    it(`F0.2-AC1 stops asking after ${MAX_POLL_FAILURES} failures in a row and can start again`, () => {
      const { api, store } = setup({ statuses: [serverError()] });

      store.startFetch();
      vi.advanceTimersByTime(POLL_INTERVAL_MS * (MAX_POLL_FAILURES + 5));

      expect(api.sourceStatus).toHaveBeenCalledTimes(MAX_POLL_FAILURES);
      expect(store.fetching()).toBe(false);
      expect(store.fetchError()).toBe('Something went wrong.');

      store.startFetch();
      expect(store.fetching()).toBe(true);
    });

    it('F0.2-AC1 waits for a slow status answer instead of cancelling it', () => {
      const { api, store } = setup({ statuses: [NEVER] });

      store.startFetch();
      vi.advanceTimersByTime(POLL_INTERVAL_MS * 5);

      expect(api.sourceStatus).toHaveBeenCalledTimes(1);
    });

    it('F0.2-AC1 stops asking when the page is left', () => {
      const { api, store } = setup({ statuses: [of(running)] });
      store.startFetch();
      vi.advanceTimersByTime(POLL_INTERVAL_MS);

      TestBed.resetTestingModule();
      vi.advanceTimersByTime(POLL_INTERVAL_MS * 5);

      expect(api.sourceStatus).toHaveBeenCalledTimes(1);
    });
  });

  describe('source status', () => {
    it('F0.2-AC4 Unavailable marks the source unavailable', () => {
      const { store } = setup({ statuses: [of({ state: 'Unavailable', lastRun: null })] });

      store.loadSourceStatus();

      expect(store.sourceUnavailable()).toBe(true);
      expect(store.fetchError()).toBeNull();
    });

    for (const state of ['Available', 'NeverRun'] as const) {
      // No currentFetchId: an older API or a stub means "nothing is running".
      it(`F0.2-AC4 ${state} does not mark the source unavailable`, () => {
        const { store } = setup({ statuses: [of({ state, lastRun: null })] });

        store.loadSourceStatus();

        expect(store.sourceUnavailable()).toBe(false);
        expect(store.fetching()).toBe(false);
      });
    }

    it('F0.2-AC4 a failed status call shows nothing and a later success still works', () => {
      const { store } = setup({
        statuses: [serverError(), of({ state: 'Unavailable', lastRun: null })],
      });

      store.loadSourceStatus();
      expect(store.sourceUnavailable()).toBe(false);
      expect(store.fetchError()).toBeNull();
      expect(store.postingsError()).toBeNull();

      store.loadSourceStatus();
      expect(store.sourceUnavailable()).toBe(true);
    });
  });
});

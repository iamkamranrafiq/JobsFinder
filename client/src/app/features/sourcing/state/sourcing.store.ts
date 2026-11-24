import { HttpErrorResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import {
  EMPTY,
  Subject,
  catchError,
  exhaustMap,
  pipe,
  switchMap,
  takeUntil,
  takeWhile,
  tap,
  timer,
} from 'rxjs';

import { toErrorMessage } from '../../../core/http/problem-details';
import { JobPosting } from '../data/posting.model';
import { FetchRun, SourceStatus } from '../data/source-status.model';
import { SourcingApi } from '../data/sourcing.api';

/** Pause between status requests while a fetch runs. */
export const POLL_INTERVAL_MS = 2000;

/** Status requests that may fail in a row before the page stops asking. */
export const MAX_POLL_FAILURES = 5;

interface SourcingState {
  postings: JobPosting[];
  page: number;
  pageSize: number;
  totalCount: number;
  loading: boolean;
  postingsError: string | null;
  status: SourceStatus | null;
  /** The start request is on its way. */
  starting: boolean;
  /** The running fetch this page follows. */
  watchedFetchId: string | null;
  /** The successful fetch this page has just followed to its end. */
  finishedRun: FetchRun | null;
  fetchError: string | null;
  /** The page has been left; the store outlives it (see leave). */
  away: boolean;
}

const initialState: SourcingState = {
  postings: [],
  page: 1,
  pageSize: 20,
  totalCount: 0,
  loading: false,
  postingsError: null,
  status: null,
  starting: false,
  watchedFetchId: null,
  finishedRun: null,
  fetchError: null,
  away: false,
};

/** Feature store (F0.2): provide it on the route that hosts the sourced postings, not in root. */
export const SourcingStore = signalStore(
  withState(initialState),
  withProps(() => ({ _left: new Subject<void>() })),
  withComputed(({ totalCount, pageSize, status, starting, watchedFetchId }) => ({
    totalPages: computed(() => Math.max(1, Math.ceil(totalCount() / pageSize()))),
    fetching: computed(() => starting() || watchedFetchId() !== null),
    /** The last finished fetch failed (F0.2-AC4). */
    sourceUnavailable: computed(() => status()?.state === 'Unavailable'),
    failureReason: computed(() => status()?.lastRun?.failureReason ?? null),
  })),
  withMethods((store, api = inject(SourcingApi)) => ({
    /** Loads a page of the list. The shown page stays until the new one has arrived. */
    loadPostings: rxMethod<number>(
      pipe(
        tap(() => patchState(store, { loading: true, postingsError: null })),
        switchMap((page) =>
          api.postings(page, store.pageSize()).pipe(
            tapResponse({
              next: (result) =>
                patchState(store, {
                  postings: result.items,
                  page: result.page,
                  totalCount: result.totalCount,
                }),
              error: (err) => patchState(store, { postingsError: toErrorMessage(err) }),
              finalize: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),
  })),
  withMethods((store, api = inject(SourcingApi)) => ({
    /** Polls until the fetch has ended, then shows its outcome (F0.2-AC1). */
    _watch: rxMethod<string>(
      pipe(
        tap((fetchId) => patchState(store, { watchedFetchId: fetchId })),
        switchMap((fetchId) => {
          let failures = 0;
          return timer(POLL_INTERVAL_MS, POLL_INTERVAL_MS).pipe(
            exhaustMap(() =>
              api.sourceStatus().pipe(
                tap(() => (failures = 0)),
                catchError((err: unknown) => {
                  if (++failures >= MAX_POLL_FAILURES) {
                    throw err;
                  }
                  return EMPTY;
                }),
              ),
            ),
            takeWhile((status) => status.currentFetchId === fetchId, true),
            takeUntil(store._left),
            tapResponse({
              next: (status) => {
                patchState(store, { status });
                if (status.currentFetchId === fetchId) {
                  return;
                }

                // The run is stored under the id of the fetch, so any other last run means ours left none.
                patchState(store, { watchedFetchId: null });
                if (status.lastRun?.id !== fetchId) {
                  patchState(store, { fetchError: 'The fetch was interrupted. Try again.' });
                } else if (status.lastRun.status === 'Succeeded') {
                  patchState(store, { finishedRun: status.lastRun });
                  store.loadPostings(1);
                }
              },
              error: (err) =>
                patchState(store, { watchedFetchId: null, fetchError: toErrorMessage(err) }),
            }),
          );
        }),
      ),
    ),
  })),
  withMethods((store, api = inject(SourcingApi)) => ({
    /** Also picks up a fetch that is already running, e.g. after a reload. */
    loadSourceStatus: rxMethod<void>(
      pipe(
        switchMap(() =>
          api.sourceStatus().pipe(
            takeUntil(store._left),
            tapResponse({
              next: (status) => {
                patchState(store, { status });
                if (status.currentFetchId && status.currentFetchId !== store.watchedFetchId()) {
                  store._watch(status.currentFetchId);
                }
              },
              // Secondary information: the list reports its own errors, so stay silent here.
              error: () => patchState(store, { status: null }),
            }),
          ),
        ),
      ),
    ),
  })),
  withMethods((store, api = inject(SourcingApi)) => ({
    /** Starts a fetch (F0.2-AC1); ignored while one is running. */
    startFetch: rxMethod<void>(
      pipe(
        exhaustMap(() => {
          if (store.fetching()) {
            return EMPTY;
          }
          patchState(store, { starting: true, finishedRun: null, fetchError: null });
          // Not cancelled when the page is left: the user asked for the fetch. Only its answer is ignored then.
          return api.startFetch().pipe(
            tapResponse({
              next: (status) => {
                if (store.away()) {
                  return;
                }
                patchState(store, { status });
                if (status.currentFetchId) {
                  store._watch(status.currentFetchId);
                }
              },
              error: (err) => {
                if (store.away()) {
                  return;
                }
                if (err instanceof HttpErrorResponse && err.status === 409) {
                  // Someone else started one: follow it, or show what it found if it has just ended.
                  store.loadSourceStatus();
                  store.loadPostings(1);
                } else {
                  patchState(store, { fetchError: toErrorMessage(err) });
                }
              },
              finalize: () => patchState(store, { starting: false }),
            }),
          );
        }),
      ),
    ),

    /** The page is shown: load the list and pick up a running fetch. */
    enter(): void {
      patchState(store, { away: false });
      store.loadPostings(1);
      store.loadSourceStatus();
    },

    /**
     * The page is left. The router keeps a route's providers, and so this store, until the app closes: stop asking
     * for the status and forget what belongs to this visit. A running fetch continues on the server.
     */
    leave(): void {
      store._left.next();
      patchState(store, {
        away: true,
        status: null,
        watchedFetchId: null,
        finishedRun: null,
        fetchError: null,
      });
    },
  })),
);

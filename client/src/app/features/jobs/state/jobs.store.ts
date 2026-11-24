import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { pipe, switchMap, tap } from 'rxjs';

import { toErrorMessage } from '../../../core/http/problem-details';
import { Job, JobSearch } from '../data/job.model';
import { JobsApi } from '../data/jobs.api';

interface JobsState {
  search: JobSearch;
  jobs: Job[];
  totalCount: number;
  loading: boolean;
  error: string | null;
  /** The last job fetch failed (F0.2-AC4). */
  sourceUnavailable: boolean;
}

const initialState: JobsState = {
  search: { text: '', remotePolicy: null, page: 1, pageSize: 20 },
  jobs: [],
  totalCount: 0,
  loading: false,
  error: null,
  sourceUnavailable: false,
};

/** Feature store: provide it on the route (see jobs.routes.ts), not in root. */
export const JobsStore = signalStore(
  withState(initialState),
  withComputed(({ totalCount, search }) => ({
    totalPages: computed(() => Math.max(1, Math.ceil(totalCount() / search().pageSize))),
  })),
  withMethods((store, api = inject(JobsApi)) => ({
    load: rxMethod<Partial<JobSearch>>(
      pipe(
        tap((change) =>
          patchState(store, { search: { ...store.search(), ...change }, loading: true, error: null }),
        ),
        switchMap(() =>
          api.search(store.search()).pipe(
            tapResponse({
              next: (page) => patchState(store, { jobs: page.items, totalCount: page.totalCount }),
              error: (err) => patchState(store, { error: toErrorMessage(err), jobs: [], totalCount: 0 }),
              finalize: () => patchState(store, { loading: false }),
            }),
          ),
        ),
      ),
    ),

    loadSourceStatus: rxMethod<void>(
      pipe(
        switchMap(() =>
          api.sourceStatus().pipe(
            tapResponse({
              next: (status) =>
                patchState(store, { sourceUnavailable: status.state === 'Unavailable' }),
              // Secondary information: the job list reports its own errors, so stay silent here.
              error: () => patchState(store, { sourceUnavailable: false }),
            }),
          ),
        ),
      ),
    ),
  })),
);

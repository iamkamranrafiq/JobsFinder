import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { EMPTY, exhaustMap, filter, map, pipe, switchMap, tap } from 'rxjs';

import { toErrorMessage } from '../../../core/http/problem-details';
import { CvApi } from '../data/cv.api';
import { CV_ACCEPTED_EXTENSIONS, CV_MAX_SIZE_BYTES, CandidateProfileData, Cv } from '../data/cv.model';

export type CvStatus = 'idle' | 'uploading' | 'parsed' | 'saving' | 'saved' | 'error';

interface CvState {
  cv: Cv | null;
  status: CvStatus;
  error: string | null;
  /** True while the profile form is open (F0.1-AC4). */
  editing: boolean;
}

const initialState: CvState = { cv: null, status: 'idle', error: null, editing: false };

/** Client-side pre-check (F0.1-BR1). Returns a reason, or null when the file may be sent. */
export function checkCvFile(file: File): string | null {
  const name = file.name.toLowerCase();
  if (!CV_ACCEPTED_EXTENSIONS.some((ext) => name.endsWith(ext))) {
    return 'Only PDF or DOCX files are accepted.';
  }
  if (file.size === 0) {
    return 'The CV file is empty.';
  }
  if (file.size > CV_MAX_SIZE_BYTES) {
    return 'The file is larger than 5 MB.';
  }
  return null;
}

/** Feature store: provide it on the route (see cv.routes.ts), not in root. */
export const CvStore = signalStore(
  withState(initialState),
  withComputed(({ cv, status }, api = inject(CvApi)) => ({
    uploading: computed(() => status() === 'uploading'),
    saving: computed(() => status() === 'saving'),
    saved: computed(() => status() === 'saved'),
    hasProfile: computed(() => cv() !== null),
    downloadUrl: computed(() => {
      const current = cv();
      return current ? api.fileUrl(current.document.id) : null;
    }),
  })),
  withMethods((store, api = inject(CvApi)) => ({
    upload: rxMethod<File>(
      pipe(
        map((file) => ({ file, problem: checkCvFile(file) })),
        tap(({ problem }) =>
          patchState(
            store,
            problem
              ? { status: 'error', error: problem }
              : { status: 'uploading', error: null, cv: null, editing: false },
          ),
        ),
        filter(({ problem }) => problem === null),
        switchMap(({ file }) =>
          api.upload(file).pipe(
            tapResponse({
              next: (cv) => patchState(store, { cv, status: 'parsed' }),
              error: (err) => patchState(store, { status: 'error', error: toErrorMessage(err) }),
            }),
          ),
        ),
      ),
    ),
    startEdit(): void {
      if (store.cv()) {
        patchState(store, { editing: true, error: null, status: 'parsed' });
      }
    },
    cancelEdit(): void {
      patchState(store, { editing: false, error: null });
    },
    /** F0.1-AC4/BR3: every save creates a new version from the one being edited; double submits are ignored. */
    saveProfile: rxMethod<CandidateProfileData>(
      pipe(
        exhaustMap((data) => {
          const current = store.cv();
          if (!current) {
            return EMPTY;
          }
          patchState(store, { status: 'saving', error: null });
          return api.saveVersion(current.document.id, { baseVersion: current.profile.version, data }).pipe(
            tapResponse({
              next: (cv) => patchState(store, { cv, status: 'saved', editing: false }),
              error: (err) => patchState(store, { status: 'parsed', error: toErrorMessage(err) }),
            }),
          );
        }),
      ),
    ),
  })),
);

// Mirrors JobsFinder.Application.Sourcing.SourceStatusDto. Enums are serialized as strings by the API.
export type SourceState = 'NeverRun' | 'Available' | 'Unavailable';
export type FetchRunStatus = 'Succeeded' | 'Failed';
export type FetchFailureReason =
  | 'NotConfigured'
  | 'Unauthorized'
  | 'Unreachable'
  | 'ActorFailed'
  | 'TimedOut'
  | 'Interrupted'
  | 'Unexpected';

export interface FetchRun {
  id: string;
  source: string;
  status: FetchRunStatus;
  failureReason: FetchFailureReason | null;
  startedAt: string;
  completedAt: string;
  fetchedCount: number;
  insertedCount: number;
  duplicateCount: number;
  malformedCount: number;
}

export interface SourceStatus {
  /** Outcome of the latest finished fetch; a running one does not change it. */
  state: SourceState;
  lastRun: FetchRun | null;
  /** The running fetch, or null. Its run gets this id when it ends. Optional for stubs and older APIs. */
  currentFetchId?: string | null;
}

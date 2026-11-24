// Mirrors JobsFinder.Application.Sourcing.SourceStatusDto. Enums are serialized as strings by the API.
export type SourceState = 'NeverRun' | 'Available' | 'Unavailable';
export type FetchRunStatus = 'Succeeded' | 'Failed';
export type FetchFailureReason =
  'NotConfigured' | 'Unauthorized' | 'Unreachable' | 'ActorFailed' | 'TimedOut';

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
  state: SourceState;
  lastRun: FetchRun | null;
}

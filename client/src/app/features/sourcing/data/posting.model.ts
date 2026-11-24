// Mirrors JobsFinder.Application.Sourcing.JobPostingDto. Enums are serialized as strings by the API.
export type ApplyPath = 'Unknown' | 'AtsUrl' | 'Email' | 'NativeApply';

export interface JobPosting {
  id: string;
  source: string;
  company: string;
  title: string;
  location: string | null;
  isRemote: boolean;
  /** An http(s) or mailto link, if the source gave one. */
  applyUrl: string | null;
  applyPath: ApplyPath;
  postedAt: string | null;
  createdAt: string;
}

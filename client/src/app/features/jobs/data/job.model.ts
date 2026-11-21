// Mirrors JobsFinder.Application.Jobs.JobDto. Enums are serialized as strings by the API.
export type RemotePolicy = 'OnSite' | 'Hybrid' | 'Remote';

export interface Job {
  id: string;
  title: string;
  company: string;
  location: string | null;
  remotePolicy: RemotePolicy;
  url: string;
  postedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface JobSearch {
  text: string;
  remotePolicy: RemotePolicy | null;
  page: number;
  pageSize: number;
}

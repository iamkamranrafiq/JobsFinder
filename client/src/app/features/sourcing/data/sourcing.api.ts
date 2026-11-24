import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../../../core/http/paged-result';
import { JobPosting } from './posting.model';
import { SourceStatus } from './source-status.model';

/** Thin HTTP wrapper. No state here; stores call it. */
@Injectable({ providedIn: 'root' })
export class SourcingApi {
  private readonly http = inject(HttpClient);

  postings(page: number, pageSize: number): Observable<PagedResult<JobPosting>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<JobPosting>>('/api/sourcing/postings', { params });
  }

  sourceStatus(): Observable<SourceStatus> {
    return this.http.get<SourceStatus>('/api/sourcing/status');
  }

  /** Answers at once; the fetch runs in the background. The API requires a JSON body. */
  startFetch(): Observable<SourceStatus> {
    return this.http.post<SourceStatus>('/api/sourcing/fetches', {});
  }
}

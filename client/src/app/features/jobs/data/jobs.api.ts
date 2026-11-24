import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PagedResult } from '../../../core/http/paged-result';
import { Job, JobSearch } from './job.model';

/** Thin HTTP wrapper. No state here; stores call it. */
@Injectable({ providedIn: 'root' })
export class JobsApi {
  private readonly http = inject(HttpClient);

  search(search: JobSearch): Observable<PagedResult<Job>> {
    let params = new HttpParams().set('page', search.page).set('pageSize', search.pageSize);
    if (search.text.trim()) {
      params = params.set('text', search.text.trim());
    }
    if (search.remotePolicy) {
      params = params.set('remotePolicy', search.remotePolicy);
    }
    return this.http.get<PagedResult<Job>>('/api/jobs', { params });
  }

  get(id: string): Observable<Job> {
    return this.http.get<Job>(`/api/jobs/${id}`);
  }
}

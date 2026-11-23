import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { Cv, SaveProfileVersionRequest } from './cv.model';

/** Thin HTTP wrapper. No state here; the CV store calls it. */
@Injectable({ providedIn: 'root' })
export class CvApi {
  private readonly http = inject(HttpClient);

  upload(file: File): Observable<Cv> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<Cv>('/api/cv', form);
  }

  get(id: string): Observable<Cv> {
    return this.http.get<Cv>(`/api/cv/${id}`);
  }

  /** The active profile (newest version across all CVs). Later features read the profile through this. */
  getActive(): Observable<Cv> {
    return this.http.get<Cv>('/api/cv/active');
  }

  /** F0.1-AC4: stores the corrected profile as a new version, which becomes active. */
  saveVersion(cvId: string, request: SaveProfileVersionRequest): Observable<Cv> {
    return this.http.post<Cv>(`/api/cv/${cvId}/profile/versions`, request);
  }

  fileUrl(id: string): string {
    return `/api/cv/${id}/file`;
  }
}

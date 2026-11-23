import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { Cv } from './cv.model';

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

  fileUrl(id: string): string {
    return `/api/cv/${id}/file`;
  }
}

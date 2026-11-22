import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { CvApi } from '../data/cv.api';
import { Cv } from '../data/cv.model';
import { CvStore, checkCvFile } from './cv.store';

const cv: Cv = {
  document: {
    id: 'doc-1',
    fileName: 'ada.pdf',
    contentType: 'application/pdf',
    format: 'Pdf',
    sizeBytes: 1234,
    uploadedAt: '2026-11-22T12:00:00Z',
  },
  profile: {
    id: 'p-1',
    cvDocumentId: 'doc-1',
    version: 1,
    createdAt: '2026-11-22T12:00:00Z',
    data: {
      identity: { fullName: 'Ada Lovelace', headline: null },
      contact: { email: 'ada@example.com', phone: null, location: null },
      summary: null,
      skills: [],
      experience: [],
      education: [],
      certifications: [],
      links: [],
      workAuthorisation: null,
      currentEmployer: 'Analytical Engines Ltd',
      preferences: {
        targetRoles: [],
        locations: [],
        remote: null,
        salaryMin: null,
        salaryMax: null,
        salaryCurrency: null,
        noticePeriod: null,
      },
    },
  },
};

function file(name: string, size = 10): File {
  return new File([new Uint8Array(size)], name);
}

function setup(upload: (f: File) => Observable<Cv>) {
  TestBed.configureTestingModule({
    providers: [CvStore, { provide: CvApi, useValue: { upload, fileUrl: (id: string) => `/api/cv/${id}/file` } }],
  });
  return TestBed.inject(CvStore);
}

describe('CvStore', () => {
  it('stores the parsed profile after a successful upload (F0.1-AC1)', () => {
    const sent: File[] = [];
    const store = setup((f) => {
      sent.push(f);
      return of(cv);
    });

    store.upload(file('ada.PDF'));

    expect(sent.map((f) => f.name)).toEqual(['ada.PDF']);
    expect(store.cv()).toEqual(cv);
    expect(store.status()).toBe('parsed');
    expect(store.hasProfile()).toBe(true);
    expect(store.error()).toBeNull();
  });

  it('accepts DOCX files (F0.1-AC2)', () => {
    const store = setup(() => of({ ...cv, document: { ...cv.document, format: 'Docx' } }));

    store.upload(file('ada.docx'));

    expect(store.status()).toBe('parsed');
    expect(store.cv()?.document.format).toBe('Docx');
  });

  it('exposes the API rejection reason (F0.1-AC3)', () => {
    const store = setup(() =>
      throwError(
        () =>
          new HttpErrorResponse({
            status: 422,
            error: { title: 'This file is password-protected.', code: 'cv_password_protected' },
          }),
      ),
    );

    store.upload(file('locked.pdf'));

    expect(store.status()).toBe('error');
    expect(store.error()).toBe('This file is password-protected.');
    expect(store.cv()).toBeNull();
  });

  it('rejects other types and files over 5 MB without calling the API (F0.1-AC3)', () => {
    let calls = 0;
    const store = setup(() => {
      calls++;
      return of(cv);
    });

    store.upload(file('photo.png'));
    expect(store.error()).toBe('Only PDF or DOCX files are accepted.');

    store.upload(file('big.pdf', 5 * 1024 * 1024 + 1));
    expect(store.error()).toBe('The file is larger than 5 MB.');

    expect(calls).toBe(0);
    expect(store.status()).toBe('error');
  });

  it('points the download link at the raw file (F0.1-AC6)', () => {
    const store = setup(() => of(cv));
    expect(store.downloadUrl()).toBeNull();

    store.upload(file('ada.pdf'));

    expect(store.downloadUrl()).toBe('/api/cv/doc-1/file');
  });
});

describe('checkCvFile', () => {
  it('accepts a 5 MB PDF and rejects empty files (F0.1-BR1)', () => {
    expect(checkCvFile(file('cv.pdf', 5 * 1024 * 1024))).toBeNull();
    expect(checkCvFile(file('cv.pdf', 0))).toBe('The CV file is empty.');
  });
});

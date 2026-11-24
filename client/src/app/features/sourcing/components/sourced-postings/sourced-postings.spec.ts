import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { SourceStatus } from '../../data/source-status.model';
import { SourcingApi } from '../../data/sourcing.api';
import { POLL_INTERVAL_MS, SourcingStore } from '../../state/sourcing.store';
import { SourcedPostings } from './sourced-postings';

@Component({ template: 'elsewhere' })
class Elsewhere {}

const running: SourceStatus = { state: 'NeverRun', lastRun: null, currentFetchId: 'f1' };
const finished: SourceStatus = {
  state: 'Available',
  lastRun: {
    id: 'f1',
    source: 'apify',
    status: 'Succeeded',
    failureReason: null,
    startedAt: '2026-11-24T10:00:00Z',
    completedAt: '2026-11-24T10:00:05Z',
    fetchedCount: 1,
    insertedCount: 1,
    duplicateCount: 0,
    malformedCount: 0,
  },
  currentFetchId: null,
};

/** Real navigation: the router keeps the store of a left route alive, so the component has to stop it. */
describe('SourcedPostings leaving the route', () => {
  let status: SourceStatus;
  const sourceStatus = vi.fn(() => of(status));
  const empty = { items: [], page: 1, pageSize: 20, totalCount: 0 };

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    status = running;
    sourceStatus.mockClear();
    TestBed.configureTestingModule({
      providers: [
        { provide: SourcingApi, useValue: { sourceStatus, postings: () => of(empty) } },
        provideRouter([
          {
            path: 'jobs',
            providers: [SourcingStore],
            children: [{ path: '', component: SourcedPostings }],
          },
          { path: 'cv', component: Elsewhere },
        ]),
      ],
    });
  });
  afterEach(() => void vi.useRealTimers());

  it('F0.2-AC1 stops asking for the status after navigating away and resumes on return', async () => {
    const harness = await RouterTestingHarness.create('/jobs');
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS * 2);
    expect(sourceStatus.mock.calls.length).toBeGreaterThan(1);

    await harness.navigateByUrl('/cv');
    const calls = sourceStatus.mock.calls.length;
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS * 10);
    expect(sourceStatus.mock.calls.length).toBe(calls);

    await harness.navigateByUrl('/jobs');
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS * 2);
    expect(sourceStatus.mock.calls.length).toBeGreaterThan(calls + 1);
  });

  it('F0.2-AC1 does not show an old result after coming back', async () => {
    const harness = await RouterTestingHarness.create('/jobs');
    const text = () => harness.routeNativeElement?.textContent ?? '';
    status = finished;
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS);
    harness.detectChanges();
    expect(text()).toContain('Fetch finished: 1 new');

    await harness.navigateByUrl('/cv');
    await harness.navigateByUrl('/jobs');
    harness.detectChanges();

    expect(text()).toContain('Sourced postings');
    expect(text()).not.toContain('Fetch finished');
  });
});

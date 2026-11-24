import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

import { stubPostings } from './support/stub-source-status';

// JF-7: "Source unavailable" on the Jobs page. The API is stubbed with page.route.

const jobsPage = {
  items: [
    {
      id: '1',
      title: 'Backend Engineer',
      company: 'Globex',
      location: null,
      remotePolicy: 'Hybrid',
      url: 'https://example.com/1',
      postedAt: '2026-11-02T00:00:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
};

function run(status: 'Succeeded' | 'Failed') {
  return {
    id: 'run-1',
    source: 'apify',
    status,
    failureReason: status === 'Failed' ? 'Unauthorized' : null,
    startedAt: '2026-11-24T10:00:00Z',
    completedAt: '2026-11-24T10:00:05Z',
    fetchedCount: 0,
    insertedCount: 0,
    duplicateCount: 0,
    malformedCount: 0,
  };
}

const unavailable = { state: 'Unavailable', lastRun: run('Failed') };

/** Opens the Jobs page and waits until the stubbed status has been answered. */
async function openJobs(page: Page, statusResponse: { status?: number; json: unknown }) {
  await stubPostings(page);
  await page.route('**/api/jobs?**', (route) => route.fulfill({ json: jobsPage }));
  await page.route('**/api/sourcing/status', (route) => route.fulfill(statusResponse));
  const answered = page.waitForResponse('**/api/sourcing/status');
  await page.goto('/jobs');
  await answered;
  await expect(page.getByTestId('job-list')).toContainText('Backend Engineer');
}

test('F0.2-AC4 shows "Source unavailable" after a failed fetch and still lists stored jobs', async ({
  page,
}) => {
  await openJobs(page, { json: unavailable });

  await expect(page.getByRole('alert')).toHaveText('Source unavailable');
  await expect(page.getByRole('alert')).toHaveClass(/inline-alert--error/);
  await expect(page.getByText('Jobs already stored are not affected.')).toBeVisible();
  await expect(page.getByTestId('job-list')).toContainText('Backend Engineer');
});

for (const [name, response] of [
  ['the last fetch succeeded', { json: { state: 'Available', lastRun: run('Succeeded') } }],
  ['nothing was fetched yet', { json: { state: 'NeverRun', lastRun: null } }],
  ['the status cannot be read', { status: 500, json: { title: 'An unexpected error occurred.' } }],
] as const) {
  test(`F0.2-AC4 shows no source message when ${name}`, async ({ page }) => {
    await openJobs(page, response);

    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByText('Source unavailable')).toHaveCount(0);
  });
}

for (const colorScheme of ['light', 'dark'] as const) {
  test(`F0.2-AC4 the message passes axe in ${colorScheme} and fits 375px`, async ({ page }) => {
    await page.emulateMedia({ colorScheme, reducedMotion: 'reduce' });
    await page.setViewportSize({ width: 375, height: 812 });
    await openJobs(page, { json: unavailable });
    await expect(page.getByRole('alert')).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze();
    expect(results.violations).toEqual([]);
  });
}

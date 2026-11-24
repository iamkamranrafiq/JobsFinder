import AxeBuilder from '@axe-core/playwright';
import { expect, Page, test } from '@playwright/test';

// QA tests for JF-7 ("Source unavailable"). They run against the REAL stack on :4200.
// The ONLY stub is GET /api/sourcing/status (user-approved exception for the AC4 screen tests); the job
// list, the API and everything else is real. Nothing here writes to the database or the global active
// profile, and no fetch run is created: the status is global, so a failed run would affect other specs.

const failedRun = {
  id: '00000000-0000-7000-8000-000000000001',
  source: 'apify',
  status: 'Failed',
  failureReason: 'Unauthorized',
  startedAt: '2026-11-24T10:00:00Z',
  completedAt: '2026-11-24T10:00:05Z',
  fetchedCount: 0,
  insertedCount: 0,
  duplicateCount: 0,
  malformedCount: 0,
};
const okRun = { ...failedRun, status: 'Succeeded', failureReason: null, fetchedCount: 3, insertedCount: 3 };

async function openJobs(page: Page, status: { status?: number; json?: unknown; abort?: boolean }) {
  await page.route('**/api/sourcing/status', (route) =>
    status.abort ? route.abort() : route.fulfill({ status: status.status ?? 200, json: status.json ?? {} }),
  );
  await page.goto('/jobs');
  await expect(page.getByRole('heading', { name: 'Jobs' })).toBeVisible({ timeout: 30_000 });
  // The list (real API) has finished loading when the progress bar is gone.
  await expect(page.getByTestId('job-list')).toBeVisible({ timeout: 30_000 });
}

test.describe('JF-7 source status on /jobs (status stubbed, everything else real)', () => {
  test('TC43 Unavailable shows the "Source unavailable" alert and the stored job list stays', async ({ page }) => {
    await openJobs(page, { json: { state: 'Unavailable', lastRun: failedRun } });

    const alert = page.getByRole('alert');
    await expect(alert).toHaveCount(1);
    await expect(alert).toHaveText('Source unavailable');
    await expect(page.getByTestId('job-list')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Search' })).toBeEnabled();
    // Nothing leaks from the failure into the UI.
    await expect(page.locator('body')).not.toContainText('Unauthorized');
    await expect(page.locator('body')).not.toContainText('apify_api');
  });

  for (const [name, json] of [
    ['Available', { state: 'Available', lastRun: okRun }],
    ['NeverRun', { state: 'NeverRun', lastRun: null }],
  ] as const) {
    test(`TC44 ${name} shows no banner`, async ({ page }) => {
      await openJobs(page, { json });
      await expect(page.getByText('Source unavailable')).toHaveCount(0);
      await expect(page.getByRole('alert')).toHaveCount(0);
    });
  }

  test('TC45 a failing status request shows no banner and no second error', async ({ page }) => {
    await openJobs(page, { status: 500, json: { title: 'boom' } });
    await expect(page.getByText('Source unavailable')).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByTestId('job-list')).toBeVisible();
  });

  test('TC45b an unreachable status request shows no banner', async ({ page }) => {
    await openJobs(page, { abort: true });
    await expect(page.getByText('Source unavailable')).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  test('TC49 the banner follows the latest status on reload (recovery clears it)', async ({ page }) => {
    let state = 'Unavailable';
    await page.route('**/api/sourcing/status', (route) =>
      route.fulfill({
        json: state === 'Unavailable' ? { state, lastRun: failedRun } : { state, lastRun: okRun },
      }),
    );
    await page.goto('/jobs');
    await expect(page.getByRole('alert')).toHaveText('Source unavailable');
    state = 'Available';
    await page.reload();
    await expect(page.getByTestId('job-list')).toBeVisible();
    await expect(page.getByText('Source unavailable')).toHaveCount(0);
  });

  test('TC47 at 375px the banner is visible and the page does not scroll sideways', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await openJobs(page, { json: { state: 'Unavailable', lastRun: failedRun } });

    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible();
    const box = (await alert.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(375);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow).toBeLessThanOrEqual(0);
  });

  for (const scheme of ['light', 'dark'] as const) {
    test(`TC47b banner passes automated accessibility/contrast checks (${scheme})`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await openJobs(page, { json: { state: 'Unavailable', lastRun: failedRun } });
      await expect(page.getByRole('alert')).toBeVisible();

      const results = await new AxeBuilder({ page }).include('app-source-status-banner').analyze();
      expect(results.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([]);
    });
  }
});

test.describe('JF-7 real stack (no stubs at all)', () => {
  test('TC46 the status endpoint answers through nginx and /jobs shows no banner', async ({ page, request }) => {
    const response = await request.get('/api/sourcing/status');
    expect(response.status()).toBe(200);
    expect(await response.json()).toEqual({ state: 'NeverRun', lastRun: null });

    const status = page.waitForResponse('**/api/sourcing/status');
    await page.goto('/jobs');
    expect((await status).status()).toBe(200);
    await expect(page.getByTestId('job-list')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText('Source unavailable')).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveCount(0);
  });

  test('TC46b nothing in the real app can start a fetch', async ({ request }) => {
    for (const [method, url] of [
      ['post', '/api/sourcing/fetch'],
      ['post', '/api/sourcing/status'],
      ['post', '/api/sourcing'],
    ] as const) {
      const res = await request[method](url, { data: {} });
      expect([404, 405]).toContain(res.status());
    }
    const status = await (await request.get('/api/sourcing/status')).json();
    expect(status.state).toBe('NeverRun');
  });
});

import { type Page } from '@playwright/test';

/**
 * The Jobs page also reads the job source status. Stub it in every spec that opens /jobs, or a failed
 * fetch in whatever backend is running adds a second alert to the page.
 */
export async function stubSourceStatus(page: Page) {
  await page.route('**/api/sourcing/status', (route) =>
    route.fulfill({ json: { state: 'NeverRun', lastRun: null } }),
  );
}

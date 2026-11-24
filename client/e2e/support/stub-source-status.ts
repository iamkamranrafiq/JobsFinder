import { type Page } from '@playwright/test';

/**
 * The Jobs page also reads the job source status and the sourced postings. Stub them in every spec that opens
 * /jobs, or whatever backend is running (a failed fetch, an older API) changes the page. A spec may never start a
 * real fetch.
 */
export async function stubSourceStatus(page: Page) {
  await stubPostings(page);
  await page.route('**/api/sourcing/status', (route) =>
    route.fulfill({ json: { state: 'NeverRun', lastRun: null } }),
  );
}

/** An empty postings list, and no way to reach the real fetch trigger. */
export async function stubPostings(page: Page) {
  await page.route('**/api/sourcing/fetches', (route) => route.abort());
  await page.route('**/api/sourcing/postings?**', (route) =>
    route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }),
  );
}

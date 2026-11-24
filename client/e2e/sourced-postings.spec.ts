import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Route } from '@playwright/test';

// JF-8 (F0.2-AC1): fetch jobs and list the sourced postings. The whole API is stubbed: no spec may start a real
// fetch or depend on what a backend has stored.

const posting = {
  id: 'p1',
  source: 'apify',
  company: 'Acme',
  title: 'Backend Engineer',
  location: 'London',
  isRemote: true,
  applyUrl: 'https://boards.greenhouse.io/acme/jobs/1',
  applyPath: 'AtsUrl',
  postedAt: '2026-11-20T10:00:00Z',
  createdAt: '2026-11-24T10:00:00Z',
};

function run(id: string, change: object = {}) {
  return {
    id,
    source: 'apify',
    status: 'Succeeded',
    failureReason: null,
    startedAt: '2026-11-24T10:00:00Z',
    completedAt: '2026-11-24T10:00:05Z',
    fetchedCount: 16,
    insertedCount: 12,
    duplicateCount: 3,
    malformedCount: 1,
    ...change,
  };
}

const neverRun = { state: 'NeverRun', lastRun: null, currentFetchId: null };
const running = { state: 'NeverRun', lastRun: null, currentFetchId: 'f1' };
const succeeded = { state: 'Available', lastRun: run('f1'), currentFetchId: null };

function failed(failureReason: string) {
  return {
    state: 'Unavailable',
    lastRun: run('f1', { status: 'Failed', failureReason, insertedCount: 0 }),
    currentFetchId: null,
  };
}

/** A stubbed backend. Tests change its fields while the page is open. */
class Backend {
  status: object = neverRun;
  postings: object[] = [];
  totalCount: number | null = null;
  onFetch: (route: Route) => Promise<void> = (route) => {
    this.status = running;
    return route.fulfill({ status: 202, json: running });
  };
  readonly fetchBodies: (string | null)[] = [];
  readonly postingQueries: string[] = [];

  async install(page: Page) {
    await page.route('**/api/jobs?**', (route) =>
      route.fulfill({ json: { items: [], page: 1, pageSize: 20, totalCount: 0 } }),
    );
    await page.route('**/api/sourcing/status', (route) => route.fulfill({ json: this.status }));
    await page.route('**/api/sourcing/fetches', (route) => {
      this.fetchBodies.push(route.request().postData());
      return this.onFetch(route);
    });
    await page.route('**/api/sourcing/postings?**', (route) => {
      const url = new URL(route.request().url());
      this.postingQueries.push(url.search);
      return route.fulfill({
        json: {
          items: this.postings,
          page: Number(url.searchParams.get('page')),
          pageSize: 20,
          totalCount: this.totalCount ?? this.postings.length,
        },
      });
    });
  }
}

let backend: Backend;

test.beforeEach(async ({ page }) => {
  backend = new Backend();
  await backend.install(page);
});

const fetchButton = (page: Page) => page.getByRole('button', { name: 'Fetch jobs' });
const list = (page: Page) => page.getByTestId('posting-list');

async function openJobs(page: Page) {
  await page.goto('/jobs');
  await expect(page.getByRole('heading', { name: 'Sourced postings' })).toBeVisible();
  await expect(list(page)).not.toContainText('Loading');
}

test('F0.2-AC1 fetching shows progress, then the counts and the new postings', async ({ page }) => {
  await openJobs(page);
  await expect(list(page)).toContainText('No postings yet.');

  await fetchButton(page).click();

  await expect(page.getByRole('progressbar', { name: 'Fetching jobs' })).toBeVisible();
  await expect(page.getByTestId('fetch-running')).toContainText('this can take a few minutes');
  await expect(fetchButton(page)).toBeDisabled();
  expect(backend.fetchBodies).toEqual(['{}']);

  backend.postings = [posting];
  backend.status = succeeded;

  await expect(page.getByRole('status')).toHaveText(
    'Fetch finished: 12 new, 3 already stored, 1 skipped.',
  );
  await expect(list(page)).toContainText('Backend Engineer');
  await expect(list(page)).toContainText('Acme');
  await expect(list(page)).toContainText('London');
  await expect(list(page)).toContainText('Remote');
  await expect(list(page)).toContainText('ATS link');
  await expect(list(page).getByRole('link', { name: 'Backend Engineer' })).toHaveAttribute(
    'href',
    posting.applyUrl,
  );
  await expect(page.getByRole('progressbar', { name: 'Fetching jobs' })).toHaveCount(0);
  await expect(fetchButton(page)).toBeEnabled();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

for (const [reason, hint] of [
  ['Unauthorized', 'The last job fetch failed.'],
  ['NotConfigured', 'The job source is not set up.'],
  ['Interrupted', 'The last job fetch was interrupted.'],
] as const) {
  test(`F0.2-AC1 a fetch that fails (${reason}) says so and keeps the list`, async ({ page }) => {
    backend.postings = [posting];
    await openJobs(page);

    await fetchButton(page).click();
    await expect(fetchButton(page)).toBeDisabled();
    backend.status = failed(reason);

    await expect(page.getByRole('alert')).toHaveText('Source unavailable');
    await expect(page.getByText(`${hint} Jobs already stored are not affected.`)).toBeVisible();
    await expect(page.locator('body')).not.toContainText(reason);
    await expect(list(page)).toContainText('Backend Engineer');
    await expect(fetchButton(page)).toBeEnabled();
    await expect(page.getByRole('status')).toHaveCount(0);
  });
}

test('F0.2-AC1 a new fetch can clear an old failure', async ({ page }) => {
  backend.status = failed('Unauthorized');
  await openJobs(page);
  await expect(page.getByRole('alert')).toHaveText('Source unavailable');

  backend.onFetch = (route) => {
    backend.status = { ...failed('Unauthorized'), currentFetchId: 'f2' };
    return route.fulfill({ status: 202, json: backend.status });
  };
  await fetchButton(page).click();
  await expect(fetchButton(page)).toBeDisabled();
  await expect(page.getByRole('alert')).toHaveText('Source unavailable');
  backend.status = { state: 'Available', lastRun: run('f2'), currentFetchId: null };

  await expect(page.getByRole('status')).toContainText('12 new');
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('F0.2-AC1 a fetch the API lost is reported as interrupted', async ({ page }) => {
  await openJobs(page);

  await fetchButton(page).click();
  await expect(fetchButton(page)).toBeDisabled();
  backend.status = neverRun;

  await expect(page.getByRole('alert')).toHaveText('The fetch was interrupted. Try again.');
  await expect(fetchButton(page)).toBeEnabled();
});

test('F0.2-AC1 a fetch started elsewhere is followed instead of reported as an error', async ({
  page,
}) => {
  await openJobs(page);
  backend.onFetch = (route) => {
    backend.status = running;
    return route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      json: { title: 'A job fetch is already running.', code: 'fetch_in_progress' },
    });
  };

  await fetchButton(page).click();

  await expect(page.getByRole('progressbar', { name: 'Fetching jobs' })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  backend.status = succeeded;
  await expect(page.getByRole('status')).toContainText('12 new');
});

test('F0.2-AC1 a start that fails shows the API error title', async ({ page }) => {
  backend.onFetch = (route) =>
    route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      json: { title: 'An unexpected error occurred.' },
    });
  await openJobs(page);

  await fetchButton(page).click();

  await expect(page.getByRole('alert')).toHaveText('An unexpected error occurred.');
  await expect(fetchButton(page)).toBeEnabled();
});

test('F0.2-AC1 a running fetch is still shown after a reload', async ({ page }) => {
  await openJobs(page);
  await fetchButton(page).click();
  await expect(fetchButton(page)).toBeDisabled();

  await page.reload();

  await expect(page.getByRole('progressbar', { name: 'Fetching jobs' })).toBeVisible();
  await expect(fetchButton(page)).toBeDisabled();
  backend.status = succeeded;
  await expect(page.getByRole('status')).toContainText('12 new');
  expect(backend.fetchBodies).toHaveLength(1);
});

test('F0.2-AC1 a finished fetch is not announced again after a reload', async ({ page }) => {
  backend.status = succeeded;
  backend.postings = [posting];
  await openJobs(page);

  await expect(list(page)).toContainText('Backend Engineer');
  await expect(page.getByRole('status')).toHaveCount(0);
});

test('F0.2-AC1 pages through the postings', async ({ page }) => {
  backend.postings = [posting];
  backend.totalCount = 41;
  await openJobs(page);
  const pager = page.getByRole('navigation', { name: 'Posting pages' });

  await expect(pager).toContainText('Page 1 of 3');
  await expect(pager.getByRole('button', { name: 'Previous' })).toBeDisabled();
  await pager.getByRole('button', { name: 'Next' }).click();

  await expect(pager).toContainText('Page 2 of 3');
  expect(backend.postingQueries.at(-1)).toBe('?page=2&pageSize=20');
});

test('F0.2-AC1 shows an error when the postings cannot be loaded', async ({ page }) => {
  await page.route('**/api/sourcing/postings?**', (route) =>
    route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      json: { title: 'An unexpected error occurred.' },
    }),
  );
  await openJobs(page);

  await expect(page.getByRole('alert')).toHaveText('An unexpected error occurred.');
  await expect(list(page)).toContainText('Not loaded.');
});

test('F0.2-AC1 renders untrusted text as text and only real links as links', async ({ page }) => {
  backend.postings = [
    { ...posting, id: '1', title: '<img src=x onerror="document.title=\'hacked\'">Evil' },
    { ...posting, id: '2', title: 'No link', applyUrl: null, applyPath: 'Unknown' },
    { ...posting, id: '3', title: 'By mail', applyUrl: 'mailto:jobs@acme.com', applyPath: 'Email' },
    { ...posting, id: '4', title: 'Other site', location: null, postedAt: null, isRemote: false },
  ];
  await openJobs(page);

  await expect(list(page)).toContainText('<img src=x');
  await expect(list(page).locator('img')).toHaveCount(0);
  expect(await page.title()).not.toBe('hacked');
  await expect(list(page).getByText('No link')).toBeVisible();
  await expect(list(page).getByRole('link', { name: 'No link' })).toHaveCount(0);
  const mail = list(page).getByRole('link', { name: 'By mail' });
  await expect(mail).toHaveAttribute('href', 'mailto:jobs@acme.com');
  await expect(mail).not.toHaveAttribute('target', /.*/);
  const web = list(page).getByRole('link', { name: 'Other site' });
  await expect(web).toHaveAttribute('target', '_blank');
  await expect(web).toHaveAttribute('rel', /noopener/);
});

const long = [
  {
    ...posting,
    title:
      'Principal Platform Engineer for distributed event-driven payments infrastructure and tooling',
    company: 'Analytical Engines International Holdings Ltd',
    location: 'Remote within the European Union and the United Kingdom',
  },
  { ...posting, id: 'p2', title: 'x'.repeat(300), applyUrl: null, applyPath: 'NativeApply' },
];

for (const width of [375, 1000]) {
  test(`F0.2-AC1 rows grow with their content and nothing overflows at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    backend.postings = long;
    backend.status = running;
    await openJobs(page);
    await expect(list(page)).toContainText('Analytical Engines');
    await expect(page.getByTestId('fetch-running')).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);
    const clipped = await list(page).evaluate((el) =>
      [...el.querySelectorAll<HTMLElement>('mat-list-item, mat-list-item *')]
        .filter(
          (e) =>
            getComputedStyle(e).overflow !== 'visible' &&
            (e.scrollHeight > e.clientHeight + 1 || e.scrollWidth > e.clientWidth + 1),
        )
        .map((e) => `${e.tagName}.${e.className}`),
    );
    expect(clipped).toEqual([]);
  });
}

for (const colorScheme of ['light', 'dark'] as const) {
  test(`F0.2-AC1 passes axe in ${colorScheme}: list, running, finished and failed`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme, reducedMotion: 'reduce' });
    const violations = async () =>
      (
        await new AxeBuilder({ page })
          .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
          .analyze()
      ).violations;
    backend.postings = [posting, { ...posting, id: 'p2', applyUrl: null, applyPath: 'Unknown' }];
    backend.totalCount = 41;
    await openJobs(page);
    await expect(list(page)).toContainText('Backend Engineer');
    expect(await violations()).toEqual([]);

    await fetchButton(page).click();
    await expect(page.getByTestId('fetch-running')).toBeVisible();
    expect(await violations()).toEqual([]);

    backend.status = succeeded;
    await expect(page.getByRole('status')).toBeVisible();
    expect(await violations()).toEqual([]);

    await fetchButton(page).click();
    await expect(fetchButton(page)).toBeDisabled();
    backend.status = failed('Unauthorized');
    await expect(page.getByRole('alert')).toBeVisible();
    expect(await violations()).toEqual([]);
  });
}

test('F0.2-AC1 the fetch can be started with the keyboard', async ({ page }) => {
  await openJobs(page);

  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Jobs' }).focus();
  await page.keyboard.press('Tab');
  await expect(fetchButton(page)).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(page.getByTestId('fetch-running')).toBeVisible();
});

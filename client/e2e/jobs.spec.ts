import { expect, test } from '@playwright/test';

import { stubSourceStatus } from './support/stub-source-status';

const page1 = {
  items: [
    {
      id: '1',
      title: 'Senior .NET Developer',
      company: 'Acme',
      location: 'Worldwide',
      remotePolicy: 'Remote',
      url: 'https://example.com/1',
      postedAt: '2026-11-01T00:00:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
};

test.beforeEach(({ page }) => stubSourceStatus(page));

test('lists jobs and sends the search text to the API', async ({ page }) => {
  const requested: string[] = [];
  await page.route('**/api/jobs?**', async (route) => {
    requested.push(route.request().url());
    await route.fulfill({ json: page1 });
  });

  await page.goto('/');

  await expect(page).toHaveURL(/\/jobs$/);
  await expect(page.getByTestId('job-list')).toContainText('Senior .NET Developer');

  await page.getByLabel('Search jobs').fill('dotnet');
  await page.getByRole('button', { name: 'Search' }).click();

  await expect.poll(() => requested.some((u) => u.includes('text=dotnet'))).toBe(true);
});

test('shows the API error title', async ({ page }) => {
  await page.route('**/api/jobs?**', (route) =>
    route.fulfill({ status: 500, contentType: 'application/problem+json', json: { title: 'An unexpected error occurred.' } }),
  );

  await page.goto('/jobs');

  await expect(page.getByRole('alert')).toHaveText('An unexpected error occurred.');
});

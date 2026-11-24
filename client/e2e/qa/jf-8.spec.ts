import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

// QA tests for JF-8 (fetch jobs on demand and list the sourced postings), run against the REAL compose stack on :4200.
//
// Nothing here may start a fetch: the compose stack has no Apify settings, so a fetch would store a failed run for
// good (the global "Source unavailable" banner would then show for every other spec). So these tests only READ (GET and
// the CORS preflight OPTIONS); no request to /api/sourcing/fetches is ever a POST here. The refusals of a wrong content
// type or bad JSON are tested in-process (QA/JF-8 API tests), not on the shared stack. The full flow (press "Fetch jobs", progress, counts, new rows) runs against a throwaway stack outside the repo.

test.describe('JF-8 sourced postings section (real stack, read-only)', () => {
  test('TC30 the section sits above the sample data, offers Fetch jobs and the credit hint, and starts nothing', async ({
    page,
  }) => {
    const posts: string[] = [];
    page.on('request', (r) => {
      if (r.method() !== 'GET') posts.push(`${r.method()} ${r.url()}`);
    });

    const status = await (await page.request.get('/api/sourcing/status')).json();
    await page.goto('/jobs');

    const sourced = page.getByRole('heading', { name: 'Sourced postings' });
    const sample = page.getByRole('heading', { name: 'Sample data' });
    await expect(sourced).toBeVisible({ timeout: 30_000 });
    await expect(sample).toBeVisible();
    const sourcedBox = await sourced.boundingBox();
    const sampleBox = await sample.boundingBox();
    expect(sourcedBox!.y).toBeLessThan(sampleBox!.y);

    const button = page.getByRole('button', { name: 'Fetch jobs' });
    await expect(button).toBeVisible();
    await expect(button).toBeEnabled();
    await expect(page.getByText('Each fetch uses Apify credit.')).toBeVisible();
    await expect(page.getByTestId('posting-list')).toBeVisible();
    await expect(page.getByTestId('fetch-running')).toHaveCount(0);
    await expect(page.getByTestId('fetch-result')).toHaveCount(0);

    // The state of the shared stack decides whether the list is empty or the banner shows; never assume NeverRun.
    if (status.state === 'NeverRun') {
      await expect(page.getByRole('alert')).toHaveCount(0);
    }

    expect(posts, 'opening /jobs must not send a POST').toEqual([]);
  });

  test('TC30b no horizontal scrolling at 375px, and axe finds nothing serious on the section', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/jobs');
    await expect(page.getByRole('heading', { name: 'Sourced postings' })).toBeVisible({
      timeout: 30_000,
    });
    await expect(page.getByRole('button', { name: 'Fetch jobs' })).toBeVisible();

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow).toBeLessThanOrEqual(0);

    const results = await new AxeBuilder({ page }).analyze();
    expect(
      results.violations
        .filter((v) => v.impact === 'serious' || v.impact === 'critical')
        .map((v) => `${v.id}: ${v.help}`),
    ).toEqual([]);
  });

  test('TC30c the sourced list is reachable through nginx and has the paged shape', async ({
    request,
  }) => {
    const response = await request.get('/api/sourcing/postings');
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body.page).toBe(1);
    expect(body.pageSize).toBe(20);
    expect(Array.isArray(body.items)).toBe(true);
    expect(body.totalCount).toBeGreaterThanOrEqual(body.items.length);
    for (const item of body.items) {
      expect(item).not.toHaveProperty('description');
      expect(item).not.toHaveProperty('hash');
      expect(item).not.toHaveProperty('contentHash');
    }

    const status = await (await request.get('/api/sourcing/status')).json();
    expect(status).toHaveProperty('currentFetchId');
  });

  for (const query of [
    '?page=0',
    '?pageSize=0',
    '?pageSize=101',
    '?page=100001',
    '?page=99999999999',
    '?page=abc',
  ]) {
    test(`TC16 bad paging ${query} is a 400 problem through nginx`, async ({ request }) => {
      const response = await request.get(`/api/sourcing/postings${query}`);
      expect(response.status()).toBe(400);
      expect(response.headers()['content-type']).toContain('application/problem+json');
    });
  }

  test('TC14 the preflight for the trigger is refused for a foreign origin and allowed for the app origin', async ({
    request,
  }) => {
    const foreign = await request.fetch('/api/sourcing/fetches', {
      method: 'OPTIONS',
      headers: {
        origin: 'http://evil.example',
        'access-control-request-method': 'POST',
        'access-control-request-headers': 'content-type',
      },
    });
    expect(foreign.headers()['access-control-allow-origin']).toBeUndefined();

    const app = await request.fetch('/api/sourcing/fetches', {
      method: 'OPTIONS',
      headers: {
        origin: 'http://localhost:4200',
        'access-control-request-method': 'POST',
        'access-control-request-headers': 'content-type',
      },
    });
    expect(app.headers()['access-control-allow-origin']).toBe('http://localhost:4200');
  });

  test('TC24 other verbs on the trigger and the list never start anything', async ({ request }) => {
    const before = await (await request.get('/api/sourcing/status')).json();
    for (const [method, url] of [
      ['GET', '/api/sourcing/fetches'],
      ['POST', '/api/sourcing/postings'],
      ['DELETE', '/api/sourcing/postings'],
      ['POST', '/api/sourcing/status'],
    ] as const) {
      const response = await request.fetch(url, {
        method,
        headers: { 'content-type': 'application/json' },
        data: '{}',
      });
      expect([404, 405], `${method} ${url}`).toContain(response.status());
    }

    expect(await (await request.get('/api/sourcing/status')).json()).toEqual(before);
  });
});

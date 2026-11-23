import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

// JF-174 (decision D4): Angular Material design foundation. The API is stubbed with page.route.

const jobsPage = {
  items: [
    {
      id: '1',
      title:
        'Senior .NET Developer with a deliberately long title that has to wrap on a phone screen',
      company: 'Acme',
      location: 'Worldwide',
      remotePolicy: 'Remote',
      url: 'https://example.com/1',
      postedAt: '2026-11-01T00:00:00Z',
    },
    {
      id: '2',
      title: 'Backend Engineer',
      company: 'Globex',
      location: null,
      remotePolicy: 'Hybrid',
      url: 'https://example.com/2',
      postedAt: '2026-11-02T00:00:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
};

const profile = {
  identity: { fullName: 'Ada Lovelace', headline: 'Senior .NET Engineer' },
  contact: { email: 'ada@example.com', phone: null, location: 'London, UK' },
  summary: 'Backend engineer.',
  skills: [
    { name: 'C#', proficiency: 'Expert', years: 10 },
    { name: 'PostgreSQL', proficiency: null, years: null },
  ],
  experience: [
    {
      role: 'Lead Engineer',
      company: 'Analytical Engines Ltd',
      startDate: '2019-03',
      endDate: null,
      isCurrent: true,
      achievements: ['Built the difference engine'],
    },
  ],
  education: [
    {
      institution: 'University of London',
      qualification: 'BSc',
      field: 'Mathematics',
      startDate: '2010',
      endDate: '2013',
    },
  ],
  certifications: [{ name: 'Azure Developer', issuer: 'Microsoft', year: 2022 }],
  links: [{ label: 'GitHub', url: 'https://github.com/ada' }],
  workAuthorisation: 'UK citizen',
  currentEmployer: 'Analytical Engines Ltd',
  preferences: {
    targetRoles: ['Staff Engineer'],
    locations: ['London'],
    remote: 'Hybrid',
    salaryMin: 90000,
    salaryMax: 110000,
    salaryCurrency: 'GBP',
    noticePeriod: '1 month',
  },
};

const cv = {
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
    createdAt: '2026-11-23T09:00:00Z',
    data: profile,
  },
};

async function stubJobs(page: Page, gate?: Promise<void>) {
  await page.route('**/api/jobs?**', async (route) => {
    await gate;
    await route.fulfill({ json: jobsPage });
  });
}

async function openJobs(page: Page) {
  await stubJobs(page);
  await page.goto('/jobs');
  await expect(page.getByTestId('job-list')).toContainText('Backend Engineer');
}

async function openCvReview(page: Page, gate?: Promise<void>) {
  await page.route('**/api/cv', async (route) => {
    await gate;
    await route.fulfill({ status: 201, json: cv });
  });
  await page.goto('/cv');
  await page
    .getByLabel('CV file')
    .setInputFiles({
      name: 'ada.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('%PDF-1.7 test'),
    });
  await page.getByRole('button', { name: 'Upload' }).click();
}

async function openCvEditor(page: Page) {
  await openCvReview(page);
  await page.getByRole('button', { name: 'Edit profile' }).click();
  await expect(page.getByTestId('profile-form')).toBeVisible();
}

function luminance(rgb: string): number {
  const [r, g, b] = (rgb.match(/[\d.]+/g) ?? []).slice(0, 3).map((v) => {
    const c = Number(v) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function scssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      return scssFiles(path);
    }
    return entry.name.endsWith('.scss') ? [path] : [];
  });
}

async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}

/** True when the focused element shows an outline or a Material strong focus indicator. */
async function focusIsVisible(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el || el === document.body) {
      return false;
    }
    const style = getComputedStyle(el);
    if (style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0) {
      return true;
    }
    // Material form fields show focus by thickening the outline to the primary colour.
    const field = el.closest('.mat-mdc-form-field.mat-focused');
    if (field) {
      const outline = field.querySelector('.mdc-notched-outline__leading');
      return !!outline && parseFloat(getComputedStyle(outline).borderTopWidth) >= 2;
    }
    const indicator =
      el.querySelector('.mat-focus-indicator') ?? el.closest('.mat-focus-indicator');
    if (indicator) {
      const before = getComputedStyle(indicator, '::before');
      return before.display !== 'none' && parseFloat(before.borderTopWidth) > 0;
    }
    return false;
  });
}

/** Tabs through the page and returns a description of every element that received focus. */
async function tabThrough(page: Page, steps: number): Promise<string[]> {
  const seen: string[] = [];
  for (let i = 0; i < steps; i++) {
    await page.keyboard.press('Tab');
    const id = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null;
      if (!el || el === document.body) {
        return '';
      }
      return el.getAttribute('data-e2e-id') ?? '';
    });
    if (id) {
      expect(await focusIsVisible(page), `visible focus on ${id}`).toBe(true);
      seen.push(id);
    }
  }
  return seen;
}

/** Tags every enabled, visible control so tab order can be compared against it. */
async function tagControls(page: Page, scope: string): Promise<string[]> {
  return page.evaluate((selector) => {
    const root = document.querySelector(selector)!;
    const controls = [
      ...root.querySelectorAll<HTMLElement>('a[href], button, input, select, textarea'),
    ].filter((el) => {
      const disabled = (el as HTMLButtonElement).disabled;
      const hidden = el.getAttribute('tabindex') === '-1';
      return !disabled && !hidden;
    });
    return controls.map((el, i) => {
      const id = `${el.tagName.toLowerCase()}-${i}-${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 24)}`;
      el.setAttribute('data-e2e-id', id);
      return id;
    });
  }, scope);
}

test.describe('D4 design foundation', () => {
  test('D4-AC1 defines one global Material 3 theme with tokens and a system font', async ({
    page,
  }) => {
    await openJobs(page);

    const theme = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      return {
        primary: root.getPropertyValue('--mat-sys-primary').trim(),
        tertiary: root.getPropertyValue('--mat-sys-tertiary').trim(),
        bodyFont: root.getPropertyValue('--mat-sys-body-medium-font').trim(),
        fontFamily: getComputedStyle(document.body).fontFamily,
      };
    });
    expect(theme.primary).not.toBe('');
    expect(theme.tertiary).not.toBe('');
    expect(theme.bodyFont).toContain('system-ui');
    expect(theme.fontFamily).toContain('system-ui');
  });

  test('D4-AC2 follows the OS colour scheme with a light and a dark variant', async ({ page }) => {
    await page.emulateMedia({ colorScheme: 'light' });
    await openJobs(page);
    const light = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    const lightText = await page.evaluate(() => getComputedStyle(document.body).color);

    await page.emulateMedia({ colorScheme: 'dark' });
    const dark = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    const darkText = await page.evaluate(() => getComputedStyle(document.body).color);

    expect(luminance(light)).toBeGreaterThan(0.8);
    expect(luminance(lightText)).toBeLessThan(0.1);
    expect(luminance(dark)).toBeLessThan(0.05);
    expect(luminance(darkText)).toBeGreaterThan(0.6);
  });

  test('D4-AC2 component and global styles use theme tokens, not hard-coded colours', () => {
    const src = join(__dirname, '..', 'src');
    const files = [...scssFiles(join(src, 'app')), join(src, 'styles.scss')];
    const offenders = files.filter((file) =>
      /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/.test(readFileSync(file, 'utf8')),
    );

    expect(files.length).toBeGreaterThan(5);
    expect(offenders).toEqual([]);
  });

  test('D4-AC3 shows a Material toolbar with the product name and highlights the active route', async ({
    page,
  }) => {
    await openJobs(page);
    const toolbar = page.locator('mat-toolbar');
    const nav = page.getByRole('navigation', { name: 'Main' });

    await expect(toolbar).toBeVisible();
    await expect(toolbar.getByRole('link', { name: 'JobsFinder' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Jobs' })).toHaveAttribute('aria-current', 'page');
    await expect(nav.getByRole('link', { name: 'CV' })).not.toHaveAttribute('aria-current', 'page');

    await nav.getByRole('link', { name: 'CV' }).click();
    await expect(page).toHaveURL(/\/cv$/);
    await expect(nav.getByRole('link', { name: 'CV' })).toHaveAttribute('aria-current', 'page');
    await expect(nav.getByRole('link', { name: 'CV' })).toHaveClass(/active/);
    await expect(nav.getByRole('link', { name: 'Jobs' })).not.toHaveAttribute(
      'aria-current',
      'page',
    );
  });

  test('D4-AC3 page content sits in a centred container of at most 960px', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await openJobs(page);

    const box = await page.locator('main.container').boundingBox();
    const viewport = await page.evaluate(() => document.documentElement.clientWidth);
    expect(box!.width).toBeLessThanOrEqual(960);
    expect(Math.abs(box!.x + box!.width / 2 - viewport / 2)).toBeLessThanOrEqual(1);
  });

  test('D4-AC4 the Jobs page uses Material form field, select, button, list, chip and progress bar', async ({
    page,
  }) => {
    let release!: () => void;
    await stubJobs(page, new Promise<void>((resolve) => (release = resolve)));
    await page.goto('/jobs');

    await expect(page.locator('mat-progress-bar')).toBeVisible();
    release();
    await expect(page.getByTestId('job-list')).toContainText('Backend Engineer');

    await expect(
      page
        .locator('mat-form-field')
        .filter({ has: page.getByRole('searchbox', { name: 'Search jobs' }) }),
    ).toBeVisible();
    await expect(
      page
        .locator('mat-form-field')
        .filter({ has: page.getByRole('combobox', { name: 'Remote policy' }) }),
    ).toBeVisible();
    await expect(page.getByRole('button', { name: 'Search' })).toHaveClass(/mat-mdc-button-base/);
    await expect(page.locator('mat-list[data-testid="job-list"] mat-list-item')).toHaveCount(2);
    await expect(page.locator('mat-chip').filter({ hasText: 'Hybrid' })).toBeVisible();
  });

  test('D4-AC4 the Jobs page shows API errors as an inline alert', async ({ page }) => {
    await page.route('**/api/jobs?**', (route) =>
      route.fulfill({
        status: 500,
        contentType: 'application/problem+json',
        json: { title: 'An unexpected error occurred.' },
      }),
    );
    await page.goto('/jobs');

    await expect(page.getByRole('alert')).toHaveClass(/inline-alert--error/);
  });

  test('D4-AC4 the CV page uses Material card, buttons, progress, chips, lists and form fields', async ({
    page,
  }) => {
    let release!: () => void;
    await openCvReview(page, new Promise<void>((resolve) => (release = resolve)));

    await expect(page.locator('mat-card').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Choose file' })).toHaveClass(
      /mat-mdc-button-base/,
    );
    await expect(page.getByTestId('cv-parsing')).toBeVisible();
    await expect(page.locator('mat-progress-bar')).toBeVisible();
    release();

    const review = page.getByTestId('cv-profile');
    await expect(review).toContainText('Review your profile');
    await expect(review.locator('mat-chip').filter({ hasText: 'C#' })).toBeVisible();
    await expect(
      review.locator('mat-list-item').filter({ hasText: 'Lead Engineer' }),
    ).toBeVisible();

    await page.getByRole('button', { name: 'Edit profile' }).click();
    const form = page.getByTestId('profile-form');
    await expect(
      form.locator('mat-form-field').filter({ has: page.getByLabel('Headline') }),
    ).toBeVisible();
    await expect(form.locator('mat-form-field select').first()).toBeVisible();
    await expect(form.locator('mat-checkbox')).toContainText('Current role');
    await expect(form.getByRole('button', { name: 'Save profile' })).toHaveClass(
      /mat-mdc-button-base/,
    );
  });

  test('D4-AC4 the CV page shows upload errors as an inline alert', async ({ page }) => {
    await page.route('**/api/cv', (route) =>
      route.fulfill({
        status: 422,
        contentType: 'application/problem+json',
        json: { title: 'No text found.', status: 422 },
      }),
    );
    await page.goto('/cv');
    await page
      .getByLabel('CV file')
      .setInputFiles({
        name: 'ada.pdf',
        mimeType: 'application/pdf',
        buffer: Buffer.from('%PDF-1.7 test'),
      });
    await page.getByRole('button', { name: 'Upload' }).click();

    await expect(page.getByRole('alert')).toHaveClass(/inline-alert--error/);
  });

  test.describe('at 375px', () => {
    test.use({ viewport: { width: 375, height: 812 } });

    test('D4-AC5 no page scrolls horizontally', async ({ page }) => {
      await openJobs(page);
      await expectNoHorizontalScroll(page);

      await page.goto('/cv');
      await expect(page.getByRole('heading', { name: 'Your CV' })).toBeVisible();
      await expectNoHorizontalScroll(page);

      await openCvEditor(page);
      await expectNoHorizontalScroll(page);
      await page.getByRole('button', { name: 'Cancel' }).click();
      await expect(page.getByTestId('cv-profile')).toContainText('Lead Engineer');
      await expectNoHorizontalScroll(page);
    });

    test('D4-AC5 form fields and buttons stack in a single column', async ({ page }) => {
      await openJobs(page);
      const search = await page
        .getByRole('searchbox', { name: 'Search jobs' })
        .locator('xpath=ancestor::mat-form-field')
        .boundingBox();
      const policy = await page
        .getByRole('combobox', { name: 'Remote policy' })
        .locator('xpath=ancestor::mat-form-field')
        .boundingBox();
      const button = await page.getByRole('button', { name: 'Search' }).boundingBox();
      expect(policy!.x).toBeCloseTo(search!.x, 0);
      expect(policy!.y).toBeGreaterThan(search!.y);
      expect(button!.width).toBeCloseTo(search!.width, 0);

      await openCvEditor(page);
      const fields = page
        .getByTestId('profile-form')
        .locator('fieldset')
        .first()
        .locator('mat-form-field');
      const first = await fields.nth(0).boundingBox();
      const second = await fields.nth(1).boundingBox();
      expect(second!.x).toBeCloseTo(first!.x, 0);
      expect(second!.y).toBeGreaterThan(first!.y);
      const save = await page.getByRole('button', { name: 'Save profile' }).boundingBox();
      const cancel = await page.getByRole('button', { name: 'Cancel' }).boundingBox();
      expect(cancel!.x).toBeCloseTo(save!.x, 0);
      expect(cancel!.y).toBeGreaterThan(save!.y);
    });
  });

  test('D4-AC6 every Jobs page control is reachable by keyboard with visible focus', async ({
    page,
  }) => {
    await openJobs(page);
    const controls = await tagControls(page, 'body');

    const seen = await tabThrough(page, controls.length + 2);

    expect(new Set(seen)).toEqual(new Set(controls));
  });

  test('D4-AC6 every CV edit form control is reachable by keyboard with visible focus', async ({
    page,
  }) => {
    await openCvEditor(page);
    await page.getByLabel('Headline').fill('Staff Engineer');
    const controls = await tagControls(page, '[data-testid="profile-form"]');
    await page.getByLabel('Full name').focus();
    await page.keyboard.press('Shift+Tab');

    const seen = await tabThrough(page, controls.length + 2);

    expect(new Set(seen)).toEqual(new Set(controls));
  });

  for (const colorScheme of ['light', 'dark'] as const) {
    test(`D4-AC6 meets WCAG 2.1 AA (including contrast) in the ${colorScheme} theme`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme });
      const scan = () =>
        new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();

      await openJobs(page);
      expect((await scan()).violations).toEqual([]);

      await openCvReview(page);
      await expect(page.getByTestId('cv-profile')).toContainText('Lead Engineer');
      expect((await scan()).violations).toEqual([]);

      await page.getByRole('button', { name: 'Edit profile' }).click();
      await expect(page.getByTestId('profile-form')).toBeVisible();
      expect((await scan()).violations).toEqual([]);
    });
  }
});

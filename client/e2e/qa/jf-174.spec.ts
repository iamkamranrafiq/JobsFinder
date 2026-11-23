import AxeBuilder from '@axe-core/playwright';
import { expect, Locator, Page, request as pwRequest, test } from '@playwright/test';
import { readFileSync } from 'node:fs';

// QA tests for JF-174 (Material foundation). Real stack, no API stubs.

function textPdf(lines: string[]): Buffer {
  const content = lines.map((l, i) => `BT /F1 12 Tf 40 ${780 - i * 20} Td (${l}) Tj ET`).join('\n');
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${content.length} >>\nstream\n${content}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((o, i) => {
    offsets.push(pdf.length);
    pdf += `${i + 1} 0 obj\n${o}\nendobj\n`;
  });
  const xref = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  offsets.forEach((o) => (pdf += `${String(o).padStart(10, '0')} 00000 n \n`));
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, 'latin1');
}

async function uploadCv(page: Page) {
  await page.goto('/cv');
  await page.getByLabel('CV file').setInputFiles({
    name: `qa174-${Date.now()}.pdf`,
    mimeType: 'application/pdf',
    buffer: textPdf([
      'Ada Lovelace',
      'Senior Engineer London',
      'ada@example.com',
      'Skills: C and Angular',
    ]),
  });
  await page.getByRole('button', { name: 'Upload' }).click();
  await expect(page.getByRole('heading', { name: 'Review your profile' })).toBeVisible({
    timeout: 60_000,
  });
}

const noHScroll = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);

const axe = (page: Page) =>
  new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();

const SHOT_DIR = process.env['QA_SHOTS'];
async function shot(page: Page, name: string) {
  if (SHOT_DIR) await page.screenshot({ path: `${SHOT_DIR}/${name}.png`, fullPage: true });
}

/**
 * True only when a focus ring is really drawn on the element. Strong focus indicators keep a
 * ::before border on every Material control; the ring exists only when ::before has `content`.
 * Self-contained so it can run in the page.
 */
const focusRingDrawn = (el: Element): boolean => {
  const transparent = (c: string) => c === 'transparent' || /rgba\(.*,\s*0\)$/.test(c);
  const style = getComputedStyle(el);
  if (
    style.outlineStyle !== 'none' &&
    parseFloat(style.outlineWidth) > 0 &&
    !transparent(style.outlineColor)
  ) {
    return true;
  }
  const field = el.closest('.mat-mdc-form-field.mat-focused');
  if (field) {
    const o = field.querySelector('.mdc-notched-outline__leading');
    return !!o && parseFloat(getComputedStyle(o).borderTopWidth) >= 2;
  }
  const ind = el.matches('.mat-focus-indicator')
    ? el
    : (el.querySelector('.mat-focus-indicator') ?? el.closest('.mat-focus-indicator'));
  if (ind) {
    const b = getComputedStyle(ind, '::before');
    return (
      b.display !== 'none' &&
      b.content !== 'none' &&
      b.content !== 'normal' &&
      parseFloat(b.borderTopWidth) > 0 &&
      !transparent(b.borderTopColor)
    );
  }
  return false;
};

/** Problems that mean list-item content is clipped, truncated or overlapping (D4-AC5). */
const listClipping = (scope: string): string[] => {
  const problems: string[] = [];
  const items = [...document.querySelectorAll<HTMLElement>(`${scope} .mat-mdc-list-item`)];
  if (items.length === 0) return ['no list items found'];
  let prevBottom = -Infinity;
  items.forEach((item, idx) => {
    const r = item.getBoundingClientRect();
    const label = `item${idx}`;
    if (r.top < prevBottom - 1) problems.push(`${label} overlaps previous item`);
    prevBottom = r.bottom;
    for (const el of item.querySelectorAll<HTMLElement>('*')) {
      if (!el.textContent?.trim()) continue;
      const e = el.getBoundingClientRect();
      if (e.width === 0 && e.height === 0) continue;
      const d = `${label} <${el.tagName.toLowerCase()}.${el.className}>`;
      if (e.bottom > r.bottom + 1 || e.top < r.top - 1 || e.right > r.right + 1) {
        problems.push(`${d} outside item box`);
      }
      const cs = getComputedStyle(el);
      if (cs.overflowY !== 'visible' && el.scrollHeight > el.clientHeight + 1) {
        problems.push(`${d} clips vertically`);
      }
      if (cs.textOverflow === 'ellipsis' && el.scrollWidth > el.clientWidth + 1) {
        problems.push(`${d} ellipsised`);
      }
      if (cs.overflowX !== 'visible' && el.scrollWidth > el.clientWidth + 1) {
        problems.push(`${d} clips horizontally`);
      }
    }
    if (
      item.scrollHeight > item.clientHeight + 1 &&
      getComputedStyle(item).overflowY !== 'visible'
    ) {
      problems.push(`${label} item itself clips`);
    }
  });
  return problems;
};

const LONG_TITLE =
  'Principal Platform Engineer for distributed event-driven payments infrastructure and developer tooling';

/** Creates three QA-marked jobs through the real API (once; later runs reuse them). */
async function seedQaJobs() {
  const api = await pwRequest.newContext({ baseURL: 'http://localhost:4200' });
  const existing = await (await api.get('/api/jobs?text=QA174&pageSize=50')).json();
  const stamp = Date.now();
  const defs = [
    {
      title: `QA174 ${LONG_TITLE}`,
      company: 'QA174 Acme Global Holdings International',
      location: 'Remote within the European Union',
      remotePolicy: 2,
    },
    {
      title: 'QA174 Senior .NET Developer (C#, ASP.NET Core, PostgreSQL, Azure)',
      company: 'QA174 Initech',
      location: 'London, United Kingdom',
      remotePolicy: 0,
    },
    {
      title: 'QA174 Staff Frontend Engineer',
      company: 'QA174 Globex Corporation Worldwide Ltd',
      location: null,
      remotePolicy: 1,
    },
  ];
  if (existing.totalCount < defs.length) {
    for (const [i, d] of defs.entries()) {
      const res = await api.post('/api/jobs', {
        data: {
          ...d,
          url: `https://example.com/qa174/${stamp}/${i}`,
          postedAt: '2026-11-01T00:00:00Z',
        },
      });
      expect(res.status()).toBe(201);
    }
  }
  await api.dispose();
}

async function openQaJobs(page: Page, width: number) {
  await page.setViewportSize({ width, height: 900 });
  await page.goto('/jobs');
  await expect(page.getByRole('heading', { name: 'Jobs' })).toBeVisible();
  await page.getByLabel('Search jobs').fill('QA174');
  await page.getByRole('button', { name: 'Search' }).click();
  await expect(page.getByTestId('job-list').getByRole('link').nth(2)).toBeVisible();
  await expect(page.getByTestId('job-list').getByRole('link', { name: /Staff Frontend/ })).toBeVisible();
}

/** Adds experience, education and a link through the real edit form, then saves. */
async function fillAndSaveProfile(page: Page) {
  await page.getByRole('button', { name: 'Edit profile' }).click();
  await expect(page.getByLabel('Headline')).toBeVisible();
  await page.getByRole('button', { name: 'Add role' }).click();
  await page.getByLabel('Role 1').fill(LONG_TITLE);
  await page.getByLabel('Company').first().fill('Analytical Engines International Holdings Ltd');
  await page.getByLabel('Start (YYYY or YYYY-MM)').first().fill('2019-01');
  await page
    .getByLabel('Achievements (one per line)')
    .first()
    .fill(
      'Designed and built the difference engine control plane used by every downstream team\nCut deployment time from hours to minutes\nMentored eight engineers',
    );
  await page.getByRole('button', { name: 'Add education' }).click();
  await page
    .getByLabel('Institution 1')
    .fill('University of London, Royal Holloway College of Mathematics and Natural Philosophy');
  await page.getByLabel('Qualification').fill('Bachelor of Science in Mathematics');
  await page.getByRole('button', { name: 'Add link' }).click();
  await page.getByLabel('Link 1 label').fill('Personal portfolio and engineering blog');
  await page
    .getByLabel('Link 1 URL')
    .fill('https://example.com/a/very/long/path/to/a/portfolio/page');
  await page.getByRole('button', { name: 'Save profile' }).click();
  await expect(page.getByTestId('profile-saved')).toBeVisible({ timeout: 30_000 });
}

const height = async (l: Locator) => (await l.boundingBox())!.height;

test.describe('JF-174 design foundation (real stack)', () => {
  test.describe.configure({ mode: 'serial' });

  test('JF-174-TC01 material and cdk are v21', () => {
    const pkg = JSON.parse(readFileSync('package.json', 'utf8'));
    expect(pkg.dependencies['@angular/material']).toMatch(/\^?21\./);
    expect(pkg.dependencies['@angular/cdk']).toMatch(/\^?21\./);
  });

  test('JF-174-TC12 no other component kits', () => {
    const pkg = JSON.parse(readFileSync('package.json', 'utf8'));
    const all = Object.keys({ ...pkg.dependencies, ...pkg.devDependencies }).join(' ');
    expect(all).not.toMatch(/bootstrap|tailwind|ng-zorro|primeng/i);
  });

  for (const scheme of ['light', 'dark'] as const) {
    test(`JF-174-TC0${scheme === 'light' ? 2 : 3} ${scheme} OS scheme drives theme`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await page.goto('/cv');
      const lum = await page.evaluate(() => {
        const c = getComputedStyle(document.body)
          .backgroundColor.match(/[\d.]+/g)!
          .map(Number);
        return (c[0] + c[1] + c[2]) / 3;
      });
      if (scheme === 'light') expect(lum).toBeGreaterThan(200);
      else expect(lum).toBeLessThan(60);
    });
  }

  test('JF-174-TC04 toolbar, product name, nav, active route', async ({ page }) => {
    await page.goto('/cv');
    await expect(page.locator('mat-toolbar')).toContainText('JobsFinder');
    const nav = page.getByRole('navigation', { name: 'Main' });
    await expect(nav.getByRole('link', { name: 'CV' })).toHaveAttribute('aria-current', 'page');
    await expect(nav.getByRole('link', { name: 'Jobs' })).not.toHaveAttribute(
      'aria-current',
      'page',
    );
    await nav.getByRole('link', { name: 'Jobs' }).click();
    await expect(page).toHaveURL(/\/jobs/);
    await expect(nav.getByRole('link', { name: 'Jobs' })).toHaveAttribute('aria-current', 'page');
    await expect(nav.getByRole('link', { name: 'CV' })).not.toHaveAttribute('aria-current', 'page');
  });

  test('JF-174-TC05 content container max 960px and centred', async ({ page }) => {
    await page.setViewportSize({ width: 1600, height: 900 });
    await page.goto('/jobs');
    const box = (await page.locator('main').boundingBox())!;
    expect(box.width).toBeLessThanOrEqual(961);
    expect(Math.abs(box.x - (1600 - box.width - box.x))).toBeLessThan(20);
  });

  test('JF-174-TC06 CV flow uses material and keeps selectors', async ({ page }) => {
    await uploadCv(page);
    await expect(page.locator('mat-card').first()).toBeVisible();
    await expect(page.getByTestId('cv-profile')).toBeVisible();
    await page.getByRole('button', { name: 'Edit profile' }).click();
    await expect(page.locator('mat-form-field').first()).toBeVisible();
    await expect(page.getByLabel('Headline')).toBeVisible();
    await expect(page.getByLabel('Current employer')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save profile' })).toBeVisible();
    await expect(page.locator('.mat-mdc-button-base').first()).toBeVisible();
  });

  test('JF-174-TC07 invalid file shows an alert', async ({ page }) => {
    await page.goto('/cv');
    await page.getByLabel('CV file').setInputFiles({
      name: 'bad.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('not a pdf at all'),
    });
    await page.getByRole('button', { name: 'Upload' }).click();
    await expect(page.getByRole('alert').first()).toBeVisible({ timeout: 30_000 });
  });

  test('JF-174-TC08 jobs page uses material', async ({ page }) => {
    await page.goto('/jobs');
    await expect(
      page.locator('mat-card, mat-form-field, .mat-mdc-button-base').first(),
    ).toBeVisible();
  });

  test('JF-174-TC09 375px no horizontal scroll on cv, edit form, jobs', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/jobs');
    // Wait for the page to render, otherwise the check passes trivially on an empty document.
    await expect(page.getByRole('heading', { name: 'Jobs' })).toBeVisible();
    await expect(page.locator('mat-form-field').first()).toBeVisible();
    expect(await noHScroll(page)).toBe(true);
    await uploadCv(page);
    expect(await noHScroll(page)).toBe(true);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    await expect(page.getByLabel('Headline')).toBeVisible();
    expect(await noHScroll(page)).toBe(true);
    const a = (await page.getByLabel('Headline').boundingBox())!;
    const b = (await page.getByLabel('Current employer').boundingBox())!;
    expect(Math.abs(a.x - b.x)).toBeLessThan(5); // stacked single column
  });

  test('JF-174-TC10 focus ring is really drawn on tab stops, and not on unfocused controls', async ({
    page,
  }) => {
    await page.goto('/cv');
    await expect(page.getByRole('heading', { name: 'Your CV' })).toBeVisible();
    await expect(page.locator('mat-toolbar')).toContainText('JobsFinder');

    // Negative control: unfocused controls must report NO ring, so the check can fail.
    expect(await page.getByRole('button', { name: 'Choose file' }).evaluate(focusRingDrawn)).toBe(
      false,
    );
    const jobsLink = page
      .getByRole('navigation', { name: 'Main' })
      .getByRole('link', { name: 'Jobs' });
    expect(await jobsLink.evaluate(focusRingDrawn)).toBe(false);

    const seen: string[] = [];
    for (let i = 0; i < 4; i++) {
      await page.keyboard.press('Tab');
      const focused = page.locator(':focus');
      const desc = await focused.evaluate(
        (el) => `${el.tagName}:${(el.textContent ?? '').trim().slice(0, 20)}`,
      );
      expect(desc, `tab stop ${i + 1} must be a real control`).not.toMatch(/^BODY/);
      seen.push(desc);
      expect(await focused.evaluate(focusRingDrawn), `focus ring drawn on ${desc}`).toBe(true);
    }
    expect(
      new Set(seen).size,
      `tab moved through distinct controls: ${seen}`,
    ).toBeGreaterThanOrEqual(3);
  });

  for (const scheme of ['light', 'dark'] as const) {
    test(`JF-174-TC11 axe contrast ${scheme}`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      for (const url of ['/cv', '/jobs']) {
        await page.goto(url);
        await page.waitForLoadState('networkidle');
        const r = await axe(page);
        expect(
          r.violations.map((v) => `${url} ${v.id}: ${v.nodes.map((n) => n.target).join(',')}`),
        ).toEqual([]);
      }
    });
  }

  test('JF-174-TC11b axe on profile view and edit form, both themes', async ({ page }) => {
    await uploadCv(page);
    for (const scheme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: scheme });
      const r = await axe(page);
      expect(
        r.violations.map(
          (v) => `${scheme} view ${v.id}: ${v.nodes.map((n) => n.target).join(',')}`,
        ),
      ).toEqual([]);
    }
    await page.getByRole('button', { name: 'Edit profile' }).click();
    for (const scheme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: scheme });
      const r = await axe(page);
      expect(
        r.violations.map(
          (v) => `${scheme} edit ${v.id}: ${v.nodes.map((n) => n.target).join(',')}`,
        ),
      ).toEqual([]);
    }
  });

  test.describe('visual-defect regressions Q1-Q4 and list clipping', () => {
    test.beforeAll(async () => {
      await seedQaJobs();
    });

    for (const width of [375, 1000]) {
      for (const scheme of ['light', 'dark'] as const) {
        test(`JF-174-TC14 Q1 jobs filter fields have normal height, ${width}px ${scheme}`, async ({
          page,
        }) => {
          await page.emulateMedia({ colorScheme: scheme });
          await openQaJobs(page, width);
          for (const f of await page.locator('.filters mat-form-field').all()) {
            expect(await height(f), 'form field height').toBeLessThan(80);
            expect(await height(f)).toBeGreaterThan(40);
          }
          expect(await height(page.getByRole('button', { name: 'Search' }))).toBeLessThan(60);
          expect(await noHScroll(page)).toBe(true);
          await shot(page, `jobs-${scheme}-${width}`);
        });

        test(`JF-174-TC15 clipping: jobs list rows grow with content, ${width}px ${scheme}`, async ({
          page,
        }) => {
          await page.emulateMedia({ colorScheme: scheme });
          await openQaJobs(page, width);
          expect(await page.evaluate(listClipping, '[data-testid=job-list]')).toEqual([]);
          const title = page.getByRole('link', { name: `QA174 ${LONG_TITLE}`, exact: true });
          await expect(title).toHaveText(new RegExp(LONG_TITLE.slice(-20)));
          const row = page.locator('[data-testid=job-list] .mat-mdc-list-item', { has: title });
          expect(await height(row)).toBeGreaterThan(60);
        });
      }
    }

    test('JF-174-TC15 negative control: clipping check detects clipped rows', async ({ page }) => {
      await openQaJobs(page, 375);
      await page.addStyleTag({
        content: '.mat-mdc-list-item{height:48px!important;overflow:hidden!important}',
      });
      expect((await page.evaluate(listClipping, '[data-testid=job-list]')).length).toBeGreaterThan(
        0,
      );
    });

    for (const width of [375, 1000]) {
      for (const scheme of ['light', 'dark'] as const) {
        test(`JF-174-TC16 Q2 upload card is compact, ${width}px ${scheme}`, async ({ page }) => {
          await page.emulateMedia({ colorScheme: scheme });
          await page.setViewportSize({ width, height: 900 });
          await page.goto('/cv');
          await expect(page.getByRole('heading', { name: 'Your CV' })).toBeVisible();
          await page.getByLabel('CV file').setInputFiles({
            name: 'qa174-a-reasonably-long-cv-file-name.pdf',
            mimeType: 'application/pdf',
            buffer: textPdf(['Ada Lovelace']),
          });
          const choose = page.getByRole('button', { name: 'Choose file' });
          const name = page.getByTestId('cv-file-name');
          const upload = page.getByRole('button', { name: 'Upload' });
          await expect(name).toContainText('qa174-a-reasonably');
          const c = (await choose.boundingBox())!;
          const n = (await name.boundingBox())!;
          const u = (await upload.boundingBox())!;
          expect(await height(upload)).toBeLessThan(60);
          expect(await height(choose)).toBeLessThan(60);
          if (width === 375) {
            expect(n.y - (c.y + c.height), 'gap choose->name').toBeLessThan(40);
            expect(u.y - (n.y + n.height), 'gap name->upload').toBeLessThan(40);
          }
          expect(await noHScroll(page)).toBe(true);
          await shot(page, `cv-upload-${scheme}-${width}`);
        });
      }
    }

    for (const width of [375, 1000]) {
      for (const scheme of ['light', 'dark'] as const) {
        test(`JF-174-TC17 Q3/Q4 edit form hint clearance and floated labels, ${width}px ${scheme}`, async ({
          page,
        }) => {
          await page.emulateMedia({ colorScheme: scheme });
          await page.setViewportSize({ width, height: 900 });
          await uploadCv(page);
          await page.getByRole('button', { name: 'Edit profile' }).click();
          await expect(page.getByLabel('Headline')).toBeVisible();
          const hint = page.getByTestId('current-employer-hint');
          await expect(hint).toBeVisible();
          const h = (await hint.boundingBox())!;
          const next = (await page
            .getByLabel('Work authorisation')
            .locator('xpath=ancestor::mat-form-field')
            .boundingBox())!;
          const field = (await page
            .getByTestId('current-employer-input')
            .locator('xpath=ancestor::mat-form-field')
            .boundingBox())!;
          expect(h.y, 'hint is below its field input').toBeGreaterThan(field.y);
          expect(next.y - (h.y + h.height), 'gap hint -> next field').toBeGreaterThanOrEqual(4);
          const unfloated = await page.evaluate(() =>
            [...document.querySelectorAll('[data-testid=profile-form] mat-form-field')]
              .filter((f) => !f.querySelector('.mdc-floating-label--float-above'))
              .map((f) => f.querySelector('mat-label')?.textContent?.trim()),
          );
          expect(unfloated, 'fields whose label is not floated').toEqual([]);
          expect(await noHScroll(page)).toBe(true);
          await shot(page, `cv-edit-${scheme}-${width}`);
        });
      }
    }

    for (const width of [375, 1000]) {
      for (const scheme of ['light', 'dark'] as const) {
        test(`JF-174-TC18 clipping: profile view rows with content, ${width}px ${scheme}`, async ({
          page,
        }) => {
          await page.emulateMedia({ colorScheme: scheme });
          await page.setViewportSize({ width, height: 900 });
          await uploadCv(page);
          await fillAndSaveProfile(page);
          await expect(page.getByTestId('cv-profile').getByText(LONG_TITLE).first()).toBeVisible();
          expect(await page.evaluate(listClipping, '[data-testid=cv-profile]')).toEqual([]);
          expect(await noHScroll(page)).toBe(true);
          await shot(page, `cv-view-${scheme}-${width}`);
          await page.getByRole('button', { name: 'Edit profile' }).click();
          await expect(page.getByLabel('Headline')).toBeVisible();
          await shot(page, `cv-edit-filled-${scheme}-${width}`);
        });
      }
    }
  });
});

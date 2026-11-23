import AxeBuilder from '@axe-core/playwright';
import { expect, Page, test } from '@playwright/test';
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
    buffer: textPdf(['Ada Lovelace', 'Senior Engineer London', 'ada@example.com', 'Skills: C and Angular']),
  });
  await page.getByRole('button', { name: 'Upload' }).click();
  await expect(page.getByRole('heading', { name: 'Review your profile' })).toBeVisible({ timeout: 60_000 });
}

const noHScroll = (page: Page) =>
  page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth);

const axe = (page: Page) =>
  new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();

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
    test(`JF-174-TC0${scheme === 'light' ? 2 : 3} ${scheme} OS scheme drives theme`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await page.goto('/cv');
      const lum = await page.evaluate(() => {
        const c = getComputedStyle(document.body).backgroundColor.match(/[\d.]+/g)!.map(Number);
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
    await expect(nav.getByRole('link', { name: 'Jobs' })).not.toHaveAttribute('aria-current', 'page');
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
    await expect(page.locator('mat-card, mat-form-field, .mat-mdc-button-base').first()).toBeVisible();
  });

  test('JF-174-TC09 375px no horizontal scroll on cv, edit form, jobs', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/jobs');
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

  test('JF-174-TC10 keyboard focus is visible on first tab stops', async ({ page }) => {
    await page.goto('/cv');
    for (let i = 0; i < 4; i++) {
      await page.keyboard.press('Tab');
      const info = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement;
        if (!el || el === document.body) return null;
        const cs = getComputedStyle(el);
        const ind = el.querySelector('.mat-focus-indicator') ?? el;
        const before = getComputedStyle(ind, '::before');
        return {
          outline: cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0,
          ring: before.borderTopStyle !== 'none' && parseFloat(before.borderTopWidth) > 0,
          shadow: cs.boxShadow !== 'none',
        };
      });
      expect(info).not.toBeNull();
      expect(info!.outline || info!.ring || info!.shadow).toBe(true);
    }
  });

  for (const scheme of ['light', 'dark'] as const) {
    test(`JF-174-TC11 axe contrast ${scheme}`, async ({ page }) => {
      await page.emulateMedia({ colorScheme: scheme });
      for (const url of ['/cv', '/jobs']) {
        await page.goto(url);
        await page.waitForLoadState('networkidle');
        const r = await axe(page);
        expect(r.violations.map((v) => `${url} ${v.id}: ${v.nodes.map((n) => n.target).join(',')}`)).toEqual([]);
      }
    });
  }

  test('JF-174-TC11b axe on profile view and edit form, both themes', async ({ page }) => {
    await uploadCv(page);
    for (const scheme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: scheme });
      const r = await axe(page);
      expect(r.violations.map((v) => `${scheme} view ${v.id}: ${v.nodes.map((n) => n.target).join(',')}`)).toEqual([]);
    }
    await page.getByRole('button', { name: 'Edit profile' }).click();
    for (const scheme of ['light', 'dark'] as const) {
      await page.emulateMedia({ colorScheme: scheme });
      const r = await axe(page);
      expect(r.violations.map((v) => `${scheme} edit ${v.id}: ${v.nodes.map((n) => n.target).join(',')}`)).toEqual([]);
    }
  });
});

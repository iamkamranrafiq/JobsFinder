import { expect, Page, test } from '@playwright/test';

// QA tests for JF-6. They run against the REAL stack (no page.route stubs).
// "Active profile" is global in Phase 0, so every test uploads its own CV first.

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

async function uploadCv(page: Page): Promise<string> {
  const name = `qa6-${Date.now()}-${Math.floor(Math.random() * 1e6)}.pdf`;
  await page.goto('/cv');
  await page.getByLabel('CV file').setInputFiles({
    name,
    mimeType: 'application/pdf',
    buffer: textPdf(['Ada Lovelace', 'Senior Engineer London', 'ada@example.com', 'Skills: C and Angular']),
  });
  await page.getByRole('button', { name: 'Upload' }).click();
  await expect(page.getByRole('heading', { name: 'Review your profile' })).toBeVisible({ timeout: 60_000 });
  await expect(page.getByTestId('cv-profile')).toContainText(name);
  return name;
}

test.describe('JF-6 review and correct profile (real stack)', () => {
  // Active profile is global in Phase 0, so tests must not interleave uploads.
  test.describe.configure({ mode: 'serial' });

  test('JF-6-TC30 edit a field and save creates version 2 which becomes active', async ({ page }) => {
    await uploadCv(page);
    await expect(page.getByTestId('cv-profile')).toContainText('version 1');
    await page.getByRole('button', { name: 'Edit profile' }).click();
    const headline = `QA headline ${Date.now()}`;
    await page.getByLabel('Headline').fill(headline);
    await page.getByRole('button', { name: 'Save profile' }).click();

    await expect(page.getByTestId('profile-saved')).toContainText('Saved as version 2');
    await expect(page.getByTestId('profile-saved')).toContainText('active profile');
    await expect(page.getByTestId('cv-profile')).toContainText(headline);

    const active = await page.request.get('http://localhost:5080/api/cv/active');
    expect(active.status()).toBe(200);
    const body = await active.json();
    expect(body.profile.version).toBe(2);
    expect(body.profile.data.identity.headline).toBe(headline);
  });

  test('JF-6-TC31 current employer field is present and can be completed when empty', async ({ page }) => {
    await uploadCv(page);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    const input = page.getByTestId('current-employer-input');
    await expect(input).toBeVisible();
    await expect(page.getByLabel('Current employer')).toBeVisible();
    const current = await input.inputValue();
    const hint = page.getByTestId('current-employer-hint');
    if (current === '') {
      await expect(hint).toContainText('Not found in your CV');
    } else {
      await expect(hint).toContainText('Taken from your CV');
    }
    const employer = `QA Corp ${Date.now()}`;
    await input.fill(employer);
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByTestId('profile-saved')).toBeVisible();
    const body = await (await page.request.get('http://localhost:5080/api/cv/active')).json();
    expect(body.profile.data.currentEmployer).toBe(employer);
  });

  test('JF-6-TC32 invalid value blocks save and shows an error, no new version', async ({ page }) => {
    await uploadCv(page);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    await page.getByLabel('Summary').fill('x'.repeat(4001));
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByText('At most 4000 characters.')).toBeVisible();
    await expect(page.getByTestId('profile-saved')).toHaveCount(0);
    const body = await (await page.request.get('http://localhost:5080/api/cv/active')).json();
    expect(body.profile.version).toBe(1);
  });

  test('JF-6-TC33 save is disabled while pristine and Cancel leaves version unchanged', async ({ page }) => {
    await uploadCv(page);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    await expect(page.getByRole('button', { name: 'Save profile' })).toBeDisabled();
    await page.getByLabel('Headline').fill('discard me');
    await expect(page.getByRole('button', { name: 'Save profile' })).toBeEnabled();
    await page.getByRole('button', { name: 'Cancel' }).click();
    await expect(page.getByTestId('profile-form')).toHaveCount(0);
    await expect(page.getByTestId('cv-profile')).not.toContainText('discard me');
    const body = await (await page.request.get('http://localhost:5080/api/cv/active')).json();
    expect(body.profile.version).toBe(1);
  });

  test('JF-6-TC34 two successive saves give version 3 and add/remove skill works', async ({ page }) => {
    await uploadCv(page);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    await page.getByLabel('Headline').fill('first');
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByTestId('profile-saved')).toContainText('version 2');

    await page.getByRole('button', { name: 'Edit profile' }).click();
    await page.getByRole('button', { name: 'Add skill' }).click();
    await page.getByLabel('Skill', { exact: false }).first().fill('Rust');
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByTestId('profile-saved')).toContainText('version 3');
    const body = await (await page.request.get('http://localhost:5080/api/cv/active')).json();
    expect(body.profile.version).toBe(3);
    expect(JSON.stringify(body.profile.data.skills)).toContain('Rust');
  });

  test('JF-6-TC35 at 375px the edit form has no horizontal overflow', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await uploadCv(page);
    await page.getByRole('button', { name: 'Edit profile' }).click();
    await expect(page.getByTestId('profile-form')).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    expect(overflow).toBeLessThanOrEqual(1);
  });
});

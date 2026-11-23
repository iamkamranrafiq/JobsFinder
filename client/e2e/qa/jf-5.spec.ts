import { expect, test } from '@playwright/test';

// QA tests for JF-5. They run against the REAL stack (no page.route stubs).

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

test.describe('JF-5 CV upload (real stack)', () => {
  test('JF-5-TC21 upload a text PDF shows the review profile and a download link', async ({ page }) => {
    await page.goto('/cv');
    await expect(page.getByRole('heading', { name: 'Your CV' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Upload' })).toBeDisabled();

    await page.getByLabel('CV file').setInputFiles({
      name: 'qa-cv.pdf',
      mimeType: 'application/pdf',
      buffer: textPdf(['Ada Lovelace', 'Senior .NET Engineer - London, UK', 'ada@example.com 442079460000', 'Skills: C and ASP.NET Core, Angular, PostgreSQL']),
    });
    await expect(page.getByRole('button', { name: 'Upload' })).toBeEnabled();
    await page.getByRole('button', { name: 'Upload' }).click();

    await expect(page.getByRole('heading', { name: 'Review your profile' })).toBeVisible({ timeout: 60_000 });
    await expect(page.getByTestId('cv-profile')).toContainText('qa-cv.pdf');
    const link = page.getByRole('link', { name: 'Download original' });
    await expect(link).toBeVisible();
    const href = await link.getAttribute('href');
    const res = await page.request.get(href!);
    expect(res.status()).toBe(200);
    expect(res.headers()['content-type']).toContain('application/pdf');
  });

  test('JF-5-TC22 an unreadable file with a .pdf name is rejected with a specific reason', async ({ page }) => {
    await page.goto('/cv');
    await page.getByLabel('CV file').setInputFiles({
      name: 'fake.pdf',
      mimeType: 'application/pdf',
      buffer: Buffer.from('this is not a pdf at all, just text'),
    });
    await page.getByRole('button', { name: 'Upload' }).click();
    const alert = page.getByRole('alert');
    await expect(alert).toBeVisible({ timeout: 30_000 });
    await expect(alert).toContainText(/PDF or DOCX/i);
    await expect(page.getByTestId('cv-profile')).toHaveCount(0);
  });

  test('JF-5-TC22b a wrong extension is rejected client-side with a reason', async ({ page }) => {
    await page.goto('/cv');
    await page.getByLabel('CV file').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('hello'),
    });
    await page.getByRole('button', { name: 'Upload' }).click();
    await expect(page.getByRole('alert')).toContainText('Only PDF or DOCX files are accepted.');
  });

  test('JF-5-TC22c a file over 5 MB through the real proxy gets the too-large reason', async ({ page }) => {
    await page.goto('/cv');
    const big = Buffer.alloc(5 * 1024 * 1024 + 1024, 0x20);
    big.write('%PDF-1.4\n');
    await page.getByLabel('CV file').setInputFiles({ name: 'big.pdf', mimeType: 'application/pdf', buffer: big });
    await page.getByRole('button', { name: 'Upload' }).click();
    await expect(page.getByRole('alert')).toContainText('larger than 5 MB', { timeout: 30_000 });
  });

  test('JF-5-TC23 mobile width has no horizontal overflow', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 800 });
    await page.goto('/cv');
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflow).toBe(false);
  });
});

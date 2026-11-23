import { expect, test, type Page } from '@playwright/test';

function cv(format: 'Pdf' | 'Docx', fileName: string) {
  return {
    document: {
      id: 'doc-1',
      fileName,
      contentType: format === 'Pdf' ? 'application/pdf' : 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      format,
      sizeBytes: 1234,
      uploadedAt: '2026-11-22T12:00:00Z',
    },
    profile: {
      id: 'p-1',
      cvDocumentId: 'doc-1',
      version: 1,
      createdAt: '2026-11-22T12:00:00Z',
      data: {
        identity: { fullName: 'Ada Lovelace', headline: 'Senior .NET Engineer' },
        contact: { email: 'ada@example.com', phone: null, location: 'London, UK' },
        summary: 'Backend engineer.',
        skills: [{ name: 'C#', proficiency: 'Expert', years: 10 }],
        experience: [
          {
            role: 'Senior Engineer',
            company: 'Analytical Engines Ltd',
            startDate: '2019-03',
            endDate: null,
            isCurrent: true,
            achievements: ['Cut build times by 40%'],
          },
        ],
        education: [],
        certifications: [],
        links: [{ label: 'GitHub', url: 'https://github.com/ada' }],
        workAuthorisation: null,
        currentEmployer: 'Analytical Engines Ltd',
        preferences: {
          targetRoles: [],
          locations: [],
          remote: null,
          salaryMin: null,
          salaryMax: null,
          salaryCurrency: null,
          noticePeriod: null,
        },
      },
    },
  };
}

async function chooseAndUpload(page: Page, name: string, mimeType: string) {
  await page.getByLabel('CV file').setInputFiles({ name, mimeType, buffer: Buffer.from('%PDF-1.7 test') });
  await page.getByRole('button', { name: 'Upload' }).click();
}

test('F0.1-AC1 uploads a PDF and shows the parsed profile for review', async ({ page }) => {
  let posted = false;
  await page.route('**/api/cv', async (route) => {
    posted = route.request().method() === 'POST';
    await route.fulfill({ status: 201, json: cv('Pdf', 'ada.pdf') });
  });

  await page.goto('/cv');
  await chooseAndUpload(page, 'ada.pdf', 'application/pdf');

  const review = page.getByTestId('cv-profile');
  await expect(review).toBeVisible();
  await expect(page.getByTestId('profile-name')).toHaveText('Ada Lovelace');
  await expect(page.getByTestId('profile-current-employer')).toHaveText('Analytical Engines Ltd');
  await expect(review).toContainText('Senior Engineer');
  expect(posted).toBe(true);
});

test('F0.1-AC2 uploads a DOCX and shows the parsed profile', async ({ page }) => {
  await page.route('**/api/cv', (route) => route.fulfill({ status: 201, json: cv('Docx', 'ada.docx') }));

  await page.goto('/cv');
  await chooseAndUpload(page, 'ada.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document');

  await expect(page.getByTestId('cv-profile')).toContainText('Parsed from ada.docx');
  await expect(page.getByTestId('profile-name')).toHaveText('Ada Lovelace');
});

test('F0.1-AC3 shows the specific rejection reason from the API', async ({ page }) => {
  const title = 'No text could be found in this file. It looks like a scanned or image-only document; upload a text-based PDF or DOCX.';
  await page.route('**/api/cv', (route) =>
    route.fulfill({ status: 422, contentType: 'application/problem+json', json: { title, status: 422, code: 'cv_no_text' } }),
  );

  await page.goto('/cv');
  await chooseAndUpload(page, 'scan.pdf', 'application/pdf');

  await expect(page.getByRole('alert')).toHaveText(title);
  await expect(page.getByTestId('cv-profile')).toHaveCount(0);
});

test('F0.1-AC3 rejects an unsupported file type before uploading', async ({ page }) => {
  let called = false;
  await page.route('**/api/cv', (route) => {
    called = true;
    return route.fulfill({ status: 201, json: cv('Pdf', 'x.pdf') });
  });

  await page.goto('/cv');
  await chooseAndUpload(page, 'photo.png', 'image/png');

  await expect(page.getByRole('alert')).toHaveText('Only PDF or DOCX files are accepted.');
  expect(called).toBe(false);
});

test('F0.1-AC6 offers a download link to the original file', async ({ page }) => {
  await page.route('**/api/cv', (route) => route.fulfill({ status: 201, json: cv('Pdf', 'ada.pdf') }));

  await page.goto('/cv');
  await chooseAndUpload(page, 'ada.pdf', 'application/pdf');

  await expect(page.getByRole('link', { name: 'Download original' })).toHaveAttribute('href', '/api/cv/doc-1/file');
});

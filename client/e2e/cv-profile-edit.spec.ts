import { expect, test, type Page } from '@playwright/test';

// JF-6: review and correct the parsed profile. The API is stubbed with page.route.

interface Profile {
  identity: { fullName: string | null; headline: string | null };
  currentEmployer: string | null;
  [key: string]: unknown;
}

function profileData(currentEmployer: string | null): Profile {
  return {
    identity: { fullName: 'Ada Lovelace', headline: 'Senior .NET Engineer' },
    contact: { email: 'ada@example.com', phone: null, location: 'London, UK' },
    summary: 'Backend engineer.',
    skills: [{ name: 'C#', proficiency: 'Expert', years: 10 }],
    experience: [],
    education: [],
    certifications: [],
    links: [{ label: 'GitHub', url: 'https://github.com/ada' }],
    workAuthorisation: null,
    currentEmployer,
    preferences: {
      targetRoles: [],
      locations: [],
      remote: null,
      salaryMin: null,
      salaryMax: null,
      salaryCurrency: null,
      noticePeriod: null,
    },
  };
}

function cv(version: number, data: Profile) {
  return {
    document: {
      id: 'doc-1',
      fileName: 'ada.pdf',
      contentType: 'application/pdf',
      format: 'Pdf',
      sizeBytes: 1234,
      uploadedAt: '2026-11-22T12:00:00Z',
    },
    profile: { id: `p-${version}`, cvDocumentId: 'doc-1', version, createdAt: '2026-11-23T09:00:00Z', data },
  };
}

interface SaveBody {
  baseVersion: number;
  data: Profile;
}

/** Uploads a stubbed CV and records every save request; the save echoes the next version. */
async function uploadAndOpenEditor(page: Page, parsed: Profile) {
  const saves: SaveBody[] = [];
  await page.route('**/api/cv', (route) => route.fulfill({ status: 201, json: cv(1, parsed) }));
  await page.route('**/api/cv/doc-1/profile/versions', async (route) => {
    const body = route.request().postDataJSON() as SaveBody;
    saves.push(body);
    await route.fulfill({ status: 201, json: cv(body.baseVersion + 1, body.data) });
  });

  await page.goto('/cv');
  await page.getByLabel('CV file').setInputFiles({ name: 'ada.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7 test') });
  await page.getByRole('button', { name: 'Upload' }).click();
  await expect(page.getByTestId('cv-profile')).toContainText('version 1');
  await page.getByRole('button', { name: 'Edit profile' }).click();
  return saves;
}

test('F0.1-AC4 edits the headline, saves, and shows version 2 as the active profile', async ({ page }) => {
  const saves = await uploadAndOpenEditor(page, profileData('Analytical Engines Ltd'));

  await expect(page.getByRole('button', { name: 'Save profile' })).toBeDisabled();
  await page.getByLabel('Headline').fill('Staff .NET Engineer');
  await page.getByRole('button', { name: 'Save profile' }).click();

  await expect(page.getByRole('status')).toHaveText('Saved as version 2. This is now your active profile.');
  await expect(page.getByTestId('cv-profile')).toContainText('version 2');
  await expect(page.getByTestId('cv-profile')).toContainText('Staff .NET Engineer');
  await expect(page.getByTestId('profile-form')).toHaveCount(0);
  expect(saves).toHaveLength(1);
  expect(saves[0].baseVersion).toBe(1);
  expect(saves[0].data.identity.headline).toBe('Staff .NET Engineer');
  expect(saves[0].data.currentEmployer).toBe('Analytical Engines Ltd');
});

test('F0.1-AC5 completes an empty current employer and saves it', async ({ page }) => {
  const saves = await uploadAndOpenEditor(page, profileData(null));

  const employer = page.getByLabel('Current employer');
  await expect(employer).toHaveValue('');
  await expect(page.getByTestId('current-employer-hint')).toHaveText('Not found in your CV. Add it if you are currently employed.');
  await employer.fill('Difference Engines plc');
  await page.getByRole('button', { name: 'Save profile' }).click();

  await expect(page.getByTestId('profile-current-employer')).toHaveText('Difference Engines plc');
  expect(saves[0].data.currentEmployer).toBe('Difference Engines plc');
});

test('F0.1-AC5 shows the parsed current employer prefilled', async ({ page }) => {
  await uploadAndOpenEditor(page, profileData('Analytical Engines Ltd'));

  await expect(page.getByLabel('Current employer')).toHaveValue('Analytical Engines Ltd');
});

test('F0.1-BR3 shows the conflict message when the profile changed meanwhile', async ({ page }) => {
  await uploadAndOpenEditor(page, profileData('Analytical Engines Ltd'));
  const title = 'This profile was changed since you opened it. Reload it and make your changes again.';
  await page.route('**/api/cv/doc-1/profile/versions', (route) =>
    route.fulfill({
      status: 409,
      contentType: 'application/problem+json',
      json: { title, status: 409, code: 'profile_version_conflict' },
    }),
  );

  await page.getByLabel('Headline').fill('Staff .NET Engineer');
  await page.getByRole('button', { name: 'Save profile' }).click();

  await expect(page.getByRole('alert')).toHaveText(title);
  await expect(page.getByTestId('profile-form')).toBeVisible();
  await expect(page.getByTestId('cv-profile')).toContainText('version 1');
});

test('F0.1-BR2 blocks saving an invalid link', async ({ page }) => {
  const saves = await uploadAndOpenEditor(page, profileData(null));

  await page.getByLabel('Link 1 URL').fill('not a url');
  await page.getByRole('button', { name: 'Save profile' }).click();

  await expect(page.getByTestId('profile-form-invalid')).toBeVisible();
  expect(saves).toHaveLength(0);
});

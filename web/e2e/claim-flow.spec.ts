import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';

// Seeded Keycloak test user (deploy/keycloak/reroute-realm.json) — local/CI only.
const USER = { username: 'traveller', password: 'traveller' };

async function login(page: Page) {
  await page.goto('/claims');
  await page.getByLabel('Username or email').fill(USER.username);
  await page.getByLabel('Password', { exact: true }).fill(USER.password);
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByRole('heading', { name: 'My claims' })).toBeVisible();
}

test('traveller files an eligible claim and sees the event-sourced timeline', async ({ page }) => {
  await login(page);

  await page.getByLabel('Flight number').fill('KL1234');
  await page.getByLabel('Flight date').fill(new Date(Date.now() - 7 * 86_400_000).toISOString().slice(0, 10));
  await page.getByLabel('From (IATA)').fill('AMS');
  await page.getByLabel('To (IATA)').fill('ATH');
  await page.getByLabel('Arrival delay (minutes)').fill('210');
  await page.getByRole('button', { name: /file claim/i }).click();

  await expect(page.getByText('€400')).toBeVisible();
  const history = page.getByRole('list', { name: 'Claim history' });
  await expect(history.getByText('Claim filed')).toBeVisible();
  await expect(history.getByText('Eligibility assessed')).toBeVisible();

  const a11y = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).analyze();
  expect(a11y.violations).toEqual([]);
});

test('a simulated cancellation reaches the traveller in real time', async ({ page, request }) => {
  await login(page);
  await page.getByRole('link', { name: 'Flights' }).click();
  const today = new Date().toISOString().slice(0, 10);
  await page.getByLabel('Flight number').fill('KL9999');
  await page.getByLabel('Flight date').fill(today);
  await page.getByRole('button', { name: 'Watch' }).click();
  await expect(page.getByRole('status')).toContainText('KL9999');

  // Ops endpoint (role "ops") injects a status change into Kafka; we reuse the browser session's cookies.
  const cookies = await page.context().storageState();
  await request.post('/api/ops/disruptions/simulate', {
    headers: { 'X-CSRF': '1', Cookie: cookies.cookies.map((c) => `${c.name}=${c.value}`).join('; ') },
    data: {
      flightKey: `KL9999:${today}`, flightNumber: 'KL9999', flightDate: today, origin: 'AMS', destination: 'CDG',
      status: 'Cancelled', delayMinutes: 0, reason: 'Crew shortage', observedAt: new Date().toISOString(),
    },
  });

  await expect(page.getByRole('alert').filter({ hasText: 'KL9999 has been cancelled' })).toBeVisible({ timeout: 15_000 });
});

import { test as base, expect } from '@playwright/test';
import { AppApi, GroupPage } from './support.js';

const test = base.extend({api: async ({request}, use) => {
  const api = new AppApi(request);
  try { await use(api); } finally { await api.cleanup(); }
}});

test('@ui @L1 owner sees submitted and draft selections and live totals', async ({page, api}) => {
  const f = await api.fixture(), ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await expect(ui.participant('AQA Ready')).toContainText('Vegetable pasta × 2');
  await expect(ui.participant('AQA Ready')).toContainText('$36.00');
  await expect(ui.participant('AQA Draft')).toContainText('Draft · excluded');
  await expect(ui.summary).toContainText('$54.00');
  await api.call('POST', `/guest/sessions/${f.draft.sessionId}/submit`, {}, f.draft.sessionToken);
  // Actual 15-second polling, without reload or fixed sleeps.
  await expect(ui.summary).toContainText('$63.00', {timeout: 35_000});
});

test('@ui @L1 empty group explains why sending is disabled', async ({page, api}) => {
  const f = await api.fixture({guests: false, ownerDish: false}), ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await expect(page.getByText('No guests have joined yet.', {exact: false})).toBeVisible();
  await expect(ui.summary).toContainText('$0.00');
  await page.getByRole('checkbox').check();
  await expect(page.getByRole('button', {name: 'Send group order'})).toBeDisabled();
  await api.guest(f, 'AQA Empty');
  await expect(ui.participant('AQA Empty')).toContainText('No dishes selected yet.', {timeout: 35_000});
  await expect(ui.participant('AQA Empty')).toContainText('$0.00');
});

test('@ui @L2 owner sends merged dishes, excludes drafts and waiter sees the result', async ({page, api}) => {
  const f = await api.fixture(), ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await expect(page.getByRole('button', {name: 'Send group order'})).toBeDisabled();
  await ui.submit();
  await expect(ui.summary).toContainText('Vegetable pasta × 3');
  await expect(ui.summary).toContainText('$54.00');
  await expect(ui.summary).not.toContainText('Berry dessert');
  // A separate browser context represents the assigned waiter, using the normal login form.
  const staffContext = await page.context().browser().newContext({baseURL: process.env.UI_URL});
  try {
    const staffPage = await staffContext.newPage(), staffUi = new GroupPage(staffPage);
    await staffUi.signIn({email: process.env.WAITER_USERNAME, password: process.env.WAITER_PASSWORD});
    await staffPage.goto('/waiter-reservations');
    const card = staffPage.locator('app-card-reservation').filter({hasText: f.owner.name});
    await card.getByRole('button', {name: 'Pre-order', exact: true}).click();
    await expect(staffPage.getByRole('region', {name: 'Group participants'})).toContainText('AQA Ready');
    await expect(staffPage.getByRole('region', {name: 'Group participants'})).not.toContainText('AQA Draft');
    await expect(staffPage.locator('.order-summary')).toContainText('$54.00');
  } finally { await staffContext.close(); }
});

test('@ui @L3 owner removes a submitted guest and sees recalculated total', async ({page, api}) => {
  const f = await api.fixture(), ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await ui.participant('AQA Ready').getByRole('button', {name: 'Remove guest'}).click();
  const [removed] = await Promise.all([
    page.waitForResponse(response => response.request().method() === 'DELETE' && response.url().includes('/guest/groups/')),
    ui.participant('AQA Ready').getByRole('button', {name: 'Yes, remove', exact: true}).click(),
  ]);
  expect(removed.status(), 'Removing a submitted participant').toBe(200);
  await expect(ui.participant('AQA Ready')).toHaveCount(0);
  await expect(ui.summary).toContainText('$18.00');
  await ui.submit();
  expect((await api.view(f)).consolidatedItems[0].quantity).toBe(1);
});

test('@api @L1 only the owner can read an open group', async ({api}) => {
  const f = await api.fixture(), outsider = await api.account();
  for (const [token, status] of [[undefined, 401], [outsider.token, 403], [f.ready.sessionToken, 401], [f.staff, 404]])
    await api.call('GET', `/reservations/${f.id}/group-order`, undefined, token, status);
  await api.call('GET', '/reservations/00000000-0000-0000-0000-000000000001/group-order', undefined, f.owner.token, 404);
});

test('@api @L2 stale review rejected and retry is idempotent', async ({api}) => {
  const f = await api.fixture(), before = await api.view(f);
  await api.call('POST', `/guest/sessions/${f.draft.sessionId}/dishes`, {dishId: 'demo-dessert', quantity: 2}, f.draft.sessionToken);
  await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: before.revision}, f.owner.token, 409);
  const current = await api.view(f), data = {revision: current.revision};
  await api.call('POST', `/reservations/${f.id}/group-order/submit`, data, f.staff, 403);
  const sent = await api.call('POST', `/reservations/${f.id}/group-order/submit`, data, f.owner.token);
  const retry = await api.call('POST', `/reservations/${f.id}/group-order/submit`, data, f.owner.token);
  expect(retry.orderId).toBe(sent.orderId); expect(sent.total).toBe(54);
  await api.call('POST', `/orders/${f.id}/unsubmit`, {}, f.owner.token, 409);
});

test('@api @L2 an empty selection cannot be sent', async ({api}) => {
  const f = await api.fixture({ownerDish: false, guests: false}), view = await api.view(f);
  expect(view.canSubmit).toBe(false);
  await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: view.revision}, f.owner.token, 400);
});

test('@api @L3 foreign, missing and unauthorized participant removal denied', async ({api}) => {
  const a = await api.fixture(), b = await api.fixture(), outsider = await api.account();
  const path = `/guest/groups/${a.groupId}/sessions/${a.ready.sessionId}`;
  await api.call('DELETE', path, undefined, outsider.token, 403);
  await api.call('DELETE', `/guest/groups/${a.groupId}/sessions/${b.ready.sessionId}`, undefined, a.owner.token, 400);
  await api.call('DELETE', `/guest/groups/${a.groupId}/sessions/00000000-0000-0000-0000-000000000001`, undefined, a.owner.token, 404);
  expect((await api.view(a)).joinedGuests).toBe(2);
});

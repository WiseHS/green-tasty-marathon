import { test as base, expect } from '@playwright/test';
import { AppApi, GroupPage } from './support.js';
const test = base.extend({api: async ({request}, use) => {
  const api = new AppApi(request);
  try { await use(api); } finally { await api.cleanup(); }
}});
const path = guest => `/guest/sessions/${guest.sessionId}/dish-requests`;

test('@ui @feature-requests guest saves a request and owner sends it to the waiter', async ({page, api}) => {
  const f = await api.fixture({guests: false});
  await page.goto(`/join/${f.inviteToken}`);
  await page.getByRole('textbox', {name: 'Enter Your Name'}).fill('AQA Request Guest');
  await page.getByRole('button', {name: 'Browse Menu', exact: true}).click();
  await page.locator('app-dish-card').filter({hasText: 'Vegetable pasta'})
    .getByRole('button', {name: 'Pre-Order', exact: true}).click();
  await page.getByRole('button', {name: 'Open cart, 1 portions', exact: true}).click();
  const note = 'Без цибулі; sauce separately';
  await page.getByRole('textbox', {name: 'Request for Vegetable pasta', exact: true}).fill(`  ${note}  `);
  await expect(page.getByRole('button', {name: 'Submit Pre-Order', exact: true})).toBeDisabled();
  await page.getByRole('button', {name: 'Save request', exact: true}).click();
  await expect(page.getByText('Request saved', {exact: true})).toBeVisible();
  await test.info().attach('guest-saved-request', {body: await page.screenshot({fullPage: true}), contentType: 'image/png'});
  await page.setViewportSize({width: 390, height: 844});
  const requestBox = await page.getByRole('textbox', {name: 'Request for Vegetable pasta', exact: true}).boundingBox();
  expect(requestBox).not.toBeNull(); expect(requestBox.x).toBeGreaterThanOrEqual(0);
  expect(requestBox.x + requestBox.width).toBeLessThanOrEqual(390);
  await test.info().attach('guest-request-mobile', {body: await page.screenshot({fullPage: true}), contentType: 'image/png'});
  await page.setViewportSize({width: 1280, height: 720});
  await Promise.all([page.waitForResponse(response => response.request().method() === 'GET' && response.url().endsWith('/dish-requests')), page.reload()]);
  await page.getByRole('button', {name: 'Open cart, 1 portions', exact: true}).click();
  await expect(page.getByRole('textbox', {name: 'Request for Vegetable pasta', exact: true})).toHaveValue(note);
  await page.getByRole('button', {name: 'Submit Pre-Order', exact: true}).click();
  await expect(page.getByRole('heading', {name: 'Pre-Order Submitted!', exact: true})).toBeVisible();
  await expect(page.getByText(note, {exact: false})).toBeVisible();
  const ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await expect(page.getByRole('region', {name: 'Special requests to restaurant'})).toContainText(note);
  await expect(ui.participant('AQA Request Guest')).toContainText(note);
  await test.info().attach('owner-request-review', {body: await page.screenshot({fullPage: true}), contentType: 'image/png'});
  await ui.submit();
  expect((await api.view(f)).total).toBe(36);
  const staffContext = await page.context().browser().newContext({baseURL: process.env.UI_URL});
  try {
    const staff = await staffContext.newPage();
    await new GroupPage(staff).signIn({email: process.env.WAITER_USERNAME, password: process.env.WAITER_PASSWORD});
    await staff.goto('/waiter-reservations');
    await staff.locator('app-card-reservation').filter({hasText: f.owner.name}).getByRole('button', {name: 'Pre-order', exact: true}).click();
    await expect(staff.getByRole('region', {name: 'Group participants'})).toContainText(note);
    await expect(staff.locator('.order-summary')).toContainText('$36.00');
    await test.info().attach('waiter-received-request', {body: await staff.screenshot({fullPage: true}), contentType: 'image/png'});
  } finally { await staffContext.close(); }
});

test('@api @feature-requests validates requests and isolates guest credentials', async ({api}) => {
  const f = await api.fixture({guests: false});
  const a = await api.guest(f, 'AQA First', 'demo-pasta', 1), b = await api.guest(f, 'AQA Second', 'demo-pasta', 1);
  const url = `${path(a)}/demo-pasta`;
  for (const token of [undefined, b.sessionToken, f.owner.token, 'invalid'])
    await api.call('PUT', url, {note: 'Not authorized'}, token, 401);
  await api.call('GET', path(a), undefined, b.sessionToken, 401);
  await api.call('PUT', `${path(a)}/demo-dessert`, {note: 'Not in cart'}, a.sessionToken, 404);
  for (const body of [{note: 'x'.repeat(301)}, {note: 123}, {note: null}, {}, [], {note: 'bad\u0000text'}])
    await api.call('PUT', url, body, a.sessionToken, 400);
  await api.call('PUT', url, {note: 'x'.repeat(300)}, a.sessionToken);
  expect((await api.call('GET', path(a), undefined, a.sessionToken)).notes[0].note).toHaveLength(300);
  await api.call('PUT', url, {note: '   '}, a.sessionToken);
  expect((await api.call('GET', path(a), undefined, a.sessionToken)).notes).toEqual([]);
});

test('@api @feature-requests changed requests invalidate review and submitted notes are immutable', async ({api}) => {
  const f = await api.fixture({guests: false}), a = await api.guest(f, 'AQA Request', 'demo-pasta', 1);
  const url = `${path(a)}/demo-pasta`;
  await api.call('PUT', url, {note: 'No onions'}, a.sessionToken);
  const before = await api.view(f);
  await api.call('PUT', url, {note: 'No garlic'}, a.sessionToken);
  await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: before.revision}, f.owner.token, 409);
  await api.call('POST', `/guest/sessions/${a.sessionId}/submit`, {}, a.sessionToken);
  await api.call('PUT', url, {note: 'Changed too late'}, a.sessionToken, 409);
  const ready = await api.view(f);
  const sent = await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: ready.revision}, f.owner.token);
  await api.call('PUT', url, {note: 'Changed after owner submission'}, a.sessionToken, 409);
  const staff = await api.view(f, f.staff);
  expect(staff.participants.find(participant => participant.id === a.sessionId).items[0].note).toBe('No garlic');
  expect(staff.orderId).toBe(sent.orderId); expect(staff.total).toBe(36);
  expect((await api.call('GET', path(a), undefined, a.sessionToken)).notes[0].note).toBe('No garlic');
});

test('@ui @feature-requests identical dishes keep distinct literal requests for guests with the same name', async ({page, api}) => {
  const f = await api.fixture({guests: false});
  const a = await api.guest(f, 'AQA Alex', 'demo-pasta', 2), b = await api.guest(f, 'AQA Alex', 'demo-pasta', 1);
  await api.call('PUT', `${path(a)}/demo-pasta`, {note: 'No onions'}, a.sessionToken);
  const literal = '<img src=x onerror="alert(1)"> extra basil';
  await api.call('PUT', `${path(b)}/demo-pasta`, {note: literal}, b.sessionToken);
  for (const guest of [a, b]) await api.call('POST', `/guest/sessions/${guest.sessionId}/submit`, {}, guest.sessionToken);
  const ui = new GroupPage(page); await ui.signIn(f.owner); await ui.open(f.id);
  const summary = page.getByRole('region', {name: 'Special requests to restaurant'});
  await expect(summary).toContainText('No onions'); await expect(summary).toContainText(literal);
  await expect(summary.locator('img')).toHaveCount(0); await expect(ui.summary).toContainText('$72.00');
  await ui.submit();
  const sent = await api.view(f);
  expect(sent.consolidatedItems[0].quantity).toBe(4);
  expect(sent.participants.find(p => p.id === a.sessionId).items[0].note).toBe('No onions');
  expect(sent.participants.find(p => p.id === b.sessionId).items[0].note).toBe(literal);
});

test('@api @feature-requests removing a dish or guest removes their requests', async ({api}) => {
  const f = await api.fixture({guests: false}), a = await api.guest(f, 'AQA Removed', 'demo-pasta', 1);
  await api.call('PUT', `${path(a)}/demo-pasta`, {note: 'No onions'}, a.sessionToken);
  await api.call('DELETE', `/guest/sessions/${a.sessionId}/dishes`, {dishId: 'demo-pasta', quantity: 1}, a.sessionToken);
  expect((await api.call('GET', path(a), undefined, a.sessionToken)).notes).toEqual([]);
  await api.call('POST', `/guest/sessions/${a.sessionId}/dishes`, {dishId: 'demo-pasta', quantity: 1}, a.sessionToken);
  expect((await api.call('GET', path(a), undefined, a.sessionToken)).notes).toEqual([]);
  await api.call('PUT', `${path(a)}/demo-pasta`, {note: 'New request'}, a.sessionToken);
  await api.call('DELETE', `/guest/groups/${f.groupId}/sessions/${a.sessionId}`, undefined, f.owner.token);
  await api.call('GET', path(a), undefined, a.sessionToken, 401);
  expect((await api.view(f)).participants.some(p => p.id === a.sessionId)).toBe(false);
});

for (const condition of ['deadline', 'closed group']) {
  test(`@api @feature-requests @isolated ${condition} locks editing`, async ({api}) => {
    test.skip(process.env.AQA_ISOLATED_DATABASE !== 'true', 'Requires the disposable database.');
    const f = await api.fixture({guests: false}), a = await api.guest(f, 'AQA Locked', 'demo-pasta', 1);
    if (condition === 'deadline') await api.database.change('Reservation', f.id, {startTime: new Date(Date.now() + 20 * 60_000).toISOString()});
    else await api.database.change('GroupReservation', f.groupId, {status: 2});
    await api.call('PUT', `${path(a)}/demo-pasta`, {note: 'Too late'}, a.sessionToken, 409);
  });
}

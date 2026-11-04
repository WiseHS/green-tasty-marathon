import { test as base, expect } from '@playwright/test';
import { AppApi, GroupPage } from './support.js';

const test = base.extend({api: async ({request}, use) => {
  const api = new AppApi(request);
  try { await use(api); } finally { await api.cleanup(); }
}});

async function expectBlocked(page, api, f, message) {
  const ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await expect(page.getByText(message, {exact: true})).toBeVisible();
  await page.getByRole('checkbox').check();
  await expect(page.getByRole('button', {name: 'Send group order', exact: true})).toBeDisabled();
  const view = await api.view(f);
  const refusal = await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: view.revision}, f.owner.token, 400);
  expect(refusal.error).toBe(message);
  expect((await api.view(f)).status).toBe('open');
}

test('@ui @L2 drafts alone cannot be sent', async ({page, api}) => {
  const f = await api.fixture({guests: false, ownerDish: false});
  await api.guest(f, 'AQA Draft', 'demo-pasta', 2, false);
  await expectBlocked(page, api, f, 'Add your dishes or wait for a guest to submit their selection.');
});

test('@ui @L3 removing the last guest leaves an empty group', async ({page, api}) => {
  const f = await api.fixture({guests: false, ownerDish: false});
  await api.guest(f, 'AQA Last', 'demo-pasta', 1, true);
  const ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id);
  await ui.participant('AQA Last').getByRole('button', {name: 'Remove guest', exact: true}).click();
  await ui.participant('AQA Last').getByRole('button', {name: 'Yes, remove', exact: true}).click();
  await expect(page.getByText('No guests have joined yet.', {exact: false})).toBeVisible();
  await expect(ui.summary).toContainText('$0.00');
  const after = await api.view(f);
  expect(after.joinedGuests).toBe(0); expect(after.participants).toEqual([]); expect(after.total).toBe(0);
});

test('@ui @L3 submitted group hides editing and the API refuses further mutations', async ({page, api}) => {
  const f = await api.fixture(), ui = new GroupPage(page);
  await ui.signIn(f.owner); await ui.open(f.id); await ui.submit();
  await page.reload();
  await expect(page.getByRole('status')).toContainText('submitted selection is locked');
  await expect(page.getByRole('button', {name: 'Remove guest', exact: true})).toHaveCount(0);
  await expect(page.getByRole('button', {name: 'Send group order', exact: true})).toHaveCount(0);
  await api.call('DELETE', `/guest/groups/${f.groupId}/sessions/${f.ready.sessionId}`, undefined, f.owner.token, 400);
  await api.call('POST', `/guest/sessions/${f.ready.sessionId}/dishes`, {dishId: 'demo-pasta', quantity: 1}, f.ready.sessionToken, 400);
  await api.call('DELETE', `/guest/sessions/${f.ready.sessionId}/dishes`, {dishId: 'demo-pasta', quantity: 1}, f.ready.sessionToken, 400);
  await api.call('POST', `/orders/${f.id}/unsubmit`, {}, f.owner.token, 409);
  const after = await api.view(f);
  expect(after.total).toBe(54); expect(after.consolidatedItems[0].quantity).toBe(3);
});

test('@api @L2 anonymous, guest and foreign customer cannot submit', async ({api}) => {
  const f = await api.fixture(), other = await api.account(), view = await api.view(f);
  for (const [token, status] of [[undefined, 401], [f.ready.sessionToken, 401], [other.token, 403]])
    await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: view.revision}, token, status);
  expect((await api.view(f)).status).toBe('open');
});

test('@api @L2 missing reservation cannot be submitted', async ({api}) => {
  const owner = await api.account();
  const refusal = await api.call('POST', '/reservations/00000000-0000-0000-0000-000000000001/group-order/submit', {revision: 'missing'}, owner.token, 404);
  expect(refusal.error).toBe('Reservation not found.');
});

test('@api @L3 anonymous, guest and waiter cannot remove a participant', async ({api}) => {
  const f = await api.fixture();
  for (const [token, status] of [[undefined, 401], [f.ready.sessionToken, 401], [f.staff, 403]])
    await api.call('DELETE', `/guest/groups/${f.groupId}/sessions/${f.ready.sessionId}`, undefined, token, status);
  expect((await api.view(f)).joinedGuests).toBe(2);
});

test.describe('Disposable database only', () => {
  test.skip(process.env.AQA_ISOLATED_DATABASE !== 'true', 'Requires the isolated loopback CI database; never mutate production menu or clock data.');

  for (const state of ['OnStop', 'OutOfStock']) {
    test(`@ui @L2 @isolated ${state} dish blocks submission`, async ({page, api}) => {
      const f = await api.fixture();
      await api.database.change('Dish', 'demo-pasta', {state});
      await expectBlocked(page, api, f, 'An included dish is unavailable. Review the participant orders before sending.');
    });
  }

  for (const minutes of [-60, 20]) {
    test(`@ui @L2 @isolated reservation starting in ${minutes} minutes is past the pre-order deadline`, async ({page, api}) => {
      const f = await api.fixture(), start = new Date(Date.now() + minutes * 60_000);
      await api.database.change('Reservation', f.id, {startTime: start.toISOString(), endTime: new Date(+start + 90 * 60_000).toISOString()});
      await expectBlocked(page, api, f, 'Group orders close 30 minutes before the reservation.');
    });
  }

  test('@ui @L2 @isolated submitted baskets without dishes cannot be sent', async ({page, api}) => {
    const f = await api.fixture(), view = await api.view(f);
    await api.database.change('Order', view.orderId, {dishes: []});
    await api.database.change('GuestSession', f.ready.sessionId, {dishes: []});
    await expectBlocked(page, api, f, 'Add your dishes or wait for a guest to submit their selection.');
  });

  test('@ui @L2 @isolated missing order blocks submission even with a submitted guest', async ({page, api}) => {
    const f = await api.fixture(), view = await api.view(f);
    await api.database.change('Order', view.orderId, null);
    await expectBlocked(page, api, f, 'The reservation order was not found. Add your own selection before sending the group order.');
  });

  test('@ui @L2 @isolated canceled reservation cannot be sent', async ({page, api}) => {
    const f = await api.fixture();
    await api.call('DELETE', '/reservations', {id: f.id}, f.owner.token);
    await expectBlocked(page, api, f, 'The reservation is no longer open for pre-orders.');
  });

  test('@ui @L2 @isolated missing group shows an error and cannot be submitted', async ({page, api}) => {
    const f = await api.fixture(), ui = new GroupPage(page);
    await api.database.change('GroupReservation', f.groupId, null);
    await ui.signIn(f.owner); await page.goto(`/reservations/${f.id}/group-order`);
    await expect(page.getByRole('alert')).toContainText('Group reservation not found.');
    await expect(page.getByRole('button', {name: 'Send group order', exact: true})).toHaveCount(0);
    await api.call('POST', `/reservations/${f.id}/group-order/submit`, {revision: 'missing'}, f.owner.token, 404);
  });
});

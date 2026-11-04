import { randomUUID, randomInt } from 'node:crypto';
import { expect } from '@playwright/test';
import { setTimeout as delay } from 'node:timers/promises';
import { IsolatedDatabase } from './isolated-database.js';

const authenticationTimes = [];
async function reserveAuthentication() {
  // Respect the app's 20/minute auth limit. This paces requests, not UI readiness.
  while (authenticationTimes.length && Date.now() - authenticationTimes[0] >= 61_000) authenticationTimes.shift();
  if (authenticationTimes.length >= 18) {
    await delay(61_000 - (Date.now() - authenticationTimes[0]));
    return reserveAuthentication();
  }
  authenticationTimes.push(Date.now());
}

// Legacy Lambda DTOs use PascalCase, portable host DTOs use camelCase.
function camelKeys(value) {
  if (Array.isArray(value)) return value.map(camelKeys);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value)
    .map(([key, item]) => [key[0].toLowerCase() + key.slice(1), camelKeys(item)]));
  return value;
}

export class AppApi {
  constructor(request) { this.request = request; this.bookings = []; this.database = new IsolatedDatabase(); }
  async call(method, path, data, token, status = 200) {
    if (['/signin', '/register'].includes(path)) await reserveAuthentication();
    const response = await this.request.fetch(`${process.env.API_URL}${path}`, {
      method, data, headers: token ? { Authorization: `Bearer ${token}` } : {}, timeout: 90_000,
    });
    // Never include request headers or login bodies in assertion messages.
    const detail = response.status() !== status && !['/signin', '/register'].includes(path)
      ? (await response.text()).slice(0, 400) : '';
    expect(response.status(), `${method} ${path} ${detail}`).toBe(status);
    return response.status() === 204 ? null : camelKeys(await response.json());
  }
  async account() {
    const email = `aqa-${randomUUID()}@example.com`, password = `Aqa!${randomUUID().slice(0, 10)}`;
    const lastName = 'Tester' + randomUUID().replace(/[^a-f]/g, '').slice(0, 10);
    await this.call('POST', '/register', {firstName: 'Aqa', lastName, email, password, confirmPassword: password});
    const login = await this.call('POST', '/signin', {email, password});
    return { email, password, name: `Aqa ${lastName}`, token: login.idToken };
  }
  async fixture({ guests = true, ownerDish = true } = {}) {
    if (!process.env.WAITER_PASSWORD) throw new Error('WAITER_PASSWORD is required; never put it in source control.');
    const owner = await this.account();
    const staff = await this.call('POST', '/signin', {email: process.env.WAITER_USERNAME, password: process.env.WAITER_PASSWORD});
    // Separate future minute/table per scenario. POST is not retried on failures.
    const start = new Date(); start.setUTCDate(start.getUTCDate() + randomInt(10, 330));
    start.setUTCHours(10 + randomInt(0, 8), randomInt(0, 60), 0, 0);
    const booking = await this.call('POST', '/reservations', {
      customerEmail: owner.email, tableId: `demo-table-${randomInt(1, 7)}`, guestCount: 4,
      startTime: start.toISOString(), endTime: new Date(+start + 90 * 60_000).toISOString(),
    }, staff.idToken, 201);
    const id = booking.id || booking.reservationId;
    this.bookings.push({id, token: owner.token});
    const invite = await this.call('GET', `/reservations/${id}/invite-token`, undefined, owner.token);
    const group = await this.call('GET', `/guest/invites/${invite.token}`);
    const f = {id, owner, staff: staff.idToken, groupId: group.groupReservationId};
    if (ownerDish) await this.call('POST', '/orders', {reservationId: id, orderItems: [{dishId: 'demo-pasta', quantity: 1}]}, owner.token, 201);
    if (guests) {
      f.ready = await this.guest(f, 'AQA Ready', 'demo-pasta', 2, true);
      f.draft = await this.guest(f, 'AQA Draft', 'demo-dessert', 1, false);
    }
    return f;
  }
  async guest(f, guestName, dishId, quantity, submitted) {
    const guest = await this.call('POST', '/guest/sessions', {groupReservationId: f.groupId, guestName});
    if (dishId) await this.call('POST', `/guest/sessions/${guest.sessionId}/dishes`, {dishId, quantity}, guest.sessionToken);
    if (submitted) await this.call('POST', `/guest/sessions/${guest.sessionId}/submit`, {}, guest.sessionToken);
    return guest;
  }
  view(f, token = f.owner.token) { return this.call('GET', `/reservations/${f.id}/group-order`, undefined, token); }
  async cleanup() {
    try { await this.database.restore(); }
    finally {
      const results = await Promise.allSettled(this.bookings.map(({id, token}) => this.call('DELETE', '/reservations', {id}, token)));
      const failures = results.filter(result => result.status === 'rejected');
      if (failures.length) throw new AggregateError(failures.map(result => result.reason), 'Synthetic reservation cleanup failed');
    }
  }
}

export class GroupPage {
  constructor(page) { this.page = page; this.summary = page.getByRole('complementary', {name: 'Consolidated order'}); }
  async signIn(account) {
    await this.page.goto('/login');
    await this.page.getByRole('textbox', {name: 'Email', exact: true}).fill(account.email);
    await this.page.locator('input[formcontrolname="password"]').fill(account.password);
    await reserveAuthentication();
    await this.page.getByRole('button', {name: 'Sign In', exact: true}).click();
    await expect(this.page).not.toHaveURL(/\/login/);
  }
  async open(id) {
    await this.page.goto(`/reservations/${id}/group-order`);
    await expect(this.page.getByRole('heading', {name: 'Who ordered what'})).toBeVisible();
  }
  participant(name) { return this.page.locator('article.participant').filter({has: this.page.getByRole('heading', {name, exact: true})}); }
  async submit() {
    await this.page.getByRole('checkbox').check();
    await this.page.getByRole('button', {name: 'Send group order', exact: true}).click();
    await expect(this.page.getByRole('status')).toContainText('Group order sent to the restaurant');
  }
}

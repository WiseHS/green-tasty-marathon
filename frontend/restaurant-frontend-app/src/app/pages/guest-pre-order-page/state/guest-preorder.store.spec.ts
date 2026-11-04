import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GuestPreorderStore } from './guest-preorder.store';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

describe('Saved guest dish requests', () => {
  let store: GuestPreorderStore, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({providers: [provideHttpClient(), provideHttpClientTesting(),
      {provide: API_BASE_URL, useValue: 'https://test.api'}]});
    store = TestBed.inject(GuestPreorderStore); http = TestBed.inject(HttpTestingController);
    store.guestSessionId.set('guest'); store.guestSessionToken.set('guest-token');
    store.cartItems.set([{dishId: 'pasta', title: 'Pasta', price: 18, qty: 1, note: ''}]);
  });
  afterEach(() => { http.verify(); });
  it('updates the saved request only after server acknowledgement', async () => {
    const pending = store.saveNote('pasta', 'No onions');
    expect(store.cartItems()[0].note).toBe(''); expect(store.cartBusy()).toBeTrue();
    http.expectOne('https://test.api/guest/sessions/guest/dish-requests/pasta').flush({dishId: 'pasta', note: 'No onions'});
    await pending; expect(store.cartItems()[0].note).toBe('No onions'); expect(store.cartBusy()).toBeFalse();
  });
  it('keeps the old saved request and exposes a failed save', async () => {
    const pending = store.saveNote('pasta', 'No onions');
    http.expectOne('https://test.api/guest/sessions/guest/dish-requests/pasta')
      .flush({error: 'Selection locked'}, {status: 409, statusText: 'Conflict'});
    await pending; expect(store.cartItems()[0].note).toBe(''); expect(store.cartError()).toBe('Selection locked');
  });
  it('loads authoritative requests when restoring the menu', async () => {
    const pending = store.loadMenu();
    http.expectOne('https://test.api/guest/sessions/guest/menu').flush({dishes: {}});
    await Promise.resolve();
    http.expectOne('https://test.api/guest/sessions/guest/dish-requests').flush({notes: [{dishId: 'pasta', note: 'Sauce separately'}]});
    await pending; expect(store.cartItems()[0].note).toBe('Sauce separately');
  });
  it('does not save oversized requests or a submitted selection', async () => {
    await store.saveNote('pasta', 'x'.repeat(301)); expect(store.cartError()).toContain('300');
    store.status.set('submitted'); await store.saveNote('pasta', 'No onions');
    http.expectNone('https://test.api/guest/sessions/guest/dish-requests/pasta');
  });
  it('clears the previous invitation session and notes before opening another invitation', async () => {
    store.guestName.set('Previous guest');
    store.initContext('request-test-new-invite');
    expect(store.guestSessionId()).toBeNull(); expect(store.guestSessionToken()).toBeNull();
    expect(store.guestName()).toBe(''); expect(store.cartItems()).toEqual([]);
    http.expectOne('https://test.api/guest/invites/request-test-new-invite').flush({});
    await Promise.resolve();
  });
  it('ignores a late menu response from a previous guest session', async () => {
    const pending = store.loadMenu();
    store.guestSessionId.set('different-guest'); store.guestSessionToken.set('different-token');
    http.expectOne('https://test.api/guest/sessions/guest/menu').flush({dishes: {OldCategory: []}});
    await pending;
    expect(store.menuByCategory()).toEqual({});
    http.expectNone('https://test.api/guest/sessions/guest/dish-requests');
  });
});

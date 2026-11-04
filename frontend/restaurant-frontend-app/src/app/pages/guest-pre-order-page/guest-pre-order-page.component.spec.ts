import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { GuestPreOrderPageComponent } from './guest-pre-order-page.component';
import { GuestPreorderStore } from './state/guest-preorder.store';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

describe('Guest cart badge', () => {
  it('counts portions across dishes, updates quantities and hides when empty', async () => {
    await TestBed.configureTestingModule({
      imports: [GuestPreOrderPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'https://test.api' }],
    }).compileComponents();
    const store = TestBed.inject(GuestPreorderStore);
    store.guestName.set('Guest');
    const fixture = TestBed.createComponent(GuestPreOrderPageComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cart-badge')).toBeNull();
    store.cartItems.set([
      { dishId: 'salad', title: 'Salad', price: 12, qty: 2 },
      { dishId: 'pasta', title: 'Pasta', price: 18, qty: 1 },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cart-badge').textContent.trim()).toBe('3');
    expect(fixture.nativeElement.querySelector('.cart-btn').getAttribute('aria-label')).toBe('Open cart, 3 portions');
    store.cartItems.set([{ dishId: 'salad', title: 'Salad', price: 12, qty: 1 }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cart-badge').textContent.trim()).toBe('1');
    store.cartItems.set([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cart-badge')).toBeNull();
    fixture.destroy();
  });
});

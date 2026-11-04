import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BehaviorSubject, of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { HeaderComponent } from './header.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { UserModel } from '@app/auth/models/user.model';
import { NotificationsService } from '@app/services/notifications/notifications.service';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { Roles } from '@app/shared/constants/roles';

describe('Header cart quantity', () => {
  let fixture: ComponentFixture<HeaderComponent>;
  let http: HttpTestingController;
  let users: BehaviorSubject<UserModel | null>;
  let orders: PreOrdersService;
  const customer: UserModel = {
    id: 'customer', firstName: 'Cart', lastName: 'Tester',
    email: 'cart@example.com', role: Roles.CUSTOMER,
  };
  const order = (quantity: number, status = 0) => ({
    orderId: 'order', reservationId: 'reservation', status,
    orderItems: [{ dish: { id: 'salad' }, quantity }],
  });

  beforeEach(async () => {
    users = new BehaviorSubject<UserModel | null>(null);
    await TestBed.configureTestingModule({
      imports: [HeaderComponent],
      providers: [
        provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'https://test.api' },
        { provide: AuthenticationService, useValue: {
          user$: users, isAuthorised$: of(true), signOut: () => users.next(null),
        } },
        { provide: NotificationsService, useValue: {
          loadNotifications: () => of([]), markAsRead: () => of(undefined),
        } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    orders = TestBed.inject(PreOrdersService);
    fixture = TestBed.createComponent(HeaderComponent);
    fixture.detectChanges();
  });
  afterEach(() => { fixture.destroy(); http.verify(); });

  it('restores total portions on login and hides the badge for an empty cart', () => {
    users.next(customer);
    http.expectOne('https://test.api/orders').flush([order(2), order(1, 1), order(9, 2)]);
    fixture.detectChanges();
    const badges = fixture.nativeElement.querySelectorAll('.cart-badge') as NodeListOf<HTMLElement>;
    expect(badges.length).toBe(2);
    badges.forEach(badge => expect(badge.textContent?.trim()).toBe('3'));
    orders.cartChanged.next();
    http.expectOne('https://test.api/orders').flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.cart-badge')).toBeNull();
  });

  it('refreshes after successful deletion and ignores a failed deletion', () => {
    users.next(customer);
    http.expectOne('https://test.api/orders').flush([order(2)]);
    orders.cancelPreOrderByReservationId('reservation').subscribe({ error: () => undefined });
    http.expectOne('https://test.api/orders/reservation').flush('failed', { status: 500, statusText: 'Error' });
    expect(fixture.componentInstance.cartQuantity()).toBe(2);
    orders.cancelPreOrderByReservationId('reservation').subscribe();
    const deletion = http.expectOne('https://test.api/orders/reservation');
    expect(deletion.request.method).toBe('DELETE');
    deletion.flush({});
    http.expectOne('https://test.api/orders').flush([]);
    expect(fixture.componentInstance.cartQuantity()).toBe(0);
  });

  it('updates immediately after creating an order and changing its portions', () => {
    users.next(customer);
    http.expectOne('https://test.api/orders').flush([]);
    const dish = {
      id: 'salad', name: 'Salad', price: 12, imageUrl: '', state: 'Available',
      weight: '250 g', dishType: 'Appetizer', description: '', calories: '100',
      proteins: '1', carbohydrates: '2', fats: '3', vitamins: '',
    };
    orders.addDishToPreOrderByReservationId('reservation', dish, 1).subscribe();
    http.expectOne('https://test.api/orders').flush([]);
    const creation = http.expectOne('https://test.api/orders');
    expect(creation.request.method).toBe('POST');
    expect(creation.request.body.orderItems).toEqual([{ dishId: 'salad', quantity: 1 }]);
    creation.flush(order(1));
    http.expectOne('https://test.api/orders').flush([order(1)]);
    expect(fixture.componentInstance.cartQuantity()).toBe(1);
    orders.addDishToPreOrderByReservationId('reservation', dish, 1).subscribe();
    http.expectOne('https://test.api/orders').flush([order(1)]);
    const update = http.expectOne('https://test.api/orders/reservation');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body.orderItems).toEqual([{ dishId: 'salad', quantity: 2 }]);
    update.flush({});
    http.expectOne('https://test.api/orders').flush([order(2)]);
    expect(fixture.componentInstance.cartQuantity()).toBe(2);
  });

  it('retains the last confirmed count on a read failure and recovers on the next change', () => {
    users.next(customer);
    http.expectOne('https://test.api/orders').flush([order(2)]);
    orders.cartChanged.next();
    http.expectOne('https://test.api/orders').flush('failed', { status: 500, statusText: 'Error' });
    expect(fixture.componentInstance.cartQuantity()).toBe(2);
    orders.cartChanged.next();
    http.expectOne('https://test.api/orders').flush([order(4)]);
    expect(fixture.componentInstance.cartQuantity()).toBe(4);
  });

  it('clears on logout and cancels an in-flight response from the previous account', () => {
    users.next(customer);
    const pending = http.expectOne('https://test.api/orders');
    users.next(null);
    expect(pending.cancelled).toBeTrue();
    expect(fixture.componentInstance.cartQuantity()).toBe(0);
    orders.cartChanged.next();
    http.expectNone('https://test.api/orders');
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { throwError } from 'rxjs';
import { MenuPageComponent } from './menu-page.component';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { DishListResponse } from '@app/models/dishes/dish-list-response.model';

describe('MenuPageComponent order errors', () => {
  const dish: DishListResponse = {
    id: 'salad', name: 'Salad', imageUrl: '', price: 12, state: 'Available', weight: '',
    dishType: 'Appetizer', description: '', calories: '', proteins: '',
    carbohydrates: '', fats: '', vitamins: '',
  };
  let component: MenuPageComponent;
  let orders: jasmine.SpyObj<PreOrdersService>;
  let toast: jasmine.SpyObj<ToastService>;
  beforeEach(() => {
    orders = jasmine.createSpyObj('PreOrdersService', ['addDishToPreOrderByReservationId']);
    toast = jasmine.createSpyObj('ToastService', ['error', 'success']);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(),
        { provide: API_BASE_URL, useValue: 'https://test.api' },
        { provide: PreOrdersService, useValue: orders },
        { provide: ToastService, useValue: toast }],
    });
    component = TestBed.runInInjectionContext(() => new MenuPageComponent());
  });
  it('does not send an order without a selected reservation', () => {
    component.addDishToCart(dish);
    expect(orders.addDishToPreOrderByReservationId).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(jasmine.stringMatching('Select an active reservation'), 'Reservation required');
  });
  it('shows the server reason rather than the generic 400 OK transport message', () => {
    component.preOrderReservationId = 'reservation';
    const reason = 'Pre-orders can only be modified up to 30 minutes before the reservation start time.';
    orders.addDishToPreOrderByReservationId.and.returnValue(throwError(() =>
      new HttpErrorResponse({ status: 400, statusText: 'OK', error: { error: reason } })));
    component.addDishToCart(dish);
    expect(toast.error).toHaveBeenCalledWith(reason, 'Error');
    expect(toast.success).not.toHaveBeenCalled();
  });
});

import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GroupOrderView } from './group-order.model';
@Injectable({ providedIn: 'root' })
export class GroupOrderService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  get(reservationId: string) {
    return this.http.get<GroupOrderView>(`${this.baseUrl}/reservations/${reservationId}/group-order`, {
      context: new HttpContext().set(SKIP_ERROR_TOAST, true),
    });
  }
  submit(reservationId: string, revision: string) {
    return this.http.post<GroupOrderView>(`${this.baseUrl}/reservations/${reservationId}/group-order/submit`, { revision });
  }
  remove(groupId: string, sessionId: string) {
    return this.http.delete<unknown>(`${this.baseUrl}/guest/groups/${groupId}/sessions/${sessionId}`);
  }
}

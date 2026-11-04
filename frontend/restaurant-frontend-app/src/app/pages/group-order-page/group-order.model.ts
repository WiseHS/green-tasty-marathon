import { DishListResponse } from '@app/models/dishes/dish-list-response.model';
export interface GroupDish { dish: DishListResponse; quantity: number; total: number; }
export interface GroupParticipant {
  id: string; name: string; isOwner: boolean; isSubmitted: boolean; included: boolean;
  items: GroupDish[]; total: number;
}
export interface GroupOrderView {
  reservationId: string; groupReservationId: string; tableName: string; locationAddress: string;
  startTime: string; status: 'open' | 'submitted'; canSubmit: boolean; blockReason: string | null;
  participants: GroupParticipant[]; consolidatedItems: GroupDish[]; total: number;
  readyGuests: number; joinedGuests: number; invitedGuests: number; orderId: string | null; revision: string;
}

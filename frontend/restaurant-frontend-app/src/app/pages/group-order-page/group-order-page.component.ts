import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, exhaustMap, merge, switchMap, timer } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { GroupOrderService } from './group-order.service';
import { GroupOrderView } from './group-order.model';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { getServerErrorMessage } from '@app/shared/helpers/http-error-helpers';
@Component({
  selector: 'app-group-order-page', standalone: true,
  imports: [CurrencyPipe, DatePipe, RouterLink],
  templateUrl: './group-order-page.component.html', styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(GroupOrderService);
  private readonly orders = inject(PreOrdersService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refresh = new Subject<void>();
  readonly view = signal<GroupOrderView | null>(null);
  readonly error = signal('');
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly reviewed = signal(false);
  readonly removing = signal<string | null>(null);
  readonly specialRequests = computed(() => (this.view()?.participants ?? [])
    .filter(participant => participant.included)
    .flatMap(participant => participant.items.filter(item => !!item.note)
      .map(item => ({guest: participant.name, guestId: participant.id, ...item}))));
  ngOnInit(): void {
    this.route.paramMap.pipe(
      switchMap(parameters => {
        this.view.set(null); this.loading.set(true); this.reviewed.set(false);
        return merge(timer(0, 15000), this.refresh).pipe(
          exhaustMap(() => this.service.get(parameters.get('reservationId') ?? '').pipe(
            catchError((error: HttpErrorResponse) => {
              this.loading.set(false);
              this.error.set(getServerErrorMessage(error) || 'Could not load the group order. Try again.');
              this.reviewed.set(false);
              return EMPTY;
            }),
          )),
        );
      }), takeUntilDestroyed(this.destroyRef),
    ).subscribe(view => {
      const previous = this.view();
      if (previous?.status === 'submitted' && view.status === 'open') return;
      if (previous && JSON.stringify(previous) !== JSON.stringify(view)) this.reviewed.set(false);
      this.view.set(view); this.error.set(''); this.loading.set(false);
    });
  }
  retry(): void { this.refresh.next(); }
  setReviewed(event: Event): void { this.reviewed.set((event.target as HTMLInputElement).checked); }
  submit(): void {
    const view = this.view();
    if (!view?.canSubmit || !this.reviewed() || this.busy() || this.error()) return;
    this.busy.set(true);
    this.service.submit(view.reservationId, view.revision).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => { this.view.set(result); this.busy.set(false); this.orders.cartChanged.next(); },
      error: (error: HttpErrorResponse) => {
        this.error.set(getServerErrorMessage(error) || 'Could not send the group order. Try again.');
        this.busy.set(false); this.reviewed.set(false);
      },
    });
  }
  remove(): void {
    const view = this.view(); const sessionId = this.removing();
    if (!view || view.status !== 'open' || !sessionId || this.busy()) return;
    this.busy.set(true);
    this.service.remove(view.groupReservationId, sessionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.removing.set(null); this.reviewed.set(false); this.retry(); },
      error: (error: HttpErrorResponse) => {
        this.error.set(getServerErrorMessage(error) || 'Could not remove the guest.'); this.busy.set(false);
      },
    });
  }
}

import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { GuestCartItem } from '@app/pages/guest-pre-order-page/state/guest-preorder.store'; 

@Component({
    standalone: true,
    selector: 'app-guest-cart-drawer',
    templateUrl: './guest-cart-drawer.component.html',
    styleUrl: './guest-cart-drawer.component.scss',
    imports: [CurrencyPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GuestCartDrawerComponent {
    readonly open = input(false);
    readonly items = input<GuestCartItem[]>([]);
    readonly total = input(0);
    readonly busy = input(false);
    readonly error = input<string | null>(null);

    readonly closed = output<void>();
    readonly goToMenu = output<void>();
    readonly inc = output<string>();
    readonly dec = output<string>();
    readonly remove = output<string>();
    readonly submitted = output<void>();
    readonly requestSaved = output<{dishId: string; note: string}>();
    readonly retryRequests = output<void>();
    readonly drafts = signal<Record<string, string>>({});
    readonly hasUnsavedRequests = computed(() => this.items().some(item => this.noteText(item) !== (item.note ?? '')));
    constructor() {
        effect(() => {
            const ids = new Set(this.items().map(item => item.dishId));
            if (Object.keys(this.drafts()).some(id => !ids.has(id)))
                this.drafts.update(drafts => Object.fromEntries(Object.entries(drafts).filter(([id]) => ids.has(id))));
        });
    }

    noteText(item: GuestCartItem): string { return this.drafts()[item.dishId] ?? item.note ?? ''; }
    changeNote(item: GuestCartItem, event: Event): void {
        const note = (event.target as HTMLTextAreaElement).value;
        this.drafts.update(drafts => ({...drafts, [item.dishId]: note}));
    }
    discardNote(item: GuestCartItem): void {
        this.drafts.update(drafts => ({...drafts, [item.dishId]: item.note ?? ''}));
    }
    saveNote(item: GuestCartItem): void {
        const raw = this.noteText(item);
        if (raw.length <= 300) {
            const note = raw.replace(/\r\n/g, '\n').trim();
            this.drafts.update(drafts => ({...drafts, [item.dishId]: note}));
            this.requestSaved.emit({dishId: item.dishId, note});
        }
    }
}

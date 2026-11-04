import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GuestCartDrawerComponent } from './guest-cart-drawer.component';

describe('Guest dish requests', () => {
  let fixture: ComponentFixture<GuestCartDrawerComponent>;
  const item = {dishId: 'pasta', title: 'Pasta', qty: 2, price: 18, note: ''};
  beforeEach(async () => {
    await TestBed.configureTestingModule({imports: [GuestCartDrawerComponent]}).compileComponents();
    fixture = TestBed.createComponent(GuestCartDrawerComponent);
    fixture.componentRef.setInput('open', true); fixture.componentRef.setInput('items', [item]); fixture.detectChanges();
  });
  afterEach(() => fixture.destroy());
  function type(note: string): void {
    const input = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    input.value = note; input.dispatchEvent(new Event('input')); fixture.detectChanges();
  }
  it('blocks submission until the request is saved and acknowledged by the server', () => {
    const received = jasmine.createSpy('requestSaved'); fixture.componentInstance.requestSaved.subscribe(received);
    type('  No onions  ');
    expect(fixture.nativeElement.querySelector('.submit').disabled).toBeTrue();
    fixture.componentInstance.saveNote(item);
    expect(received).toHaveBeenCalledWith({dishId: 'pasta', note: 'No onions'});
    expect(fixture.componentInstance.hasUnsavedRequests()).toBeTrue();
    fixture.componentRef.setInput('items', [{...item, note: 'No onions'}]); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.submit').disabled).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('Request saved');
  });
  it('discards changes without saving and restores the saved request', () => {
    fixture.componentRef.setInput('items', [{...item, note: 'Sauce separately'}]); fixture.detectChanges();
    type('No onions'); fixture.componentInstance.discardNote({...item, note: 'Sauce separately'}); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('textarea').value).toBe('Sauce separately');
    expect(fixture.nativeElement.querySelector('.submit').disabled).toBeFalse();
  });
  it('supports clearing a saved request and rejects oversized drafts', () => {
    const received = jasmine.createSpy('requestSaved'); fixture.componentInstance.requestSaved.subscribe(received);
    fixture.componentRef.setInput('items', [{...item, note: 'No onions'}]); fixture.detectChanges();
    type(''); fixture.componentInstance.saveNote({...item, note: 'No onions'});
    expect(received).toHaveBeenCalledWith({dishId: 'pasta', note: ''});
    received.calls.reset(); type('x'.repeat(301)); fixture.componentInstance.saveNote(item);
    expect(received).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('textarea').maxLength).toBe(300);
  });
  it('keeps the draft after a failed save and blocks submission until recovery', () => {
    type('No onions'); fixture.componentRef.setInput('error', 'Connection failed'); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('textarea').value).toBe('No onions');
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Connection failed');
    expect(fixture.nativeElement.querySelector('.submit').disabled).toBeTrue();
  });
  it('does not restore the previous draft when a removed dish is re-added', () => {
    type('No onions'); fixture.componentRef.setInput('items', []); fixture.detectChanges();
    fixture.componentRef.setInput('items', [item]); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('textarea').value).toBe('');
  });
});

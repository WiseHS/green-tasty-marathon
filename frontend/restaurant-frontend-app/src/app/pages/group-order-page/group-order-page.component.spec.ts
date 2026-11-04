import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { GroupOrderPageComponent } from './group-order-page.component';
import { GroupOrderView } from './group-order.model';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

describe('GroupOrderPageComponent', () => {
  let fixture: ComponentFixture<GroupOrderPageComponent>;
  let http: HttpTestingController;
  const view: GroupOrderView = {
    reservationId: 'reservation', groupReservationId: 'group', tableName: 'Table 1', locationAddress: 'Kyiv',
    startTime: '2026-12-01T12:00:00Z', status: 'open', canSubmit: true, blockReason: null,
    participants: [
      { id: 'owner', name: 'Your order', isOwner: true, isSubmitted: false, included: true, items: [], total: 0 },
      { id: 'guest', name: 'Mary', isOwner: false, isSubmitted: true, included: true, items: [], total: 0 },
      { id: 'draft', name: 'John', isOwner: false, isSubmitted: false, included: false, items: [], total: 0 },
    ], consolidatedItems: [], total: 36, readyGuests: 1, joinedGuests: 2, invitedGuests: 2, orderId: null, revision: 'reviewed-version',
  };
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: {paramMap: of(convertToParamMap({reservationId: 'reservation'}))} },
        { provide: API_BASE_URL, useValue: 'https://test.api' }],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GroupOrderPageComponent);
  });
  afterEach(() => { fixture.destroy(); http.verify(); });
  const endpoint = 'https://test.api/reservations/reservation/group-order';
  it('shows participants and excluded drafts, requiring review before sending', fakeAsync(() => {
    fixture.detectChanges(); tick(); http.expectOne(endpoint).flush(view); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Mary');
    expect(fixture.nativeElement.textContent).toContain('Draft · excluded');
    expect(fixture.nativeElement.querySelector('.send').disabled).toBeTrue();
    fixture.componentInstance.reviewed.set(true); fixture.detectChanges();
    fixture.nativeElement.querySelector('.send').click();
    const submission = http.expectOne(endpoint + '/submit');
    expect(submission.request.method).toBe('POST');
    expect(submission.request.body).toEqual({ revision: 'reviewed-version' });
    submission.flush({ ...view, status: 'submitted', canSubmit: false, orderId: 'order' }); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.send')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Group order sent');
    fixture.destroy();
  }));
  it('polls every 15 seconds and clears review when participant selections change', fakeAsync(() => {
    fixture.detectChanges(); tick(); http.expectOne(endpoint).flush(view);
    fixture.componentInstance.reviewed.set(true);
    tick(15000); http.expectOne(endpoint).flush({...view, revision: 'changed-version', total: 48});
    expect(fixture.componentInstance.reviewed()).toBeFalse();
    expect(fixture.componentInstance.view()?.total).toBe(48);
    fixture.destroy(); tick(15000); http.expectNone(endpoint);
  }));
  it('shows loading failure and can retry', fakeAsync(() => {
    fixture.detectChanges(); tick();
    http.expectOne(endpoint).flush({error: 'Access denied'}, {status: 403, statusText: 'Forbidden'});
    fixture.detectChanges(); expect(fixture.nativeElement.textContent).toContain('Access denied');
    fixture.componentInstance.retry(); http.expectOne(endpoint).flush(view);
    expect(fixture.componentInstance.error()).toBe('');
    fixture.destroy();
  }));
  it('keeps the selection open when submitting fails', fakeAsync(() => {
    fixture.detectChanges(); tick(); http.expectOne(endpoint).flush(view);
    fixture.componentInstance.reviewed.set(true); fixture.componentInstance.submit();
    http.expectOne(endpoint + '/submit').flush({error: 'Selections changed'}, {status: 409, statusText: 'Conflict'});
    expect(fixture.componentInstance.error()).toBe('Selections changed');
    expect(fixture.componentInstance.busy()).toBeFalse();
    expect(fixture.componentInstance.reviewed()).toBeFalse();
    fixture.destroy();
  }));
  it('removes a guest only after explicit confirmation and reloads the total', fakeAsync(() => {
    fixture.detectChanges(); tick(); http.expectOne(endpoint).flush(view);
    fixture.componentInstance.removing.set('guest'); fixture.componentInstance.remove();
    const deletion=http.expectOne('https://test.api/guest/groups/group/sessions/guest');
    expect(deletion.request.method).toBe('DELETE'); deletion.flush({});
    http.expectOne(endpoint).flush({...view, total: 0, readyGuests: 0});
    expect(fixture.componentInstance.view()?.total).toBe(0);
    expect(fixture.componentInstance.removing()).toBeNull();
    fixture.destroy();
  }));
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { GuestSessionsService } from './guest-sessions.service';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

describe('Guest dish request API', () => {
  let service: GuestSessionsService, http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({providers: [provideHttpClient(), provideHttpClientTesting(),
      {provide: API_BASE_URL, useValue: 'https://test.api'}]});
    service = TestBed.inject(GuestSessionsService); http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('loads requests using the guest credential', () => {
    let notes: unknown;
    service.getDishRequests('guest', 'guest-token').subscribe(result => notes = result.notes);
    const request = http.expectOne('https://test.api/guest/sessions/guest/dish-requests');
    expect(request.request.method).toBe('GET'); expect(request.request.headers.get('Authorization')).toBe('Bearer guest-token');
    request.flush({notes: [{dishId: 'pasta', note: 'No onions'}]});
    expect(notes).toEqual([{dishId: 'pasta', note: 'No onions'}]);
  });
  it('sends only the selected dish request and propagates lock errors', () => {
    let status = 0;
    service.saveDishRequest('guest', 'guest-token', 'pasta', 'No onions').subscribe({error: error => status = error.status});
    const request = http.expectOne('https://test.api/guest/sessions/guest/dish-requests/pasta');
    expect(request.request.method).toBe('PUT'); expect(request.request.body).toEqual({note: 'No onions'});
    expect(request.request.headers.get('Authorization')).toBe('Bearer guest-token');
    request.flush({error: 'Selection locked'}, {status: 409, statusText: 'Conflict'}); expect(status).toBe(409);
  });
});

import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';

const URL = 'https://test.api/reservations';

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    const authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.returnValue(null);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('uses the owner token when removing a participant from a group', () => {
    const auth = TestBed.inject(AuthenticationService) as jasmine.SpyObj<AuthenticationService>;
    auth.getTokens.and.returnValue({ idToken: 'owner-token' } as ReturnType<AuthenticationService['getTokens']>);
    http.delete('https://test.api/guest/groups/group-id/sessions/session-id').subscribe();
    const request = httpMock.expectOne('https://test.api/guest/groups/group-id/sessions/session-id');
    expect(request.request.headers.get('Authorization')).toBe('Bearer owner-token');
    request.flush({});
  });

  it('preserves guest session credentials and the guest request body', () => {
    const auth = TestBed.inject(AuthenticationService) as jasmine.SpyObj<AuthenticationService>;
    auth.getTokens.and.returnValue({ idToken: 'owner-token' } as ReturnType<AuthenticationService['getTokens']>);
    const body = {dishId: 'demo-pasta', quantity: 2};
    http.post('https://test.api/guest/sessions/guest-id/dishes', body,
      {headers: {Authorization: 'Bearer guest-token'}}).subscribe();
    const request = httpMock.expectOne('https://test.api/guest/sessions/guest-id/dishes');
    expect(request.request.headers.get('Authorization')).toBe('Bearer guest-token');
    expect(request.request.body).toEqual(body);
    request.flush({});
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });
});

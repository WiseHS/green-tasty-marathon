param([string]$BaseUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference='Stop'
$checks=0
function Request([string]$Method,[string]$Path,$Body=$null,$Headers=@{}) {
    $parameters=@{Uri="$BaseUrl$Path";Method=$Method;Headers=$Headers;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$parameters.ContentType='application/json';$parameters.Body=$Body|ConvertTo-Json -Depth 12}
    $response=Invoke-WebRequest @parameters
    $json=if($response.Content){$response.Content|ConvertFrom-Json}else{$null}
    return @{Status=$response.StatusCode;Body=$json}
}
function Expect($Response,[int]$Status,[string]$Name) {
    if($Response.Status -ne $Status){throw "$Name expected $Status, received $($Response.Status): $($Response.Body|ConvertTo-Json -Compress -Depth 5)"}
    $script:checks++;Write-Output "PASS $Name ($Status)"
}
$health=Request GET '/health/ready';Expect $health 200 'PostgreSQL readiness'
$locations=Request GET '/locations';Expect $locations 200 'Read locations'
$dishes=Request GET '/dishes';Expect $dishes 200 'Read menu'
Expect (Request GET '/me') 401 'Anonymous profile denied'
$email='smoke-'+[guid]::NewGuid().ToString('N')+'@example.com'
$password='Test!A1'+[guid]::NewGuid().ToString('N').Substring(0,8)
$account=@{firstName='Smoke';lastName='Tester';email=$email;password=$password;confirmPassword=$password}
Expect (Request POST '/register' $account) 200 'Register customer'
Expect (Request POST '/signin' @{email=$email;password='Wrong!A123'}) 401 'Wrong password denied'
$login=Request POST '/signin' @{email=$email;password=$password};Expect $login 200 'Sign in'
$headers=@{Authorization="Bearer $($login.Body.idToken)"}
$profile=Request GET '/me' $null $headers;Expect $profile 200 'Read own profile'
Expect (Request POST '/locations' @{address='Unauthorized'} $headers) 403 'Customer cannot create location'
$start=(Get-Date).ToUniversalTime().Date.AddDays((Get-Random -Minimum 3 -Maximum 300)).AddHours(12)
$reservation=@{tableId=('demo-table-'+(Get-Random -Minimum 1 -Maximum 7));startTime=$start.ToString('o');endTime=$start.AddMinutes(90).ToString('o');guestCount=3}
$booking=Request POST '/reservations' $reservation $headers;Expect $booking 201 'Create reservation'
$reservationId=$booking.Body.id
if(-not $reservationId){$reservationId=$booking.Body.reservationId}
if(-not $reservationId){throw 'Reservation response did not contain an identifier'}
Expect (Request GET "/reservations/$reservationId" $null $headers) 200 'Reload persisted reservation'
Expect (Request POST '/reservations' $reservation $headers) 400 'Overlapping reservation denied'
$parallelReservation=$reservation.Clone();$parallelStart=$start.AddDays(1)
$parallelReservation.startTime=$parallelStart.ToString('o');$parallelReservation.endTime=$parallelStart.AddMinutes(90).ToString('o')
$client=[Net.Http.HttpClient]::new()
try {
    $client.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$login.Body.idToken)
    $json=$parallelReservation|ConvertTo-Json
    $first=$client.PostAsync("$BaseUrl/reservations",[Net.Http.StringContent]::new($json,[Text.Encoding]::UTF8,'application/json'))
    $second=$client.PostAsync("$BaseUrl/reservations",[Net.Http.StringContent]::new($json,[Text.Encoding]::UTF8,'application/json'))
    $responses=[Threading.Tasks.Task]::WhenAll([Threading.Tasks.Task[Net.Http.HttpResponseMessage][]]@($first,$second)).GetAwaiter().GetResult()
    $statuses=($responses|ForEach-Object{[int]$_.StatusCode}|Sort-Object)-join ','
    if($statuses -ne '201,400'){throw "Concurrent booking expected 201,400; received $statuses"}
    $checks++;Write-Output 'PASS Concurrent booking admits exactly one request'
} finally { $client.Dispose() }
$order=Request POST '/orders' @{reservationId=$reservationId;orderItems=@(@{dishId='demo-pasta';quantity=2})} $headers
if($order.Status -notin @(200,201)){throw "Create order failed: $($order.Body|ConvertTo-Json -Compress -Depth 6)"}
$checks++;Write-Output "PASS Create pre-order ($($order.Status))"
Expect (Request GET "/orders/$reservationId" $null $headers) 200 'Reload persisted pre-order'
$invite=Request GET "/reservations/$reservationId/invite-token" $null $headers;Expect $invite 200 'Owner invite token'
$group=Request GET "/guest/invites/$($invite.Body.token)";Expect $group 200 'Open guest invitation'
$session=Request POST '/guest/sessions' @{groupReservationId=$group.Body.groupReservationId;guestName='Guest Tester'}
Expect $session 200 'Create guest session'
$guestHeaders=@{Authorization="Bearer $($session.Body.sessionToken)"}
$sessionId=$session.Body.sessionId
Expect (Request POST "/guest/sessions/$sessionId/dishes" @{dishId='demo-pasta';quantity=2} $guestHeaders) 200 'Guest adds dish'
$cart=Request GET "/guest/sessions/$sessionId" $null $guestHeaders;Expect $cart 200 'Guest cart persists'
if($cart.Body.total -ne 36){throw "Guest total expected 36, received $($cart.Body.total)"}
$checks++;Write-Output 'PASS Guest total is 36'
Expect (Request POST "/guest/sessions/$sessionId/submit" @{} $guestHeaders) 200 'Guest submits cart'
$png='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jXioAAAAASUVORK5CYII='
Expect (Request PUT '/me/profile-image' @{base64Image=$png} $headers) 200 'Upload profile image'
$photo=Request GET "/users/$($profile.Body.id)/profile-image" $null $headers;Expect $photo 200 'Retrieve private image URL'
$download=Invoke-WebRequest -Uri $photo.Body.presignedUrl -SkipHttpErrorCheck
if($download.StatusCode -ne 200 -or $download.Headers['Content-Type'] -notmatch 'image/png'){throw 'Image download failed'}
$checks++;Write-Output 'PASS Signed image download'
$unsigned=Invoke-WebRequest -Uri ($photo.Body.presignedUrl.Split('?')[0]) -SkipHttpErrorCheck
if($unsigned.StatusCode -ne 403){throw 'Unsigned private image should be denied'}
$checks++;Write-Output 'PASS Unsigned private image denied'
$otherEmail='other-'+[guid]::NewGuid().ToString('N')+'@example.com'
$account.email=$otherEmail
Expect (Request POST '/register' $account) 200 'Register unrelated customer'
$otherLogin=Request POST '/signin' @{email=$otherEmail;password=$password}
$otherHeaders=@{Authorization="Bearer $($otherLogin.Body.idToken)"}
Expect (Request GET "/reservations/$reservationId/invite-token" $null $otherHeaders) 403 'Foreign invitation denied'
Expect (Request GET "/orders/$reservationId" $null $otherHeaders) 403 'Foreign order denied'
Expect (Request GET "/users/$($profile.Body.id)/profile-image" $null $otherHeaders) 403 'Foreign private image denied'
Expect (Request GET '/users' $null $otherHeaders) 403 'Customer cannot list accounts'
$changed=Request PUT '/me/change-password' @{oldPassword=$password;newPassword='Updated!Pass123';accessToken=$login.Body.accessToken} $headers
Expect $changed 200 'Change password'
Expect (Request GET '/me' $null $headers) 401 'Old token revoked after password change'
Write-Output "Smoke checks passed: $checks"

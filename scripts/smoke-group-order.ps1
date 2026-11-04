param([string]$BaseUrl='http://127.0.0.1:5080', [string]$ContextFile='')
$ErrorActionPreference='Stop'
. "$PSScriptRoot/smoke-api.ps1" -BaseUrl $BaseUrl
$login=Request POST '/signin' @{email=$email;password='Updated!Pass123'}
$headers=@{Authorization="Bearer $($login.Body.idToken)"}
$groupId=$group.Body.groupReservationId
if($env:DEMO_WAITER_PASSWORD){
  $staff=Request POST '/signin' @{email='demo-waiter@example.com';password=$env:DEMO_WAITER_PASSWORD}
  Expect $staff 200 'Sign in demo waiter for assigned test reservation'
  $staffHeaders=@{Authorization="Bearer $($staff.Body.idToken)"}
  $assigned=$reservation.Clone(); $assigned.customerEmail=$email
  $assignedStart=$start.AddDays(2); $assigned.startTime=$assignedStart.ToString('o'); $assigned.endTime=$assignedStart.AddMinutes(90).ToString('o')
  $assignedResult=Request POST '/reservations' $assigned $staffHeaders
  Expect $assignedResult 201 'Create group reservation assigned to demo waiter'
  $reservationId=$assignedResult.Body.id
  $invite=Request GET "/reservations/$reservationId/invite-token" $null $headers
  $group=Request GET "/guest/invites/$($invite.Body.token)"
  $groupId=$group.Body.groupReservationId
  Expect (Request POST '/orders' @{reservationId=$reservationId;orderItems=@(@{dishId='demo-pasta';quantity=2})} $headers) 201 'Create organizer selection for assigned reservation'
  $session=Request POST '/guest/sessions' @{groupReservationId=$groupId;guestName='Guest Tester'}
  $sessionId=$session.Body.sessionId; $guestHeaders=@{Authorization="Bearer $($session.Body.sessionToken)"}
  Expect (Request POST "/guest/sessions/$sessionId/dishes" @{dishId='demo-pasta';quantity=2} $guestHeaders) 200 'Add assigned guest selection'
  Expect (Request POST "/guest/sessions/$sessionId/submit" @{} $guestHeaders) 200 'Submit assigned guest selection'
}
if($ContextFile){@{email=$email;reservationId=$reservationId;groupId=$groupId} | ConvertTo-Json | Set-Content -LiteralPath $ContextFile}
Expect (Request GET "/reservations/$reservationId/group-order" $null $otherHeaders) 403 'Unrelated customer cannot read group'
$draft=Request POST '/guest/sessions' @{groupReservationId=$groupId;guestName='Draft Guest'}
Expect $draft 200 'Join second guest'
$draftId=$draft.Body.sessionId
$draftHeaders=@{Authorization="Bearer $($draft.Body.sessionToken)"}
Expect (Request POST "/guest/sessions/$draftId/dishes" @{dishId='demo-dessert';quantity=3} $draftHeaders) 200 'Draft guest selects dessert'
$view=Request GET "/reservations/$reservationId/group-order" $null $headers
Expect $view 200 'Owner sees all participant selections'
if($view.Body.participants.Count -ne 3 -or $view.Body.total -ne 72 -or $view.Body.consolidatedItems[0].quantity -ne 4){throw 'Expected owner + ready guest + excluded draft, pasta quantity 4 and total 72'}
$checks++;Write-Output 'PASS Submitted guests only consolidated; shared dishes merged'
$stale=$view.Body.revision
Expect (Request POST "/guest/sessions/$draftId/dishes" @{dishId='demo-dessert';quantity=1} $draftHeaders) 200 'Guest draft changes while owner reviews'
Expect (Request POST "/reservations/$reservationId/group-order/submit" @{revision=$stale} $headers) 409 'Changed selection requires review again'
$view=Request GET "/reservations/$reservationId/group-order" $null $headers
Expect (Request POST "/reservations/$reservationId/group-order/submit" @{revision=$view.Body.revision} $otherHeaders) 403 'Foreign group submission denied'
$sent=Request POST "/reservations/$reservationId/group-order/submit" @{revision=$view.Body.revision} $headers
Expect $sent 200 'Owner sends consolidated group order'
if($sent.Body.status -ne 'submitted' -or $sent.Body.total -ne 72){throw 'Invalid submitted snapshot'}
$retry=Request POST "/reservations/$reservationId/group-order/submit" @{revision=$view.Body.revision} $headers
Expect $retry 200 'Retry group submission succeeds without duplicates'
if($retry.Body.orderId -ne $sent.Body.orderId){throw 'Retry created another order'}
$persisted=Request GET "/orders/$reservationId" $null $headers
if($persisted.Body.orderItems.Count -ne 1 -or $persisted.Body.orderItems[0].quantity -ne 4){throw 'Waiter order must contain 4 pasta portions only'}
$checks++;Write-Output 'PASS Ordinary order contains the full consolidated selection'
Expect (Request POST "/orders/$reservationId/unsubmit" @{} $headers) 409 'Submitted group cannot be reopened through personal cart'
$after=Request POST "/guest/sessions/$draftId/dishes" @{dishId='demo-dessert';quantity=1} $draftHeaders
if($after.Status -lt 400){throw 'Guest cart should lock after group submission'}
$checks++;Write-Output 'PASS Guest editing locked after group submission'
$removed=Request DELETE "/guest/groups/$groupId/sessions/$sessionId" $null $headers
if($removed.Status -lt 400){throw 'Guest removal must be rejected after submission'}
$checks++;Write-Output 'PASS Submitted participant cannot be removed'
if($env:DEMO_WAITER_PASSWORD){
  $staff=Request POST '/signin' @{email='demo-waiter@example.com';password=$env:DEMO_WAITER_PASSWORD}
  Expect $staff 200 'Sign in assigned waiter'
  $staffHeaders=@{Authorization="Bearer $($staff.Body.idToken)"}
  $staffProfile=Request GET '/me' $null $staffHeaders
  $bookingView=Request GET "/reservations/$reservationId" $null $headers
  if($bookingView.Body.waiterId -and $staffProfile.Body.id -ne $bookingView.Body.waiterId){throw 'This test reservation was assigned to a different waiter. Use a clean demo branch with only the seeded waiter.'}
  $staffView=Request GET "/reservations/$reservationId/group-order" $null $staffHeaders
  Expect $staffView 200 'Waiter reads submitted participant breakdown'
  Expect (Request GET "/orders/$reservationId" $null $staffHeaders) 200 'Waiter reads consolidated order'
  Expect (Request POST "/reservations/$reservationId/group-order/submit" @{revision=$view.Body.revision} $staffHeaders) 403 'Waiter cannot submit as owner'
  Expect (Request PATCH "/reservations/$reservationId/order/confirmations/demo-pasta" @{} $staffHeaders) 200 'Waiter confirms consolidated dish'
  $reload=Request GET "/orders/$reservationId" $null $staffHeaders
  if($reload.Body.orderItems[0].state -ne 1){throw 'Dish confirmation state should survive reload'}
  $checks++;Write-Output 'PASS Waiter confirmation state persists'
}
Write-Output "Combined smoke checks passed: $checks"

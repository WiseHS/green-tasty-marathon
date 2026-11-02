# Render + Neon

## Architecture and scope

Angular calls `Restaurant.Web`, an ASP.NET Core .NET 10 HTTP host that reuses the original API handlers. Routes remain compatible with the existing frontend. PostgreSQL repositories replace DynamoDB. A compatibility boundary keeps Cognito-shaped DTOs inside the old handlers, but identity lives in `app_users`; passwords use ASP.NET Identity's salted PBKDF2 hashes and signed JWTs expire after one hour. Password changes invalidate previous tokens. Registration retains the original immediate-confirmation behavior; no verification email is sent.

Domain entities are stored as JSONB documents with parameterized SQL; images are bounded binary records in PostgreSQL. The schema is versioned under `backend/Restaurant.Web/Migrations`. Profile images require short-lived signed URLs. Catalog images are public. Core write workflows run in PostgreSQL transactions, with an advisory lock and a local writer gate to prevent lost cart updates and double booking. This serialized strategy is intended for the small demonstration deployment, not a high-throughput service.

Completed-reservation reports are stored and aggregated synchronously instead of going through SQS. The separate AWS scheduled email/report-sender Lambda is not deployed by this configuration; email delivery is not provided. Report export and all staff/admin workflows still need their own end-to-end review.

The original Lambda/Terraform source and EPAM attribution remain in the repository. This migration is deployment preparation; it is not claimed as the additional competition feature.

## Local prerequisites and secrets

- .NET SDK 10 and Node.js compatible with frontend `engines` (tested: Node 24.22.2).
- Existing Neon project; use a development database branch for tests.
- Root `.env.local`, ignored by Git, containing `DATABASE_URL`, `DATABASE_URL_UNPOOLED`, `JWT_SIGNING_KEY`, and optionally `DEMO_WAITER_PASSWORD`. See `.env.example` for names only. Use a random signing secret of at least 32 bytes.

Local env loading is enabled only with `ASPNETCORE_ENVIRONMENT=Development` and a working directory of `backend/Restaurant.Web`. Production uses injected environment variables. Database URLs belong exclusively on the server. TLS verifies the certificate and hostname. The app uses pooled connections, and migrations use `DATABASE_URL_UNPOOLED`.

The current local context uses `dev-render-neon`, created from `production` with a seven-day TTL. Do not use this temporary branch for the public deployment. Production has not been migrated or seeded by this development work.

## Local launch (PowerShell)

From `backend/Restaurant.Web`:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
dotnet run -- --migrate
dotnet run -- --seed-demo
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'
dotnet run
```

Seed is explicit and idempotent; it creates one demo restaurant, six four-seat tables, three menu dishes, and the synthetic `demo-waiter@example.com` account. Set your own private `DEMO_WAITER_PASSWORD` before seeding. It is not published in the repository. Demo photos are shared placeholder assets, not separate photographs for every dish.

From `frontend/restaurant-frontend-app` in another terminal:

```powershell
npm ci
npm start
```

Open `http://localhost:4200`. The development environment points to `http://127.0.0.1:5080`.

## Validation

From the repository root with the backend running against the development branch:

```powershell
./scripts/smoke-api.ps1
dotnet test backend/restaurant-backend-app/Tests/APIHandler.Tests.csproj
dotnet publish backend/Restaurant.Web/Restaurant.Web.csproj -c Release
```

The smoke script creates synthetic users, reservations and images in the selected database. Do not point it at real customer data. It checks simultaneous booking, guest cart totals/submission, persisted orders, access denial for unrelated users, signed image download and token revocation.

Frontend checks, from its directory:

```powershell
npx tsc --noEmit -p tsconfig.app.json
npx tsc --noEmit -p tsconfig.spec.json
$env:BACKEND_URL='http://127.0.0.1:5080'
npm run build
npm test -- --watch=false --browsers=ChromeHeadless
```

Verified: 146 backend tests; 33 API checks on a published Release build; TypeScript checks and frontend build passed. Browser registration/login and menu/location loading passed. Frontend tests remain 195 passing/47 failing; rerunning with the original development config produced the identical 47 failing test names. Bundle size remains 789.28 kB; existing style-budget and CommonJS warnings remain.

Docker is not installed on the development machine, so the Linux Docker build and actual Render deployment remain unverified. Original report dependencies still include an ImageSharp version with NuGet advisories through NPOI; it is no longer used to decode uploaded user images. XML/PKCS cryptography dependencies were updated. Remaining dependency advisories need a separate dependency review before production use.

A subsequent final Release publish also succeeded, but Windows application-control policy blocked running that new published DLL. The earlier published Release build passed 33 checks; current-source checks are repeated through the normal SDK development launch. No Windows security policy was disabled or changed.

## Render deployment preparation

`render.yaml` declares a free Docker backend in Frankfurt and a static Angular site. Do not assume the suggested service names will be the exact public URLs: verify the URLs assigned by Render.

The migration is prepared on Git branch `feature/render-neon`. Select that branch for a deployment preview, or merge it after review before deploying from `main`.

1. Select the permanent `production` Neon branch, review its schema plan, apply migrations through the direct connection, and explicitly seed demonstration data if needed. These are separate from the local development checks.
2. Deploy the backend from `backend/Restaurant.Web/Dockerfile`, with repository root as build context. Set pooled `DATABASE_URL`, a random `JWT_SIGNING_KEY`, and `FRONTEND_ORIGIN` to the actual frontend origin. Render's `RENDER_EXTERNAL_URL` supplies the backend origin for image URLs; set `PUBLIC_BACKEND_URL` explicitly if using another hostname.
3. Deploy the static frontend with `BACKEND_URL` set to the actual backend HTTPS URL. Build: `npm ci --no-audit --no-fund && npm run build`. Publish directory: `dist/restaurant-frontend-app/browser`, relative to the frontend root. Rewrite all client routes to `/index.html`.
4. Verify `/health/ready`, browser registration/login, images, booking, guest invite/cart, and order submission on the real public URLs. Check CORS using the actual frontend origin.

The free backend can sleep between requests. Recheck account quotas and provider terms at deployment time. No Render services have been created by the local migration work.

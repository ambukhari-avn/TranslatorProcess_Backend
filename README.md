# Translation Backend

ASP.NET Core (.NET 10) API in front of the extraction (FastAPI) service. It signs people in with Microsoft, stores
their uploads, starts translations, follows their progress, serves the results and keeps a history.
Layout and sign-in follow the SupplyChain backend, so the same frontend auth screens work unchanged.

```
API/             controllers, filters, middleware, SignalR hub
Application/     DTOs, service interfaces, services (auth, employees, audit, jobs), options
Domain/          entities (Users, TranslationJob, AuditLog) and enums
Infrastructure/  EF Core context + migrations, repositories, Microsoft / extraction clients, file storage, background services
Tests/           xUnit: unit tests and in-memory API tests
```

## Run it

1. **Database.** SQL Server, Windows authentication: `ConnectionStrings:DefaultConnection` (default
   `Server=localhost;Database=TranslationService;...`). In Development `Database:MigrateOnStartup` is `true`, so the first start
   creates the database and applies the migration. Elsewhere run `dotnet ef database update`.
2. **Secrets.** `appsettings.Development.json` (git-ignored) holds the local values: a generated `Jwt:Key`, the Microsoft
   `ClientId` and `TenantId`, `Storage:Root`. **Fill in `Microsoft:ClientSecret`** (from the app registration; the
   registration must list this frontend's redirect URI, `Microsoft:RedirectUri`). In production use environment
   variables (`Jwt__Key`, `Microsoft__ClientSecret`, `Extraction__ApiKey`) or user-secrets, never the repo.
3. **Shared folder.** `Storage:Root` must be the same folder as `EXTRACTION_FILES_ROOT` of the extraction service
   (both read and write `<jobId>/input` and `<jobId>/output`). `Extraction:ApiKey` must equal its `EXTRACTION_API_KEY`.
4. **Start the extraction service** (`python -m uvicorn app.main:create_app --factory --port 8002`), then:

```powershell
dotnet run --project TranslationBackend.csproj        # http://localhost:5290, Swagger at /swagger
```

Sign in through the frontend (or the Microsoft callback in Swagger) to get a token, then press "Authorize" in Swagger.

## Roles and sign-in

Microsoft proves who the person is, and there is no approval step: the only check is the email domain
(`Auth:AllowedEmailDomains`, default `avanceon.ae`). The first sign-in creates an **Employee** who gets a token at once.
A Developer can still switch an account off (`PATCH /api/employees/{id}/status`); every request re-checks the account,
so a deactivated user is cut off immediately.

| Role | Can |
|---|---|
| Employee (normal user) | translate documents, see and download their own |
| SuperUser (the "Developer") | everything: switch accounts on/off, the audit log, `GET /api/history` and any user's job |

Only these two roles exist. SuperUsers are seeded from `Seed:Users` (name, email, role) at start-up; add a person and restart.

## Endpoints

| Endpoint | Who | Purpose |
|---|---|---|
| `GET /api/auth/microsoft/login`, `/register`, `/callback`, `/developers` | public | Microsoft sign-in; `/developers` lists who to contact if you are switched off |
| `POST /api/documents` (multipart: `File`, `Language`, `Verify`) | signed in | validate, store, start a translation -> 201 `Queued` |
| `GET /api/documents`, `GET /api/documents/{id}` | signed in | own jobs; status, progress, stage, counts |
| `POST /api/documents/{id}/cancel` | owner | cancel a queued or running job |
| `GET /api/documents/{id}/download`, `/review` | owner | the translated file, the review file |
| `GET /api/history` | SuperUser | everyone's jobs (filter: userId, status, search, from, to) |
| `GET/PATCH/POST /api/employees...`, `GET /api/audit-logs` | SuperUser | employee management, audit trail |
| `GET /api/health` | public | database + translation service |
| `/hubs/notifications` (SignalR) | signed in | `JobChanged`, `ReceiveNotification`, `EmployeeChanged` |

Translation errors are `{ "code": "...", "message": "..." }` (for example `UNSUPPORTED_FILE_TYPE`, `INVALID_FILE`,
`FILE_TOO_LARGE`, `TOO_MANY_ACTIVE_JOBS`, `SERVICE_UNAVAILABLE`, `JOB_NOT_FOUND`, `NOT_READY`, `FILES_EXPIRED`).
The sign-in and employee endpoints keep the SupplyChain `{ message }` / `{ error }` shapes.

## How a translation runs

1. Upload is checked (extension, size, and that the content really is a PDF or a Word file), saved to
   `<root>/<jobId>/input/source.<ext>`, and a `TranslationJob` row is created.
2. The backend calls `POST /jobs` on the extraction service with paths relative to the shared root.
3. A background poller asks the service for every Queued/Running job every `Extraction:PollSeconds`, stores progress, and
   pushes `JobChanged` over SignalR to the owner; the browser can poll `GET /api/documents/{id}` instead.
4. When the service has been unreachable for `Extraction:UnavailableMinutes` the unfinished jobs are marked Failed.
5. Files of jobs finished more than `Jobs:RetentionDays` ago are deleted; the history rows stay and show "expired".

Limits (`Jobs:*`): upload size (default 50 MB; also raise IIS `maxAllowedContentLength` when hosting there), translations
per user in progress (5), retention (30 days).

## Tests

```powershell
dotnet test Tests/TranslationBackend.Tests/TranslationBackend.Tests.csproj
```

42 tests: job rules (validation, visibility, progress, cancel, expiry), storage path safety, sign-in and approval,
seeding, JWT claims, and the whole API in memory (auth, upload, download, history, employees, health, Swagger).

# Proposal Studio

Two pieces: **ProposalStudio** (ASP.NET API) and **proposal-frontend** (Vite/React). On the public server, `deploy/build-host.ps1` bakes the UI into `wwwroot` so one process serves both. Caddy terminates HTTPS and reverse-proxies to Kestrel on `http://127.0.0.1:5080`.

---

## `Program.cs`

Host bootstrap. No application class of its own: load `.env`, register services, migrate/seed, then run the HTTP pipeline.

### Environments

| Environment | Meaning | HTTPS redirect | Swagger | SPA fallback |
|---|---|---|---|---|
| Development | Local API + Vite frontend | yes | yes (`/swagger`) | no |
| Host | Public server, HTTP on `127.0.0.1:5080` behind Caddy | no | no | `index.html` |

### Fail-fast config

Refuses to start without `ConnectionStrings__DefaultConnection`, or if `JwtSettings__SecretKey` is missing, shorter than 32 characters, or still the public sample value. Issuer/audience default to `ProposalStudio` (`JwtSettings:Issuer` / `Audience`). Connection string and JWT secret belong in `.env` (gitignored), not in `appsettings.json`.

`bin/` and `obj/` are gitignored. They are build output: compiled DLLs and copies of config. Committing them bloated the repo and could re-introduce old secrets that a previous build copied into those folders.

### Services

- **EF Core + Npgsql** — PostgreSQL is the source of truth.
- **Controllers + Newtonsoft + `JsonDocumentConverter`** — Postgres JSON columns serialize cleanly.
- **Scoped:** `PricingGovernance`, `AuditService`, `ProposalExpiryService`, `BrandStyleService` (per request).
- **Singleton:** `ObjectMediaStore`, `ProductImageStore`, `ProposalPdfService` (shared file/PDF stores).
- **Upload cap** — Kestrel and form multipart both use `ProductImageStore.MaxUploadBytes`.
- **Auth** — JWT Bearer, HMAC-signed. Issuer and audience are validated. `RequireHttpsMetadata` is off on Host (Kestrel listens on HTTP; Caddy holds the certificate). After a token is valid, `JwtSessionEvents.RejectInactiveUsers` still rejects disabled accounts.
- **Fallback authorization** — every endpoint requires a logged-in user unless it has `[AllowAnonymous]`.
- **CORS** — only if `Cors:AllowedOrigins` is non-empty. Development lists Vite (`5173`) and Vite preview (`4173`). Host is same-origin, so the list is empty and CORS is skipped.

Login tokens must carry the same issuer and audience. Changing those values (or first enabling them) invalidates existing sessions until people log in again.

### Startup jobs (before traffic)

1. `MigrateAsync()` — schema must be current. **Fatal** if it fails.
2. `DbSeeder.SeedAsync` — catalog/demo data. Logged, not fatal.
3. `AuditService.ArchiveNotificationsAsync` — leftover notifications into the audit log. Not fatal.
4. `ProposalExpiryService.ExpireOverdueAsync` — mark overdue proposals expired. Not fatal.

### Swagger (Development only)

Kept on purpose: fastest way to hit the API without the frontend.

- Registered and mapped only when `ASPNETCORE_ENVIRONMENT=Development`.
- Host never loads Swashbuckle services, so `/swagger` is not available there.
- Bearer security definition lets you paste a JWT; Swagger then sends `Authorization` like the React app.
- The `Swashbuckle.AspNetCore` package is referenced only in Debug, so it does not ship in a Host (Release) publish.

### Static files and SPA

`wwwroot/images/products` and `wwwroot/pdfs` are created if missing. `ObjectMediaStore.RestoreAsync` pulls product/brand files from object storage onto disk; `ProductImageStore.AdoptExisting` picks up images already on disk.

Static files run **before** authentication so catalog images load without a token (login prefetch). Images/PDFs get a 1-day cache and `Access-Control-Allow-Origin: *` (Vite on another origin needs that).

If `/images`, `/pdfs`, `/fonts`, `/brand`, or `/assets` is requested and the file is missing, the pipeline returns **404** instead of falling through to JWT (which would be **401**, because `<img>` and `@font-face` do not send `Authorization`).

Host maps unknown paths to `index.html` so client-side routes work.

### Pipeline order

Swagger (dev) → HTTPS (Development) → static files → missing-static 404 → routing → CORS (if configured) → authentication → **business picker** (`X-Business-Id` or `?businessId=`) → authorization → controllers → SPA fallback (Host).

After the JWT is accepted, middleware copies `X-Business-Id` (or `businessId` query) into `BusinessScope` for that request, then clears it. Platform Admins use that header to scope lists to one house. Non-admins ignore it and stay on their linked house.

---

## `ProposalStudio.csproj`

MSBuild project file. SDK `Microsoft.NET.Sdk.Web`, `net8.0`, nullable reference types, implicit usings.

**Fonts** (`Fonts/**`) copy to the output directory. PDF generation needs those TTF/OTF files next to the published binary.

**Packages**

| Package | Why |
|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Postgres via EF Core |
| `Microsoft.EntityFrameworkCore.Design` / `Tools` | Migrations only (`PrivateAssets=all`) |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Bearer JWT |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | JSON for controllers (not RFC 6902 JSON Patch) |
| `BCrypt.Net-Next` | Password hashes |
| `QuestPDF` | Proposal PDFs |
| `SixLabors.ImageSharp` / `SkiaSharp` (+ Linux native assets) | Product images |
| `AWSSDK.S3` | Object storage for media |
| `Swashbuckle.AspNetCore` | Swagger, **Debug only** so Host publish does not ship it |

Removed as unused: nested `proposal-frontend` exclude, `Microsoft.AspNetCore.JsonPatch`, Pack/exe environment.

---

## Controllers

HTTP API. Default is login required (`FallbackPolicy`). `[AllowAnonymous]` is the opt-out; `[Authorize(Roles=…)]` narrows writes. PATCH copies non-null DTO fields; it is not RFC 6902. DTOs usually live in the same file.

Staff rows hang off `Users.BusinessId`. `BusinessScope.ResolveAsync` + `Filter` keep lists on one house. JWT role `Admin` is platform-wide; the Admin UI sends `X-Business-Id` so that Admin still sees one house at a time.

---

## `Controllers/AuthController.cs`

Login and “who am I.” Route `/api/auth`.

| Verb | Path | Auth | Why |
|---|---|---|---|
| POST | `/login` | anonymous | Verify email/password (BCrypt). Issue an 8-hour JWT with issuer/audience `ProposalStudio`. Claims: user id, email, name, **JWT role** (`Admin` / `Manager` / `SalesAdvisor`). Body returns `token`, app `role` (`admin` / `manager` / `advisor`), `userId`, `businessId`, `email`, `name`. |
| GET | `/me` | logged in | Reload the active user from the token. Inactive → 401 (same check as `JwtSessionEvents` on every request). Nested `Business` is id, name, slug, prefix. |

`MapRole` translates the DB lowercase role to the JWT PascalCase role and the frontend role. Unknown roles become advisor.

Emails are stored and looked up lowercase so login can use the unique index. Typed email is lowercased in C#; the SQL comparison is `users.email = @email` (no `ToLower()` on the column).

---

## `Controllers/UsersController.cs`

Staff directory for the scoped house. File name `UsersController.cs`; class is `UserController`; route is `/api/users`. New people join via **invites**, not this controller. Passwords change via **password-resets**.

| Verb | Path | Who | Why |
|---|---|---|---|
| GET | `/` | logged in | Active users in the house (builder advisor picker + Admin list). Soft-deleted rows stay out. |
| GET | `/{id}` | logged in | One person, same house. |
| PATCH | `/{id}` | Admin | Name / email / role / `Active`. Email must be unique (409 if taken). Audits role or active changes. Response matches GET (no `PasswordHash`). |
| DELETE | `/{id}` | Admin | Soft-delete (`Active = false`). Cannot delete yourself. Next JWT-validated request for that user is 401. |

The SPA polls `/auth/me` and bounces to login on 401, so a deactivated session does not linger on screen.

---

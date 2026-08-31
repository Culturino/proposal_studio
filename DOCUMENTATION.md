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

Swagger (dev) → HTTPS (Development) → static files → missing-static 404 → routing → CORS (if configured) → authentication → authorization → controllers → SPA fallback (Host).

# Consent UX & Auth – Data & Service Foundations

This note captures the concrete data model and service changes required before building the consent UX and tenant-auth flows defined in ADR 0001.

## 1. Data Model Additions

### Tenants
| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `uuid` | Primary key |
| `Slug` | `text` | Unique, lowercase identifier (`agent_acme`, `mockboard_eu`) |
| `DisplayName` | `text` | Full label for UI |
| `Type` | `smallint` | Enum (`0 = Agent`, `1 = Board`) |
| `JwksEndpoint` | `text` | Optional; used when verifying detached JWS |
| `CallbackUrl` | `text` | Optional; webhooks / redirect hooks |
| `CreatedAt` | `timestamp` | UTC |
| `UpdatedAt` | `timestamp` | UTC |
| `IsActive` | `bool` | Soft-disable tenant access |

### Tenant Credentials
| Column | Type | Notes |
| --- | --- | --- |
| `Id` | `uuid` | Primary key |
| `TenantId` | `uuid` | FK → `Tenants` |
| `ClientId` | `text` | Public identifier |
| `ClientSecretHash` | `text` | PBKDF2/BCrypt hash of client secret |
| `Scopes` | `text` | Space-delimited scope codes (initially `apply.submit`) |
| `CreatedAt` | `timestamp` | UTC |
| `LastRotatedAt` | `timestamp` | Nullable |
| `IsActive` | `bool` | Secret revocation |

### Consent Enhancements
- Add `TokenId` (`uuid`) – unique identifier for issued consent token.
- Add `TokenExpiresAt` (`timestamp`) – max validity (mirrors JWT `exp`).
- Add `ApprovedByEmail` (`text`) – lower-cased candidate email used during UX flow.
- Keep existing `Scopes`, `IssuedAt`, `RevokedAt`.

### Audit Trail (Phase Two)
- Introduce `ConsentAudit` table to capture transitions (Created, Approved, Revoked) once API endpoints are wired. Not part of this initial schema pass but noted for later work.

## 2. Domain Layer Changes
1. Extend `Gateway.Domain` with new entities:
   - `Tenant`, `TenantType`, `TenantCredential`.
   - Update `Consent` to include new properties.
2. Add service contracts for hashing and JWT issuance (interfaces only for now):
   - `IClientSecretHasher` (hash + verify).
   - `IConsentTokenFactory` (issue/extract `TokenId`, `exp`, etc.).

## 3. Infrastructure Layer Changes
1. Update `GatewayDbContext`:
   - `DbSet<Tenant> Tenants`, `DbSet<TenantCredential> TenantCredentials`.
   - Configure relationships, unique indexes (`Tenants.Slug`, `TenantCredentials.ClientId`).
2. Create EF Core migration `AddTenantsAndConsentToken`.
3. Provide design-time hooks for seeding via `IHost` extension (later step).

## 4. Application/API Skeleton
- Placeholder endpoints:
  - `/internal/tenants` (future admin provisioning).
  - `/oauth/token` (client credentials) stub returning 501 until implemented.
- Middleware slot for bearer token validation (to be wired after JWT factory exists).

## 5. Deliverables for First Iteration
1. Schema changes + migration.
2. Domain models & DbContext updates.
3. Infrastructure wiring (DI registrations, service interfaces).
4. Unit tests covering entity configuration (optional but recommended).
5. Documentation update summarizing runtime changes (README + ADR link).

Once the above foundation is merged we can proceed with:
- Implementing client credential grant (secret hashing, JWT issuance).
- Building the consent web experience.
- Replacing `AcceptAllVerifier` with tenant-aware signature validation.

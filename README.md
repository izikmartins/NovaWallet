# NovaWallet Ledger Service

NovaWallet is a simplified wallet ledger backend built with ASP.NET Core (.NET 8), Entity Framework Core, and PostgreSQL.

The service supports wallet creation, credits, wallet-to-wallet transfers, idempotent transfer processing, daily outbound limits, transaction statements, JWT authentication, and an immutable audit trail.

## Technology Stack

- C# / .NET 8
- ASP.NET Core Web API
- Entity Framework Core 8
- PostgreSQL 16
- JWT Bearer Authentication
- Swagger / OpenAPI
- Docker & Docker Compose
- xUnit
- Testcontainers for PostgreSQL integration and concurrency tests

## Architecture

The application follows a simple layered structure:

```text
HTTP Request
     |
     v
Controllers
     |
     v
IWalletService / WalletService
     |
     v
Entity Framework Core
     |
     v
PostgreSQL
```

### Main Components

- **Controllers** — HTTP endpoints and request/response handling.
- **Services** — wallet and ledger business logic.
- **DTOs** — API request and response contracts.
- **Models** — wallet, transaction, audit, and idempotency entities.
- **Data** — EF Core database context and database configuration.
- **Middleware** — centralized exception handling using Problem Details.
- **Migrations** — schema creation and database-level audit immutability.

## Money Representation

All monetary values are represented as integer **kobo** using C# `long`.

No `float` or `double` values are used for money.

For example:

```text
₦1.00       = 100 kobo
₦1,000.00   = 100,000 kobo
₦500,000.00 = 50,000,000 kobo
```

Wallet currency is currently fixed to `NGN`.

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/token` | Generate a development JWT |
| POST | `/api/wallets` | Create a wallet |
| GET | `/api/wallets/{walletId}/balance` | Get wallet balance |
| POST | `/api/wallets/{walletId}/credit` | Credit a wallet |
| POST | `/api/transfers` | Transfer between wallets |
| GET | `/api/wallets/{walletId}/statement` | Get paginated wallet statement |

All wallet endpoints require JWT Bearer authentication.

### Transfer Idempotency

`POST /api/transfers` requires:

```text
Idempotency-Key: <unique-key>
```

A request hash is stored against the key.

- Replaying the same key with the same payload returns the original transfer result without moving money again.
- Reusing the same key with a different payload is rejected.
- A unique database index provides a final concurrency safeguard against duplicate keys.

## Concurrency and Double-Spend Protection

Balance mutations execute inside database transactions.

Wallet rows are explicitly locked using PostgreSQL:

```sql
SELECT ... FOR UPDATE
```

For transfers, both wallets are locked in deterministic UUID order. This prevents two opposite-direction transfers from acquiring the wallet locks in inconsistent order.

The service uses `ReadCommitted` transaction isolation together with explicit row-level locking.

Before debiting a source wallet, its locked balance is checked. This ensures concurrent requests cannot spend the same available balance multiple times.

The database also has a check constraint requiring:

```text
BalanceKobo >= 0
```

This provides an additional database-level invariant.

## Daily Outbound Transfer Limit

Each wallet has a daily outbound transfer limit of:

```text
₦500,000 = 50,000,000 kobo
```

Outbound `TransferDebit` transactions for the current day are summed before a transfer is accepted.

The limit resets at midnight **West Africa Time (WAT / UTC+1)**.

The source wallet is locked while this check is performed, preventing concurrent outbound transfers from independently bypassing the daily limit.

## Ledger and Audit Trail

Every balance mutation creates a ledger transaction.

Transfers create:

- `TransferDebit` for the source wallet
- `TransferCredit` for the destination wallet

Credits create a `Credit` transaction.

Audit records are stored separately from the transaction table and record:

- wallet
- action
- amount
- balance before
- balance after
- transfer reference where applicable
- timestamp

The audit trail is append-only.

A PostgreSQL trigger rejects both `UPDATE` and `DELETE` operations against the `AuditLogs` table, enforcing immutability at the database layer rather than relying only on application code.

## Statements

Wallet statements support pagination:

```text
GET /api/wallets/{walletId}/statement?page=1&pageSize=20
```

Transactions are returned newest first.

Page size is limited to a maximum of 100 records.

## Authentication

The API uses JWT Bearer authentication.

For assessment/demo purposes, a development token can be generated using:

```text
POST /api/auth/token
```

The included signing key and mock issuer are development-only. A production implementation would use an external identity provider and secrets stored outside source control.

## Transfer Rate Limiting

The transfer endpoint is protected by ASP.NET Core rate-limiting middleware.

Authenticated clients are limited to:

```text
30 transfer requests per minute
```

Requests exceeding the limit receive HTTP `429 Too Many Requests`.

The authenticated identity is used as the rate-limit partition key, with the client IP address as a fallback.

## Health Check

A database-aware health endpoint is available at:

```text
GET /health
```

The endpoint is anonymous so it can be used by container orchestration and monitoring systems.

It verifies connectivity to the PostgreSQL database and returns `Healthy` when the service and datastore are available.

## Error Handling

Application errors are handled by centralized middleware and returned using RFC 7807-style Problem Details.

Example:

```json
{
  "title": "Insufficient funds",
  "status": 409,
  "detail": "The source wallet has insufficient funds.",
  "instance": "/api/transfers",
  "traceId": "..."
}
```

## Running with Docker

### Prerequisites

- Docker Desktop
- Docker Compose

From the repository root:

```bash
docker compose up --build
```

This starts:

- PostgreSQL
- NovaWallet API

The API automatically applies pending EF Core migrations during startup for assessment convenience.

Swagger is available at:

```text
http://localhost:8080/swagger
```

PostgreSQL is exposed on host port `5433`.

To stop the application:

```bash
docker compose down
```

## Using Swagger

1. Start the application with Docker Compose.
2. Open `http://localhost:8080/swagger`.
3. Call `POST /api/auth/token`.
4. Copy the returned `accessToken`.
5. Click **Authorize** in Swagger.
6. Enter the token.
7. Call the protected wallet and transfer endpoints.

## Running Tests

Docker Desktop must be running because the integration tests use Testcontainers with a real PostgreSQL instance.

From the repository root:

```bash
dotnet test
```

The automated tests cover:

- idempotent replay
- idempotency-key payload conflict
- insufficient funds with no partial mutation
- daily outbound transfer limit
- concurrent transfer/double-spend protection
- database-enforced audit immutability

The concurrency test creates multiple simultaneous transfer attempts against a wallet whose balance can fund only part of them. It verifies that the wallet never becomes negative and that the total credited amount never exceeds the available source balance.

## Database Migrations

Migrations are stored under:

```text
src/NovaWallet.Api/Migrations
```

The application applies pending migrations automatically at startup.

For local development they can also be applied manually:

```bash
dotnet ef database update \
  --project src/NovaWallet.Api \
  --startup-project src/NovaWallet.Api
```

## Key Design Decisions

**Integer money representation:** `long` kobo avoids floating-point rounding errors.

**PostgreSQL row locking:** explicit `FOR UPDATE` locks serialize mutations to the same wallet and protect against double spending.

**Deterministic wallet lock order:** reduces deadlock risk when two wallet rows must be locked.

**Database constraints:** important financial invariants such as non-negative balances and positive transaction amounts are also enforced by PostgreSQL.

**Database-enforced immutable audit trail:** a PostgreSQL trigger prevents modification or deletion of existing audit records.

**Real PostgreSQL tests:** concurrency behavior is tested against PostgreSQL through Testcontainers instead of relying on an in-memory database with different locking semantics.

## Trade-offs and Production Considerations

This project intentionally prioritizes the assessment's core ledger correctness requirements over production-scale infrastructure.

For assessment convenience, migrations run during application startup. In a multi-instance production deployment, migrations would normally be executed as a controlled deployment step or migration job.

The JWT issuer and signing key are development-only. Production credentials would be provided through a secret manager or secure environment configuration.

The wallet stores its current balance for efficient reads while transaction and audit records preserve the history of balance mutations.

A larger production payment platform could additionally introduce an outbox/event-publishing mechanism, distributed observability, stronger operational key management, and broader resilience controls.

## AI Assistance

AI tools were used during development for implementation assistance, debugging, review, and documentation.

The use of AI, including examples where generated recommendations were reviewed and corrected, is documented in:

```text
AI_USAGE.md
```
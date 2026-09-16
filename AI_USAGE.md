# AI Usage

## Tool Used

**ChatGPT (GPT-5.6 Sol)**

AI assistance was used during the NovaWallet Ledger Service assessment. I used it as a development assistant for architecture planning, C#/.NET implementation guidance, debugging, test design, Docker configuration, and documentation.

I reviewed and tested generated suggestions rather than treating AI output as automatically correct, particularly for financial-system concerns such as concurrency, idempotency, and audit integrity.

## How AI Was Used

AI assistance was used for:

- Translating the assessment requirements into an implementation plan.
- Implementing ASP.NET Core and Entity Framework Core patterns.
- Designing PostgreSQL transaction and row-locking behavior.
- Creating integration and concurrency tests using Testcontainers.
- Troubleshooting PostgreSQL and Docker configuration.
- Reviewing the implementation against the assessment requirements.
- Preparing project documentation.

## Example Prompt 1 — Architecture and Implementation

### Prompt

> I need to complete a Backend/API Developer (.NET) take-home assessment. I need to build a NovaWallet Ledger Service in C#/.NET 8 with PostgreSQL. It needs wallet creation, credits, atomic wallet-to-wallet transfers, idempotency, daily outbound limits, an immutable audit trail, JWT authentication, Docker Compose, Swagger and automated concurrency tests. Help me build it step by step.

### AI Response / Recommendation

The AI proposed an ASP.NET Core Web API using:

- Controllers for HTTP endpoints.
- A service layer for wallet business logic.
- Entity Framework Core with PostgreSQL.
- Integer `long` values representing kobo.
- Database transactions and PostgreSQL row locks for balance mutations.
- A separate ledger transaction table and audit table.
- JWT Bearer authentication.
- xUnit and Testcontainers for integration testing.

### My Review

I implemented the architecture incrementally and tested each major part rather than accepting the complete design without validation.

In particular, financial invariants were backed by PostgreSQL constraints and real PostgreSQL integration tests instead of relying only on application-level checks.

---

## Example Prompt 2 — PostgreSQL Connection Failure

### Prompt

> `dotnet ef database update` is failing with a PostgreSQL password authentication error even though the Docker PostgreSQL credentials look correct.

### AI Response / Recommendation

The AI suggested checking whether another PostgreSQL installation or service was already using host port `5432`.

It recommended changing the Docker host mapping to:

```text
5433:5432
```

and changing the local development connection string to use port `5433`.

### Result

After changing the host port, the EF Core migration connected to the intended Docker PostgreSQL instance and completed successfully.

The API container still connects internally to PostgreSQL using port `5432`, because Docker Compose services communicate through the Docker network.

---

## Example Prompt 3 — Concurrency Failure Under Load

### Prompt

> My concurrent transfer test failed with PostgreSQL error `40001: could not serialize access due to concurrent update`.

### Initial AI Approach

The initial implementation used a `Serializable` database transaction together with explicit PostgreSQL:

```sql
SELECT ... FOR UPDATE
```

row locks.

Under concurrent transfer testing, this produced an intermittent PostgreSQL serialization failure.

### Review and Correction

The concurrency test exposed that the initial approach was unnecessarily aggressive for this design.

I changed the transaction isolation level to `ReadCommitted` while retaining explicit `FOR UPDATE` row locks.

For wallet-to-wallet transfers, wallet rows are also locked in deterministic UUID order.

I then repeatedly executed the concurrent transfer test ten times. All ten executions passed, followed by the complete test suite.

This demonstrated why concurrency behavior needed to be validated against a real PostgreSQL database rather than assuming the generated transaction strategy was correct.

---

# Example of AI Output That Was Too Naive for a Financial System

An early AI-assisted implementation created a separate `AuditLogs` table and inserted an audit record for each balance mutation.

Although this satisfied the separation between ledger transactions and audit records, calling the table "append-only" was not sufficient. Nothing at the database level prevented an application bug or database client from executing:

```sql
UPDATE "AuditLogs" ...
```

or:

```sql
DELETE FROM "AuditLogs" ...
```

For a financial audit trail, relying only on application code to avoid these operations was too weak.

## How I Caught It

While reviewing the implementation against the requirement for an **append-only immutable audit trail**, I identified that the database itself did not enforce immutability.

## How I Fixed It

I added an EF Core migration that creates a PostgreSQL trigger.

The trigger executes before `UPDATE` or `DELETE` operations against `AuditLogs` and rejects the operation.

I then added an integration test using a real PostgreSQL Testcontainer that:

1. Creates an audit record.
2. Attempts to update it and expects PostgreSQL to reject the change.
3. Verifies the original record remains unchanged.
4. Attempts to delete the record and expects PostgreSQL to reject the deletion.
5. Verifies the audit record still exists.

This changed audit immutability from an application convention into a database-enforced invariant.

# AI Verification Approach

AI-generated code and recommendations were validated using:

- `dotnet build` with zero warnings and zero errors.
- Automated xUnit tests.
- Real PostgreSQL instances through Testcontainers.
- Repeated concurrent transfer tests.
- Manual Swagger API testing.
- Docker Compose end-to-end startup testing.
- Review of the implementation against the assessment requirements.

At the final verification stage, the project had six automated tests passing, including concurrency and audit immutability tests.

AI accelerated implementation and debugging, but correctness decisions were based on requirements review, database behavior, and test results rather than AI output alone.
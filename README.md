# AwardCo Points Ledger (learning project)

A learning project: a simplified clone of AwardCo's employee points ledger. It exposes a small REST API for creating employees, recording point transactions, checking balances, and transferring points between employees.

## Key ideas

- **Balance is computed, not stored.** An employee's balance is `SUM(Amount)` over their rows in the `Transactions` table.
- **Add-only transactions.** The API only inserts transactions; there are no update or delete endpoints.
- **Atomic transfers.** A transfer checks the sender's balance and writes the debit and credit inside one database transaction, so either both rows are saved or neither is.
- **Idempotency.** The transaction and transfer endpoints take an `Idempotency-Key` header. Retrying with the same key returns the original transaction ID instead of applying it twice; reusing a key with a different request returns `409 Conflict`.
- **Seeded dev data.** In Development, an empty database is filled with 25 fake employees and 500 transactions (fixed random seed).

## Tech stack

C# / ASP.NET Core (.NET 10, `net10.0`), SQLite via Microsoft.Data.Sqlite, OpenAPI + Scalar, xUnit for tests.

## Project structure

- `Program.cs` - app setup, service registration, table creation, dev-only seeding and Scalar.
- `Controllers/` - `PointsLedgerController`, the API endpoints.
- `Data/` - `Database` (connection string, table creation) and `DatabaseSeeder`.
- `Model/` - `Employee`, `Transaction`, their repositories, and custom exceptions.
- `awardco.Tests/` - xUnit tests for the repositories and seeder.
- `awardco.http` - sample requests.
- `lessons.md` - the author's notes on what was learned.

## API endpoints

All routes are under `/api/pointsLedger`.

| Method | Route | Description | Idempotency-Key |
| --- | --- | --- | --- |
| POST | `/` | Create an employee. Returns `201` with the employee. | No |
| GET | `/{employeeId}` | Get an employee, or `404`. | No |
| GET | `/{employeeId}/balance` | Get an employee's balance, or `404`. | No |
| POST | `/transactions` | Add a transaction (JSON body). Returns the transaction ID; `404` for an unknown employee, `409` on a key conflict. | Yes |
| POST | `/transfer` | Transfer points (`fromEmployeeId`, `toEmployeeId`, `amount` as query parameters). Returns `400` for an invalid transfer, `404` for an unknown employee, `409` on a key conflict, `422` for insufficient points. | Yes |

## Getting started

Requires the .NET 10 SDK.

```sh
dotnet run
```

The app listens on `http://localhost:5287` (the `https` profile also uses `https://localhost:7153`). In Development, the Scalar API reference is at `/scalar` and the OpenAPI document at `/openapi/v1.json`.

Use `awardco.http` (in Rider, Visual Studio, or VS Code with a REST client) to send sample requests.

Run the tests:

```sh
dotnet test awardco.Tests
```

The database is a local `ledger.db` file. Tables are created with `CREATE TABLE IF NOT EXISTS`, which does not migrate existing tables, so delete `ledger.db` after changing the schema and let the app recreate it.

## Lessons learned

[lessons.md](lessons.md) holds the author's notes from building this: design choices, C# and ASP.NET Core concepts, SQLite and data access, idempotency, testing with xUnit, problems solved along the way, and what to work on next.

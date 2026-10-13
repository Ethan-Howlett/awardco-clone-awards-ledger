# Lessons Learned: Points Ledger

What I learned building a simplified version of AwardCo's points ledger in C# / ASP.NET Core with SQLite.

## Design choices

| Choice | Why |
|---|---|
| **Balance is calculated, not stored.** It is `SUM(Amount)` of an employee's transactions. | Every point has a history, so the balance can't drift away from the records. This is what makes it a *ledger*. |
| **Transactions are add-only.** No edit or delete; mistakes are fixed with a new correcting transaction (e.g. −50). | Like a bank statement: the history always shows exactly what happened. |
| **Transfers run inside a database transaction.** | **Atomicity**: the deduction and the addition are both saved, or neither is. Points can't disappear halfway through. |
| **Email is unique** and used to detect duplicate employees. | First + last name isn't unique (two people can be named John Smith). |
| **Repositories are organized by table.** `GetBalance` lives in `TransactionRepository`, not `EmployeeRepository`. | It only reads the `Transactions` table. The API URL (`/{employeeId}/balance`) can still feel employee-centered, because the URL and the code's location don't have to match. |
| **Repositories return `null` for "not found"; the controller turns it into 404.** | "Not found" is an expected result, not an error. Exceptions are for unexpected failures. Throwing would show up as a 500. |
| **Inputs are checked at the top of a method** (`amount <= 0`, transferring to yourself). | Reject bad input before doing any work. A negative transfer would have let someone take points from a coworker. |
| **The seeder uses a fixed random seed and only runs on an empty database, in Development.** | Same fake data every time, so bugs can be reproduced, and restarting doesn't duplicate data. |
| **Each test gets its own throwaway database file.** | Tests can't affect each other or my real `ledger.db`. |
| **Awards and transfers require an `Idempotency-Key` header.** It's a non-nullable `[FromHeader]` parameter, so ASP.NET returns 400 if it's missing. | Endpoints that move points must be safe to retry. |
| **A transfer's key is stored with the sender's deduction transaction ID**, and that's what the endpoint returns. | The `ProcessedRequests` table holds one `TransactionId` per key, and the deduction identifies the transfer. |

## C# concepts

- **Naming:** classes, methods, and properties use PascalCase (`Employee`, `GetBalance`). C# is case-sensitive: `Id` and `ID` are different names.
- **Namespaces and `using`:** "type or namespace could not be found" means a typo or a missing `using`.
- **`static class` + `static` property:** one shared value the whole app can reach (`Database.ConnectionString`). I started with a `const`, but a const is fixed at compile time and copied into the code that uses it, so tests couldn't change it. A `const` also never triggers a `static` constructor, which is why my tables weren't being created at first.
- **Nullable types:** `Employee?` means "might be null." Warnings like CS8605 are real questions: *can this actually be null here?* Fix them with `Convert.ToInt64(...)` or `!` ("I promise it's not null"). Aim for 0 warnings.
- **`nameof(GetBalance)`:** turns a name into a string that the compiler checks, so a rename can't silently break it.
- **Raw string literals (`"""`):** text must start on the line after the opening `"""`, and the closing `"""` goes on its own line.
- **`Task` / async:** `Task.FromResult(...)` does **not** make code async. It wraps a value that is already finished. Real async needs `async`/`await` all the way down (`ExecuteScalarAsync`, etc.). Also, `Task<Derived>` won't convert to `Task<Base>`.

## ASP.NET Core

- **Controllers must be registered:** `AddControllers()` plus `MapControllers()`, or the routes don't exist.
- **Dependency injection:** the controller asks for repositories in its constructor, and `AddScoped<EmployeeRepository>()` tells ASP.NET how to create them.
- **Routing:** `[Route("api/[controller]")]` makes `PointsLedgerController` available at `/api/pointsLedger`. Two actions with the same verb and route (two plain `[HttpPost]`) can't be told apart, and the app crashes at runtime.
- **`CreatedAtAction`:** returns `201 Created`, a `Location` header pointing to the new resource, and the new object with its ID.
- **Scalar + OpenAPI:** `AddOpenApi()` + `MapOpenApi()` + `MapScalarApiReference()`, then browse to `/scalar` to test endpoints.
- **`.http` files:** test requests saved next to the code, with `###` between requests and a blank line before a JSON body.

## SQLite / data access

- **Parameters (`@Email`) prevent SQL injection.** Values are treated as data and never executed as SQL.
- **Parameters stick to a command.** Reusing a command means the old parameters are still there. Adding the same name twice causes *"Must add values for the following parameters:"* (with a blank name). Add each parameter once, or call `Parameters.Clear()` first.
- **`ExecuteScalar()`** returns one value as `object?`. `RETURNING ID` gets the new row's ID in the same query.
- **`COALESCE(SUM(Amount), 0)`:** `SUM` of zero rows is `NULL`, not 0.
- **`Transaction` is a SQL keyword**, so the table is named `Transactions`.
- **`CREATE TABLE IF NOT EXISTS` doesn't update existing tables.** After a schema change (adding `Email`), delete `ledger.db`.
- **When a connection has a database transaction open, every command must have `command.Transaction` set.**
- **One transaction for bulk inserts** (the seeder) is much faster than hundreds of separate saves.
- **A table can have only one `PRIMARY KEY`.** Use `UNIQUE` for other columns that must not repeat.
- **`ENUM(...)` is MySQL, not SQLite.** In SQLite use `TEXT` with `CHECK (Status IN (...))`.
- **SQL inside a C# string isn't checked by the compiler.** A trailing comma before `)` builds fine and crashes at runtime, so tests that create the tables catch it.
- **`ExecuteScalar()` returns `null` when no row matches.** That's how I tell "new key" from "seen before."

## Idempotency

**Idempotent** means doing the same request twice has the same effect as doing it once. If a request times out, the client can't tell whether it worked, so it retries. Without idempotency, a retried award or transfer moves points twice.

- **GETs are already idempotent.** Reading doesn't change anything.
- **Instead of rejecting a duplicate email** I return the existing employee.
- **Awards and transfers use an `Idempotency-Key` header**, the same pattern payment APIs like Stripe use. The client creates a unique key (a GUID) for each action and sends the **same key on every retry**.
- **A `ProcessedRequests` table stores each key** with the `TransactionId` it produced. `Key` is `UNIQUE`, so the database itself refuses a second copy.
- **The key and the work are saved in the same database transaction.** If they were saved separately, a crash in between would either lose the points (key saved, work not) or allow a double award (work saved, key not). This is atomicity again.
- **A retry gets the original result, not an error.** The client retried because it didn't know whether the first request worked, so it gets the same `200 OK` and the same transaction ID.
- **Check-then-insert has a race.** Two identical requests arriving at the same moment can both see "no key yet." The `UNIQUE` constraint is the real protection: catch `SqliteException` with `SqliteExtendedErrorCode == 2067` (UNIQUE failed), roll back, and return the ID the other request saved. Code 19 alone would also catch `NOT NULL` and foreign-key failures.
- **In a transfer, the key check comes before the balance check.** After a successful transfer the sender might no longer have enough points, and a retry must not fail for a transfer that already happened.
- **A failed request doesn't save its key**, because the rollback undoes it. A retry after fixing the problem (for example, after earning more points) can still succeed.
- **Disposing an uncommitted database transaction rolls it back automatically.** With `using var dbTransaction`, any early `return` or exception before `Commit()` undoes everything.
- **I didn't need a `Status` column** (`Waiting`/`Failure`). Because saving is atomic, a key row only exists if the work succeeded. Statuses are for slow, multi-step systems.

## Testing (xUnit)

- xUnit creates a **new instance of the test class for every test**: the constructor is setup, `Dispose` is cleanup.
- Shared static state (`ConnectionString`) means tests must run **one at a time** (`DisableTestParallelization`).
- **When many tests fail, look for the same line in every stack trace.** 8 failures all pointed to `Employee.cs:39`, which meant one bug.
- **A test can pass for the wrong reason.** My "insufficient points" test passed because the code crashed with a *different* `InvalidOperationException`. Check the message too, not just the exception type.
- A test that needs a unique value (email) can use `Guid.NewGuid()`.
- **Idempotency tests:** same key twice gives the same ID and changes the balance once; different keys change it twice; a retry still succeeds after the balance drops; a failed request doesn't save its key.

## Problems I solved

| Problem | Cause | Fix |
|---|---|---|
| Build failed on the `CREATE TABLE` string | Raw string literal formatting | Put the text on the line after `"""` |
| `dbName` doesn't exist | Variable was never defined | `static` `Database.ConnectionString` |
| Controller method called itself forever | `CreateEmployee(employee)` inside `CreateEmployee` | Call the repository instead |
| Tables never created | Reading a `const` doesn't run a `static` constructor | Call `Database.CreateTables()` in `Program.cs` |
| `EmployeeRespository` not found | Typo | Spelling, and use autocomplete |
| `Task<CreatedAtActionResult>` won't convert | `Task.FromResult` wrapping | Removed `Task`s (code wasn't really async) |
| INSERT failed: not enough values | Added `Email` column but not `@Email` in `VALUES` | Added it |
| "Must add values for the following parameters" | Same parameter added twice to one command | Removed the duplicate |
| "Execute requires the command to have a transaction object" | Command not linked to the database transaction | `command.Transaction = transaction;` |
| Negative transfers stole points | No input validation | `ArgumentException` when `amount <= 0` |
| `[FromHeader]` didn't compile | Put on a local variable inside the method | Attributes like this go on method parameters |
| Enum and property both named `Status` | Two members of a class can't share a name | Removed the `Status` column (not needed) |
| `ProcessedRequests` table failed to create | Two `PRIMARY KEY`s, MySQL `ENUM`, trailing comma | `Key TEXT UNIQUE NOT NULL`, removed `Status`, removed the comma |
| Retries still created duplicates | Key was checked but never saved, and saved on a separate connection from the work | Save the key in the same database transaction as the work |

## Next up

- Reject a reused key with a different request body (for example, a different amount) with `409 Conflict`
- Controller tests with `WebApplicationFactory` (for example, a missing `Idempotency-Key` returns 400)
- Delete old keys after a while using `ProcessedAt`
- Return 400 / 409 instead of 500 for bad input and duplicates
- Real `async`/`await`
- Turn on `PRAGMA foreign_keys = ON;` so points can't go to an employee who doesn't exist

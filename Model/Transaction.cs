using Microsoft.Data.Sqlite;
using awardco.Data;

namespace awardco.Model;

public class Transaction
{
    public int? Id { get; set; }
    public int EmployeeId { get; set; }
    public int Amount { get; set; }
}

public class TransactionRepository
{
    public int CreateTransaction(Transaction transaction, string idempotencyKey)
    {
        // Amount can be negative for deductions, so we don't check for that here. TransferPoints checks for positive amounts itself.
        var request = new RequestDetails("award", transaction.EmployeeId, null, transaction.Amount);
        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();
        using var dbTransaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;

        try
        {
            // Step 2: Already processed? Return the original transaction ID instead of doing it again.
            // Throws if the key was used for a different request.
            int? existingId = FindTransactionIdByKey(command, idempotencyKey, request);
            if (existingId != null)
            {
                return existingId.Value;
            }

            // Make sure the employee exists before saving anything for them.
            if (!EmployeeExists(command, transaction.EmployeeId))
            {
                throw new EmployeeNotFoundException(transaction.EmployeeId);
            }

            // Step 3: Insert into Transactions with RETURNING ID, and save the new ID in a variable.
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO Transactions (EmployeeId, Amount) VALUES (@EmployeeID, @Amount) RETURNING ID;";
            command.Parameters.AddWithValue("@EmployeeID", transaction.EmployeeId);
            command.Parameters.AddWithValue("@Amount", transaction.Amount);
            int newTransactionId = Convert.ToInt32(command.ExecuteScalar());

            // Step 4: Insert into ProcessedRequests using that ID.
            SaveProcessedKey(command, idempotencyKey, newTransactionId, request);

            dbTransaction.Commit();
            return newTransactionId;
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067) // UNIQUE constraint violation
        {
            // Another request with the same key finished between our check and our insert.
            // Its request must match ours too, or this throws.
            dbTransaction.Rollback();
            using var lookup = connection.CreateCommand();
            return FindTransactionIdByKey(lookup, idempotencyKey, request)!.Value;
        }
    }

    // The request an idempotency key was used for. "award" requests have no ToEmployeeId;
    // for "transfer" requests EmployeeId is the sender and Amount is the positive amount sent.
    private record RequestDetails(string Endpoint, int EmployeeId, int? ToEmployeeId, int Amount);

    // Returns the stored transaction ID for this key, or null if the key hasn't been used.
    // Throws IdempotencyKeyConflictException if the key was used for a different request.
    private static int? FindTransactionIdByKey(SqliteCommand command, string key, RequestDetails request)
    {
        command.Parameters.Clear();
        command.CommandText = "SELECT TransactionId, Endpoint, EmployeeId, ToEmployeeId, Amount FROM ProcessedRequests WHERE Key = @Key;";
        command.Parameters.AddWithValue("@Key", key);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        int transactionId = reader.GetInt32(0);

        // Legacy rows were saved before the request details were stored, so there is nothing to compare.
        // Keep the old behaviour and return the stored ID.
        if (reader.IsDBNull(1))
        {
            return transactionId;
        }

        var stored = new RequestDetails(
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetInt32(4));
        if (stored != request)
        {
            throw new IdempotencyKeyConflictException();
        }
        return transactionId;
    }

    // Returns true if an employee with this ID is in the database.
    private static bool EmployeeExists(SqliteCommand command, int employeeId)
    {
        command.Parameters.Clear();
        command.CommandText = "SELECT 1 FROM Employee WHERE ID = @Id;";
        command.Parameters.AddWithValue("@Id", employeeId);
        return command.ExecuteScalar() != null;
    }

    // Saves the key so a retry with the same key returns transactionId instead of doing the work again.
    // Must run on the same database transaction as the work it records, so both are saved or neither is.
    // The request details are saved too, so a reused key can be checked against them.
    private static void SaveProcessedKey(SqliteCommand command, string key, int transactionId, RequestDetails request)
    {
        command.Parameters.Clear();
        command.CommandText = """
        INSERT INTO ProcessedRequests (Key, TransactionId, ProcessedAt, Endpoint, EmployeeId, ToEmployeeId, Amount)
        VALUES (@Key, @TransactionId, @ProcessedAt, @Endpoint, @EmployeeId, @ToEmployeeId, @Amount);
        """;
        command.Parameters.AddWithValue("@Key", key);
        command.Parameters.AddWithValue("@TransactionId", transactionId);
        command.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@Endpoint", request.Endpoint);
        command.Parameters.AddWithValue("@EmployeeId", request.EmployeeId);
        command.Parameters.AddWithValue("@ToEmployeeId", (object?)request.ToEmployeeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Amount", request.Amount);
        command.ExecuteNonQuery();
    }

    public int GetBalance(int employeeId)
    {
        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        // Learned: Retrieve the employee's balance by summing the amounts of all transactions associated with the employee
        // We do this because we will keep a more accurate record of the employee's balance.
        command.CommandText = "SELECT COALESCE(SUM(Amount), 0) FROM Transactions WHERE EmployeeId = @EmployeeId;";
        command.Parameters.AddWithValue("@EmployeeId", employeeId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // Returns the ID of the sender's deduction transaction. A retry with the same key returns the same ID.
    public int TransferPoints(int fromEmployeeId, int toEmployeeId, int amount, string idempotencyKey)
    {
        if (amount < 0)
        {
            throw new InvalidTransferException("Transfer amount must be greater than zero.");
        }

        if (fromEmployeeId == toEmployeeId)
        {
            throw new InvalidTransferException("Cannot transfer points to the same employee.");
        }

        var request = new RequestDetails("transfer", fromEmployeeId, toEmployeeId, amount);
        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();

        // Start a transaction to ensure atomicity. If we return or throw before Commit(),
        // disposing it rolls everything back automatically.
        using var dbTransaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;

        try
        {
            // Check the key BEFORE the balance. If this transfer already succeeded, the sender may
            // no longer have enough points, and a retry must not fail for a transfer that went through.
            // Throws if the key was used for a different request.
            int? existingId = FindTransactionIdByKey(command, idempotencyKey, request);
            if (existingId != null)
            {
                return existingId.Value;
            }

            // Make sure both employees exist, checking the sender first.
            if (!EmployeeExists(command, fromEmployeeId))
            {
                throw new EmployeeNotFoundException(fromEmployeeId);
            }
            if (!EmployeeExists(command, toEmployeeId))
            {
                throw new EmployeeNotFoundException(toEmployeeId);
            }

            // Check if the sender has enough points
            command.Parameters.Clear();
            command.CommandText = "SELECT COALESCE(SUM(Amount), 0) FROM Transactions WHERE EmployeeId = @FromEmployeeId;";
            command.Parameters.AddWithValue("@FromEmployeeId", fromEmployeeId);
            var senderBalance = Convert.ToInt32(command.ExecuteScalar());
            if (senderBalance < amount)
            {
                throw new InsufficientPointsException();
            }

            // Deduct points from the sender
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO Transactions (EmployeeId, Amount) VALUES (@FromEmployeeId, @DeductAmount) RETURNING ID;";
            command.Parameters.AddWithValue("@FromEmployeeId", fromEmployeeId);
            command.Parameters.AddWithValue("@DeductAmount", -amount);
            int deductionId = Convert.ToInt32(command.ExecuteScalar());

            // Add points to the receiver
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO Transactions (EmployeeId, Amount) VALUES (@ToEmployeeId, @AddAmount);";
            command.Parameters.AddWithValue("@ToEmployeeId", toEmployeeId);
            command.Parameters.AddWithValue("@AddAmount", amount);
            command.ExecuteNonQuery();

            SaveProcessedKey(command, idempotencyKey, deductionId, request);

            dbTransaction.Commit();
            return deductionId;
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == 2067) // UNIQUE constraint violation
        {
            // Another request with the same key finished between our check and our insert.
            // Its request must match ours too, or this throws.
            dbTransaction.Rollback();
            using var lookup = connection.CreateCommand();
            return FindTransactionIdByKey(lookup, idempotencyKey, request)!.Value;
        }
    }
}
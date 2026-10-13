using awardco.Data;
using awardco.Model;
using Microsoft.Data.Sqlite;

namespace awardco.Tests;

public class TransactionRepositoryTests : TestDatabase
{
    private readonly EmployeeRepository _employees = new EmployeeRepository();
    private readonly TransactionRepository _transactions = new TransactionRepository();

    // Each call returns a new unique idempotency key, like a client would send for a new request.
    private static string NewKey() => Guid.NewGuid().ToString();

    // Helper so each test doesn't repeat the same setup lines.
    private int CreateEmployeeWithBalance(int startingPoints)
    {
        var employee = new Employee { FirstName = "Test", LastName = "Employee", Email = $"test{Guid.NewGuid()}@example.com" };
        _employees.CreateEmployee(employee);
        int id = employee.Id!.Value;

        if (startingPoints != 0)
        {
            _transactions.CreateTransaction(new Transaction { EmployeeId = id, Amount = startingPoints }, NewKey());
        }
        return id;
    }

    [Fact]
    public void CreateTransaction_ReturnsANewId()
    {
        int employeeId = CreateEmployeeWithBalance(0);

        int transactionId = _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, NewKey());

        Assert.True(transactionId > 0);
    }

    [Fact]
    public void GetEmployeeBalance_IsZero_ForNewEmployee()
    {
        var employee = new Employee { FirstName = "Jane", LastName = "Doe", Email = "jane.doe@test.com" };
        _employees.CreateEmployee(employee);

        Assert.Equal(0, _transactions.GetBalance(employee.Id!.Value));
    }

    [Fact]
    public void GetEmployeeBalance_IsTheSumOfTheirTransactions()
    {
        var employee = new Employee { FirstName = "Jane", LastName = "Doe", Email = "jane.doe@test.com" };
        _employees.CreateEmployee(employee);
        int id = employee.Id!.Value;

        _transactions.CreateTransaction(new Transaction { EmployeeId = id, Amount = 100 }, NewKey());
        _transactions.CreateTransaction(new Transaction { EmployeeId = id, Amount = 50 }, NewKey());
        _transactions.CreateTransaction(new Transaction { EmployeeId = id, Amount = -30 }, NewKey());

        Assert.Equal(120, _transactions.GetBalance(id));
    }

    [Fact]
    public void TransferPoints_MovesPointsFromSenderToReceiver()
    {
        int sender = CreateEmployeeWithBalance(500);
        int receiver = CreateEmployeeWithBalance(0);

        _transactions.TransferPoints(sender, receiver, 200, NewKey());

        Assert.Equal(300, _transactions.GetBalance(sender));
        Assert.Equal(200, _transactions.GetBalance(receiver));
    }

    [Fact]
    public void TransferPoints_Throws_WhenSenderDoesNotHaveEnoughPoints()
    {
        int sender = CreateEmployeeWithBalance(100);
        int receiver = CreateEmployeeWithBalance(0);

        var ex = Assert.Throws<InsufficientPointsException>(() => _transactions.TransferPoints(sender, receiver, 500, NewKey()));
        Assert.Equal("Insufficient points", ex.Message);

        // Nothing should have changed.
        Assert.Equal(100, _transactions.GetBalance(sender));
        Assert.Equal(0, _transactions.GetBalance(receiver));
    }

    [Fact]
    public void TransferPoints_Throws_WhenAmountIsNotPositive()
    {
        int sender = CreateEmployeeWithBalance(0);
        int receiver = CreateEmployeeWithBalance(500);

        var ex = Assert.Throws<InvalidTransferException>(() => _transactions.TransferPoints(sender, receiver, -200, NewKey()));
        Assert.Equal("Transfer amount must be greater than zero.", ex.Message);

        // A negative transfer would take points FROM the receiver, so it must be rejected.
        Assert.ThrowsAny<ArgumentException>(() => _transactions.TransferPoints(sender, receiver, -200, NewKey()));
        Assert.Equal(0, _transactions.GetBalance(sender));
        Assert.Equal(500, _transactions.GetBalance(receiver));
    }

    [Fact]
    public void TransferPoints_Throws_WhenSenderAndReceiverAreTheSame()
    {
        int employee = CreateEmployeeWithBalance(500);

        var ex = Assert.Throws<InvalidTransferException>(() => _transactions.TransferPoints(employee, employee, 100, NewKey()));
        Assert.Equal("Cannot transfer points to the same employee.", ex.Message);

        // Nothing should have changed.
        Assert.Equal(500, _transactions.GetBalance(employee));
    }

    // ---------- Missing employees ----------

    [Fact]
    public void CreateTransaction_Throws_WhenEmployeeDoesNotExist()
    {
        int missingId = 9999;

        var ex = Assert.Throws<EmployeeNotFoundException>(() => _transactions.CreateTransaction(new Transaction { EmployeeId = missingId, Amount = 100 }, NewKey()));
        Assert.Equal(missingId, ex.EmployeeId);

        // Nothing should have been saved.
        Assert.Equal(0, _transactions.GetBalance(missingId));
    }

    [Fact]
    public void TransferPoints_Throws_WhenReceiverDoesNotExist()
    {
        int sender = CreateEmployeeWithBalance(500);
        int missingReceiver = 9999;

        var ex = Assert.Throws<EmployeeNotFoundException>(() => _transactions.TransferPoints(sender, missingReceiver, 200, NewKey()));
        Assert.Equal(missingReceiver, ex.EmployeeId);

        // The sender keeps all their points.
        Assert.Equal(500, _transactions.GetBalance(sender));
    }

    [Fact]
    public void TransferPoints_Throws_WhenSenderDoesNotExist()
    {
        int missingSender = 9999;
        int receiver = CreateEmployeeWithBalance(0);

        var ex = Assert.Throws<EmployeeNotFoundException>(() => _transactions.TransferPoints(missingSender, receiver, 200, NewKey()));
        Assert.Equal(missingSender, ex.EmployeeId);

        // Nothing should have changed.
        Assert.Equal(0, _transactions.GetBalance(receiver));
    }

    // ---------- Idempotency ----------

    [Fact]
    public void CreateTransaction_SameKeyTwice_ReturnsSameIdAndAddsPointsOnce()
    {
        int employeeId = CreateEmployeeWithBalance(0);
        string key = NewKey();

        int firstId = _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, key);
        int retryId = _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, key);

        Assert.Equal(firstId, retryId);
        Assert.Equal(100, _transactions.GetBalance(employeeId));
    }

    [Fact]
    public void CreateTransaction_DifferentKeys_AddsPointsTwice()
    {
        int employeeId = CreateEmployeeWithBalance(0);

        int firstId = _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, NewKey());
        int secondId = _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, NewKey());

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(200, _transactions.GetBalance(employeeId));
    }

    [Fact]
    public void TransferPoints_SameKeyTwice_ReturnsSameIdAndMovesPointsOnce()
    {
        int sender = CreateEmployeeWithBalance(500);
        int receiver = CreateEmployeeWithBalance(0);
        string key = NewKey();

        int firstId = _transactions.TransferPoints(sender, receiver, 200, key);
        int retryId = _transactions.TransferPoints(sender, receiver, 200, key);

        Assert.Equal(firstId, retryId);
        Assert.Equal(300, _transactions.GetBalance(sender));
        Assert.Equal(200, _transactions.GetBalance(receiver));
    }

    [Fact]
    public void TransferPoints_Retry_DoesNotFail_WhenSenderNoLongerHasEnoughPoints()
    {
        // After the first transfer the sender has 100 left, which is less than 200.
        // The retry must still succeed, because the transfer already happened.
        int sender = CreateEmployeeWithBalance(300);
        int receiver = CreateEmployeeWithBalance(0);
        string key = NewKey();

        int firstId = _transactions.TransferPoints(sender, receiver, 200, key);
        int retryId = _transactions.TransferPoints(sender, receiver, 200, key);

        Assert.Equal(firstId, retryId);
        Assert.Equal(100, _transactions.GetBalance(sender));
    }

    [Fact]
    public void TransferPoints_FailedTransfer_DoesNotSaveKey_SoRetryCanSucceedLater()
    {
        int sender = CreateEmployeeWithBalance(100);
        int receiver = CreateEmployeeWithBalance(0);
        string key = NewKey();

        Assert.Throws<InsufficientPointsException>(() => _transactions.TransferPoints(sender, receiver, 200, key));

        // The sender earns more points, then the client retries with the same key.
        _transactions.CreateTransaction(new Transaction { EmployeeId = sender, Amount = 100 }, NewKey());
        _transactions.TransferPoints(sender, receiver, 200, key);

        Assert.Equal(0, _transactions.GetBalance(sender));
        Assert.Equal(200, _transactions.GetBalance(receiver));
    }

    // ---------- Reused keys for a different request ----------

    [Fact]
    public void TransferPoints_Throws_WhenKeyWasUsedForAnAward()
    {
        int sender = CreateEmployeeWithBalance(500);
        int receiver = CreateEmployeeWithBalance(0);
        string key = NewKey();
        _transactions.CreateTransaction(new Transaction { EmployeeId = sender, Amount = 100 }, key);

        Assert.Throws<IdempotencyKeyConflictException>(() => _transactions.TransferPoints(sender, receiver, 100, key));

        // No points moved.
        Assert.Equal(600, _transactions.GetBalance(sender));
        Assert.Equal(0, _transactions.GetBalance(receiver));
    }

    [Fact]
    public void CreateTransaction_Throws_WhenKeyWasUsedForATransfer()
    {
        int sender = CreateEmployeeWithBalance(500);
        int receiver = CreateEmployeeWithBalance(0);
        string key = NewKey();
        _transactions.TransferPoints(sender, receiver, 200, key);

        Assert.Throws<IdempotencyKeyConflictException>(() => _transactions.CreateTransaction(new Transaction { EmployeeId = sender, Amount = 200 }, key));

        Assert.Equal(300, _transactions.GetBalance(sender));
        Assert.Equal(200, _transactions.GetBalance(receiver));
    }

    [Fact]
    public void CreateTransaction_Throws_WhenKeyIsReusedWithADifferentAmount()
    {
        int employeeId = CreateEmployeeWithBalance(0);
        string key = NewKey();
        _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, key);

        Assert.Throws<IdempotencyKeyConflictException>(() => _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 999 }, key));

        Assert.Equal(100, _transactions.GetBalance(employeeId));
    }

    [Fact]
    public void CreateTransaction_Throws_WhenKeyIsReusedForADifferentEmployee()
    {
        int first = CreateEmployeeWithBalance(0);
        int second = CreateEmployeeWithBalance(0);
        string key = NewKey();
        _transactions.CreateTransaction(new Transaction { EmployeeId = first, Amount = 100 }, key);

        Assert.Throws<IdempotencyKeyConflictException>(() => _transactions.CreateTransaction(new Transaction { EmployeeId = second, Amount = 100 }, key));

        Assert.Equal(100, _transactions.GetBalance(first));
        Assert.Equal(0, _transactions.GetBalance(second));
    }

    [Fact]
    public void TransferPoints_Throws_WhenKeyIsReusedForADifferentReceiver()
    {
        int sender = CreateEmployeeWithBalance(500);
        int receiver = CreateEmployeeWithBalance(0);
        int otherReceiver = CreateEmployeeWithBalance(0);
        string key = NewKey();
        _transactions.TransferPoints(sender, receiver, 200, key);

        Assert.Throws<IdempotencyKeyConflictException>(() => _transactions.TransferPoints(sender, otherReceiver, 200, key));

        Assert.Equal(300, _transactions.GetBalance(sender));
        Assert.Equal(200, _transactions.GetBalance(receiver));
        Assert.Equal(0, _transactions.GetBalance(otherReceiver));
    }

    [Fact]
    public void TransferPoints_Throws_WhenKeyIsReusedWithADifferentAmount()
    {
        int sender = CreateEmployeeWithBalance(500);
        int receiver = CreateEmployeeWithBalance(0);
        string key = NewKey();
        _transactions.TransferPoints(sender, receiver, 200, key);

        Assert.Throws<IdempotencyKeyConflictException>(() => _transactions.TransferPoints(sender, receiver, 50, key));

        Assert.Equal(300, _transactions.GetBalance(sender));
        Assert.Equal(200, _transactions.GetBalance(receiver));
    }

    // ---------- Migrating an old database ----------

    [Fact]
    public void CreateTables_AddsMissingColumns_AndLegacyKeysStillReturnTheirId()
    {
        int employeeId = CreateEmployeeWithBalance(0);
        string legacyKey = NewKey();
        int legacyTransactionId;

        // Rebuild ProcessedRequests with the old schema and save a row the way the old code did.
        using (var connection = new SqliteConnection(Database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
            DROP TABLE ProcessedRequests;
            CREATE TABLE ProcessedRequests (
            ID INTEGER PRIMARY KEY AUTOINCREMENT,
            Key TEXT UNIQUE NOT NULL,
            TransactionId INTEGER NOT NULL,
            ProcessedAt DATETIME NOT NULL
            );
            INSERT INTO Transactions (EmployeeId, Amount) VALUES (@EmployeeId, 100);
            """;
            command.Parameters.AddWithValue("@EmployeeId", employeeId);
            command.ExecuteNonQuery();

            command.Parameters.Clear();
            command.CommandText = "SELECT last_insert_rowid();";
            legacyTransactionId = Convert.ToInt32(command.ExecuteScalar());

            command.CommandText = "INSERT INTO ProcessedRequests (Key, TransactionId, ProcessedAt) VALUES (@Key, @TransactionId, @ProcessedAt);";
            command.Parameters.AddWithValue("@Key", legacyKey);
            command.Parameters.AddWithValue("@TransactionId", legacyTransactionId);
            command.Parameters.AddWithValue("@ProcessedAt", DateTime.UtcNow);
            command.ExecuteNonQuery();
        }

        Database.CreateTables();

        var columns = new List<string>();
        using (var connection = new SqliteConnection(Database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(ProcessedRequests);";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }
        Assert.Contains("Endpoint", columns);
        Assert.Contains("EmployeeId", columns);
        Assert.Contains("ToEmployeeId", columns);
        Assert.Contains("Amount", columns);

        // A retry with the legacy key returns the stored ID and adds no points.
        int retryId = _transactions.CreateTransaction(new Transaction { EmployeeId = employeeId, Amount = 100 }, legacyKey);
        Assert.Equal(legacyTransactionId, retryId);
        Assert.Equal(100, _transactions.GetBalance(employeeId));

        // Running CreateTables again on the migrated database changes nothing.
        Database.CreateTables();
    }
}

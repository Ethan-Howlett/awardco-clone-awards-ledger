using Microsoft.Data.Sqlite;

namespace awardco.Data;

// Learned: How to make the database connection string available to the entire application. I made it a public static class with a public static property.
// It is a property instead of a const so the tests can point it at their own throwaway database file.
// Also moved the creation of the database tables to this class so that they are created when the application starts.
public static class Database
{
    public static string ConnectionString { get; set; } = "Data Source=ledger.db";

    public static void CreateTables()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        // Create the Employee table if it doesn't exist
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS Employee (
        ID INTEGER PRIMARY KEY AUTOINCREMENT,
        FirstName TEXT NOT NULL,
        LastName TEXT NOT NULL,
        Email TEXT NOT NULL UNIQUE
        );
        """;
        command.ExecuteNonQuery();

        // Create the Transaction table if it doesn't exist
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS Transactions (
        ID INTEGER PRIMARY KEY AUTOINCREMENT,
        EmployeeId INTEGER NOT NULL,
        Amount INTEGER NOT NULL,
        FOREIGN KEY(EmployeeId) REFERENCES Employee(ID)
        );
        """;
        command.ExecuteNonQuery();

        // Create a ProccessedRequests table to track requests to prevent duplicates.
        // Endpoint, EmployeeId, ToEmployeeId and Amount record the request, so a reused key can be checked against it.
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS ProcessedRequests (
        ID INTEGER PRIMARY KEY AUTOINCREMENT,
        Key TEXT UNIQUE NOT NULL,
        TransactionId INTEGER NOT NULL,
        ProcessedAt DATETIME NOT NULL,
        Endpoint TEXT,
        EmployeeId INTEGER,
        ToEmployeeId INTEGER,
        Amount INTEGER
        );
        """;
        command.ExecuteNonQuery();

        // Databases created before those columns existed: add any that are missing.
        // Old rows get NULL in them and are treated as legacy (their key returns the stored ID without comparing).
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        command.CommandText = "PRAGMA table_info(ProcessedRequests);";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                existingColumns.Add(reader.GetString(1)); // column 1 is the column name
            }
        }
        foreach (var (name, type) in new[] { ("Endpoint", "TEXT"), ("EmployeeId", "INTEGER"), ("ToEmployeeId", "INTEGER"), ("Amount", "INTEGER") })
        {
            if (!existingColumns.Contains(name))
            {
                command.CommandText = $"ALTER TABLE ProcessedRequests ADD COLUMN {name} {type};";
                command.ExecuteNonQuery();
            }
        }

        connection.Close();
    }
}
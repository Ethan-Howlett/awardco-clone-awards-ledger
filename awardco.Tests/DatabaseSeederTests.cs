using awardco.Data;
using Microsoft.Data.Sqlite;

namespace awardco.Tests;

public class DatabaseSeederTests : TestDatabase
{
    // Small helper to run a query that returns a single number.
    private static int QueryNumber(string sql)
    {
        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public void Seed_AddsEmployeesAndTransactions()
    {
        DatabaseSeeder.Seed();

        Assert.Equal(DatabaseSeeder.EmployeeCount, QueryNumber("SELECT COUNT(*) FROM Employee;"));
        Assert.Equal(DatabaseSeeder.TransactionCount, QueryNumber("SELECT COUNT(*) FROM Transactions;"));
    }

    [Fact]
    public void Seed_DoesNothing_WhenDatabaseAlreadyHasData()
    {
        DatabaseSeeder.Seed();
        DatabaseSeeder.Seed();

        Assert.Equal(DatabaseSeeder.EmployeeCount, QueryNumber("SELECT COUNT(*) FROM Employee;"));
        Assert.Equal(DatabaseSeeder.TransactionCount, QueryNumber("SELECT COUNT(*) FROM Transactions;"));
    }

    [Fact]
    public void Seed_NeverLeavesAnEmployeeWithANegativeBalance()
    {
        DatabaseSeeder.Seed();

        int employeesBelowZero = QueryNumber("""
            SELECT COUNT(*) FROM
                (SELECT EmployeeId FROM Transactions GROUP BY EmployeeId HAVING SUM(Amount) < 0);
            """);

        Assert.Equal(0, employeesBelowZero);
    }
}

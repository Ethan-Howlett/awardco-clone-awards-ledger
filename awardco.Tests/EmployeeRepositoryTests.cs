using awardco.Data;
using awardco.Model;
using Microsoft.Data.Sqlite;

namespace awardco.Tests;

public class EmployeeRepositoryTests : TestDatabase
{
    private readonly EmployeeRepository _employees = new EmployeeRepository();
    private readonly TransactionRepository _transactions = new TransactionRepository();

    [Fact]
    public void CreateEmployee_AssignsAnId()
    {
        var employee = new Employee { FirstName = "Jane", LastName = "Doe", Email = "jane.doe@test.com" };

        employee = _employees.CreateEmployee(employee);

        Assert.NotNull(employee.Id);
        Assert.True(employee.Id > 0);
    }

    [Fact]
    public void GetEmployeeById_ReturnsTheSavedEmployee()
    {
        var employee = new Employee { FirstName = "Jane", LastName = "Doe", Email = "jane.doe@test.com" };
        _employees.CreateEmployee(employee);

        var found = _employees.GetEmployeeById(employee.Id!.Value);

        Assert.NotNull(found);
        Assert.Equal("Jane", found.FirstName);
        Assert.Equal("Doe", found.LastName);
        Assert.Equal("jane.doe@test.com", found.Email);
    }

    [Fact]
    public void GetEmployeeById_ReturnsNull_WhenEmployeeDoesNotExist()
    {
        var found = _employees.GetEmployeeById(999);

        Assert.Null(found);
    }

    [Fact]
    public void CreateEmployee_ReturnsExistingEmployee_WhenEmailAlreadyExists()
    {
        var employee1 = new Employee { FirstName = "Jane", LastName = "Doe", Email = "jane.doe@test.com" };
        _employees.CreateEmployee(employee1);
        var employee2 = new Employee { FirstName = "John", LastName = "Smith", Email = "jane.doe@test.com" };

        var result = _employees.CreateEmployee(employee2);

        Assert.NotNull(result.Id);
        Assert.Equal("Jane", result.FirstName);
        Assert.Equal("Doe", result.LastName);
        Assert.Equal("jane.doe@test.com", result.Email);
    }

    [Fact]
    public void CreateEmployee_ReturnsExistingEmployee_WhenEmailDiffersOnlyByCase()
    {
        var first = _employees.CreateEmployee(new Employee { FirstName = "Jane", LastName = "Doe", Email = "Jane@X.com" });

        var second = _employees.CreateEmployee(new Employee { FirstName = "Jane", LastName = "Doe", Email = "jane@x.com" });

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public void CreateEmployee_StoresEmailLowercasedAndTrimmed()
    {
        var employee = _employees.CreateEmployee(new Employee { FirstName = "Jane", LastName = "Doe", Email = " Jane@X.com " });

        var found = _employees.GetEmployeeById(employee.Id!.Value);

        Assert.NotNull(found);
        Assert.Equal("jane@x.com", found.Email);
    }

    [Fact]
    public void CreateEmployee_FindsLegacyMixedCaseRow()
    {
        // Insert a row directly, bypassing the repository, to simulate data saved before emails were normalized.
        int legacyId;
        using (var connection = new SqliteConnection(Database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Employee (FirstName, LastName, Email) VALUES ('Bob', 'Smith', 'Bob@X.com') RETURNING ID;";
            legacyId = Convert.ToInt32(command.ExecuteScalar());
        }

        var result = _employees.CreateEmployee(new Employee { FirstName = "Bob", LastName = "Smith", Email = "bob@x.com" });

        Assert.Equal(legacyId, result.Id);
    }
}

using awardco.Data;
using Microsoft.Data.Sqlite;

namespace awardco.Model;

public class Employee
{
    public int? Id{ get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
}

public class EmployeeRepository
{
    public Employee CreateEmployee(Employee employee)
    {
        // Normalize the email so "Jane@X.com " and "jane@x.com" are treated as the same employee.
        employee.Email = employee.Email.Trim().ToLowerInvariant();

        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        // Check if the employee already exists in the database by email.
        // lower(Email) is used because rows saved before emails were normalized may still be mixed case.
        command.CommandText = "SELECT COUNT(*) FROM Employee WHERE lower(Email) = @Email;";
        command.Parameters.AddWithValue("@Email", employee.Email);
        var count = Convert.ToInt32(command.ExecuteScalar());
        if (count > 0)
        {
            // throw new Exception("Employee already exists."); // Instead of throwing an exception, let's return the existing employee.
            command.CommandText = "SELECT ID, FirstName, LastName, Email FROM Employee WHERE lower(Email) = @Email;";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new Employee
                {
                    Id = reader.GetInt32(0),
                    FirstName = reader.GetString(1),
                    LastName = reader.GetString(2),
                    Email = reader.GetString(3)
                };
            }
            else
            {
                throw new Exception("Employee exists but could not be retrieved.");
            }
        }

        // Insert the new employee into the database
        command.CommandText = "INSERT INTO Employee (FirstName, LastName, Email) VALUES (@FirstName, @LastName, @Email) RETURNING ID;";
        // Learned: I use Parameters to prevent SQL injection attacks as the values are treated as literal values and never executed.
        // Clear the parameters from the lookup above so the INSERT adds all of its own explicitly.
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@FirstName", employee.FirstName);
        command.Parameters.AddWithValue("@LastName", employee.LastName);
        command.Parameters.AddWithValue("@Email", employee.Email);

        // Save the returnedIdfrom the database to the employee object.
        employee.Id = Convert.ToInt32(command.ExecuteScalar());

        return employee;
    }

    public Employee? GetEmployeeById(int employeeId)
    {
        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        // Retrieve the employee from the database
        command.CommandText = "SELECT ID, FirstName, LastName, Email FROM Employee WHERE Id= @Id;";
        command.Parameters.AddWithValue("@Id", employeeId);
        using var reader = command.ExecuteReader();

        if (reader.Read())
        {
            var employee = new Employee
            {
                Id= reader.GetInt32(0),
                FirstName = reader.GetString(1),
                LastName = reader.GetString(2),
                Email = reader.GetString(3),
            };
            return employee;
        }
        else
        {
            return null; // Employee not found
        }
    }

}

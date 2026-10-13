namespace awardco.Model;

// Thrown when a transaction or transfer names an employee ID that isn't in the database.
public class EmployeeNotFoundException : Exception
{
    public int EmployeeId { get; }

    public EmployeeNotFoundException(int employeeId)
        : base($"Employee {employeeId} not found.")
    {
        EmployeeId = employeeId;
    }
}

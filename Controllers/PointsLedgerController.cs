using Microsoft.AspNetCore.Mvc;
using awardco.Model;

namespace awardco.Controllers;

[Route("api/[controller]")]
[ApiController]
public class pointsLedgerController : ControllerBase
{
    private readonly EmployeeRepository _employeeRepository;
    private readonly TransactionRepository _transactionRepository;

    public pointsLedgerController(EmployeeRepository repository, TransactionRepository transactionRepository)
    {
        _employeeRepository = repository;
        _transactionRepository = transactionRepository;
    }

    [HttpPost]
    public ActionResult<Employee> CreateEmployee(Employee employee)
    {
        // Save the employee to the database
        employee = _employeeRepository.CreateEmployee(employee);

        // Learned: nameof(...) must name an action in this controller.
        return CreatedAtAction(nameof(GetEmployee), new { employeeId = employee.Id }, employee);
    }

    [HttpGet("{employeeId}")]
    public ActionResult<Employee> GetEmployee(int employeeId)
    {
        // Retrieve the employee from the database
        var employee = _employeeRepository.GetEmployeeById(employeeId);

        if (employee == null)
        {
            return NotFound();
        }

        return Ok(employee);
    }

    [HttpGet("{employeeId}/balance")]
    public ActionResult<int> GetBalance(int employeeId)
    {
        // Return 404 if the employee does not exist, otherwise retrieve their balance
        var employee = _employeeRepository.GetEmployeeById(employeeId);
        if (employee == null)
        {
            return NotFound($"Employee {employeeId} not found.");
        }

        var balance = _transactionRepository.GetBalance(employeeId);

        return Ok(balance);
    }

    [HttpPost("transactions")]
    public ActionResult<int> CreateTransaction(Transaction transaction, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
    {
        // Save the transaction to the database
        try
        {
            var transactionId = _transactionRepository.CreateTransaction(transaction, idempotencyKey);
            return Ok(transactionId);
        }
        catch (EmployeeNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (IdempotencyKeyConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPost("transfer")]
    public ActionResult<int> TransferPoints(int fromEmployeeId, int toEmployeeId, int amount, [FromHeader(Name = "Idempotency-Key")] string idempotencyKey)
    {
        // Transfer points between employees
        try
        {
            var transactionId = _transactionRepository.TransferPoints(fromEmployeeId, toEmployeeId, amount, idempotencyKey);
            return Ok(transactionId);
        }
        catch (EmployeeNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (IdempotencyKeyConflictException ex)
        {
            return Conflict(ex.Message);
        }
        catch (InvalidTransferException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InsufficientPointsException ex)
        {
            return UnprocessableEntity(ex.Message);
        }
    }
}
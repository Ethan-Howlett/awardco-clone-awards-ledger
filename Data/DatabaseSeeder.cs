using Microsoft.Data.Sqlite;

namespace awardco.Data;

// Fills an empty database with fake employees and transactions so there is data to try the API with.
// Only called in development mode (see Program.cs).
public static class DatabaseSeeder
{
    public const int EmployeeCount = 25;
    public const int TransactionCount = 500;

    private static readonly string[] FirstNames =
    {
        "Ava", "Liam", "Olivia", "Noah", "Emma", "James", "Sophia", "Lucas", "Mia", "Ethan",
        "Harper", "Mason", "Ella", "Logan", "Aria", "Jacob", "Chloe", "Henry", "Grace", "Owen"
    };

    private static readonly string[] LastNames =
    {
        "Smith", "Johnson", "Brown", "Garcia", "Miller", "Davis", "Martinez", "Wilson", "Anderson", "Taylor",
        "Thomas", "Moore", "Jackson", "Martin", "Lee", "Thompson", "White", "Harris", "Clark", "Young"
    };

    private static readonly string[] Domains =
    {
        "example.com", "test.com", "demo.com", "sample.com", "mydomain.com"
    };

    public static void Seed()
    {
        using var connection = new SqliteConnection(Database.ConnectionString);
        connection.Open();

        // Only seed an empty database, so restarting the app doesn't add another 25 employees every time.
        using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = "SELECT COUNT(*) FROM Employee;";
            if (Convert.ToInt32(countCommand.ExecuteScalar()) > 0)
            {
                return;
            }
        }

        // A fixed seed number means Random gives the same "random" data every time, which makes bugs easier to reproduce.
        var random = new Random(42);

        // Doing all the inserts inside one database transaction is much faster than 525 separate saves,
        // and if anything fails, nothing is saved.
        using var dbTransaction = connection.BeginTransaction();

        var employeeIds = new List<int>();
        using (var employeeCommand = connection.CreateCommand())
        {
            employeeCommand.Transaction = dbTransaction;
            employeeCommand.CommandText = "INSERT INTO Employee (FirstName, LastName, Email) VALUES (@FirstName, @LastName, @Email) RETURNING ID;";
            var firstName = employeeCommand.Parameters.Add("@FirstName", SqliteType.Text);
            var lastName = employeeCommand.Parameters.Add("@LastName", SqliteType.Text);
            var email = employeeCommand.Parameters.Add("@Email", SqliteType.Text);

            for (int i = 0; i < EmployeeCount; i++)
            {
                firstName.Value = FirstNames[random.Next(FirstNames.Length)];
                lastName.Value = LastNames[random.Next(LastNames.Length)];
                email.Value = $"{firstName.Value}.{lastName.Value}{i}@{Domains[random.Next(Domains.Length)]}".ToLower();
                employeeIds.Add(Convert.ToInt32(employeeCommand.ExecuteScalar()));
            }
        }

        // Track each employee's balance while seeding so a redemption never takes someone below 0.
        var balances = employeeIds.ToDictionary(id => id, id => 0);

        using (var transactionCommand = connection.CreateCommand())
        {
            transactionCommand.Transaction = dbTransaction;
            transactionCommand.CommandText = "INSERT INTO Transactions (EmployeeId, Amount) VALUES (@EmployeeId, @Amount);";
            var employeeId = transactionCommand.Parameters.Add("@EmployeeId", SqliteType.Integer);
            var amount = transactionCommand.Parameters.Add("@Amount", SqliteType.Integer);

            for (int i = 0; i < TransactionCount; i++)
            {
                int id = employeeIds[random.Next(employeeIds.Count)];

                // About 80% awards (+25 to +500), 20% redemptions (-25 to -300), all in steps of 25.
                int points;
                if (random.Next(100) < 80)
                {
                    points = random.Next(1, 21) * 25;
                }
                else
                {
                    points = -random.Next(1, 13) * 25;
                    if (balances[id] + points < 0)
                    {
                        points = -points; // Can't afford it, so make it an award instead.
                    }
                }

                employeeId.Value = id;
                amount.Value = points;
                transactionCommand.ExecuteNonQuery();
                balances[id] += points;
            }
        }

        dbTransaction.Commit();
    }
}

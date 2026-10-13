using awardco.Data;
using Microsoft.Data.Sqlite;

// Database.ConnectionString is shared (static), so tests must run one at a time, not in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace awardco.Tests;

// Test classes inherit from this to get a brand new, empty database for every test.
// xUnit creates a new instance of the test class for each test, so the constructor runs before
// every test (setup) and Dispose runs after every test (cleanup).
public abstract class TestDatabase : IDisposable
{
    private readonly string _filePath;

    protected TestDatabase()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"awardco-test-{Guid.NewGuid()}.db");
        Database.ConnectionString = $"Data Source={_filePath}";
        Database.CreateTables();
    }

    public void Dispose()
    {
        // SQLite keeps connections open in a pool for reuse; close them so the file can be deleted.
        SqliteConnection.ClearAllPools();
        File.Delete(_filePath);
    }
}

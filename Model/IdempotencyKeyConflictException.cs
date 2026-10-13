namespace awardco.Model;

// Thrown when an idempotency key is reused for a request that doesn't match the one it was first used for.
public class IdempotencyKeyConflictException : Exception
{
    public IdempotencyKeyConflictException()
        : base("Idempotency key was already used for a different request.")
    {
    }
}

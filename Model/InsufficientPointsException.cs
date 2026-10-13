namespace awardco.Model;

// Thrown when the sender of a transfer doesn't have enough points to cover it.
public class InsufficientPointsException : InvalidOperationException
{
    public InsufficientPointsException()
        : base("Insufficient points")
    {
    }
}

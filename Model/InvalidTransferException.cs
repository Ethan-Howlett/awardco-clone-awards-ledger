namespace awardco.Model;

// Thrown when a transfer request is invalid, such as a non-positive amount or sending points to yourself.
public class InvalidTransferException : ArgumentException
{
    public InvalidTransferException(string message)
        : base(message)
    {
    }
}

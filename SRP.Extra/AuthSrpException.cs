namespace SRP.Extra;

public class AuthSrpException : Exception
{
    public enum ReasonEnum
    {
        Undefined = 0,
        ConnectionError,
        WebSocketError,
        NotCorrectPassword,
        ProtocolErrorOrAttackDetected,
    }

    public ReasonEnum Reason { get; }

    public AuthSrpException(ReasonEnum reason)
        : base($"Error during src auth reason={reason}")
    {
        Reason = reason;
    }

    public AuthSrpException(ReasonEnum reason, Exception inner)
        : base($"Error during src auth reason={reason}", inner)
    {
        Reason = reason;
    }
}
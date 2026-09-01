namespace CleanArchitecture.Application.Common.Exceptions;

/// <summary>Raised before a handler executes when a request's authorization contract is invalid.</summary>
public sealed class AuthorizationMetadataMissingException : Exception
{
    public AuthorizationMetadataMissingException(Type requestType, string reason)
        : base($"Authorization metadata for '{requestType.FullName}' is invalid: {reason}.")
    {
        RequestType = requestType;
    }

    public Type RequestType { get; }
}

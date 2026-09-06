using CleanArchitecture.Domain.IdentityAccess.People;

namespace CleanArchitecture.Application.IdentityAccess.People;

/// <summary>
/// The one seam that turns a documentary identity into stored ciphertext and back. It is a port because the key
/// material belongs to the deployment, not to the application: nothing above Infrastructure may reach it, and
/// nothing may read a document without going through here (IA-REQ-050).
/// </summary>
public interface IIdentityDocumentProtector
{
    /// <summary>Encrypts the canonical tuple. The result is what the row stores; nothing else about it is stored.</summary>
    string Protect(NormalizedDocument document);

    /// <summary>
    /// The owner's own read. A payload this deployment's key cannot open answers <see langword="null"/> rather than
    /// throwing, because a rotated or foreign envelope is a state to report, not an unexpected failure.
    /// </summary>
    NormalizedDocument? Reveal(string ciphertext);
}

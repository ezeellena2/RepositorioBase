using CleanArchitecture.Infrastructure.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

/// <summary>
/// The local drop folder, which is how a developer and the browser suite read the links this system sends.
/// <para>
/// It reports delivery, and the dispatcher believes it: a message reported delivered has its token ciphertext
/// erased, because the token has left the building. So "delivered" here has to mean a whole message is on disk
/// and readable — not that a file with the right name exists.
/// </para>
/// </summary>
public sealed class LocalFolderEmailSenderTests
{
    private const string Recipient = "recipient@example.test";
    private const string Subject = "Confirm your email";
    private const string Body = "Open this link to confirm: https://localhost/confirm-email#token=private";

    /// <summary>
    /// The interrupted write, as a fixture: an empty file under the message's own name. It is what a crash
    /// between creating the file and writing it leaves behind, and the sender used to accept it as proof that the
    /// message had already gone — reporting success over a message nobody could read, after which the dispatcher
    /// deleted the only copy of the token.
    /// </summary>
    [Test]
    public async Task An_interrupted_write_is_not_evidence_of_delivery_and_is_published_again()
    {
        using var drop = new TemporaryFolder();
        var sender = Sender(drop);
        var messageId = Guid.NewGuid().ToString("D");
        var path = Path.Combine(drop.Path, $"{messageId}.txt");
        await File.WriteAllTextAsync(path, string.Empty);

        var receipt = await sender.SendAsync(Recipient, Subject, Body, messageId, CancellationToken.None);

        receipt.Delivered.ShouldBeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.ShouldContain(Body, Case.Sensitive, "the message the receipt claims was delivered has to be there.");
        content.ShouldContain($"To: {Recipient}");
    }

    /// <summary>A file cut off mid-headers is the same kind of lie, and is replaced the same way.</summary>
    [Test]
    public async Task A_truncated_message_is_published_again()
    {
        using var drop = new TemporaryFolder();
        var sender = Sender(drop);
        var messageId = Guid.NewGuid().ToString("D");
        var path = Path.Combine(drop.Path, $"{messageId}.txt");
        await File.WriteAllTextAsync(path, $"To: {Recipient}\r\nFrom: sender@example.test\r\n");

        await sender.SendAsync(Recipient, Subject, Body, messageId, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).ShouldContain(Body);
    }

    /// <summary>
    /// A whole message is left exactly as it was. That is what the provider's idempotency key buys and what the
    /// dispatcher's replay expects: a retry after an uncertain answer must reconcile onto the same delivery.
    /// </summary>
    [Test]
    public async Task A_complete_message_is_not_written_a_second_time()
    {
        using var drop = new TemporaryFolder();
        var sender = Sender(drop);
        var messageId = Guid.NewGuid().ToString("D");
        var path = Path.Combine(drop.Path, $"{messageId}.txt");
        await sender.SendAsync(Recipient, Subject, Body, messageId, CancellationToken.None);
        var first = await File.ReadAllTextAsync(path);

        await sender.SendAsync(Recipient, Subject, Body, messageId, CancellationToken.None);

        (await File.ReadAllTextAsync(path)).ShouldBe(first, "a delivered message is not re-sent.");
    }

    /// <summary>
    /// Nothing partial is ever visible under the name a reader watches. The acceptance journeys poll this folder
    /// while it is being written, so a message appears whole or not at all.
    /// </summary>
    [Test]
    public async Task The_published_message_appears_whole_and_leaves_no_staging_file_behind()
    {
        using var drop = new TemporaryFolder();
        var sender = Sender(drop);

        await sender.SendAsync(Recipient, Subject, Body, Guid.NewGuid().ToString("D"), CancellationToken.None);

        Directory.GetFiles(drop.Path, "*.txt").Length.ShouldBe(1);
        Directory.GetFiles(drop.Path).Length.ShouldBe(1, "staging files do not survive a publish.");
    }

    /// <summary>The delivery key is an outbox message id, so a name that is not one is a programming error.</summary>
    [Test]
    public async Task A_delivery_key_that_is_not_an_outbox_message_id_is_refused()
    {
        using var drop = new TemporaryFolder();
        var sender = Sender(drop);

        await Should.ThrowAsync<ArgumentException>(
            () => sender.SendAsync(Recipient, Subject, Body, "not-a-guid", CancellationToken.None));
    }

    private static LocalFolderEmailSender Sender(TemporaryFolder drop) =>
        new(Options.Create(new IdentityEmailOptions
        {
            Enabled = true,
            FromAddress = "sender@example.test",
            PublicOrigin = "https://localhost",
            LocalDropPath = drop.Path
        }), new LocalEnvironment());

    private sealed class LocalEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"local-mail-{Guid.NewGuid():N}");

        public TemporaryFolder() => Directory.CreateDirectory(Path);

        // The folder holds live links in plain text, so it does not outlive the test that made it.
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}

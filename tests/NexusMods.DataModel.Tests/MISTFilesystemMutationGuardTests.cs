using System.Text;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Abstractions.Loadouts.Synchronizers.Rules;
using NexusMods.Hashing.xxHash3;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-003B.5D: Exercise the mutation guard against real
/// filesystem contents without touching installed games.
/// </summary>
public class MISTFilesystemMutationGuardTests
{
    private static GamePath GamePathFor(string name) =>
        new(
            default,
            RelativePath.CreateUnsafe($"mist/{name}"));

    private static Dictionary<GamePath, SyncNode> Plan(
        GamePath path,
        Actions actions) =>
        new()
        {
            [path] = new SyncNode { Actions = actions },
        };

    private static async Task<Hash> HashBytes(byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return (await MultiHasher.HashStream(stream)).XxHash3;
    }

    [Fact]
    public async Task VerifiedManagedFileIsAccepted()
    {
        using var temp = new TestDirectory();

        var path = GamePathFor("verified.archive");
        var destination = temp.FilePath("verified.archive");

        var content = Encoding.UTF8.GetBytes("Verified managed file");
        await File.WriteAllBytesAsync(destination, content);

        var expectedHash = await HashBytes(content);
        var verificationCalled = false;

        await MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
            Plan(path, Actions.DeleteFromDisk),
            _ => destination,
            (_, observedHash) =>
            {
                verificationCalled = true;
                return observedHash == expectedHash;
            });

        verificationCalled.Should().BeTrue();
        File.ReadAllBytes(destination).Should().Equal(content);
    }

    [Fact]
    public async Task UnmanagedExistingFileIsRejectedWithoutChanges()
    {
        using var temp = new TestDirectory();

        var path = GamePathFor("unmanaged.archive");
        var destination = temp.FilePath("unmanaged.archive");

        var content = Encoding.UTF8.GetBytes("User-owned content");
        await File.WriteAllBytesAsync(destination, content);

        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                Plan(path, Actions.DeleteFromDisk),
                _ => destination,
                (_, _) => false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ownership is unavailable*");

        File.ReadAllBytes(destination).Should().Equal(content);
    }

    [Fact]
    public async Task ModifiedManagedFileIsRejected()
    {
        using var temp = new TestDirectory();

        var path = GamePathFor("modified.archive");
        var destination = temp.FilePath("modified.archive");

        var original = Encoding.UTF8.GetBytes(
            "Originally deployed content");

        var modified = Encoding.UTF8.GetBytes(
            "Content changed outside MIST");

        var deployedHash = await HashBytes(original);

        await File.WriteAllBytesAsync(destination, modified);

        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                Plan(path, Actions.DeleteFromDisk),
                _ => destination,
                (_, observedHash) =>
                    observedHash == deployedHash);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ownership is unavailable*");

        File.ReadAllBytes(destination).Should().Equal(modified);
    }

    [Fact]
    public async Task MissingDeletionTargetIsRejected()
    {
        using var temp = new TestDirectory();

        var path = GamePathFor("missing.archive");
        var destination = temp.FilePath("missing.archive");

        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                Plan(path, Actions.DeleteFromDisk),
                _ => destination,
                (_, _) => throw new Exception(
                    "Receipt verification should not be called"));

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*plan is stale*");

        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task NewFileCanBeCreatedWithoutExistingReceipt()
    {
        using var temp = new TestDirectory();

        var path = GamePathFor("new.archive");
        var destination = temp.FilePath("new.archive");

        await MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
            Plan(path, Actions.ExtractToDisk),
            _ => destination,
            (_, _) => throw new Exception(
                "Receipt verification should not be called"));

        // Preflight validates; it must not create the file.
        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task SymbolicLinkDestinationIsRejected()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TestDirectory();

        var path = GamePathFor("linked.archive");

        var realFile = temp.FilePath("real.archive");
        var link = temp.FilePath("linked.archive");

        var content = Encoding.UTF8.GetBytes("Protected target");
        await File.WriteAllBytesAsync(realFile, content);

        File.CreateSymbolicLink(link, realFile);

        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                Plan(path, Actions.DeleteFromDisk),
                _ => link,
                (_, _) => true);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*symbolic link*");

        File.ReadAllBytes(realFile).Should().Equal(content);
    }

    [Fact]
    public async Task UnauthorizedFileRejectsEntirePlanWithoutChanges()
    {
        using var temp = new TestDirectory();

        var managedPath = GamePathFor("managed.archive");
        var unmanagedPath = GamePathFor("unmanaged.archive");

        var managedFile = temp.FilePath("managed.archive");
        var unmanagedFile = temp.FilePath("unmanaged.archive");

        var managedContent = Encoding.UTF8.GetBytes(
            "MIST-owned content");

        var unmanagedContent = Encoding.UTF8.GetBytes(
            "User-owned content");

        await File.WriteAllBytesAsync(
            managedFile, managedContent);

        await File.WriteAllBytesAsync(
            unmanagedFile, unmanagedContent);

        var managedHash = await HashBytes(managedContent);

        var plan = new Dictionary<GamePath, SyncNode>
        {
            [managedPath] = new SyncNode
            {
                Actions = Actions.DeleteFromDisk,
            },
            [unmanagedPath] = new SyncNode
            {
                Actions = Actions.DeleteFromDisk,
            },
        };

        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                plan,
                path => path == managedPath
                    ? managedFile
                    : unmanagedFile,
                (path, observedHash) =>
                    path == managedPath &&
                    observedHash == managedHash);

        await act.Should()
            .ThrowAsync<InvalidOperationException>();

        // No operation was executed against either file.
        File.ReadAllBytes(managedFile)
            .Should().Equal(managedContent);

        File.ReadAllBytes(unmanagedFile)
            .Should().Equal(unmanagedContent);
    }

    [Theory]
    [InlineData(Actions.DeleteFromDisk)]
    [InlineData(Actions.ExtractToDisk)]
    public async Task LinkedParentDirectoryIsRejected(
        Actions action)
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TestDirectory();

        var realParent = temp.FilePath("real");
        var linkedParent = temp.FilePath("linked");

        Directory.CreateDirectory(realParent);
        Directory.CreateSymbolicLink(
            linkedParent, realParent);

        try
        {
            var path = GamePathFor("linked/mod.archive");
            var destination = Path.Combine(
                linkedParent, "mod.archive");

            var original = Encoding.UTF8.GetBytes(
                "Protected file");

            await File.WriteAllBytesAsync(
                destination, original);

            Func<Task> act = () =>
                MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                    Plan(path, action),
                    _ => destination,
                    (_, _) => true);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*parent directory is a symbolic link*");

            File.ReadAllBytes(
                Path.Combine(realParent, "mod.archive"))
                .Should().Equal(original);
        }
        finally
        {
            // Remove the link itself, not its target.
            Directory.Delete(linkedParent);
        }
    }

    [Fact]
    public async Task OrdinaryNestedDirectoryIsAccepted()
    {
        using var temp = new TestDirectory();

        var parent = temp.FilePath("normal");
        Directory.CreateDirectory(parent);

        var path = GamePathFor("normal/new.archive");
        var destination = Path.Combine(
            parent, "new.archive");

        await MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
            Plan(path, Actions.ExtractToDisk),
            _ => destination,
            (_, _) => false);

        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task RegularFileCannotBeUsedAsParentDirectory()
    {
        using var temp = new TestDirectory();

        var parent = temp.FilePath("not-a-directory");

        await File.WriteAllTextAsync(
            parent, "I am a file");

        var path = GamePathFor("invalid/mod.archive");

        var destination = Path.Combine(
            parent, "mod.archive");

        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                Plan(path, Actions.ExtractToDisk),
                _ => destination,
                (_, _) => false);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*parent path is not a directory*");

        File.ReadAllText(parent)
            .Should().Be("I am a file");
    }

    [Theory]
    [InlineData(Actions.ExtractToDisk)]
    [InlineData(Actions.WriteIntrinsic)]
    [InlineData(Actions.DeleteFromDisk | Actions.ExtractToDisk)]
    public async Task ExistingFileCannotEnterUnprotectedWritePath(
        Actions actions)
    {
        using var temp = new TestDirectory();

        var path = GamePathFor("replacement.archive");
        var destination = temp.FilePath("replacement.archive");

        var original = Encoding.UTF8.GetBytes(
            "Existing managed file");

        await File.WriteAllBytesAsync(
            destination, original);

        var expectedHash = await HashBytes(original);

        // Even a matching receipt must not bypass the
        // unfinished existing-file replacement workflow.
        Func<Task> act = () =>
            MISTFilesystemMutationGuard.EnsureAuthorizedAsync(
                Plan(path, actions),
                _ => destination,
                (_, observedHash) =>
                    observedHash == expectedHash);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*safe existing-file writes are not yet supported*");

        File.ReadAllBytes(destination)
            .Should().Equal(original);
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _root;

        public TestDirectory()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "mist-guard-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_root);
        }

        public string FilePath(string name) =>
            Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}

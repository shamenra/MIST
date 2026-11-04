using System.Text;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTFileQuarantineTests
{
    [Fact]
    public async Task VerifiedFileCanBeMovedAndRestored()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            await MISTFileQuarantine.MoveVerifiedAsync(
                f.Original, f.Quarantine, f.Hash);

            File.Exists(f.Original).Should().BeFalse();
            File.ReadAllBytes(f.Quarantine)
                .Should().Equal(f.Content);

            await MISTFileQuarantine.RestoreVerifiedAsync(
                f.Original, f.Quarantine, f.Hash);

            File.ReadAllBytes(f.Original)
                .Should().Equal(f.Content);

            File.Exists(f.Quarantine).Should().BeFalse();
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task ModifiedContentCannotBeQuarantined()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            var modified = Encoding.UTF8.GetBytes(
                "Externally modified content");

            await File.WriteAllBytesAsync(
                f.Original, modified);

            Func<Task> act = () =>
                MISTFileQuarantine.MoveVerifiedAsync(
                    f.Original, f.Quarantine, f.Hash);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(f.Original)
                .Should().Equal(modified);

            File.Exists(f.Quarantine).Should().BeFalse();
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task ExistingQuarantineCannotBeOverwritten()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            var existing = Encoding.UTF8.GetBytes(
                "Unrelated user data");

            await File.WriteAllBytesAsync(
                f.Quarantine, existing);

            Func<Task> act = () =>
                MISTFileQuarantine.MoveVerifiedAsync(
                    f.Original, f.Quarantine, f.Hash);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(f.Quarantine)
                .Should().Equal(existing);

            File.ReadAllBytes(f.Original)
                .Should().Equal(f.Content);
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task RestorationPreservesOccupiedOriginal()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            await MISTFileQuarantine.MoveVerifiedAsync(
                f.Original, f.Quarantine, f.Hash);

            var replacement = Encoding.UTF8.GetBytes(
                "New content from another process");

            await File.WriteAllBytesAsync(
                f.Original, replacement);

            Func<Task> act = () =>
                MISTFileQuarantine.RestoreVerifiedAsync(
                    f.Original, f.Quarantine, f.Hash);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(f.Original)
                .Should().Equal(replacement);

            File.ReadAllBytes(f.Quarantine)
                .Should().Equal(f.Content);
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task SymbolicLinkFileCannotBeQuarantined()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var f = await Fixture.CreateAsync();

        try
        {
            var target = Path.Combine(
                f.Directory, "protected.archive");

            File.Move(f.Original, target);

            File.CreateSymbolicLink(
                f.Original, target);

            Func<Task> act = () =>
                MISTFileQuarantine.MoveVerifiedAsync(
                    f.Original, f.Quarantine, f.Hash);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(target)
                .Should().Equal(f.Content);

            File.Exists(f.Quarantine).Should().BeFalse();
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task SymbolicLinkParentCannotBeQuarantined()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var f = await Fixture.CreateAsync();

        try
        {
            var linkedParent = Path.Combine(
                f.Directory, "linked");

            Directory.CreateSymbolicLink(
                linkedParent, f.Directory);

            var linkedOriginal = Path.Combine(
                linkedParent,
                Path.GetFileName(f.Original));

            var linkedQuarantine = Path.Combine(
                linkedParent,
                Path.GetFileName(f.Quarantine));

            Func<Task> act = () =>
                MISTFileQuarantine.MoveVerifiedAsync(
                    linkedOriginal,
                    linkedQuarantine,
                    f.Hash);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(f.Original)
                .Should().Equal(f.Content);

            File.Exists(f.Quarantine).Should().BeFalse();
        }
        finally
        {
            f.Cleanup();
        }
    }

    private sealed class Fixture
    {
        public required string Directory { get; init; }
        public required string Original { get; init; }
        public required string Quarantine { get; init; }
        public required byte[] Content { get; init; }
        public required Hash Hash { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "mist-quarantine-test-" +
                Guid.NewGuid().ToString("N"));

            System.IO.Directory.CreateDirectory(directory);

            var original = Path.Combine(
                directory, "managed.archive");

            var quarantine =
                MISTFileQuarantine.CreateQuarantinePath(
                    original);

            var content = Encoding.UTF8.GetBytes(
                "Verified M.I.S.T. deployment");

            await File.WriteAllBytesAsync(
                original, content);

            await using var stream =
                new MemoryStream(content);

            var hash =
                (await MultiHasher.HashStream(stream)).XxHash3;

            return new Fixture
            {
                Directory = directory,
                Original = original,
                Quarantine = quarantine,
                Content = content,
                Hash = hash,
            };
        }

        public void Cleanup()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(
                    Directory, recursive: true);
            }
        }
    }
}

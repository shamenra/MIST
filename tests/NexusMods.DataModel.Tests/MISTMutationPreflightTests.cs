using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Abstractions.Loadouts.Synchronizers.Rules;
using NexusMods.Sdk.Games;
using NexusMods.Paths;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTMutationPreflightTests
{
    // Use an initialized relative path for policy tests.
    // Filesystem access is supplied by the test callbacks.
    private static readonly GamePath TestPath = new(
        default,
        RelativePath.CreateUnsafe("mist/policy-test.archive"));

    private static Dictionary<GamePath, SyncNode> Plan(Actions actions) =>
        new()
        {
            [TestPath] = new SyncNode { Actions = actions },
        };

    [Theory]
    [InlineData(Actions.DeleteFromDisk)]
    [InlineData(Actions.ExtractToDisk)]
    [InlineData(Actions.WriteIntrinsic)]
    public void UnknownExistingFileCannotBeModified(Actions actions)
    {
        Action act = () => MISTMutationPreflight.EnsureAuthorized(
            Plan(actions),
            existsOnDisk: _ => true,
            hasVerifiedManagedReceipt: _ => false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ownership is unavailable*");
    }

    [Fact]
    public void VerifiedManagedFileCanBeModified()
    {
        Action act = () => MISTMutationPreflight.EnsureAuthorized(
            Plan(Actions.DeleteFromDisk | Actions.ExtractToDisk),
            existsOnDisk: _ => true,
            hasVerifiedManagedReceipt: _ => true);

        act.Should().NotThrow();
    }

    [Fact]
    public void CreatingNewFileDoesNotRequireExistingFileOwnership()
    {
        Action act = () => MISTMutationPreflight.EnsureAuthorized(
            Plan(Actions.ExtractToDisk),
            existsOnDisk: _ => false,
            hasVerifiedManagedReceipt: _ => false);

        act.Should().NotThrow();
    }

    [Fact]
    public void MissingFileCannotBeDeletedUsingStalePlan()
    {
        Action act = () => MISTMutationPreflight.EnsureAuthorized(
            Plan(Actions.DeleteFromDisk),
            existsOnDisk: _ => false,
            hasVerifiedManagedReceipt: _ => true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*plan is stale*");
    }

    [Fact]
    public void OrdinaryNonMutatingActionsDoNotRequireOwnership()
    {
        Action act = () => MISTMutationPreflight.EnsureAuthorized(
            Plan(Actions.DoNothing),
            existsOnDisk: _ => true,
            hasVerifiedManagedReceipt: _ => false);

        act.Should().NotThrow();
    }

    [Fact]
    public void ExistingRuleCanDeleteFileAbsentFromDesiredLoadout()
    {
        var actions = ActionMapping.MapActions(
            SignatureShorthand.AAx_XXx_iL);

        actions.HasFlag(Actions.DeleteFromDisk).Should().BeTrue();

        Action act = () => MISTMutationPreflight.EnsureAuthorized(
            Plan(actions),
            existsOnDisk: _ => true,
            hasVerifiedManagedReceipt: _ => false);

        act.Should().Throw<InvalidOperationException>();
    }
}

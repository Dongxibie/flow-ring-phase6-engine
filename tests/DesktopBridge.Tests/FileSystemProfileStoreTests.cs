using FlowRing.DesktopBridge.Storage;
using FlowRing.RingCore;
using FlowRing.RingCore.Profile;
using FlowRing.RingCore.Ring;
using FlowRing.RingCore.Storage;
using FluentAssertions;
using Xunit;

namespace FlowRing.DesktopBridge.Tests;

public sealed class FileSystemProfileStoreTests : IDisposable
{
    private readonly string _root;

    public FileSystemProfileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FlowRing-ProfileStoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            // 忽略清理失败
        }
    }

    private static ProfileData BuildSample(string id) => new()
    {
        Metadata = new ProfileMetadata(id, id, "1.0.0", "1.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, new string('0', 64)),
        RootRingId = "root",
        RingGraph = new Dictionary<string, RingNode>(),
        ActionRefs = Array.Empty<string>(),
        ContextRules = Array.Empty<ProfileResolverRule>(),
    };

    [Fact]
    public async Task SaveThenLoadReturnsSameProfile()
    {
        FileSystemProfileStore store = new(_root);
        var profile = BuildSample("test-profile");

        await store.SaveAsync(profile, default);
        var loaded = await store.LoadAsync("test-profile", default);

        loaded.Should().NotBeNull();
        loaded!.Metadata.Id.Should().Be("test-profile");
    }

    [Fact]
    public async Task ListReturnsSavedProfileMetadata()
    {
        FileSystemProfileStore store = new(_root);
        await store.SaveAsync(BuildSample("a"), default);
        await store.SaveAsync(BuildSample("b"), default);

        var list = await store.ListAsync(default);

        list.Should().Contain(p => p.Id == "a");
        list.Should().Contain(p => p.Id == "b");
    }

    [Fact]
    public async Task LoadMissingProfileReturnsNull()
    {
        FileSystemProfileStore store = new(_root);
        var loaded = await store.LoadAsync("does-not-exist", default);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRemovesProfile()
    {
        FileSystemProfileStore store = new(_root);
        await store.SaveAsync(BuildSample("to-delete"), default);
        await store.DeleteAsync("to-delete", default);
        var loaded = await store.LoadAsync("to-delete", default);
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task SaveCreatesSnapshotAndKeepsLatestFive()
    {
        FileSystemProfileStore store = new(_root);
        for (int i = 0; i < 7; i++)
        {
            await store.SaveAsync(BuildSample("p"), default);
            await Task.Delay(5);
        }

        var snapshots = await store.ListSnapshotsAsync("p", default);

        snapshots.Count.Should().Be(5);
    }
}
using FlowRing.RingCore;
using FlowRing.RingCore.Action;
using FlowRing.RingCore.Profile;
using FlowRing.RingCore.Ring;
using FluentAssertions;
using Xunit;

namespace FlowRing.RingCore.Tests;

public sealed class ActionEngineTests
{
    private sealed class FakeExecutor : IActionExecutor
    {
        public ActionKind Kind { get; }
        public PermissionTier RequiredTier => PermissionTier.Safe;
        public ExecutionResult Result { get; set; } = new(true, null, 1);
        public string? LastActionRef { get; private set; }
        public ActionContext? LastContext { get; private set; }

        public FakeExecutor(ActionKind kind) => Kind = kind;

        public ValueTask<ExecutionResult> ExecuteAsync(string actionId, ActionContext ctx, CancellationToken ct)
        {
            LastActionRef = actionId;
            LastContext = ctx;
            return ValueTask.FromResult(Result);
        }
    }

    [Fact]
    public async Task ExecuteRunsRegistryLookupAndExecutor()
    {
        var registry = new MemoryActionRegistry();
        await registry.UpsertAsync(new ActionDef("key-t", ActionKind.Keyboard, "Type T", ExecutionPriority(), "{}"), default);
        var kb = new FakeExecutor(ActionKind.Keyboard);
        var engine = new ActionEngine(registry, new Dictionary<ActionKind, IActionExecutor> { [ActionKind.Keyboard] = kb });

        var ctx = new ActionContext("default", new ApplicationContext("test", "t", 0, DateTimeOffset.UtcNow), 0);
        var result = await engine.ExecuteAsync("key-t", ctx, PermissionTier.Safe, default);

        result.Success.Should().BeTrue();
        kb.LastActionRef.Should().Be("key-t");
    }

    [Fact]
    public async Task UnknownActionReturnsFailure()
    {
        var registry = new MemoryActionRegistry();
        var engine = new ActionEngine(registry, new Dictionary<ActionKind, IActionExecutor>());
        var ctx = new ActionContext("default", new ApplicationContext("test", "t", 0, DateTimeOffset.UtcNow), 0);
        var result = await engine.ExecuteAsync("missing", ctx, PermissionTier.Safe, default);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("未在 Registry 中注册");
    }

    [Fact]
    public async Task UnauthorizedActionReturnsFailure()
    {
        var registry = new MemoryActionRegistry();
        await registry.UpsertAsync(new ActionDef("danger", ActionKind.Application, "App", PermissionTier.Dangerous, "{}"), default);
        var executor = new FakeExecutor(ActionKind.Application);
        var engine = new ActionEngine(registry, new Dictionary<ActionKind, IActionExecutor> { [ActionKind.Application] = executor });

        var ctx = new ActionContext("default", new ApplicationContext("test", "t", 0, DateTimeOffset.UtcNow), 0);
        var result = await engine.ExecuteAsync("danger", ctx, PermissionTier.Safe, default);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("权限不足");
        executor.LastActionRef.Should().BeNull();
    }

    [Fact]
    public async Task SuccessfulExecutionProducesAuditLineOnStdout()
    {
        var registry = new MemoryActionRegistry();
        await registry.UpsertAsync(new ActionDef("key-a", ActionKind.Keyboard, "A", ExecutionPriority(), "{}"), default);
        var kb = new FakeExecutor(ActionKind.Keyboard);
        var engine = new ActionEngine(registry, new Dictionary<ActionKind, IActionExecutor> { [ActionKind.Keyboard] = kb });
        var ctx = new ActionContext("default", new ApplicationContext("test", "t", 0, DateTimeOffset.UtcNow), 0);
        var result = await engine.ExecuteAsync("key-a", ctx, PermissionTier.Safe, default);
        result.Success.Should().BeTrue();
    }

    private static PermissionTier ExecutionPriority() => PermissionTier.Safe;
}
using FlowRing.RingCore.Profile;

namespace FlowRing.RingCore.Action;

/// <summary>
/// Action Pipeline 上下文。每阶段依次处理。
/// </summary>
public sealed class ActionPipelineContext
{
    public required string ActionId { get; init; }
    public required ActionDef Def { get; init; }
    public required ActionContext Context { get; init; }
    public required PermissionTier GrantedTier { get; set; }
    public ActionContext? EnrichedContext { get; set; }
    public ExecutionResult? Result { get; set; }
}

/// <summary>
/// Pipeline 阶段接口。每阶段可写 GrantedTier / EnrichedContext / Result。
/// </summary>
public interface IActionPipelineStage
{
    string Name { get; }
    ValueTask ExecuteAsync(ActionPipelineContext ctx, CancellationToken ct);
}

public sealed class PermissionCheckStage : IActionPipelineStage
{
    public string Name => "PermissionCheck";

    public ValueTask ExecuteAsync(ActionPipelineContext ctx, CancellationToken ct)
    {
        // 三级 Permission Tier 强制：
        // Safe：键盘注入 + 屏幕截图 + 窗口最小化（无破坏性）
        // Normal：键盘完整 hotkey + 系统操作
        // Dangerous：应用启动 + 网络调用 + 文件系统写入
        // 权限不足由 ActionEngine.ExecuteAsync 在 Pipeline 之后检测并 fail，本阶段不抛异常
        return ValueTask.CompletedTask;
    }
}

public sealed class ContextInjectStage : IActionPipelineStage
{
    public string Name => "ContextInject";

    public ValueTask ExecuteAsync(ActionPipelineContext ctx, CancellationToken ct)
    {
        // 给 Action 注入运行时上下文（当前 Profile / currentApp / cursor / timestamp）
        ctx.EnrichedContext = ctx.Context with
        {
            // Phase 6 简化：仅复制，不引入 cursor 信息。Phase 7 补 cursor / focused window。
        };
        return ValueTask.CompletedTask;
    }
}

public sealed class AuditLogStage : IActionPipelineStage
{
    public string Name => "AuditLog";

    public ValueTask ExecuteAsync(ActionPipelineContext ctx, CancellationToken ct)
    {
        // MVP stub：写入 stderr；Phase 7 接到 LogStore。
        Console.WriteLine($"[AuditLog] action={ctx.ActionId} profile={ctx.Context.ProfileId} result={(ctx.Result?.Success ?? false)}");
        return ValueTask.CompletedTask;
    }
}

public sealed class ActionPipeline
{
    private readonly IReadOnlyList<IActionPipelineStage> _stages;

    public ActionPipeline()
    {
        _stages = new IActionPipelineStage[]
        {
            new PermissionCheckStage(),
            new ContextInjectStage(),
            new AuditLogStage(),
        };
    }

    public async ValueTask RunAsync(ActionPipelineContext ctx, CancellationToken ct)
    {
        foreach (var stage in _stages)
        {
            await stage.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// ActionDef：MVP 阶段用 struct，Phase 6.1 起从 ActionRegistry 反序列化。
/// </summary>
public sealed record ActionDef(
    string Id,
    ActionKind Kind,
    string DisplayName,
    PermissionTier PermissionTier,
    string PayloadJson);
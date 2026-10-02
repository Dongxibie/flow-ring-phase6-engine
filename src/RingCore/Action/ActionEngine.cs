using FlowRing.RingCore.Profile;

namespace FlowRing.RingCore.Action;

/// <summary>
/// Action Engine：编排 Pipeline + Registry + Executor。
/// 完整流程：ActionRef → Registry 查 ActionDef → 跑 Pipeline → Executor.ExecuteAsync → 返回 ExecutionResult。
/// </summary>
public sealed class ActionEngine
{
    private readonly IActionRegistry _registry;
    private readonly IReadOnlyDictionary<ActionKind, IActionExecutor> _executors;
    private readonly ActionPipeline _pipeline;

    public ActionEngine(IActionRegistry registry, IReadOnlyDictionary<ActionKind, IActionExecutor> executors)
    {
        _registry = registry;
        _executors = executors;
        _pipeline = new ActionPipeline();
    }

    public async ValueTask<ExecutionResult> ExecuteAsync(string actionRef, ActionContext ctx, PermissionTier grantedTier, CancellationToken ct)
    {
        var def = await _registry.GetAsync(actionRef, ct).ConfigureAwait(false);
        if (def is null)
        {
            return new ExecutionResult(false, $"Action '{actionRef}' 未在 Registry 中注册", 0);
        }

        if (!_executors.TryGetValue(def.Kind, out var executor))
        {
            return new ExecutionResult(false, $"ActionKind {def.Kind} 无 Executor 注册", 0);
        }

        var pipelineCtx = new ActionPipelineContext
        {
            ActionId = actionRef,
            Def = def,
            Context = ctx,
            GrantedTier = grantedTier,
        };
        await _pipeline.RunAsync(pipelineCtx, ct).ConfigureAwait(false);

        if ((int)def.PermissionTier > (int)grantedTier)
        {
            return new ExecutionResult(false, $"权限不足：需要 {def.PermissionTier} 但只有 {grantedTier}", 0);
        }

        var enrichedCtx = pipelineCtx.EnrichedContext ?? ctx;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await executor.ExecuteAsync(actionRef, enrichedCtx, ct).ConfigureAwait(false);
        sw.Stop();
        pipelineCtx.Result = result with { DurationMs = sw.ElapsedMilliseconds };
        await new AuditLogStage().ExecuteAsync(pipelineCtx, ct).ConfigureAwait(false);
        return pipelineCtx.Result;
    }
}

public interface IActionRegistry
{
    ValueTask<ActionDef?> GetAsync(string actionId, CancellationToken ct);
    IAsyncEnumerable<ActionDef> ListAsync(CancellationToken ct);
    Task UpsertAsync(ActionDef def, CancellationToken ct);
    Task DeleteAsync(string actionId, CancellationToken ct);
}

public sealed class MemoryActionRegistry : IActionRegistry
{
    private readonly Dictionary<string, ActionDef> _store = new();

    public async ValueTask<ActionDef?> GetAsync(string actionId, CancellationToken ct)
    {
        await Task.Yield();
        return _store.TryGetValue(actionId, out var def) ? def : null;
    }

    public async IAsyncEnumerable<ActionDef> ListAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var def in _store.Values)
        {
            yield return def;
        }
    }

    public Task UpsertAsync(ActionDef def, CancellationToken ct)
    {
        _store[def.Id] = def;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string actionId, CancellationToken ct)
    {
        _store.Remove(actionId);
        return Task.CompletedTask;
    }
}
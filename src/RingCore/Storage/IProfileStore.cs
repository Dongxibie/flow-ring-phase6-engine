using FlowRing.RingCore.Profile;

namespace FlowRing.RingCore.Storage;

/// <summary>
/// ProfileStore OS 无关接口。MVP 仅含元数据（id / name）+ 路径推导；ProfileData 完整内容由 Phase 6 落盘层加。
/// 实际文件 IO 走 DesktopBridge（保持 RingCore 纯 OS 无关）。
/// </summary>
public interface IProfileStore
{
    Task<IReadOnlyList<ProfileMetadata>> ListAsync(CancellationToken ct);
    Task<ProfileData?> LoadAsync(string profileId, CancellationToken ct);
    Task SaveAsync(ProfileData profile, CancellationToken ct);
    Task DeleteAsync(string profileId, CancellationToken ct);
    Task<IReadOnlyList<ProfileSnapshotInfo>> ListSnapshotsAsync(string profileId, CancellationToken ct);
    Task RestoreSnapshotAsync(string profileId, string snapshotId, CancellationToken ct);
}

public sealed record ProfileSnapshotInfo(string SnapshotId, string Path, DateTimeOffset CreatedAt);
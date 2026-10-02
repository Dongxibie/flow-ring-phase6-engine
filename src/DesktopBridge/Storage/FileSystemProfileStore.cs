using System.Text.Json;
using System.Text.Json.Nodes;
using FlowRing.RingCore;
using FlowRing.RingCore.Profile;
using FlowRing.RingCore.Storage;

namespace FlowRing.DesktopBridge.Storage;

/// <summary>
/// 文件系统 ProfileStore 真实实现。分文件 JSON（每个 Profile 一个独立 JSON）+ 原子写（写 tmp → File.Replace）+ 快照（保留最近 N=5）。
/// 路径：%APPDATA%/FlowRing/profiles/{profileId}.json；快照：%APPDATA%/FlowRing/snapshots/{profileId}/{timestamp}.json。
/// </summary>
public sealed class FileSystemProfileStore : IProfileStore
{
    private const int MaxSnapshots = 5;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _root;

    public FileSystemProfileStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(SnapshotsDir);
    }

    private string ProfilesDir => Path.Combine(_root, "profiles");
    private string SnapshotsDir => Path.Combine(_root, "snapshots");

    public async Task<IReadOnlyList<ProfileMetadata>> ListAsync(CancellationToken ct)
    {
        await Task.Yield();
        var files = Directory.EnumerateFiles(ProfilesDir, "*.json");
        var result = new List<ProfileMetadata>();
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var node = JsonNode.Parse(json)?.AsObject();
                var metadataNode = node?["metadata"]?.AsObject();
                if (metadataNode is null)
                {
                    continue;
                }
                var meta = new ProfileMetadata(
                    Id: metadataNode["id"]?.GetValue<string>() ?? string.Empty,
                    Name: metadataNode["name"]?.GetValue<string>() ?? string.Empty,
                    Version: metadataNode["version"]?.GetValue<string>() ?? "1.0.0",
                    SchemaVersion: metadataNode["schemaVersion"]?.GetValue<string>() ?? "1.0",
                    CreatedAt: metadataNode["createdAt"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UnixEpoch,
                    UpdatedAt: metadataNode["updatedAt"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.UnixEpoch,
                    Checksum: metadataNode["checksum"]?.GetValue<string>() ?? new string('0', 64));
                result.Add(meta);
            }
            catch
            {
                // 单个文件损坏不阻塞列表，跳过
            }
        }
        return result;
    }

    public async Task<ProfileData?> LoadAsync(string profileId, CancellationToken ct)
    {
        var path = ProfilePath(profileId);
        if (!File.Exists(path))
        {
            return null;
        }
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ProfileData>(json, JsonOptions);
    }

    public async Task SaveAsync(ProfileData profile, CancellationToken ct)
    {
        var path = ProfilePath(profile.Metadata.Id);
        var json = JsonSerializer.Serialize(profile, JsonOptions);

        // 原子写：写 tmp → File.Replace
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);

        if (File.Exists(path))
        {
            // File.Replace 是原子的；如果失败回滚到原文件
            var backup = path + ".bak";
            try
            {
                File.Replace(tmp, path, backup);
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }
            }
            catch
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
                throw;
            }
        }
        else
        {
            File.Move(tmp, path);
        }

        // 写快照
        await CreateSnapshotAsync(profile, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(string profileId, CancellationToken ct)
    {
        var path = ProfilePath(profileId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ProfileSnapshotInfo>> ListSnapshotsAsync(string profileId, CancellationToken ct)
    {
        await Task.Yield();
        var dir = Path.Combine(SnapshotsDir, profileId);
        if (!Directory.Exists(dir))
        {
            return Array.Empty<ProfileSnapshotInfo>();
        }
        var files = Directory.EnumerateFiles(dir, "*.json");
        return files
            .Select(f => new ProfileSnapshotInfo(Path.GetFileNameWithoutExtension(f), f, File.GetCreationTime(f)))
            .ToList();
    }

    public async Task RestoreSnapshotAsync(string profileId, string snapshotId, CancellationToken ct)
    {
        var snapshotPath = Path.Combine(SnapshotsDir, profileId, snapshotId + ".json");
        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException($"Snapshot 不存在：{snapshotPath}");
        }
        var profile = await LoadFromPathAsync(snapshotPath, ct).ConfigureAwait(false);
        if (profile is null)
        {
            throw new InvalidDataException($"Snapshot 反序列化失败：{snapshotPath}");
        }
        await SaveAsync(profile, ct).ConfigureAwait(false);
    }

    private string ProfilePath(string profileId) => Path.Combine(ProfilesDir, profileId + ".json");

    private static async Task<ProfileData?> LoadFromPathAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ProfileData>(json, JsonOptions);
    }

    private async Task CreateSnapshotAsync(ProfileData profile, CancellationToken ct)
    {
        var dir = Path.Combine(SnapshotsDir, profile.Metadata.Id);
        Directory.CreateDirectory(dir);
        var snapshotId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(dir, snapshotId + ".json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);

        // 仅保留最近 N 个快照
        var files = Directory.EnumerateFiles(dir, "*.json")
            .OrderByDescending(File.GetCreationTime)
            .Skip(MaxSnapshots)
            .ToList();
        foreach (var f in files)
        {
            File.Delete(f);
        }
    }
}
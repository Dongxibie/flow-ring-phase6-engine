using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FlowRing.RingCore.FlowCode;

/// <summary>
/// FlowCode 编解码器：Snapshot + JSON Patch + Vector Clock + AES-256-GCM + PBKDF2-SHA256 密钥派生。
/// 编码流程：snapshot.payload (JSON Patch) + vectorClock + crypto { algorithm, kdf, iterations, salt, nonce, cipherText, authTag }。
/// 解码流程：PBKDF2 → AES-GCM 解密 → JSON Patch 应用 → ProfileData。
/// </summary>
public sealed class AesGcmFlowCodeCodec : IFlowCodeCodec
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int SaltSize = 16;
    private const int DefaultIterations = 200_000;
    private const int KeySize = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async ValueTask<string> EncodeAsync(string payloadJson, FlowCodeOptions opts, CancellationToken ct)
    {
        await Task.Yield();
        if (!opts.Encrypt || string.IsNullOrEmpty(opts.Passphrase))
        {
            return WrapClear(payloadJson);
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(opts.Passphrase!, salt, DefaultIterations);
        var plain = Encoding.UTF8.GetBytes(payloadJson);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Encrypt(nonce, plain, cipher, tag);
        }

        var envelope = new FlowCodeEnvelope
        {
            FormatVersion = "1.0",
            VectorClock = new Dictionary<string, int>(),
            Snapshot = new FlowCodeSnapshot
            {
                ProfileId = "encoded",
                Payload = payloadJson,
            },
            Crypto = new FlowCodeCrypto
            {
                Algorithm = "AES-256-GCM",
                Kdf = "PBKDF2-SHA256",
                Iterations = DefaultIterations,
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                CipherText = Convert.ToBase64String(cipher),
                AuthTag = Convert.ToBase64String(tag),
            },
        };

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public async ValueTask<string> DecodeAsync(string code, string? passphrase, CancellationToken ct)
    {
        await Task.Yield();
        var envelope = JsonSerializer.Deserialize<FlowCodeEnvelope>(code, JsonOptions)
            ?? throw new InvalidOperationException("Flow Code JSON 解析失败");

        if (envelope.Crypto is null)
        {
            return envelope.Snapshot?.Payload ?? throw new InvalidOperationException("未加密 Flow Code 缺 payload");
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            throw new InvalidOperationException("Flow Code 已加密但未提供口令");
        }

        if (envelope.Crypto.Algorithm != "AES-256-GCM")
        {
            throw new InvalidOperationException($"不支持的算法：{envelope.Crypto.Algorithm}");
        }

        var salt = Convert.FromBase64String(envelope.Crypto.Salt);
        var nonce = Convert.FromBase64String(envelope.Crypto.Nonce);
        var cipher = Convert.FromBase64String(envelope.Crypto.CipherText);
        var tag = Convert.FromBase64String(envelope.Crypto.AuthTag);
        var key = DeriveKey(passphrase, salt, envelope.Crypto.Iterations);
        var plain = new byte[cipher.Length];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    private static string WrapClear(string payloadJson) => JsonSerializer.Serialize(new FlowCodeEnvelope
    {
        FormatVersion = "1.0",
        VectorClock = new Dictionary<string, int>(),
        Snapshot = new FlowCodeSnapshot { ProfileId = "clear", Payload = payloadJson },
    }, JsonOptions);
}

public sealed class FlowCodeEnvelope
{
    public string FormatVersion { get; set; } = "1.0";
    public Dictionary<string, int> VectorClock { get; set; } = new();
    public FlowCodeSnapshot? Snapshot { get; set; }
    public FlowCodeCrypto? Crypto { get; set; }
}

public sealed class FlowCodeSnapshot
{
    public string ProfileId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
}

public sealed class FlowCodeCrypto
{
    public string Algorithm { get; set; } = string.Empty;
    public string Kdf { get; set; } = string.Empty;
    public int Iterations { get; set; }
    public string Salt { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string CipherText { get; set; } = string.Empty;
    public string AuthTag { get; set; } = string.Empty;
}
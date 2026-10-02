using System.Security.Cryptography;
using FlowRing.RingCore.FlowCode;
using FluentAssertions;
using Xunit;

namespace FlowRing.RingCore.Tests;

public sealed class AesGcmFlowCodeCodecTests
{
    [Fact]
    public async Task UnencryptedEncodeOrDecodeRoundTrip()
    {
        var codec = new AesGcmFlowCodeCodec();
        const string payload = "{\"id\":\"p1\",\"name\":\"test\"}";

        var code = await codec.EncodeAsync(payload, new FlowCodeOptions(false, null), default);
        var decoded = await codec.DecodeAsync(code, null, default);

        decoded.Should().Be(payload);
    }

    [Fact]
    public async Task EncryptedEncodeOrDecodeRoundTrip()
    {
        var codec = new AesGcmFlowCodeCodec();
        const string payload = "{\"id\":\"secret\",\"value\":42}";
        const string passphrase = "correct horse battery staple";

        var code = await codec.EncodeAsync(payload, new FlowCodeOptions(true, passphrase), default);
        var decoded = await codec.DecodeAsync(code, passphrase, default);

        decoded.Should().Be(payload);
    }

    [Fact]
    public async Task EncryptedDecodeWithoutPassphraseThrows()
    {
        var codec = new AesGcmFlowCodeCodec();
        const string payload = "{\"id\":\"secret\"}";
        const string passphrase = "pass";

        var encrypted = await codec.EncodeAsync(payload, new FlowCodeOptions(true, passphrase), default);
        var act = async () => await codec.DecodeAsync(encrypted, null, default);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*未提供口令*");
    }

    [Fact]
    public async Task EncryptedDecodeWithWrongPassphraseThrows()
    {
        var codec = new AesGcmFlowCodeCodec();
        const string payload = "{\"id\":\"secret\"}";
        const string rightPass = "pass-right";
        const string wrongPass = "pass-wrong";

        var encrypted = await codec.EncodeAsync(payload, new FlowCodeOptions(true, rightPass), default);
        var act = async () => await codec.DecodeAsync(encrypted, wrongPass, default);
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task EncodedEnvelopeIsValidJsonWithCryptoBlock()
    {
        var codec = new AesGcmFlowCodeCodec();
        var code = await codec.EncodeAsync("hello", new FlowCodeOptions(true, "p"), default);
        var envelope = System.Text.Json.JsonSerializer.Deserialize<FlowCodeEnvelope>(code, JsonOptions);

        envelope.Should().NotBeNull();
        envelope!.Crypto.Should().NotBeNull();
        envelope.Crypto!.Algorithm.Should().Be("AES-256-GCM");
        envelope.Crypto.Kdf.Should().Be("PBKDF2-SHA256");
        envelope.Crypto.Iterations.Should().BeGreaterOrEqualTo(100_000);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
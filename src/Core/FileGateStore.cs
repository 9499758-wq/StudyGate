namespace StudyGate.Core;

using System.Security.Cryptography;
using System.Text;

public interface IGateStore
{
    GateState Load();
    void Save(GateState state);
}

/// <summary>
/// state.json + state.json.sig。
///
/// 签名对象是 <see cref="GateStateCodec.Serialize"/> 产出的规范正文 UTF-8 字节，
/// 算法 HMAC-SHA256，签名写成 Base64 单行。规范正文的 12 个字段顺序、
/// 时间格式与 tools/core-reference.mjs 的 canonicalJson() 逐字节一致，
/// 所以 Node 算的签名和 C# 算的签名可以互换校验。
///
/// 写盘走「临时文件 + File.Replace」，断电不留半截状态。
/// 文件缺失 / 签名不符 / 正文解析失败 / IO 失败 → 一律返回默认 Locked，绝不抛。
/// </summary>
public sealed class FileGateStore : IGateStore
{
    public const string StateFileName = "state.json";
    public const string SignatureFileName = "state.json.sig";

    private readonly string _dir;
    private readonly byte[] _hmacKey;

    public FileGateStore(string dir, byte[] hmacKey)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(hmacKey);

        if (hmacKey.Length == 0)
        {
            throw new ArgumentException("hmacKey 不得为空。", nameof(hmacKey));
        }

        _dir = dir;
        _hmacKey = (byte[])hmacKey.Clone();
    }

    public string StatePath => Path.Combine(_dir, StateFileName);

    public string SignaturePath => Path.Combine(_dir, SignatureFileName);

    public GateState Load()
    {
        try
        {
            if (!File.Exists(StatePath) || !File.Exists(SignaturePath))
            {
                return GateState.Locked();
            }

            var bodyBytes = File.ReadAllBytes(StatePath);
            var signatureText = File.ReadAllText(SignaturePath, Encoding.UTF8).Trim();

            if (!VerifySignature(bodyBytes, signatureText))
            {
                return GateState.Locked();
            }

            var json = Encoding.UTF8.GetString(bodyBytes);
            return GateStateCodec.Deserialize(json);
        }
        catch (Exception)
        {
            return GateState.Locked();
        }
    }

    public void Save(GateState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Directory.CreateDirectory(_dir);

        var json = GateStateCodec.Serialize(state);
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var signatureText = Convert.ToBase64String(Sign(bodyBytes));

        // 顺序：正文先落，签名后落。第二步失败时旧签名与新正文不匹配 → Load 判篡改 → fail-closed。
        WriteAtomic(StatePath, bodyBytes);
        WriteAtomic(SignaturePath, Encoding.UTF8.GetBytes(signatureText));
    }

    /// <summary>供测试与诊断：算给定状态的规范正文与签名，不落盘。</summary>
    public (string Body, string Signature) SignState(GateState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var json = GateStateCodec.Serialize(state);
        var signature = Convert.ToBase64String(Sign(Encoding.UTF8.GetBytes(json)));
        return (json, signature);
    }

    /// <summary>按给定正文与签名独立验签，用于跨实现（Node 侧）对拍。</summary>
    public bool VerifyBody(string body, string signature)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(signature);

        var bytes = Encoding.UTF8.GetBytes(body);
        return VerifySignature(bytes, signature.Trim());
    }

    private bool VerifySignature(byte[] bodyBytes, string signatureText)
    {
        if (string.IsNullOrWhiteSpace(signatureText))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromBase64String(signatureText);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Sign(bodyBytes);

        // 长度不等时 FixedTimeEquals 直接返回 false，不会抛。
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private byte[] Sign(byte[] bodyBytes)
    {
        using var hmac = new HMACSHA256(_hmacKey);
        return hmac.ComputeHash(bodyBytes);
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture) + ".tmp";

        try
        {
            using (var stream = new FileStream(
                       temp,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(content, 0, content.Length);
                stream.Flush();
            }

            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception)
                {
                    // 清理失败不影响已提交的内容；残留 .tmp 不参与 Load。
                }
            }
        }
    }
}

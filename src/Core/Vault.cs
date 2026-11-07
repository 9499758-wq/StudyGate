namespace StudyGate.Core;

using System.Security.Cryptography;

/// <summary>
/// 口令保险库。PBKDF2-SHA256 / 200000 迭代 / 16B salt / 32B key。
/// Verify 必须走常量时间比较，禁止 ==。
/// </summary>
public sealed class Vault
{
    public const int Iterations = 200000;
    public const int SaltBytes = 16;
    public const int KeyBytes = 32;

    /// <summary>vault.dat 序列化长度：magic(4) + ver(1) + iter(4) + saltLen(1) + salt(16) + keyLen(1) + key(32)。</summary>
    public const int BlobBytes = 59;

    private static readonly byte[] Magic = { (byte)'S', (byte)'G', (byte)'V', (byte)'P' };
    private const byte FormatVersion = 1;

    private readonly byte[] _salt;
    private readonly byte[] _key;
    private readonly int _iterations;

    private Vault(byte[] salt, byte[] key, int iterations)
    {
        _salt = salt;
        _key = key;
        _iterations = iterations;
    }

    /// <summary>用调用方给定的 salt 创建（salt 必须 16 字节，便于固定向量对拍）。</summary>
    public static Vault Create(string password, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        if (salt.Length != SaltBytes)
        {
            throw new ArgumentException($"salt 必须为 {SaltBytes} 字节，实际 {salt.Length} 字节。", nameof(salt));
        }

        var key = Derive(password, salt, Iterations, KeyBytes);
        return new Vault((byte[])salt.Clone(), key, Iterations);
    }

    /// <summary>随机 salt 版本，安装流程用这个。</summary>
    public static Vault Create(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return Create(password, salt);
    }

    public byte[] Salt => (byte[])_salt.Clone();

    public byte[] Key => (byte[])_key.Clone();

    public int KdfIterations => _iterations;

    /// <summary>常量时间比较。口令为 null 或派生失败一律 false，不抛。</summary>
    public bool Verify(string password)
    {
        if (password is null)
        {
            return false;
        }

        byte[] derived;
        try
        {
            derived = Derive(password, _salt, _iterations, _key.Length);
        }
        catch (Exception)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(derived, _key);
    }

    /// <summary>导出固定 59 字节 blob，可落 vault.dat。</summary>
    public byte[] Export()
    {
        var blob = new byte[BlobBytes];
        var offset = 0;

        Array.Copy(Magic, 0, blob, offset, Magic.Length);
        offset += Magic.Length;

        blob[offset++] = FormatVersion;

        WriteUInt32BigEndian(blob, offset, (uint)_iterations);
        offset += 4;

        blob[offset++] = (byte)_salt.Length;
        Array.Copy(_salt, 0, blob, offset, _salt.Length);
        offset += _salt.Length;

        blob[offset++] = (byte)_key.Length;
        Array.Copy(_key, 0, blob, offset, _key.Length);

        return blob;
    }

    /// <summary>从 blob 还原。任何结构不符抛 FormatException（调用方按 fail-closed 处理）。</summary>
    public static Vault Import(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);

        if (blob.Length != BlobBytes)
        {
            throw new FormatException($"vault blob 长度必须为 {BlobBytes} 字节，实际 {blob.Length} 字节。");
        }

        for (var i = 0; i < Magic.Length; i++)
        {
            if (blob[i] != Magic[i])
            {
                throw new FormatException("vault blob magic 不是 SGVP。");
            }
        }

        if (blob[4] != FormatVersion)
        {
            throw new FormatException($"vault blob 版本不支持：{blob[4]}。");
        }

        var iterations = (int)ReadUInt32BigEndian(blob, 5);
        if (iterations < 10000 || iterations > 1000000)
        {
            throw new FormatException($"vault blob 迭代次数越界：{iterations}。");
        }

        if (blob[9] != SaltBytes)
        {
            throw new FormatException($"vault blob salt 长度必须为 {SaltBytes}，实际 {blob[9]}。");
        }

        var salt = new byte[SaltBytes];
        Array.Copy(blob, 10, salt, 0, SaltBytes);

        if (blob[26] != KeyBytes)
        {
            throw new FormatException($"vault blob key 长度必须为 {KeyBytes}，实际 {blob[26]}。");
        }

        var key = new byte[KeyBytes];
        Array.Copy(blob, 27, key, 0, KeyBytes);

        return new Vault(salt, key, iterations);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length)
    {
        // 用静态 Pbkdf2 而不是 Rfc2898DeriveBytes 实例构造：后者在更新的 SDK 里被标记
        // obsolete（SYSLIB0060），本机没有 dotnet 无法验证，静态方法对所有 SDK 版本都干净。
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24)
               | ((uint)buffer[offset + 1] << 16)
               | ((uint)buffer[offset + 2] << 8)
               | buffer[offset + 3];
    }
}

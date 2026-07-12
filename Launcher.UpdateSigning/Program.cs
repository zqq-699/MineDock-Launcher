using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

return UpdateSigningTool.Run(args);

internal static class UpdateSigningTool
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0)
                throw new ArgumentException("A command is required: generate, derive-public, or sign.");

            var options = ParseOptions(args.Skip(1));
            switch (args[0].ToLowerInvariant())
            {
                case "generate":
                    Generate(Required(options, "private-pem"), Required(options, "public-pem"));
                    return 0;
                case "derive-public":
                    DerivePublic(
                        Required(options, "private-pem"),
                        Required(options, "public-pem"),
                        options.GetValueOrDefault("key-id-output"));
                    return 0;
                case "sign":
                    Sign(
                        Required(options, "private-pem"),
                        Required(options, "manifest"),
                        Required(options, "signature"));
                    return 0;
                default:
                    throw new ArgumentException($"Unknown command: {args[0]}");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void Generate(string privatePath, string publicPath)
    {
        if (File.Exists(privatePath) || File.Exists(publicPath))
            throw new IOException("Refusing to overwrite an existing update signing key.");

        EnsureParentDirectory(privatePath);
        EnsureParentDirectory(publicPath);
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        WritePem(privatePath, "PRIVATE KEY", PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetDerEncoded());
        WritePublicKey(publicPath, privateKey.GeneratePublicKey());
        Console.WriteLine(CreateKeyId(privateKey.GeneratePublicKey()));
    }

    private static void DerivePublic(string privatePath, string publicPath, string? keyIdOutputPath)
    {
        var privateKey = ReadPrivateKey(privatePath);
        var publicKey = privateKey.GeneratePublicKey();
        EnsureParentDirectory(publicPath);
        WritePublicKey(publicPath, publicKey);
        var keyId = CreateKeyId(publicKey);
        if (!string.IsNullOrWhiteSpace(keyIdOutputPath))
        {
            EnsureParentDirectory(keyIdOutputPath);
            File.WriteAllText(keyIdOutputPath, keyId, Utf8NoBom);
        }
        Console.WriteLine(keyId);
    }

    private static void Sign(string privatePath, string manifestPath, string signaturePath)
    {
        var privateKey = ReadPrivateKey(privatePath);
        var manifestBytes = File.ReadAllBytes(manifestPath);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifestKeyId = document.RootElement.GetProperty("keyId").GetString();
        var expectedKeyId = CreateKeyId(privateKey.GeneratePublicKey());
        if (!string.Equals(manifestKeyId, expectedKeyId, StringComparison.Ordinal))
            throw new InvalidDataException("Manifest keyId does not match the signing key.");

        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
        var signature = signer.GenerateSignature();
        EnsureParentDirectory(signaturePath);
        File.WriteAllText(signaturePath, Convert.ToBase64String(signature), Utf8NoBom);
    }

    private static Ed25519PrivateKeyParameters ReadPrivateKey(string path)
    {
        var der = ReadPem(path, "PRIVATE KEY");
        return PrivateKeyFactory.CreateKey(der) as Ed25519PrivateKeyParameters
            ?? throw new InvalidDataException("The private PEM is not an Ed25519 PKCS#8 key.");
    }

    private static void WritePublicKey(string path, Ed25519PublicKeyParameters publicKey) =>
        WritePem(path, "PUBLIC KEY", SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded());

    private static string CreateKeyId(Ed25519PublicKeyParameters publicKey)
    {
        var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
        return Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
    }

    private static byte[] ReadPem(string path, string label)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var begin = $"-----BEGIN {label}-----";
        var end = $"-----END {label}-----";
        var startIndex = text.IndexOf(begin, StringComparison.Ordinal);
        var endIndex = text.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
            throw new InvalidDataException($"PEM block {label} was not found.");
        startIndex += begin.Length;
        return Convert.FromBase64String(text[startIndex..endIndex]);
    }

    private static void WritePem(string path, string label, byte[] der)
    {
        var base64 = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks);
        File.WriteAllText(
            path,
            $"-----BEGIN {label}-----\n{base64.Replace("\r\n", "\n", StringComparison.Ordinal)}\n-----END {label}-----\n",
            Utf8NoBom);
    }

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var values = args.ToArray();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < values.Length; index += 2)
        {
            if (!values[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= values.Length)
                throw new ArgumentException("Options must use --name value pairs.");
            result[values[index][2..]] = values[index + 1];
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value)
            : throw new ArgumentException($"Missing required option --{name}.");

    private static void EnsureParentDirectory(string path) =>
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
}

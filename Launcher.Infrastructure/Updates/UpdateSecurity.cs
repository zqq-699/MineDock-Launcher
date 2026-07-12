using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Launcher.Infrastructure.Updates;

internal sealed class UpdateSecurityException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal sealed class UpdateSourceUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal enum OfficialUpdateUriKind
{
    Manifest,
    Executable
}

internal static class OfficialUpdateHttp
{
    private const int MaxRedirects = 5;

    public static HttpClient CreateClient(TimeSpan timeout) => new(
        new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = timeout
    };

    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        Uri initialUri,
        OfficialUpdateUriKind kind,
        CancellationToken cancellationToken)
    {
        ValidateInitialUri(initialUri, kind);
        var currentUri = initialUri;
        for (var redirectCount = 0; ; redirectCount++)
        {
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                        new HttpRequestMessage(HttpMethod.Get, currentUri),
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateSourceUnavailableException("The update source timed out.");
            }
            catch (HttpRequestException exception)
            {
                throw new UpdateSourceUnavailableException("The update source could not be reached.", exception);
            }

            var finalUri = response.RequestMessage?.RequestUri ?? currentUri;
            ValidateRedirectUri(finalUri);
            if (!IsRedirect(response.StatusCode))
            {
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = response.StatusCode;
                    response.Dispose();
                    throw new UpdateSourceUnavailableException($"The update source returned HTTP {(int)statusCode}.");
                }
                return response;
            }

            if (redirectCount >= MaxRedirects)
            {
                response.Dispose();
                throw new UpdateSecurityException("The update source exceeded the redirect limit.");
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new UpdateSecurityException("The update source returned a redirect without a location.");
            currentUri = location.IsAbsoluteUri ? location : new Uri(finalUri, location);
            ValidateRedirectUri(currentUri);
        }
    }

    public static async Task<byte[]> ReadLimitedBytesAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maximumBytes)
            throw new UpdateSecurityException("The update response exceeded the size limit.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return destination.ToArray();
            if (destination.Length + read > maximumBytes)
                throw new UpdateSecurityException("The update response exceeded the size limit.");
            destination.Write(buffer, 0, read);
        }
    }

    public static void ValidateInitialUri(Uri uri, OfficialUpdateUriKind kind)
    {
        ValidateHttps(uri);
        var valid = kind switch
        {
            OfficialUpdateUriKind.Manifest => uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase),
            OfficialUpdateUriKind.Executable => uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (!valid)
            throw new UpdateSecurityException("The update URL is not an approved official source.");
    }

    private static void ValidateRedirectUri(Uri uri) => ValidateHttps(uri);

    private static void ValidateHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new UpdateSecurityException("Only HTTPS update URLs are allowed.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}

internal interface IUpdateManifestSignatureVerifier
{
    string KeyId { get; }
    bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes);
}

internal sealed class EmbeddedUpdateManifestSignatureVerifier : IUpdateManifestSignatureVerifier
{
    private const string ResourceName = "Launcher.Infrastructure.Updates.update-signing-public.pem";
    private readonly Ed25519PublicKeyParameters publicKey;

    public EmbeddedUpdateManifestSignatureVerifier()
    {
        using var stream = typeof(EmbeddedUpdateManifestSignatureVerifier).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new UpdateSecurityException("The update signing public key is unavailable.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        publicKey = ReadPublicKey(reader.ReadToEnd());
        var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
        KeyId = Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
    }

    internal EmbeddedUpdateManifestSignatureVerifier(Ed25519PublicKeyParameters publicKey)
    {
        this.publicKey = publicKey;
        var spki = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey).GetDerEncoded();
        KeyId = Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant();
    }

    public string KeyId { get; }

    public bool Verify(ReadOnlySpan<byte> manifestBytes, ReadOnlySpan<byte> signatureBytes)
    {
        if (signatureBytes.Length != 64)
            return false;
        var verifier = new Ed25519Signer();
        verifier.Init(false, publicKey);
        var message = manifestBytes.ToArray();
        verifier.BlockUpdate(message, 0, message.Length);
        return verifier.VerifySignature(signatureBytes.ToArray());
    }

    internal static byte[] DecodeSignature(ReadOnlySpan<byte> signatureFileBytes)
    {
        if (signatureFileBytes.Length == 0 || signatureFileBytes.Length > 4096)
            throw new UpdateSecurityException("The update signature file is invalid.");
        try
        {
            if (signatureFileBytes.Contains((byte)'\r') || signatureFileBytes.Contains((byte)'\n')
                || ContainsNonAscii(signatureFileBytes))
                throw new UpdateSecurityException("The update signature file must be canonical Base64 without whitespace.");
            var text = Encoding.ASCII.GetString(signatureFileBytes);
            var signature = Convert.FromBase64String(text);
            if (signature.Length != 64 || !string.Equals(Convert.ToBase64String(signature), text, StringComparison.Ordinal))
                throw new UpdateSecurityException("The update signature file is not canonical Base64.");
            return signature;
        }
        catch (FormatException exception)
        {
            throw new UpdateSecurityException("The update signature is not valid Base64.", exception);
        }
    }

    private static bool ContainsNonAscii(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            if (value > 0x7f) return true;
        return false;
    }

    private static Ed25519PublicKeyParameters ReadPublicKey(string pem)
    {
        const string begin = "-----BEGIN PUBLIC KEY-----";
        const string end = "-----END PUBLIC KEY-----";
        var startIndex = pem.IndexOf(begin, StringComparison.Ordinal);
        var endIndex = pem.IndexOf(end, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex <= startIndex)
            throw new UpdateSecurityException("The embedded update public key PEM is invalid.");
        startIndex += begin.Length;
        try
        {
            var der = Convert.FromBase64String(pem[startIndex..endIndex]);
            return PublicKeyFactory.CreateKey(der) as Ed25519PublicKeyParameters
                ?? throw new UpdateSecurityException("The embedded update public key is not Ed25519.");
        }
        catch (FormatException exception)
        {
            throw new UpdateSecurityException("The embedded update public key PEM is invalid.", exception);
        }
    }
}

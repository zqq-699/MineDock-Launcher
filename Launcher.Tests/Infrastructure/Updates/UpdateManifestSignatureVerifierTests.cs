using System.Text;
using Launcher.Infrastructure.Updates;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class UpdateManifestSignatureVerifierTests
{
    private static readonly Ed25519PrivateKeyParameters PrivateKey = new(
        Enumerable.Range(1, Ed25519PrivateKeyParameters.KeySize).Select(value => (byte)value).ToArray(), 0);

    [Fact]
    public void DeterministicEd25519SignatureVerifiesOnlyOriginalBytes()
    {
        var manifest = Encoding.UTF8.GetBytes("{\"keyId\":\"test\"}");
        var verifier = new EmbeddedUpdateManifestSignatureVerifier(PrivateKey.GeneratePublicKey());
        var signer = new Ed25519Signer();
        signer.Init(true, PrivateKey);
        signer.BlockUpdate(manifest, 0, manifest.Length);
        var signature = signer.GenerateSignature();

        Assert.True(verifier.Verify(manifest, signature));
        manifest[^2] ^= 1;
        Assert.False(verifier.Verify(manifest, signature));
        signature[0] ^= 1;
        Assert.False(verifier.Verify(Encoding.UTF8.GetBytes("{\"keyId\":\"test\"}"), signature));
    }

    [Fact]
    public void SignatureFileMustBeCanonicalBase64WithoutBomOrNewline()
    {
        var canonical = Convert.ToBase64String(new byte[64]);

        Assert.Equal(64, EmbeddedUpdateManifestSignatureVerifier.DecodeSignature(Encoding.ASCII.GetBytes(canonical)).Length);
        Assert.Throws<UpdateSecurityException>(() =>
            EmbeddedUpdateManifestSignatureVerifier.DecodeSignature(Encoding.ASCII.GetBytes(canonical + "\n")));
        Assert.Throws<UpdateSecurityException>(() =>
            EmbeddedUpdateManifestSignatureVerifier.DecodeSignature([0xef, 0xbb, 0xbf, .. Encoding.ASCII.GetBytes(canonical)]));
        Assert.Throws<UpdateSecurityException>(() =>
            EmbeddedUpdateManifestSignatureVerifier.DecodeSignature(Encoding.ASCII.GetBytes("not-base64")));
        Assert.Throws<UpdateSecurityException>(() =>
            EmbeddedUpdateManifestSignatureVerifier.DecodeSignature(Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[63]))));
    }
}

using Launcher.Infrastructure.Accounts;
using Launcher.Application.Accounts;
using System.Net;
using System.Net.Http.Headers;

namespace Launcher.Tests;

public sealed class MinecraftProfileClientTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"launcher-profile-client-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(MinecraftSkinModel.Classic, "classic")]
    [InlineData(MinecraftSkinModel.Slim, "slim")]
    public async Task UploadSkinAsyncPostsMultipartSkinWithSelectedVariant(
        MinecraftSkinModel skinModel,
        string expectedVariant)
    {
        Directory.CreateDirectory(tempRoot);
        var skinPath = Path.Combine(tempRoot, "skin.png");
        await File.WriteAllBytesAsync(skinPath, [0x89, 0x50, 0x4E, 0x47]);
        var handler = new UploadSkinCaptureHandler();
        var client = new MinecraftProfileClient(new HttpClient(handler));

        await client.UploadSkinAsync("access-token", skinPath, skinModel, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.minecraftservices.com/minecraft/profile/skins", handler.RequestUri?.ToString());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), handler.Authorization);
        Assert.Contains("name=variant", handler.Content);
        Assert.Contains(expectedVariant, handler.Content);
        Assert.Contains("name=file", handler.Content);
        Assert.Contains("filename=skin.png", handler.Content);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class UploadSkinCaptureHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string Content { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

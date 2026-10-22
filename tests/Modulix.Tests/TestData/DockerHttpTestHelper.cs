using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;

using Docker.DotNet;

namespace Modulix.Tests.TestData;

/// <summary>
/// Shared HTTP handler, credentials, and multiplexed log stream helpers for Docker client unit tests.
/// </summary>
internal static class DockerHttpTestHelper
{
    public static DockerClient CreateDockerClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        var credentials = new FakeCredentials(new FakeDockerHandler(responder));
        return new DockerClientConfiguration(new Uri("http://docker.test"), credentials).CreateClient();
    }

    public static HttpResponseMessage JsonResponse(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    public static HttpResponseMessage LogResponse(string stdout, string stderr = "") => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(CreateMultiplexedLogStream(stdout, stderr))
    };

    public static byte[] CreateMultiplexedLogStream(string stdout, string stderr)
    {
        using var stream = new MemoryStream();
        WriteFrame(stream, 1, stdout);
        WriteFrame(stream, 2, stderr);
        return stream.ToArray();

        static void WriteFrame(Stream target, byte streamType, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var payload = Encoding.UTF8.GetBytes(text);
            Span<byte> header = stackalloc byte[8];
            header[0] = streamType;
            BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)payload.Length);
            target.Write(header);
            target.Write(payload);
        }
    }

    private sealed class FakeDockerHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }

    private sealed class FakeCredentials(HttpMessageHandler handler) : Credentials
    {
        public override bool IsTlsCredentials() => false;

        public override HttpMessageHandler GetHandler(HttpMessageHandler innerHandler)
        {
            innerHandler.Dispose();
            return handler;
        }
    }
}

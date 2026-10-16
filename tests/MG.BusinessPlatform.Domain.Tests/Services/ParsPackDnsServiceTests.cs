using System.Net;
using System.Text;
using System.Text.Json;
using MG.BusinessPlatform.Infrastructure.Services;

namespace MG.BusinessPlatform.Domain.Tests.Services;

public sealed class ParsPackDnsServiceTests
{
    [Fact]
    public async Task SyncARecordAsync_CreatesRootARecordWhenMissing()
    {
        var handler = new StubHandler(EmptyRecords);
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.SyncARecordAsync("mgalexander.cam", "test-token");

        Assert.Equal(ParsPackDnsSyncStatus.Updated, result.Status);
        Assert.Equal("@", result.RecordHost);
        Assert.Equal("mgalexander.cam", result.Zone);
        var post = Assert.Single(handler.Requests.Where(request => request.Method == HttpMethod.Post));
        using var document = JsonDocument.Parse(post.Body!);
        Assert.Equal("@", document.RootElement.GetProperty("host").GetString());
        Assert.Equal("A", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("8.8.8.8", document.RootElement.GetProperty("record").GetProperty("content").GetString());
        Assert.Equal("direct", document.RootElement.GetProperty("proxy").GetString());
    }

    [Fact]
    public async Task SyncARecordAsync_ExistingMatchingRecordDoesNotMutateDns()
    {
        var handler = new StubHandler(RecordsWithA("8.8.8.8"));
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.SyncARecordAsync("mgalexander.cam", "test-token");

        Assert.Equal(ParsPackDnsSyncStatus.AlreadyCurrent, result.Status);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task SyncARecordAsync_MismatchedRecordRequiresConfirmationBeforeMutation()
    {
        var handler = new StubHandler(RecordsWithA("9.9.9.9"));
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.SyncARecordAsync("mgalexander.cam", "test-token");

        Assert.Equal(ParsPackDnsSyncStatus.ConfirmationRequired, result.Status);
        Assert.Equal("9.9.9.9", result.ExistingIpv4);
        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task SyncARecordAsync_ConfirmedReplacementDeletesThenCreatesRecord()
    {
        var handler = new StubHandler(_ => RecordsWithA("9.9.9.9"));
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.SyncARecordAsync("mgalexander.cam", "test-token", replaceExisting: true);

        Assert.Equal(ParsPackDnsSyncStatus.Updated, result.Status);
        Assert.Equal(new[] { HttpMethod.Delete, HttpMethod.Post },
            handler.Requests.Where(request => request.Method == HttpMethod.Delete || request.Method == HttpMethod.Post)
                .Select(request => request.Method).ToArray());
    }

    [Fact]
    public async Task SyncARecordAsync_RefusesToAddARecordBesideExistingCname()
    {
        const string cnameRecords = """
            {"success":true,"message":"","data":[{"host":"@","type":"CNAME","proxy":"direct","ttl":300,"records":[{"content":"origin.example.net"}]}]}
            """;
        var handler = new StubHandler(cnameRecords);
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncARecordAsync("mgalexander.cam", "test-token"));

        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task SyncARecordAsync_RefusesAmbiguousMultipleARecords()
    {
        const string multipleRecords = """
            {"success":true,"message":"","data":[
              {"host":"@","type":"A","proxy":"direct","ttl":300,"records":[{"content":"8.8.8.8"}]},
              {"host":"@","type":"A","proxy":"direct","ttl":300,"records":[{"content":"9.9.9.9"}]}
            ]}
            """;
        var handler = new StubHandler(multipleRecords);
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncARecordAsync("mgalexander.cam", "test-token"));

        Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Post || request.Method == HttpMethod.Delete);
    }

    private static ParsPackDnsService CreateService(HttpClient client) =>
        new(client, new Uri("https://my.parspack.com/cdnapi/"), new Uri("https://api.ipify.org"));

    private const string EmptyRecords = """{"success":true,"message":"","data":[]}""";

    private static string RecordsWithA(string address) =>
        $$"""{"success":true,"message":"","data":[{"host":"@","type":"A","proxy":"direct","ttl":300,"records":[{"content":"{{address}}"}]}]}""";

    private sealed class StubHandler(string recordsJson) : HttpMessageHandler
    {
        private const string ZonesJson = """{"success":true,"message":"","data":[{"uuid":"zone-1","target_domain":"mgalexander.cam"}]}""";
        private const string SuccessJson = """{"success":true,"message":"ok","data":[]}""";

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, body));

            string responseBody;
            if (request.RequestUri!.Host == "api.ipify.org")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("8.8.8.8") };
            if (request.RequestUri.AbsolutePath.EndsWith("/external/api/v1/zones", StringComparison.Ordinal))
                responseBody = ZonesJson;
            else if (request.Method == HttpMethod.Get
                && request.RequestUri.AbsolutePath.EndsWith("/dns-records", StringComparison.Ordinal))
                responseBody = recordsJson;
            else
                responseBody = SuccessJson;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Body);
}

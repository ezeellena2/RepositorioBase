using System.Net;
using System.Text;
using CleanArchitecture.Web.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class LoginRateLimitKeyMiddlewareTests
{
    private const string OpaqueKeyPattern = "^[0-9a-f]{64}$";
    private const string LoginBody = "{\"email\":\"  Owner@Example.TEST \",\"password\":\"Testing1234!\"}";

    [Test]
    public async Task Login_request_gets_opaque_account_and_client_keys_and_keeps_the_body_readable()
    {
        var context = CreateContext(LoginBody, remoteAddress: IPAddress.Parse("203.0.113.10"));
        var nextInvoked = false;

        await new LoginRateLimitKeyMiddleware(_ => { nextInvoked = true; return Task.CompletedTask; }).InvokeAsync(context);

        nextInvoked.ShouldBeTrue();
        var accountKey = context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem].ShouldBeOfType<string>();
        var clientKey = context.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBeOfType<string>();
        accountKey.ShouldMatch(OpaqueKeyPattern);
        clientKey.ShouldMatch(OpaqueKeyPattern);
        accountKey.ShouldBe(LoginRateLimitPartitioner.AccountKey("owner@example.test"));
        clientKey.ShouldBe(LoginRateLimitPartitioner.ClientKey("203.0.113.10"));
        context.Items.Values.OfType<string>().ShouldNotContain(value => value.Contains("owner", StringComparison.OrdinalIgnoreCase) || value.Contains("Testing1234!", StringComparison.Ordinal) || value.Contains("203.0.113.10", StringComparison.Ordinal));
        context.Request.Body.Position.ShouldBe(0);
        (await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync()).ShouldBe(LoginBody);
    }

    [Test]
    public async Task Email_property_name_is_matched_case_insensitively_like_the_endpoint_binder()
    {
        var context = CreateContext("{\"Email\":\"Owner@Example.test\",\"Password\":\"Testing1234!\"}", remoteAddress: IPAddress.Parse("203.0.113.10"));

        await new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem].ShouldBe(LoginRateLimitPartitioner.AccountKey("owner@example.test"));
    }

    [TestCase("{", TestName = "Truncated_json_keeps_only_the_client_partition")]
    [TestCase("[]", TestName = "Array_body_keeps_only_the_client_partition")]
    [TestCase("", TestName = "Empty_body_keeps_only_the_client_partition")]
    [TestCase("{\"email\": 123, \"password\": \"x\"}", TestName = "Non_string_email_keeps_only_the_client_partition")]
    [TestCase("{\"email\": \"   \", \"password\": \"x\"}", TestName = "Blank_email_keeps_only_the_client_partition")]
    [TestCase("{\"password\": \"x\"}", TestName = "Missing_email_keeps_only_the_client_partition")]
    public async Task Malformed_or_accountless_bodies_keep_only_the_client_partition(string body)
    {
        var context = CreateContext(body, remoteAddress: IPAddress.Parse("203.0.113.10"));
        var nextInvoked = false;

        await new LoginRateLimitKeyMiddleware(_ => { nextInvoked = true; return Task.CompletedTask; }).InvokeAsync(context);

        nextInvoked.ShouldBeTrue();
        context.Items.ContainsKey(LoginRateLimitKeyMiddleware.AccountKeyItem).ShouldBeFalse();
        context.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBe(LoginRateLimitPartitioner.ClientKey("203.0.113.10"));
        context.Request.Body.Position.ShouldBe(0);
        (await new StreamReader(context.Request.Body, Encoding.UTF8).ReadToEndAsync()).ShouldBe(body);
    }

    [Test]
    public async Task Non_json_content_keeps_only_the_client_partition_without_touching_the_body()
    {
        var context = CreateContext("email=owner@example.test", contentType: "application/x-www-form-urlencoded", remoteAddress: IPAddress.Parse("203.0.113.10"));

        await new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Items.ContainsKey(LoginRateLimitKeyMiddleware.AccountKeyItem).ShouldBeFalse();
        context.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBe(LoginRateLimitPartitioner.ClientKey("203.0.113.10"));
        context.Request.Body.Position.ShouldBe(0);
    }

    [Test]
    public async Task Unknown_remote_addresses_share_one_fail_closed_client_partition()
    {
        var first = CreateContext(LoginBody, remoteAddress: null);
        var second = CreateContext(LoginBody, remoteAddress: null);
        var middleware = new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);

        var firstKey = first.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBeOfType<string>();
        firstKey.ShouldMatch(OpaqueKeyPattern);
        second.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBe(firstKey);
        firstKey.ShouldNotBe(LoginRateLimitPartitioner.ClientKey("203.0.113.10"));
    }

    [Test]
    public async Task Requests_to_other_endpoints_are_left_untouched()
    {
        var context = CreateContext(LoginBody, remoteAddress: IPAddress.Parse("203.0.113.10"), loginEndpoint: false);
        var nextInvoked = false;

        await new LoginRateLimitKeyMiddleware(_ => { nextInvoked = true; return Task.CompletedTask; }).InvokeAsync(context);

        nextInvoked.ShouldBeTrue();
        context.Items.ContainsKey(LoginRateLimitKeyMiddleware.AccountKeyItem).ShouldBeFalse();
        context.Items.ContainsKey(LoginRateLimitKeyMiddleware.ClientKeyItem).ShouldBeFalse();
        context.Request.Body.Position.ShouldBe(0);
    }

    [Test]
    public async Task Utf16_charset_bodies_are_decoded_like_the_endpoint_and_keyed_by_account()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(LoginBody)).ToArray();
        var context = CreateRawContext(bytes, "application/json; charset=utf-16", IPAddress.Parse("203.0.113.10"));

        await new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem].ShouldBe(LoginRateLimitPartitioner.AccountKey("owner@example.test"));
        context.Request.Body.Position.ShouldBe(0);
    }

    [Test]
    public async Task Utf8_bom_bodies_are_keyed_by_account()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(LoginBody)).ToArray();
        var context = CreateRawContext(bytes, "application/json; charset=utf-8", IPAddress.Parse("203.0.113.10"));

        await new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem].ShouldBe(LoginRateLimitPartitioner.AccountKey("owner@example.test"));
    }

    [Test]
    public async Task Unknown_charsets_share_one_fail_closed_account_partition()
    {
        var context = CreateRawContext(Encoding.UTF8.GetBytes(LoginBody), "application/json; charset=x-unknown-charset", IPAddress.Parse("203.0.113.10"));

        await new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        var key = context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem].ShouldBeOfType<string>();
        key.ShouldMatch(OpaqueKeyPattern);
        key.ShouldBe(LoginRateLimitPartitioner.SentinelKey(LoginRateLimitKeyMiddleware.UndecodableBodyPartition));
        key.ShouldNotBe(LoginRateLimitPartitioner.AccountKey("owner@example.test"));
    }

    [Test]
    public async Task Oversized_bodies_are_not_parsed_and_share_one_fail_closed_account_partition()
    {
        var padding = new string('x', LoginRateLimitKeyMiddleware.MaxBodyBytes);
        var body = "{\"padding\":\"" + padding + "\",\"email\":\"owner@example.test\",\"password\":\"Testing1234!\"}";
        var context = CreateContext(body, remoteAddress: IPAddress.Parse("203.0.113.10"));

        await new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask).InvokeAsync(context);

        context.Items[LoginRateLimitKeyMiddleware.AccountKeyItem].ShouldBe(LoginRateLimitPartitioner.SentinelKey(LoginRateLimitKeyMiddleware.OversizedBodyPartition));
        context.Request.Body.Position.ShouldBe(0);
    }

    [Test]
    public async Task Ipv4_mapped_ipv6_addresses_share_the_ipv4_client_partition()
    {
        var mapped = CreateContext(LoginBody, remoteAddress: IPAddress.Parse("::ffff:203.0.113.10"));
        var plain = CreateContext(LoginBody, remoteAddress: IPAddress.Parse("203.0.113.10"));
        var middleware = new LoginRateLimitKeyMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(mapped);
        await middleware.InvokeAsync(plain);

        mapped.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBe(plain.Items[LoginRateLimitKeyMiddleware.ClientKeyItem]);
        mapped.Items[LoginRateLimitKeyMiddleware.ClientKeyItem].ShouldBe(LoginRateLimitPartitioner.ClientKey("203.0.113.10"));
    }

    private static DefaultHttpContext CreateRawContext(byte[] body, string contentType, IPAddress? remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/identity/sessions";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EnableRateLimitingAttribute(LoginRateLimitPartitioner.PolicyName)), "login"));
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = contentType;
        return context;
    }

    private static DefaultHttpContext CreateContext(string body, string contentType = "application/json", IPAddress? remoteAddress = null, bool loginEndpoint = true)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/identity/sessions";
        var metadata = loginEndpoint
            ? new EndpointMetadataCollection(new EnableRateLimitingAttribute(LoginRateLimitPartitioner.PolicyName))
            : new EndpointMetadataCollection();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, loginEndpoint ? "login" : "other"));
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = contentType;
        return context;
    }
}

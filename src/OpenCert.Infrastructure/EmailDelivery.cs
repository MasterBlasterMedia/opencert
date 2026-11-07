using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OpenCert.Application;
namespace OpenCert.Infrastructure;

public sealed class EmailDelivery(HttpClient client, IConfiguration config, IHostEnvironment environment) : IEmailDelivery
{
    public async Task Send(string to, string subject, string text, string idempotencyKey, CancellationToken cancellationToken)
    {
        var key = config["Email:ResendApiKey"];
        if (string.IsNullOrEmpty(key))
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Email delivery is not configured.");
            // Local mail sink is private filesystem output, never the application log or a public HTTP endpoint.
            var directory = config["Email:DevelopmentDirectory"] ?? ".local/mail";
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid() + ".txt");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, options);
            await using var writer = new StreamWriter(file); await writer.WriteAsync($"To: {to}\nSubject: {subject}\n\n{text}"); return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key); request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new { from = config["Email:From"], to = new[] { to }, subject, text, html = "<p>" + System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>") + "</p>" });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Email provider returned status {(int)response.StatusCode}.");
    }
}

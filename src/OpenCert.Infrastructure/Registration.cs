using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenCert.Application;
using System.Security.Cryptography.X509Certificates;
namespace OpenCert.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddOpenCert(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        var connection = config.GetConnectionString("Database") ?? throw new InvalidOperationException("Set ConnectionStrings__Database.");
        if (!environment.IsDevelopment() && new Npgsql.NpgsqlConnectionStringBuilder(connection).SslMode != Npgsql.SslMode.VerifyFull) throw new InvalidOperationException("Production PostgreSQL requires SSL Mode=VerifyFull.");
        services.AddDbContext<AppDb>(options => options.UseNpgsql(connection));
        var protection = services.AddDataProtection().SetApplicationName("OpenCert").PersistKeysToDbContext<AppDb>();
        var certificatePath = config["Security:KeyEncryptionCertificate"];
        if (config["Security:KeyEncryptionCertificateBase64"] is { Length: > 0 } encodedCertificate) protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(encodedCertificate), config["Security:KeyEncryptionPassword"]));
        else if (!string.IsNullOrEmpty(certificatePath)) protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, config["Security:KeyEncryptionPassword"]));
        else if (!environment.IsDevelopment()) throw new InvalidOperationException("Supply Security__KeyEncryptionCertificate to encrypt the persistent key ring.");
        services.AddScoped<IHttpsInspector, HttpsInspector>(); services.AddScoped<IHostingDiscovery, HostingDiscovery>();
        services.AddHttpClient<IEmailDelivery, EmailDelivery>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient("Acme", client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler(() => new AcmeBackoffHandler())
            .ConfigurePrimaryHttpMessageHandler(() => AcmeTransport.Create(config, environment));
        services.AddScoped<EmailOutbox>(); services.AddScoped<AcmeOrders>(); services.AddScoped<JobQueue>(); services.AddScoped<OperationalMaintenance>();
        services.AddScoped<AcmeRenewalInformation>();
        return services;
    }
}

using LabelsMis.Domain.Email;
using LabelsMis.Domain.Fedex;
using LabelsMis.Infrastructure.Email;
using LabelsMis.Infrastructure.Fedex;
using LabelsMis.Domain.Storage;
using LabelsMis.Infrastructure.Storage;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabelsMis.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<LabelsMisDbContext>(options =>
            options.UseNpgsql(connectionString));

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<LabelsMisDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/Login";
        });

        services.AddHttpClient<IEmailSender, MailgunEmailSender>();

        services.Configure<FedexOptions>(configuration.GetSection(FedexOptions.SectionName));
        var fedexOptions = configuration.GetSection(FedexOptions.SectionName).Get<FedexOptions>() ?? new FedexOptions();
        if (fedexOptions.UseSandbox)
        {
            services.AddSingleton<IFedexClient, SandboxFedexClient>();
        }
        else
        {
            throw new InvalidOperationException(
                "Production FedEx client is not configured. Set Fedex:UseSandbox to true for local development.");
        }

        services.AddScoped<IFileStorageClient, FileStorageService>();
        services.AddScoped<LocalFileStorageClient>();
        services.AddScoped<SpacesFileStorageClient>();

        return services;
    }
}

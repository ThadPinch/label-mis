using LabelsMis.Infrastructure;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Middleware;
using LabelsMis.Web.Services;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Invoices;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWebServices();
builder.Services.AddMasterDataAuthorization();
builder.Services.Configure<EstimateOptions>(builder.Configuration.GetSection(EstimateOptions.SectionName));
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection(JobOptions.SectionName));
builder.Services.Configure<InvoiceOptions>(builder.Configuration.GetSection(InvoiceOptions.SectionName));
builder.Services.AddRazorPages();

var app = builder.Build();

if (!IsEfDesignTime())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
    // await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(app.Services);
    await MasterDataSeeder.SeedAsync(app.Services);
}

if (IsEfDesignTime())
{
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<ForcePasswordChangeMiddleware>();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static bool IsEfDesignTime() =>
    Environment.GetCommandLineArgs().Any(arg =>
        arg.Contains("ef", StringComparison.OrdinalIgnoreCase));

public partial class Program;

using LabelsMis.Infrastructure;
using LabelsMis.Infrastructure.Identity;
using LabelsMis.Infrastructure.Persistence;
using LabelsMis.Web.Middleware;
using LabelsMis.Web.Services;
using LabelsMis.Web.Pdf;
using LabelsMis.Web.Services.Estimates;
using LabelsMis.Web.Services.Invoices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWebServices();
builder.Services.AddMasterDataAuthorization();
builder.Services.Configure<EstimateOptions>(builder.Configuration.GetSection(EstimateOptions.SectionName));
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection(JobOptions.SectionName));
builder.Services.Configure<InvoiceOptions>(builder.Configuration.GetSection(InvoiceOptions.SectionName));
builder.Services.AddRazorPages()
    .AddMvcOptions(options =>
    {
        // Make model-binding failures name the field and value instead of the
        // framework default "The value '' is invalid." so the validation summary
        // tells the user what actually went wrong.
        var messages = options.ModelBindingMessageProvider;
        messages.SetAttemptedValueIsInvalidAccessor((value, field) =>
            $"'{value}' is not a valid value for {field}.");
        messages.SetUnknownValueIsInvalidAccessor(field =>
            $"The supplied value for {field} is not valid.");
        messages.SetValueMustBeANumberAccessor(field =>
            $"{field} must be a number.");
        messages.SetMissingBindRequiredValueAccessor(field =>
            $"A value for {field} is required.");
        messages.SetValueMustNotBeNullAccessor(_ =>
            "A value is required.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value =>
            $"'{value}' is not a valid value.");
    });

var app = builder.Build();

if (!IsEfDesignTime())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LabelsMisDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(app.Services);
    // await ((Func<Task>)(async () => { var um = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var u = await um.FindByEmailAsync(IdentitySeeder.DefaultAdminEmail); if (u is not null) { await um.ResetPasswordAsync(u, await um.GeneratePasswordResetTokenAsync(u), "MagicMagenta411!"); u.MustChangePassword = false; await um.UpdateAsync(u); } }))();
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

app.MapGet("/_diag/db", async (LabelsMisDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT current_user AS usr,
               current_database() AS db,
               has_schema_privilege(current_user, 'public', 'CREATE') AS can_create,
               has_schema_privilege(current_user, 'public', 'USAGE') AS can_use
    """;
    using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(new {
        canConnect,
        user = reader["usr"]?.ToString(),
        db = reader["db"]?.ToString(),
        canCreate = reader["can_create"],
        canUse = reader["can_use"]
    });
});

public partial class Program;

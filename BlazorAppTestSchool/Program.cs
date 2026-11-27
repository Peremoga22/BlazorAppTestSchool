using BlazorAppTestSchool.Components;
using BlazorAppTestSchool.Components.Account;
using BlazorAppTestSchool.Data;
using BlazorAppTestSchool.Data.Testing;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddSingleton<BiologyAttemptStore>();
builder.Services.AddSingleton<TestDefinitionStore>();
builder.Services.AddSingleton<TestImageStore>();
builder.Services.AddSingleton<TestFolderStore>();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

try
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await database.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<BiologyAttemptStore>().BackfillLastNameKeysAsync();
}
catch (Exception exception)
{
    app.Logger.LogWarning(exception, "Could not apply database migrations.");
}

try
{
    var testStore = app.Services.GetRequiredService<TestDefinitionStore>();
    if (!await testStore.HasAnyAsync())
    {
        var seed = BiologySeed.CreateGrade7();
        await testStore.SaveAsync(seed, TestDefinitionParser.ToJson(seed));
    }
}
catch (Exception exception)
{
    app.Logger.LogWarning(exception, "Could not seed the default biology test. Apply database migrations first.");
}

try
{
    await using var scope = app.Services.CreateAsyncScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in ApplicationRoles.Seeded)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    var adminEmail = builder.Configuration["Seed:AdminEmail"];
    var adminPassword = builder.Configuration["Seed:AdminPassword"];
    if (adminEmail is { Length: > 0 } && adminPassword is { Length: > 0 })
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var created = await userManager.CreateAsync(admin, adminPassword);
            if (!created.Succeeded)
            {
                app.Logger.LogWarning("Could not create the seed admin user: {Errors}",
                    string.Join("; ", created.Errors.Select(error => error.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(admin, ApplicationRoles.Admin))
        {
            await userManager.AddToRoleAsync(admin, ApplicationRoles.Admin);
        }

        if (!await userManager.IsInRoleAsync(admin, ApplicationRoles.Teacher))
        {
            await userManager.AddToRoleAsync(admin, ApplicationRoles.Teacher);
        }
    }
}
catch (Exception exception)
{
    app.Logger.LogWarning(exception, "Could not seed application roles. Apply database migrations first.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// Serves files uploaded at runtime (e.g. teacher-uploaded test images) from wwwroot,
// which the build-time MapStaticAssets manifest below does not cover.
app.UseStaticFiles();

app.MapGet("/teacher/reports/{attemptId:guid}/word", async Task<IResult> (
    Guid attemptId, HttpContext context, IWebHostEnvironment environment, BiologyAttemptStore attempts) =>
{
    context.Response.Headers.CacheControl = "no-store";

    var report = await attempts.FindAsync(attemptId, context.RequestAborted);
    if (report is null)
    {
        return Results.NotFound();
    }

    var diagram = environment.WebRootFileProvider.GetFileInfo("root-tip.png");
    var document = BiologyAttemptWordDocument.Create(report, diagram);
    return Results.File(document, BiologyAttemptWordDocument.ContentType,
        $"Контрольна з біології {report.Grade} клас {attemptId.ToString("N")[..8]}.docx");
}).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Teacher, ApplicationRoles.Admin));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

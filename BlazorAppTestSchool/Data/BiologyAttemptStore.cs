using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data;

public sealed class BiologyAttemptStore(IServiceScopeFactory scopeFactory)
{
    public async Task SaveAsync(Guid code, BiologyAttemptReport report)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await database.BiologyAttempts.AnyAsync(attempt => attempt.Id == code))
        {
            return;
        }

        database.BiologyAttempts.Add(new BiologyAttempt
        {
            Id = code,
            ReportJson = JsonSerializer.Serialize(report)
        });
        await database.SaveChangesAsync();
    }

    public async Task<BiologyAttemptReport?> FindAsync(Guid code)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportJson = await database.BiologyAttempts
            .Where(attempt => attempt.Id == code)
            .Select(attempt => attempt.ReportJson)
            .SingleOrDefaultAsync();

        return reportJson is null ? null : JsonSerializer.Deserialize<BiologyAttemptReport>(reportJson);
    }
}

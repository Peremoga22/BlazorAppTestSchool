using BlazorAppTestSchool.Data.Testing;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlazorAppTestSchool.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<BiologyAttempt> BiologyAttempts => Set<BiologyAttempt>();
        public DbSet<TestDefinitionEntity> TestDefinitions => Set<TestDefinitionEntity>();
        public DbSet<TestFolderEntity> TestFolders => Set<TestFolderEntity>();
    }
}

using Microsoft.EntityFrameworkCore;
using TransLink.Lite.Domain.Entities;

namespace TransLink.Lite.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<TranslationSession> TranslationSessions => Set<TranslationSession>();
}
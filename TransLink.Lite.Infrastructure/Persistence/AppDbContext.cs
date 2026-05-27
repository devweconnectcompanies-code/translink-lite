using Microsoft.EntityFrameworkCore;

namespace TransLink.Lite.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }
}
using Microsoft.EntityFrameworkCore;
using TradeApi.Models;

namespace TradeApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Trade>()
            .Property(t => t.Price)
            .HasPrecision(18, 2);
    }
}
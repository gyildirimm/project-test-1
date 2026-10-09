using Expenses.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Expenses.Api.Data;

public class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<StatusHistoryEntry> StatusHistory => Set<StatusHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Expense>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.StatusHistory)
                .WithOne(h => h.Expense)
                .HasForeignKey(h => h.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusHistoryEntry>(h =>
        {
            h.Property(x => x.FromStatus).HasConversion<string?>().HasMaxLength(20);
            h.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            h.Property(x => x.Reason).HasMaxLength(500);
        });
    }
}

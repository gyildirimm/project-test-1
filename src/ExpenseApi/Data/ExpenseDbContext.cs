using ExpenseApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseApi.Data;

public class ExpenseDbContext(DbContextOptions<ExpenseDbContext> options) : DbContext(options)
{
    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<StatusHistoryEntry> StatusHistory => Set<StatusHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DecisionReason).HasMaxLength(2000);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasMany(e => e.History)
                .WithOne()
                .HasForeignKey(h => h.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StatusHistoryEntry>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.FromStatus).HasConversion<string?>().HasMaxLength(20);
            entity.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(h => h.Actor).HasMaxLength(100).IsRequired();
            entity.Property(h => h.Role).HasMaxLength(50).IsRequired();
            entity.Property(h => h.Note).HasMaxLength(2000);
            entity.HasIndex(h => h.ExpenseId);
        });
    }
}

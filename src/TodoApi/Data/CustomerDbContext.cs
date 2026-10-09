using Microsoft.EntityFrameworkCore;

namespace TodoApi.Data;

public class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerImport> CustomerImports => Set<CustomerImport>();
    public DbSet<CustomerImportRow> CustomerImportRows => Set<CustomerImportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(c => c.Name).IsRequired();
            // SQLite has no DateTime kind; values are stored as UTC, so mark them UTC on read.
            e.Property(c => c.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.Property(c => c.UpdatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });

        modelBuilder.Entity<CustomerImport>(e =>
        {
            e.Property(i => i.FileName).IsRequired();
            e.Property(i => i.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.HasMany(i => i.Rows)
                .WithOne()
                .HasForeignKey(r => r.CustomerImportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerImportRow>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
            e.HasIndex(r => new { r.CustomerImportId, r.LineNumber });
        });
    }
}

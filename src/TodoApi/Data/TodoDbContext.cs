using Microsoft.EntityFrameworkCore;

namespace TodoApi.Data;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>(e =>
        {
            e.Property(t => t.Title).IsRequired().HasMaxLength(200);
            // SQLite has no DateTime kind; values are stored as UTC, so mark them UTC on read.
            e.Property(t => t.CreatedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        });
    }
}

using Microsoft.EntityFrameworkCore;
using o_down.Core.Models;

namespace o_down.Data;

public sealed class OdownDbContext : DbContext
{
    public DbSet<DownloadItem> Downloads => Set<DownloadItem>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<PostAction> PostActions => Set<PostAction>();
    public DbSet<Mirror> Mirrors => Set<Mirror>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<Schedule> Schedules => Set<Schedule>();

    public OdownDbContext(DbContextOptions<OdownDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DownloadItem>(b =>
        {
            b.ToTable("downloads");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasConversion<int>();
            b.Property(x => x.State).HasConversion<int>();
            b.Property(x => x.SourceUrl).HasMaxLength(4096).IsRequired();
            b.Property(x => x.FilenameHint).HasMaxLength(512);
            b.Property(x => x.DestinationDirectory).HasMaxLength(1024);
            b.Property(x => x.FinalPath).HasMaxLength(2048);
            b.Property(x => x.Checksum).HasMaxLength(256);
            b.Property(x => x.ChecksumAlgorithm).HasMaxLength(16);
            b.Property(x => x.Category).HasMaxLength(64);
            b.Property(x => x.ErrorMessage).HasMaxLength(2048);
            b.Property(x => x.ReferrerUrl).HasMaxLength(2048);
            b.Property(x => x.Cookies).HasMaxLength(8192);
            b.HasIndex(x => x.State);
            b.HasIndex(x => x.Priority);
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<Segment>(b =>
        {
            b.ToTable("segments");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.DownloadId);
            b.Property(x => x.MirrorUrl).HasMaxLength(4096);
        });

        modelBuilder.Entity<PostAction>(b =>
        {
            b.ToTable("post_actions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasConversion<int>();
            b.Property(x => x.Command).HasMaxLength(2048);
            b.Property(x => x.Arguments).HasMaxLength(4096);
            b.Property(x => x.WorkingDirectory).HasMaxLength(1024);
        });

        modelBuilder.Entity<Mirror>(b =>
        {
            b.ToTable("mirrors");
            b.HasKey(x => x.Id);
            b.Property(x => x.Url).HasMaxLength(4096).IsRequired();
        });

        modelBuilder.Entity<CategoryRule>(b =>
        {
            b.ToTable("category_rules");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(64).IsRequired();
            b.Property(x => x.DestinationDirectory).HasMaxLength(1024).IsRequired();
            b.Property(x => x.ExtensionPattern).HasMaxLength(512);
            b.Property(x => x.NameRegex).HasMaxLength(512);
        });

        modelBuilder.Entity<Schedule>(b =>
        {
            b.ToTable("schedules");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(64).IsRequired();
            b.Property(x => x.CronExpression).HasMaxLength(64).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}

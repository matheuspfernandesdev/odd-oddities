using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;

namespace OddOddities.Infrastructure.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasMaxLength(500);

        builder.HasIndex(c => c.Name)
            .IsUnique();

        builder.HasIndex(c => c.IsActive);
    }
}

public class SubcategoryConfiguration : IEntityTypeConfiguration<Subcategory>
{
    public void Configure(EntityTypeBuilder<Subcategory> builder)
    {
        builder.ToTable("Subcategories");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.HasOne(s => s.Category)
            .WithMany(c => c.Subcategories)
            .HasForeignKey(s => s.CategoryId);

        builder.HasIndex(s => new { s.CategoryId, s.Name })
            .IsUnique();

        builder.HasIndex(s => new { s.CategoryId, s.IsActive });
    }
}

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("Posts");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TextContent)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.Summary)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.Theme)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(p => p.ContentHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.SourceUrl)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.ImageObjectKey)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(p => p.ImageWidth)
            .HasDefaultValue(1080);

        builder.Property(p => p.ImageHeight)
            .HasDefaultValue(1080);

        builder.Property(p => p.Caption)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<int>();

        builder.Property(p => p.FailureStep)
            .HasConversion<int?>();

        builder.Property(p => p.FailureReason)
            .HasColumnType("text");

        builder.Property(p => p.ErrorCode)
            .HasMaxLength(80);

        builder.Property(p => p.FailureDetails)
            .HasColumnType("text");

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId);

        builder.HasOne(p => p.Subcategory)
            .WithMany(s => s.Posts)
            .HasForeignKey(p => p.SubcategoryId);

        builder.HasIndex(p => new { p.Status, p.CreatedAt });
        builder.HasIndex(p => new { p.CategoryId, p.SubcategoryId, p.PublishedAt });
        builder.HasIndex(p => p.ContentHash);
        builder.HasIndex(p => p.PublishedAt);
        builder.HasIndex(p => p.Theme);
    }
}

public class GenerationAttemptConfiguration : IEntityTypeConfiguration<GenerationAttempt>
{
    public void Configure(EntityTypeBuilder<GenerationAttempt> builder)
    {
        builder.ToTable("GenerationAttempts");

        builder.HasKey(ga => ga.Id);

        builder.Property(ga => ga.ModelId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(ga => ga.Status)
            .HasConversion<int>();

        builder.Property(ga => ga.RejectionReason)
            .HasMaxLength(255);

        builder.Property(ga => ga.RawResponse)
            .HasColumnType("text");

        builder.Property(ga => ga.CostUsd)
            .HasPrecision(10, 6);

        builder.HasOne(ga => ga.Post)
            .WithMany(p => p.GenerationAttempts)
            .HasForeignKey(ga => ga.PostId);

        builder.HasIndex(ga => new { ga.PostId, ga.AttemptNumber });
    }
}

public class PublicationConfiguration : IEntityTypeConfiguration<Publication>
{
    public void Configure(EntityTypeBuilder<Publication> builder)
    {
        builder.ToTable("Publications");

        builder.HasKey(pub => pub.Id);

        builder.Property(pub => pub.MetaMediaId)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(pub => pub.MetaMediaStatus)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(pub => pub.MetaMediaStatusCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(pub => pub.MetaPermalink)
            .HasColumnType("text");

        builder.HasOne(pub => pub.Post)
            .WithOne(p => p.Publication)
            .HasForeignKey<Publication>(pub => pub.PostId);

        builder.HasIndex(pub => pub.MetaMediaId);
        builder.HasIndex(pub => pub.PostId)
            .IsUnique();
    }
}

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("SystemSettings");

        builder.HasKey(ss => ss.Key);

        builder.Property(ss => ss.Key)
            .HasMaxLength(80);

        builder.Property(ss => ss.Value)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(ss => ss.Description)
            .HasMaxLength(255);
    }
}

public class PostAuditConfiguration : IEntityTypeConfiguration<PostAudit>
{
    public void Configure(EntityTypeBuilder<PostAudit> builder)
    {
        builder.ToTable("PostAudits");

        builder.HasKey(pa => pa.Id);

        builder.Property(pa => pa.FieldName)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(pa => pa.OldValue)
            .HasColumnType("text");

        builder.Property(pa => pa.NewValue)
            .HasColumnType("text");

        builder.HasOne(pa => pa.Post)
            .WithMany()
            .HasForeignKey(pa => pa.PostId);

        builder.HasIndex(pa => new { pa.PostId, pa.ChangedAt });
    }
}

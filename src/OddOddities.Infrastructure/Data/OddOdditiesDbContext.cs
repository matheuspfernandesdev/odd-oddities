using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;

namespace OddOddities.Infrastructure.Data;

/// <summary>
/// Entity Framework Core DbContext for the OddOddities application.
/// </summary>
public class OddOdditiesDbContext : DbContext
{
    public OddOdditiesDbContext(DbContextOptions<OddOdditiesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<GenerationAttempt> GenerationAttempts => Set<GenerationAttempt>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<PostAudit> PostAudits => Set<PostAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OddOdditiesDbContext).Assembly);

        SeedCategories(modelBuilder);
        SeedSubcategories(modelBuilder);
        SeedSystemSettings(modelBuilder);
    }

    private static void SeedCategories(ModelBuilder modelBuilder)
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var categories = new[]
        {
            new Category { Id = 1, Name = "Science", Description = "Scientific discoveries, phenomena and facts", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 2, Name = "Religion", Description = "Religious beliefs, practices and historical facts", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 3, Name = "Space", Description = "Astronomy, planets, stars and cosmic phenomena", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 4, Name = "Animals", Description = "Animal behavior, species and biological facts", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 5, Name = "Nature", Description = "Natural wonders, geology and environmental phenomena", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 6, Name = "History", Description = "Historical events, civilizations and notable figures", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 7, Name = "Technology", Description = "Innovations, computing and engineering breakthroughs", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 8, Name = "Human Body", Description = "Human biology, anatomy and medical curiosities", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 9, Name = "Geography", Description = "Countries, landscapes and geographical oddities", IsActive = true, CreatedAt = now, UpdatedAt = now },
            new Category { Id = 10, Name = "Culture", Description = "Art, traditions, languages and cultural facts", IsActive = true, CreatedAt = now, UpdatedAt = now },
        };

        modelBuilder.Entity<Category>().HasData(categories);
    }

    private static void SeedSubcategories(ModelBuilder modelBuilder)
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var subcategories = new List<Subcategory>();
        long id = 1;

        var subcategoryData = new Dictionary<long, string[]>
        {
            [1] = ["Physics", "Chemistry", "Biology", "Astronomy", "Ecology"],
            [2] = ["Christianity", "Buddhism", "Islam", "Hinduism", "Mythology"],
            [3] = ["Planets", "Stars", "Galaxies", "Black Holes", "Nebulae"],
            [4] = ["Mammals", "Birds", "Marine Life", "Insects", "Reptiles"],
            [5] = ["Volcanoes", "Oceans", "Forests", "Deserts", "Mountains"],
            [6] = ["Ancient Civilizations", "World Wars", "Exploration", "Medieval", "Renaissance"],
            [7] = ["Artificial Intelligence", "Space Technology", "Robotics", "Internet", "Renewable Energy"],
            [8] = ["Brain", "Genetics", "Evolution", "Diseases", "Senses"],
            [9] = ["Continents", "Islands", "Rivers", "Climate", "Landmarks"],
            [10] = ["Music", "Languages", "Food", "Fashion", "Festivals"],
        };

        foreach (var (categoryId, names) in subcategoryData)
        {
            foreach (var name in names)
            {
                subcategories.Add(new Subcategory
                {
                    Id = id++,
                    CategoryId = categoryId,
                    Name = name,
                    Description = null,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        modelBuilder.Entity<Subcategory>().HasData(subcategories);
    }

    private static void SeedSystemSettings(ModelBuilder modelBuilder)
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var settings = new[]
        {
            new SystemSetting { Key = "MAX_CAPTION_CONTENT_LENGTH", Value = "800", IsEncrypted = false, Description = "Maximum character length for post caption content", UpdatedAt = now },
            new SystemSetting { Key = "SIMILARITY_THRESHOLD", Value = "0.80", IsEncrypted = false, Description = "Jaccard similarity threshold for content rejection (0.0 - 1.0)", UpdatedAt = now },
            new SystemSetting { Key = "MAX_GENERATION_ATTEMPTS", Value = "3", IsEncrypted = false, Description = "Maximum number of text generation attempts per pipeline execution", UpdatedAt = now },
        };

        modelBuilder.Entity<SystemSetting>().HasData(settings);
    }
}

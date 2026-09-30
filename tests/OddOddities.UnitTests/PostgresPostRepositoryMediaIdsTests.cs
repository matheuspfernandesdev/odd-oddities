using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Infrastructure.Adapters;
using OddOddities.Infrastructure.Data;

namespace OddOddities.UnitTests;

/// <summary>
/// RF-19: the comment lookback query — latest published posts carrying a
/// Publication.MetaMediaId (joined), newest first, limited to N.
/// </summary>
public class PostgresPostRepositoryMediaIdsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OddOdditiesDbContext> _options;

    public PostgresPostRepositoryMediaIdsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<OddOdditiesDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new OddOdditiesDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private OddOdditiesDbContext CreateContext() => new(_options);

    private static Post NewPost(long id, PostStatus status, DateTime? publishedAt, string? metaMediaId)
        => new()
        {
            Id = id,
            CategoryId = 1,
            SubcategoryId = 1,
            TextContent = $"Text {id}",
            Summary = $"Summary {id}",
            Theme = "Science",
            ContentHash = $"hash-{id}",
            SourceUrl = $"https://example.com/{id}",
            ImageObjectKey = $"images/{id}.png",
            Caption = $"Caption {id}",
            Status = status,
            PublishedAt = publishedAt,
            Publication = metaMediaId is null
                ? null
                : new Publication
                {
                    MetaMediaId = metaMediaId,
                    MetaMediaStatus = "published",
                    MetaMediaStatusCode = "FINISHED"
                }
        };

    [Fact]
    public async Task GetLatestPublishedMediaIdsAsync_ReturnsNewestPublishedMediaOnly()
    {
        using (var seedContext = CreateContext())
        {
            seedContext.Posts.AddRange(
                NewPost(1, PostStatus.Published, DateTime.UtcNow, "media-new"),
                NewPost(2, PostStatus.Published, DateTime.UtcNow.AddDays(-1), "media-old"),
                // Published post never sent to Instagram: no Publication row.
                NewPost(3, PostStatus.Published, DateTime.UtcNow.AddDays(-2), null),
                // Publication row exists, but the post itself is not published.
                NewPost(4, PostStatus.Generated, null, "media-unpublished"));
            await seedContext.SaveChangesAsync();
        }

        using var context = CreateContext();
        var repository = new PostgresPostRepository(context);

        var mediaIds = await repository.GetLatestPublishedMediaIdsAsync(limit: 10);
        var limited = await repository.GetLatestPublishedMediaIdsAsync(limit: 1);

        mediaIds.Should().Equal("media-new", "media-old");
        limited.Should().Equal("media-new");
    }

    [Fact]
    public async Task GetLatestPublishedMediaIdsAsync_NoPublishedPosts_ReturnsEmpty()
    {
        using var context = CreateContext();
        var repository = new PostgresPostRepository(context);

        var mediaIds = await repository.GetLatestPublishedMediaIdsAsync(limit: 15);

        mediaIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestPublishedMediaIdsAsync_NonPositiveLimit_ReturnsEmptyWithoutQuerying()
    {
        using (var seedContext = CreateContext())
        {
            seedContext.Posts.Add(NewPost(1, PostStatus.Published, DateTime.UtcNow, "media-new"));
            await seedContext.SaveChangesAsync();
        }

        using var context = CreateContext();
        var repository = new PostgresPostRepository(context);

        (await repository.GetLatestPublishedMediaIdsAsync(limit: 0)).Should().BeEmpty();
        (await repository.GetLatestPublishedMediaIdsAsync(limit: -1)).Should().BeEmpty();
    }
}

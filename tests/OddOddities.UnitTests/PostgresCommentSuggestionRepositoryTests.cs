using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OddOddities.Domain.Entities;
using OddOddities.Domain.Enums;
using OddOddities.Infrastructure.Adapters;
using OddOddities.Infrastructure.Data;

namespace OddOddities.UnitTests;

/// <summary>
/// RF-19: CommentSuggestion persistence — idempotency by CommentId (exists-check +
/// unique index) against an in-memory SQLite database.
/// </summary>
public class PostgresCommentSuggestionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OddOdditiesDbContext> _options;

    public PostgresCommentSuggestionRepositoryTests()
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

    private static CommentSuggestion NewSuggestion(string commentId) => new()
    {
        CommentId = commentId,
        MediaId = "media-1",
        AuthorUsername = "curious_fan",
        CommentText = "Great fact!"
    };

    [Fact]
    public async Task ExistsByCommentIdAsync_ReturnsFalse_ForUnseenComment()
    {
        using var context = CreateContext();
        var repository = new PostgresCommentSuggestionRepository(context);

        var exists = await repository.ExistsByCommentIdAsync("comment-1");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsByCommentIdAsync_ReturnsTrue_AfterCreate_SoCommentIsNotProcessedTwice()
    {
        using var context = CreateContext();
        var repository = new PostgresCommentSuggestionRepository(context);

        await repository.CreateAsync(NewSuggestion("comment-1"));

        (await repository.ExistsByCommentIdAsync("comment-1")).Should().BeTrue();
        (await repository.ExistsByCommentIdAsync("comment-2")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_DuplicateCommentId_Throws_UniqueIndexEnforcesIdempotency()
    {
        using var context = CreateContext();
        var repository = new PostgresCommentSuggestionRepository(context);
        await repository.CreateAsync(NewSuggestion("comment-1"));

        var act = () => repository.CreateAsync(NewSuggestion("comment-1"));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UpdateAsync_PersistsClassificationAndRejectionReason()
    {
        using var context = CreateContext();
        var repository = new PostgresCommentSuggestionRepository(context);
        var suggestion = await repository.CreateAsync(NewSuggestion("comment-1"));

        suggestion.Classification = CommentClassification.Rejected;
        suggestion.RejectionReason = "Off-topic";
        await repository.UpdateAsync(suggestion);

        using var verifyContext = CreateContext();
        var stored = await verifyContext.CommentSuggestions
            .AsNoTracking()
            .SingleAsync(cs => cs.CommentId == "comment-1");

        stored.Classification.Should().Be(CommentClassification.Rejected);
        stored.RejectionReason.Should().Be("Off-topic");
        stored.ProcessedAt.Should().NotBe(default);
    }
}

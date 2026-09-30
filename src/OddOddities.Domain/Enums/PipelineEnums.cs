namespace OddOddities.Domain.Enums;

/// <summary>
/// Represents the status of a Post in the pipeline.
/// </summary>
public enum PostStatus
{
    Generated = 0,
    Validated = 1,
    ImageProcessed = 2,
    Published = 3,
    Failed = 4
}

/// <summary>
/// Represents the step where a pipeline failure occurred.
/// </summary>
public enum FailureStep
{
    TextGeneration = 0,
    SourceValidation = 1,
    ImageGeneration = 2,
    ImageStorage = 3,
    Database = 4,
    InstagramApi = 5,
    VideoGeneration = 6,
    VideoStorage = 7,
    CommentModeration = 8
}

/// <summary>
/// Represents the status of a generation attempt.
/// </summary>
public enum AttemptStatus
{
    Success = 0,
    Rejected = 1,
    Error = 2
}

/// <summary>
/// Represents the classification of a read Instagram comment (RF-19).
/// </summary>
public enum CommentClassification
{
    NotSuggestion = 0,
    Rejected = 1,
    Accepted = 2
}

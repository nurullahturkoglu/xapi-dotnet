using System.Text;
using Xapi;
using Xapi.Client;

namespace FakeLrsInTests;

/// <summary>
/// The application code under test. It only knows <see cref="IXapiClient"/>, so in production it talks to a real
/// LRS and in the tests to <see cref="FakeLrs"/>.
/// </summary>
/// <param name="lrs">The LRS client.</param>
public sealed class CourseProgress(IXapiClient lrs)
{
    private const string BookmarkStateId = "bookmark";

    /// <summary>Records that a learner completed a course.</summary>
    /// <param name="learner">The learner.</param>
    /// <param name="course">The course's Activity id.</param>
    /// <param name="scaledScore">The score, from -1 to 1.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The statement id.</returns>
    public Task<Guid> CompleteAsync(Agent learner, Iri course, double scaledScore, CancellationToken cancellationToken = default) =>
        lrs.SaveStatementAsync(
            new Statement(learner, Verbs.Completed, new Activity(course))
            {
                Result = new Result { Completion = true, Score = new Score { Scaled = scaledScore } },
            },
            cancellationToken: cancellationToken);

    /// <summary>Stores where the learner is in a course, replacing the previous bookmark.</summary>
    /// <param name="learner">The learner.</param>
    /// <param name="course">The course's Activity id.</param>
    /// <param name="location">The location in the course.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when the bookmark is stored.</returns>
    public async Task SaveBookmarkAsync(Agent learner, Iri course, string location, CancellationToken cancellationToken = default)
    {
        var key = new StateKey(course, learner, BookmarkStateId);
        var current = await lrs.GetDocumentAsync(key, cancellationToken);

        // Replace only the version read: if another device wrote in between, the LRS answers 412
        var precondition = current?.ETag is { } etag ? DocumentPrecondition.IfMatch(etag) : DocumentPrecondition.IfNoneMatchAny;
        await lrs.PutDocumentAsync(key, new XapiDocument(Encoding.UTF8.GetBytes(location), "text/plain"), precondition, cancellationToken);
    }

    /// <summary>Reads the learner's bookmark in a course.</summary>
    /// <param name="learner">The learner.</param>
    /// <param name="course">The course's Activity id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The location, or <see langword="null"/> if there is no bookmark.</returns>
    public async Task<string?> GetBookmarkAsync(Agent learner, Iri course, CancellationToken cancellationToken = default)
    {
        var document = await lrs.GetDocumentAsync(new StateKey(course, learner, BookmarkStateId), cancellationToken);
        return document is null ? null : Encoding.UTF8.GetString(document.Content.Span);
    }
}

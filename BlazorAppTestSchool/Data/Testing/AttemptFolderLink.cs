namespace BlazorAppTestSchool.Data.Testing;

/// <summary>
/// Which folder a submitted paper belongs to. A paper follows its test, so a new empty folder
/// never shows work that was written for a test living somewhere else.
/// </summary>
public static class AttemptFolderLink
{
    /// <summary>
    /// Folder of the test this paper was written for. Null means the test is unfiled
    /// (or the paper cannot be matched to a test, which is treated the same way).
    /// </summary>
    public static Guid? Resolve(Guid? testId, string? testTitle, int grade, IReadOnlyList<TestDefinitionSummary> tests)
    {
        if (testId is Guid id)
        {
            var byId = tests.FirstOrDefault(test => test.Id == id);
            if (byId is not null)
            {
                return byId.FolderId;
            }
        }

        if (string.IsNullOrWhiteSpace(testTitle))
        {
            return null;
        }

        var titled = tests
            .Where(test => string.Equals(test.Title.Trim(), testTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (titled.Length == 0)
        {
            return null;
        }

        var graded = titled.Where(test => test.Grade == grade).ToArray();
        var pool = graded.Length > 0 ? graded : titled;
        return pool.OrderByDescending(test => test.UploadedAt).First().FolderId;
    }
}

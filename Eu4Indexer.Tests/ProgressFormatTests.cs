using Eu4Indexer.Core;

namespace Eu4Indexer.Tests;

public class ProgressFormatTests
{
    [Fact]
    public void FormatProgress_ParsingSnapshot_AlignsPhaseAndCounts()
    {
        // Arrange
        var snapshot = new Pipeline.IndexProgress(
            phase: "parsing", filesDone: 1234, filesTotal: 5678, entities: 8901, locEntries: 0, detail: "");

        // Act
        var line = Pipeline.formatProgress(snapshot);

        // Assert: phase padded to a 13-char field, then the three counters.
        Assert.Equal("parsing       1234/5678 files · 8901 entities · 0 loc", line);
    }

    [Fact]
    public void FormatProgress_LocalisationSnapshot_ShowsLocEntries()
    {
        // Arrange
        var snapshot = new Pipeline.IndexProgress(
            phase: "localisation", filesDone: 42, filesTotal: 42, entities: 8901, locEntries: 31415, detail: "");

        // Act
        var line = Pipeline.formatProgress(snapshot);

        // Assert
        Assert.Equal("localisation  42/42 files · 8901 entities · 31415 loc", line);
    }

    [Fact]
    public void FormatProgress_FinalizeStep_ShowsLabelWithoutCounters()
    {
        // Arrange: finalize steps have no countable items (FilesTotal = 0).
        var snapshot = new Pipeline.IndexProgress(
            phase: "finalizing", filesDone: 0, filesTotal: 0, entities: 8901, locEntries: 31415, detail: "indexes");

        // Act
        var line = Pipeline.formatProgress(snapshot);

        // Assert: a labelled sub-step, not an x/y counter.
        Assert.Equal("finalizing    indexes...", line);
    }
}

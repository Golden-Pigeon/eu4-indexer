// CWTools holds process-global mutable parser state (scope manager, string
// interning), so initializing or parsing from two tests at once corrupts it.
// Run the whole suite serially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

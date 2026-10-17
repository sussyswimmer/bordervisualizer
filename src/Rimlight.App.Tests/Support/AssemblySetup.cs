using Xunit;

// One test at a time: they all share the one UI thread and WPF application (UiThread), WPF-UI's theme, which some
// tests switch, and the trace lines each test checks.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

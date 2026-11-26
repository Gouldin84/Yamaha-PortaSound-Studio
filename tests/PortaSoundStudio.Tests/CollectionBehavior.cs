using Xunit;

// Disable parallel test execution across test collections to prevent race conditions
// on static application state (e.g. AppPaths.SetCustomLibraryRoot)
[assembly: CollectionBehavior(DisableTestParallelization = true)]

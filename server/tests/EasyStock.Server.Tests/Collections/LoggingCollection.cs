using Xunit;

namespace EasyStock.Server.Tests.Collections;

// Hosts started in parallel by other test classes can pick up these settings, or hide them.
[CollectionDefinition(nameof(LoggingTests), DisableParallelization = true)]
public sealed class LoggingCollection;

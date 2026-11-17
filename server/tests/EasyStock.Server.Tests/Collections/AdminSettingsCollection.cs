using Xunit;

namespace EasyStock.Server.Tests.Collections;

// Settings are global, so these tests must not run in parallel with each other or with anything that reads them.
[CollectionDefinition(nameof(AdminSettingsTests), DisableParallelization = true)]
public sealed class AdminSettingsCollection;

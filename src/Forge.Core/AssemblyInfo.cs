using System.Runtime.CompilerServices;

// Exposes internal helpers (clipboard read/write, etc.) to the unit test project.
[assembly: InternalsVisibleTo("Forge.Core.Tests")]

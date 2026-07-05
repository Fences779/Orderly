using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Orderly.Tests")]

namespace Orderly.Server;

/// <summary>
/// Exposes the top-level Program entry point to integration tests via WebApplicationFactory.
/// </summary>
public partial class Program
{
}

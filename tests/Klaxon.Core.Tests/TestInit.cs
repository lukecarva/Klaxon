using System.Runtime.CompilerServices;
using Klaxon.Core.Localization;

// No parallelism: the Loc language is global state that one test switches.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Klaxon.Core.Tests;

/// <summary>Test-run setup.</summary>
internal static class TestInit
{
    /// <summary>Sets the language to Portuguese before any test runs.</summary>
    [ModuleInitializer]
    internal static void Init() => Loc.Set(AppLanguage.Portuguese);
}

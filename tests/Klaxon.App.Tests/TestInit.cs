using System.Runtime.CompilerServices;
using Klaxon.Core.Localization;

// No parallelism: the Loc language is global state that tests switch.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Klaxon.App.Tests;

/// <summary>Test-run setup.</summary>
internal static class TestInit
{
    /// <summary>Sets the language to English before any test runs.</summary>
    [ModuleInitializer]
    internal static void Init() => Loc.Set(AppLanguage.English);
}

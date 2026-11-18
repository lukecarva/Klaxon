namespace Klaxon.Core.Abstractions;

/// <summary>Reports how long keyboard and mouse have been idle.</summary>
public interface IIdleDetector
{
    /// <summary>Time since the last keyboard/mouse input.</summary>
    TimeSpan IdleFor { get; }
}

/// <summary>Always reports present; used in tests and as a safe default.</summary>
public sealed class AlwaysPresentIdleDetector : IIdleDetector
{
    public TimeSpan IdleFor => TimeSpan.Zero;
}

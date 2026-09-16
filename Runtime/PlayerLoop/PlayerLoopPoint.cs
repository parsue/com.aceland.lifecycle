namespace AceLand.Lifecycle
{
    /// <summary>
    /// The injection point inside Unity's <see cref="UnityEngine.LowLevel.PlayerLoop"/>
    /// where the Lifecycle frame pump runs. Used by the frame scheduling API to decide
    /// when per-frame work is flushed.
    /// </summary>
    public enum PlayerLoopPoint
    {
        TimeUpdate,
        Initialization,
        EarlyUpdate,
        FixedUpdate,
        PreUpdate,
        /// <summary>Default. Runs during the regular Update segment.</summary>
        Update,
        PreLateUpdate,
        PostLateUpdate,
    }
}

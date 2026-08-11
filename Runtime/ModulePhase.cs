namespace AceLand.Lifecycle
{
    /// <summary>
    /// Module execution phases. Phases are strictly ordered: the next phase starts only
    /// after the previous one has completely finished.
    /// The order within a phase is determined by <see cref="ModuleSorter"/> based on dependencies.
    /// </summary>
    public enum ModulePhase
    {
        /// <summary>Pure C#; does not touch any UnityEngine object. Maps to AfterAssembliesLoaded.</summary>
        Core = 0,

        /// <summary>UnityEngine APIs are available, but no scene objects are created. Maps to BeforeSceneLoad.</summary>
        Runtime = 1,

        /// <summary>Requires an existing scene / needs to create GameObjects. Maps to AfterSceneLoad.</summary>
        Scene = 2,

        /// <summary>Final wrap-up once everything is ready (analytics, warm-up, intro flow). Maps to after AfterSceneLoad.</summary>
        Late = 3,
    }
}
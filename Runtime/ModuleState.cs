namespace AceLand.Lifecycle
{
    public enum ModuleState
    {
        /// <summary>Only appears during the editor's static scan; has not entered runtime yet.</summary>
        Declared = 0,
        Registered = 1,
        Initializing = 2,
        Ready = 3,
        /// <summary>Initialization threw an exception.</summary>
        Failed = 4,
        /// <summary>Skipped because a dependency never came up / was not registered.</summary>
        Skipped = 5,
        ShutDown = 6,
    }
}
using AceLand.Lifecycle;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AceLand.Sample.LifeCycle.Scripts
{
    /// <summary>
    /// When player quit game by selecting from menu or pressing x button,
    ///     Lifecycle will enter Quit Pipeline process.
    /// At this moment, player should not touch anything.
    /// Use ApplicationQuitPipeline.StatusChanged event for catching quit status.
    /// 
    /// A good design is to enable filter or load quit scene on player quit.
    /// A confirmation from player is always this best. 
    /// </summary>
    public class SafeQuitFilterUi : UIBehaviour
    {
        [SerializeField] private GameObject filter;
        
        protected override void Awake()
        {
            ApplicationQuitPipeline.StatusChanged += OnQuit;
            filter?.SetActive(false);
        }

        private void OnQuit(string status)
        {
            // empty status means quit status not started
            if (status == null || string.IsNullOrEmpty(status))
                return;
            
            Debug.Log("Application Start Quit ... enable filter", this);
            filter?.SetActive(true);
        }
    }
}
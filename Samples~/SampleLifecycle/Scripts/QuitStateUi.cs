using AceLand.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class QuitStateUi : UIBehaviour
    {
        [SerializeField] private TextMeshProUGUI stateText;

        protected override void Awake()
        {
            ApplicationQuitPipeline.StatusChanged += UpdateState;
            UpdateState(ApplicationQuitPipeline.CurrentStatus);
        }

        private void UpdateState(string state)
        {
            stateText?.SetText(state);
        }
    }
}
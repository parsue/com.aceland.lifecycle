using AceLand.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class ModuleStateUi : UIBehaviour
    {
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI stateText;
        [SerializeField] private TextMeshProUGUI errorText;

        protected override void Awake()
        {
            ModuleRegistry.ModuleStateChanged += OnStateChanged;
        }

        private void OnStateChanged(ModuleEntry entry)
        {
            nameText?.SetText(entry.DisplayName);
            stateText?.SetText(entry.State.ToString());
            errorText?.SetText(entry.Error);
        }
    }
}
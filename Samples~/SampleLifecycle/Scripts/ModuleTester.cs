using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public abstract class ModuleTester : UIBehaviour
    {
        [SerializeField] private Button button;

        protected override void OnEnable()
        {
            button?.onClick.AddListener(RunTest);
        }

        protected override void OnDisable()
        {
            button?.onClick.RemoveListener(RunTest);
        }

        protected abstract void RunTest();
    }
}
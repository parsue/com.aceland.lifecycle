using System;
using AceLand.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AceLand.Sample.LifeCycle.Scripts
{
    /// <summary>
    /// If there is long processing in ModulePhase.Late (RuntimeInitializeLoadType.AfterSceneLoad),
    ///     first scene is started but planed modules are not completed.
    /// Use ModuleRegistry.WhenInitialized() for catching the complete moment.
    ///
    /// In your game, you may create a logo slide show and other stuff as first scene.
    /// Slide finished or player skipped, load a Load Scene with this WhenInitialized().
    /// This scenario provides a smooth getting in for player and game system.
    /// </summary>
    public class InitialFilterUi : UIBehaviour
    {
        [SerializeField] private GameObject filter;
        [SerializeField] private TextMeshProUGUI message;
        
        private IDisposable _sub;
        
        protected override void Awake()
        {
            filter?.SetActive(true);
            message?.SetText("loading ... ");
            _sub = ModuleRegistry.WhenInitialized(OnInitialCompleted);
            ModuleRegistry.ModuleStateChanged += OnStateChanged;
        }

        protected override void OnDestroy()
        {
            _sub?.Dispose();
            ModuleRegistry.ModuleStateChanged -= OnStateChanged;
        }

        private void OnStateChanged(ModuleEntry obj)
        {
            message?.SetText($"{obj.State} ... {obj.DisplayName}");
        }

        private void OnInitialCompleted()
        {
            Debug.Log("Modules initialization completed");
            filter?.SetActive(false);
        }
    }
}
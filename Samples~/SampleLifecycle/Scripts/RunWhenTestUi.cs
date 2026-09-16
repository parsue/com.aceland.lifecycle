using System;
using AceLand.Lifecycle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunWhenTestUi : UIBehaviour
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;

        [SerializeField] private Button runButton;
        [SerializeField] private Button triggerButton;
        [SerializeField] private Button cancelButton;

        private IDisposable _disposable;
        private bool _trigger;

        protected override void Awake()
        {
            runButton.interactable = false;
            triggerButton.interactable = false;
            cancelButton.interactable = false;
        }

        protected override void OnEnable()
        {
            runButton.onClick.AddListener(Run);
            triggerButton.onClick.AddListener(Trigger);
            cancelButton.onClick.AddListener(Cancel);
            
            _disposable = null;

            runButton.interactable = true;
            triggerButton.interactable = false;
            cancelButton.interactable = false;
        }

        protected override void OnDisable()
        {
            runButton.onClick.RemoveListener(Run);
            triggerButton.onClick.RemoveListener(Trigger);
            cancelButton.onClick.RemoveListener(Cancel);
            
            runButton.interactable = false;
            triggerButton.interactable = false;
            cancelButton.interactable = false;
            
            _disposable?.Dispose();
        }

        private void Run()
        {
            _trigger = false;
            runButton.interactable = false;
            triggerButton.interactable = true;
            cancelButton.interactable = true;

            Debug.Log("Run When: pending ...");
            _disposable = LifecycleFrame.RunWhen(() => _trigger, () =>
                {
                    Debug.Log("Run When: triggered.");

                    runButton.interactable = true;
                    triggerButton.interactable = false;
                    cancelButton.interactable = false;
                },
                point: pointUi.CurrentPoint
            );
        }

        private void Trigger()
        {
            _trigger = true;
        }

        private void Cancel()
        {
            _disposable?.Dispose();
            Debug.Log("Run When: canceled.");

            _disposable = null;
            runButton.interactable = true;
            triggerButton.interactable = false;
            cancelButton.interactable = false;
        }
    }
}
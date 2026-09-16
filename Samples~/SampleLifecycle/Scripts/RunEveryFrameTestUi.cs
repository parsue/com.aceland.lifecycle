using System;
using AceLand.Lifecycle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunEveryFrameTestUi : UIBehaviour
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;

        [SerializeField] private Button runButton;
        [SerializeField] private Button cancelButton;

        private IDisposable _disposable;

        protected override void Awake()
        {
            runButton.interactable = false;
            cancelButton.interactable = false;
        }

        protected override void OnEnable()
        {
            runButton.onClick.AddListener(Run);
            cancelButton.onClick.AddListener(Cancel);
            
            _disposable = null;

            runButton.interactable = true;
            cancelButton.interactable = false;
        }

        protected override void OnDisable()
        {
            runButton.onClick.RemoveListener(Run);
            cancelButton.onClick.RemoveListener(Cancel);
            
            runButton.interactable = false;
            cancelButton.interactable = false;
            
            _disposable?.Dispose();
        }

        private void Run()
        {
            runButton.interactable = false;
            cancelButton.interactable = true;

            Debug.Log($"Run Every Frame called: {Time.frameCount}");
            _disposable = LifecycleFrame.RunEveryFrame(() =>
                Debug.Log($"Run Every Frame: {Time.frameCount}"),
                point: pointUi.CurrentPoint
            );
        }

        private void Cancel()
        {
            _disposable?.Dispose();
            Debug.Log("Run Every Frame: canceled.");

            _disposable = null;
            runButton.interactable = true;
            cancelButton.interactable = false;
        }
    }
}
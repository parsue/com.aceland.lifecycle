using System;
using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunDelaySeconds : ModuleTester
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;

        private int _id;
        
        protected override void RunTest()
        {
            _id++;
            Debug.Log($"Run Delay 5s ({_id}) called: {DateTime.Now:mm:ss}");

            LifecycleFrame.RunDelayed(() =>
                {
                    Debug.Log($"Run Delay 5s ({_id}) completed: {DateTime.Now:mm:ss}");
                },
                point: pointUi.CurrentPoint,
                seconds: 5f,
                unscaled: true
            );
        }
    }
}
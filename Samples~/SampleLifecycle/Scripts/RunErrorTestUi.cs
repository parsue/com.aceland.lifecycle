using System;
using AceLand.Lifecycle;
using TMPro;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunErrorTestUi : ModuleTester
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;
        
        private int _id;
        
        protected override void RunTest()
        {
            _id++;
            Debug.Log($"Run Error ({_id}) called: {Time.frameCount}");

            LifecycleFrame.RunDelayed(() =>
                throw new Exception($"Run Error ({_id}) completed"),
                point: pointUi.CurrentPoint,
                seconds: 5f,
                unscaled: true
            );
        }
    }
}
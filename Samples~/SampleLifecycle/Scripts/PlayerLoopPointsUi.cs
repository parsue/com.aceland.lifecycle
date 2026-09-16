using System;
using System.Linq;
using AceLand.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class PlayerLoopPointsUi : UIBehaviour
    {
        [SerializeField] private TMP_Dropdown dropdown;

        public PlayerLoopPoint CurrentPoint => (PlayerLoopPoint)dropdown.value;
        
        protected override void Awake()
        {
            var names = Enum.GetNames(typeof(PlayerLoopPoint)).ToList();
            
            dropdown.ClearOptions();
            dropdown.AddOptions(names);

            dropdown.value = (int)PlayerLoopPoint.Update;
        }
    }
}
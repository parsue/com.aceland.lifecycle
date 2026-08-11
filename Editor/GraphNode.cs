using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceLand.Lifecycle.Editor
{
    internal sealed class GraphNode
    {
        public Type Id;
        public string DisplayName;
        public string Namespace;
        public ModulePhase Phase;
        public int Order;
        public Type[] DependsOn = Type.EmptyTypes;
        public bool IsAsync;
        public bool AutoRegister;
        
        public bool AssemblyOptedIn = true;
        public string AssemblyName;

        public ModuleState State = ModuleState.Declared;
        public string Error;
        public double InitMilliseconds;
        public int SortIndex = -1;

        /// <summary>exist in execute bug no [LifecycleModule]（manual register）。</summary>
        public bool LiveOnly;

        public readonly List<GraphNode> Dependents = new List<GraphNode>();
        public Rect Rect;
    }

    internal sealed class PhaseBand
    {
        public ModulePhase Phase;
        public Rect Rect;
        public int Count;
    }

    internal sealed class GraphData
    {
        public readonly List<GraphNode> Nodes = new List<GraphNode>();
        public readonly List<PhaseBand> Bands = new List<PhaseBand>();
        public readonly List<string> Issues = new List<string>();
        public readonly HashSet<Type> CycleMembers = new HashSet<Type>();
        public Rect Bounds;
        public bool IsLive;
    }
}
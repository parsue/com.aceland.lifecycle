using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AceLand.Lifecycle.Tests.Editor
{
    public class ModuleSorterTests
    {
        class A : ModuleBase { }
        class B : ModuleBase { }
        class C : ModuleBase { }
        class D : ModuleBase { }

        static ModuleEntry E(Type id, params Type[] deps) => new ModuleEntry
        {
            Id = id, Module = (IModule)Activator.CreateInstance(id),
            Phase = ModulePhase.Core, DependsOn = deps ?? Type.EmptyTypes,
        };

        [Test]
        public void Sorts_Dependencies_First()
        {
            var c = E(typeof(C), typeof(B));
            var b = E(typeof(B), typeof(A));
            var a = E(typeof(A));

            var batch = new List<ModuleEntry> { c, b, a };
            var all = new Dictionary<Type, ModuleEntry>
            {
                { typeof(A), a }, { typeof(B), b }, { typeof(C), c }
            };

            var sorted = ModuleSorter.Sort(batch, all, new List<string>());

            Assert.AreEqual(typeof(A), sorted[0].Id);
            Assert.AreEqual(typeof(B), sorted[1].Id);
            Assert.AreEqual(typeof(C), sorted[2].Id);
        }

        [Test]
        public void Reports_Circular_Dependency()
        {
            var a = E(typeof(A), typeof(B));
            var b = E(typeof(B), typeof(A));

            var all = new Dictionary<Type, ModuleEntry> { { typeof(A), a }, { typeof(B), b } };
            var issues = new List<string>();

            ModuleSorter.Sort(new List<ModuleEntry> { a, b }, all, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("Circular")));
        }

        [Test]
        public void Reports_Missing_Dependency()
        {
            var a = E(typeof(A), typeof(C));
            var all = new Dictionary<Type, ModuleEntry> { { typeof(A), a } };
            var issues = new List<string>();

            ModuleSorter.Sort(new List<ModuleEntry> { a }, all, issues);

            Assert.IsTrue(issues.Exists(i => i.Contains("not registered")));
        }

        [Test]
        public void Assigns_Levels_Along_Dependency_Chain()
        {
            var c = E(typeof(C), typeof(B));
            var b = E(typeof(B), typeof(A));
            var a = E(typeof(A));

            var all = new Dictionary<Type, ModuleEntry>
            {
                { typeof(A), a }, { typeof(B), b }, { typeof(C), c }
            };

            ModuleSorter.Sort(new List<ModuleEntry> { c, b, a }, all, new List<string>());

            Assert.AreEqual(0, a.Level);
            Assert.AreEqual(1, b.Level);
            Assert.AreEqual(2, c.Level);
        }

        [Test]
        public void Independent_Modules_Share_Level_Zero()
        {
            var a = E(typeof(A));
            var b = E(typeof(B));

            var all = new Dictionary<Type, ModuleEntry> { { typeof(A), a }, { typeof(B), b } };

            ModuleSorter.Sort(new List<ModuleEntry> { a, b }, all, new List<string>());

            Assert.AreEqual(0, a.Level);
            Assert.AreEqual(0, b.Level);
        }

        [Test]
        public void Diamond_Dependency_Levels()
        {
            // A → B, A → C, (B,C) → D  ⇒  A:0, B:1, C:1, D:2
            var a = E(typeof(A));
            var b = E(typeof(B), typeof(A));
            var c = E(typeof(C), typeof(A));
            var d = E(typeof(D), typeof(B), typeof(C));

            var all = new Dictionary<Type, ModuleEntry>
            {
                { typeof(A), a }, { typeof(B), b }, { typeof(C), c }, { typeof(D), d }
            };

            ModuleSorter.Sort(new List<ModuleEntry> { d, c, b, a }, all, new List<string>());

            Assert.AreEqual(0, a.Level);
            Assert.AreEqual(1, b.Level);
            Assert.AreEqual(1, c.Level);
            Assert.AreEqual(2, d.Level);
        }
    }
}
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using CardChat.UI;
using UIE = UnityEngine.UIElements;

namespace CardChat.UI.Tests.Editor
{
    /// <summary>EditMode coverage for the scanner's change-detection / event-firing behaviour.</summary>
    [TestFixture]
    public class MenuStateDiffTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private Canvas NewCanvas(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<Canvas>();
        }

        private void AddButton(string label, Transform parent)
        {
            var go = new GameObject(label + "Button");
            _spawned.Add(go);
            go.transform.SetParent(parent, false);
            go.AddComponent<Button>();
            var t = new GameObject("Label");
            _spawned.Add(t);
            t.transform.SetParent(go.transform, false);
            t.AddComponent<Text>().text = label;
        }

        private MenuStateScanner NewScanner(Canvas canvas)
        {
            var go = new GameObject("Scanner");
            _spawned.Add(go);
            var scanner = go.AddComponent<MenuStateScanner>();
            scanner.uiDocumentsToScan = new List<UIE.UIDocument>();
            scanner.canvasesToScan = new List<Canvas> { canvas };
            scanner.scanIntervalSeconds = 0.5f;
            return scanner;
        }

        [Test]
        public void OnMenuStateChanged_FiredOnFirstScan()
        {
            var canvas = NewCanvas("C");
            AddButton("Play", canvas.transform);
            var scanner = NewScanner(canvas);

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;
            scanner.ForceScan();

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void OnMenuStateChanged_NotFiredIfUnchanged()
        {
            var canvas = NewCanvas("C");
            AddButton("Play", canvas.transform);
            var scanner = NewScanner(canvas);

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;
            scanner.ForceScan();
            scanner.ForceScan();

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void OnMenuStateChanged_FiredWhenButtonAdded()
        {
            var canvas = NewCanvas("C");
            AddButton("Play", canvas.transform);
            var scanner = NewScanner(canvas);

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;
            scanner.ForceScan();
            AddButton("Quit", canvas.transform);
            scanner.ForceScan();

            Assert.AreEqual(2, fired);
        }

        [Test]
        public void OnMenuStateChanged_EventPayloadMatchesBuildTextTree()
        {
            var canvas = NewCanvas("C");
            AddButton("Play", canvas.transform);
            var scanner = NewScanner(canvas);

            string payload = null;
            scanner.OnMenuStateChanged += t => payload = t;
            scanner.ForceScan();

            Assert.AreEqual(scanner.BuildTextTree(), payload);
        }

        [Test]
        public void ForceScan_IgnoresInterval()
        {
            var canvas = NewCanvas("C");
            AddButton("Play", canvas.transform);
            var scanner = NewScanner(canvas);
            scanner.scanIntervalSeconds = 999f;

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;
            scanner.ForceScan();

            Assert.AreEqual(1, fired);
        }
    }
}

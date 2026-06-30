using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using CardChat.UI;
using UIE = UnityEngine.UIElements;

namespace CardChat.UI.Tests.Editor
{
    /// <summary>
    /// EditMode coverage for <see cref="MenuStateScanner.BuildTextTree"/> against synthetic uGUI
    /// hierarchies, plus the UI Toolkit traversal via the internal collection helper.
    /// </summary>
    [TestFixture]
    public class MenuStateScannerTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private MenuStateScanner NewScanner()
        {
            var go = new GameObject("Scanner");
            _spawned.Add(go);
            var scanner = go.AddComponent<MenuStateScanner>();
            scanner.canvasesToScan = new List<Canvas>();
            scanner.uiDocumentsToScan = new List<UIE.UIDocument>();
            return scanner;
        }

        private Canvas NewCanvas(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<Canvas>();
        }

        private GameObject NewText(string name, string value, Transform parent)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            go.transform.SetParent(parent, false);
            go.AddComponent<Text>().text = value;
            return go;
        }

        private GameObject NewButton(string label, Transform parent)
        {
            var go = new GameObject(label + "Button");
            _spawned.Add(go);
            go.transform.SetParent(parent, false);
            go.AddComponent<Button>();
            NewText("Label", label, go.transform);
            return go;
        }

        private static string FieldValue(string tree, string field)
        {
            foreach (var line in tree.Split('\n'))
                if (line.StartsWith(field + ": "))
                    return line.Substring(field.Length + 2);
            return null;
        }

        [Test]
        public void BuildTextTree_EmptyScene_ReturnsEmptyOrDefault()
        {
            var scanner = NewScanner();
            string tree = scanner.BuildTextTree();
            Assert.IsNotNull(tree);
            StringAssert.Contains("MENU USER:", tree);
            StringAssert.Contains("ACTIONS:", tree);
        }

        [Test]
        public void BuildTextTree_SingleCanvas_IncludesText()
        {
            var canvas = NewCanvas("MainCanvas");
            NewText("Body", "Welcome", canvas.transform);
            var scanner = NewScanner();
            scanner.canvasesToScan.Add(canvas);

            StringAssert.Contains("Welcome", scanner.BuildTextTree());
        }

        [Test]
        public void BuildTextTree_ButtonsAreCollected()
        {
            var canvas = NewCanvas("MainCanvas");
            NewButton("Play", canvas.transform);
            NewButton("Settings", canvas.transform);
            NewButton("Quit", canvas.transform);
            var scanner = NewScanner();
            scanner.canvasesToScan.Add(canvas);

            string buttons = FieldValue(scanner.BuildTextTree(), "BUTTONS");
            StringAssert.Contains("Play", buttons);
            StringAssert.Contains("Settings", buttons);
            StringAssert.Contains("Quit", buttons);
        }

        [Test]
        public void BuildTextTree_InactiveObjectsExcluded()
        {
            var canvas = NewCanvas("MainCanvas");
            NewButton("Active", canvas.transform);
            var inactive = NewButton("Hidden", canvas.transform);
            inactive.SetActive(false);
            var scanner = NewScanner();
            scanner.canvasesToScan.Add(canvas);

            string buttons = FieldValue(scanner.BuildTextTree(), "BUTTONS");
            StringAssert.Contains("Active", buttons);
            StringAssert.DoesNotContain("Hidden", buttons);
        }

        [Test]
        public void BuildTextTree_TitleTextFromNamedObject()
        {
            var canvas = NewCanvas("MainCanvas");
            NewText("Title", "Main Menu", canvas.transform);
            var scanner = NewScanner();
            scanner.canvasesToScan.Add(canvas);

            string title = FieldValue(scanner.BuildTextTree(), "TITLE");
            StringAssert.Contains("Main Menu", title);
        }

        [Test]
        public void BuildTextTree_ContentsTruncatedAt512()
        {
            var canvas = NewCanvas("MainCanvas");
            NewText("Body", new string('x', 600), canvas.transform);
            var scanner = NewScanner();
            scanner.canvasesToScan.Add(canvas);

            string contents = FieldValue(scanner.BuildTextTree(), "CONTENTS");
            Assert.LessOrEqual(contents.Length, MenuStateScanner.MaxFieldLength);
        }

        [Test]
        public void BuildTextTree_MenuChatCanvasExcluded()
        {
            var menuChatCanvas = NewCanvas("MenuChatUI");
            NewText("Secret", "SecretMenuContent", menuChatCanvas.transform);
            var scanner = NewScanner();
            scanner.excludeMenuChatCanvas = true;
            scanner.canvasesToScan.Add(menuChatCanvas);

            StringAssert.DoesNotContain("SecretMenuContent", scanner.BuildTextTree());
        }

        [Test]
        public void CollectFromVisualElement_ButtonsAndTitleCollected()
        {
            var root = new UIE.VisualElement();
            var title = new UIE.Label("Settings");
            title.AddToClassList("title");
            root.Add(title);
            root.Add(new UIE.Button { text = "Play" });
            root.Add(new UIE.Label("Some body text"));

            var buttons = new List<string>();
            var contents = new List<string>();
            var labels = new HashSet<string>();
            string foundTitle = null;
            MenuStateScanner.CollectFromVisualElement(root, false, buttons, contents, labels, ref foundTitle);

            CollectionAssert.Contains(buttons, "Play");
            Assert.AreEqual("Settings", foundTitle);
            CollectionAssert.Contains(contents, "Some body text");
        }
    }
}

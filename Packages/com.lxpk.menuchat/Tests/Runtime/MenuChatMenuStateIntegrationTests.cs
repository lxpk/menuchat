using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using CardChat.UI;
using UIE = UnityEngine.UIElements;

namespace CardChat.UI.Tests.Runtime
{
    /// <summary>
    /// PlayMode coverage for the parts of the menustate pipeline that need a live frame loop:
    /// the automatic poll in <see cref="MenuStateScanner.Update"/> and the connection gating.
    /// </summary>
    /// <remarks>
    /// Over-the-wire delivery and the "ack is not shown in the chat log" assertion require a real
    /// WebSocket server and a fully bound UI (PanelSettings + UXML); per MENUCHAT.md that end-to-end
    /// validation against HERMES is deferred and exercised manually in the sample scene.
    /// </remarks>
    public class MenuChatMenuStateIntegrationTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned)
                if (o != null) Object.Destroy(o);
            _spawned.Clear();
        }

        private Canvas NewCanvasWithButton(string label)
        {
            var canvasGo = new GameObject("Canvas");
            _spawned.Add(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();

            var btnGo = new GameObject("Button");
            _spawned.Add(btnGo);
            btnGo.transform.SetParent(canvasGo.transform, false);
            btnGo.AddComponent<Button>();

            var textGo = new GameObject("Label");
            _spawned.Add(textGo);
            textGo.transform.SetParent(btnGo.transform, false);
            textGo.AddComponent<Text>().text = label;
            return canvas;
        }

        private MenuStateScanner NewScanner(Canvas canvas, float interval)
        {
            var go = new GameObject("Scanner");
            _spawned.Add(go);
            var scanner = go.AddComponent<MenuStateScanner>();
            scanner.uiDocumentsToScan = new List<UIE.UIDocument>();
            scanner.canvasesToScan = new List<Canvas> { canvas };
            scanner.scanIntervalSeconds = interval;
            return scanner;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float elapsed = 0f;
            while (!condition() && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Scanner_AutoPoll_FiresOnStateChange()
        {
            var canvas = NewCanvasWithButton("Play");
            var scanner = NewScanner(canvas, 0.1f);
            scanner.scanOnlyWhenConnected = false;

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;

            yield return WaitUntil(() => fired > 0, 2f);
            Assert.GreaterOrEqual(fired, 1, "Automatic poll should fire OnMenuStateChanged once for the initial state.");
        }

        [UnityTest]
        public IEnumerator Scanner_NotFired_WhenConnectionGateClosed()
        {
            var canvas = NewCanvasWithButton("Play");
            var scanner = NewScanner(canvas, 0.1f);
            scanner.scanOnlyWhenConnected = true;
            scanner.ConnectionStatusProvider = () => false;

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;

            yield return WaitUntil(() => fired > 0, 1f);
            Assert.AreEqual(0, fired, "Scanner must not fire while the connection gate reports closed.");
        }

        [UnityTest]
        public IEnumerator Scanner_Fired_WhenConnectionGateOpens()
        {
            var canvas = NewCanvasWithButton("Play");
            var scanner = NewScanner(canvas, 0.1f);
            scanner.scanOnlyWhenConnected = true;
            bool open = false;
            scanner.ConnectionStatusProvider = () => open;

            int fired = 0;
            scanner.OnMenuStateChanged += _ => fired++;

            yield return WaitUntil(() => fired > 0, 0.5f);
            Assert.AreEqual(0, fired);

            open = true;
            yield return WaitUntil(() => fired > 0, 2f);
            Assert.GreaterOrEqual(fired, 1, "Scanner should fire once the connection gate opens.");
        }

        [UnityTest]
        public IEnumerator HiddenMenustate_SendWhenDisconnected_DoesNotThrow()
        {
            var docGo = new GameObject("MenuChatHost");
            _spawned.Add(docGo);
            docGo.AddComponent<UIE.UIDocument>();
            var ui = docGo.AddComponent<MenuChatUIUXml>();
            ui.connectionMode = MenuChatUIUXml.ConnectionMode.RemoteWebSocket;
            ui.connectOnStart = false;
            yield return null;

            // No socket open — must be a safe no-op, not an exception.
            Assert.DoesNotThrow(() => ui.SendHiddenMenuStateForTest("MENU USER: tester\nTITLE: x"));
        }

        [UnityTest]
        public IEnumerator QueuedAck_ProcessedWithoutError()
        {
            var docGo = new GameObject("MenuChatHost");
            _spawned.Add(docGo);
            docGo.AddComponent<UIE.UIDocument>();
            var ui = docGo.AddComponent<MenuChatUIUXml>();
            ui.connectionMode = MenuChatUIUXml.ConnectionMode.RemoteWebSocket;
            ui.connectOnStart = false;
            yield return null;

            int before = ui.ChatLogLineCountForTest;
            string ack = "{\"object\": \"hidden_context.queued\", \"character\": \"Elowyn\", \"interaction_id\": \"abc\", \"wrapped\": \"...\"}";
            Assert.DoesNotThrow(() => ui.ProcessIncomingForTest(ack));
            Assert.AreEqual(before, ui.ChatLogLineCountForTest, "Queued ack must not add a visible chat log line.");
        }
    }
}

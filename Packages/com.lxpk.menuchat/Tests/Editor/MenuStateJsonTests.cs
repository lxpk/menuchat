using NUnit.Framework;
using UnityEngine;
using CardChat.UI;

namespace CardChat.UI.Tests.Editor
{
    /// <summary>EditMode coverage for the hidden menustate JSON serializer (<see cref="MenuStateJson"/>).</summary>
    [TestFixture]
    public class MenuStateJsonTests
    {
        private const string Content = "MENU USER: player1\nTITLE: Main Menu";

        [Test]
        public void SerializeHiddenMenustate_ContainsHiddenContextKey()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, "menuchat", null);
            StringAssert.Contains("\"hidden_context\"", json);
        }

        [Test]
        public void SerializeHiddenMenustate_TypeIsMenustate()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, "menuchat", null);
            StringAssert.Contains("\"type\": \"menustate\"", json);
        }

        [Test]
        public void SerializeHiddenMenustate_ContentIsEscaped()
        {
            string json = MenuStateJson.SerializeHiddenMenustate("line1\nline2 \"quote\"", "menuchat", null);
            StringAssert.Contains("\\n", json);
            StringAssert.Contains("\\\"", json);
            // Raw newline / unescaped quote must not appear inside the content value.
            Assert.IsFalse(json.Contains("line1\nline2"));
        }

        [Test]
        public void SerializeHiddenMenustate_ChatModeIsMenuchat()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, "menuchat", null);
            StringAssert.Contains("\"chat_mode\": \"menuchat\"", json);
        }

        [Test]
        public void SerializeHiddenMenustate_DefaultsChatModeWhenEmpty()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, null, null);
            StringAssert.Contains("\"chat_mode\": \"menuchat\"", json);
        }

        [Test]
        public void SerializeHiddenMenustate_ModelOmittedWhenEmpty()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, "menuchat", null);
            StringAssert.DoesNotContain("\"model\"", json);
        }

        [Test]
        public void SerializeHiddenMenustate_ModelIncludedWhenSet()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, "menuchat", "Elowyn");
            StringAssert.Contains("\"model\": \"Elowyn\"", json);
        }

        [Test]
        public void SerializeHiddenMenustate_EmptyContentRejected()
        {
            Assert.IsNull(MenuStateJson.SerializeHiddenMenustate(null, "menuchat", null));
            Assert.IsNull(MenuStateJson.SerializeHiddenMenustate("", "menuchat", null));
        }

        [Test]
        public void SerializeHiddenMenustate_RoundTrip()
        {
            string json = MenuStateJson.SerializeHiddenMenustate(Content, "menuchat", "Elowyn");
            var parsed = JsonUtility.FromJson<MenuStateWsMessage>(json);

            Assert.AreEqual("menustate", parsed.hidden_context.type);
            Assert.AreEqual(Content, parsed.hidden_context.content);
            Assert.AreEqual("menuchat", parsed.chat_mode);
            Assert.AreEqual("Elowyn", parsed.model);
        }

        [Test]
        public void IsHiddenContextQueuedAck_RecognisesAck()
        {
            string ack = "{\"object\": \"hidden_context.queued\", \"character\": \"Elowyn\", \"interaction_id\": \"abc\", \"wrapped\": \"<hidden type=menustate>...</menustate>\"}";
            Assert.IsTrue(MenuStateJson.IsHiddenContextQueuedAck(ack));
        }

        [Test]
        public void IsHiddenContextQueuedAck_IgnoresNormalContent()
        {
            string normal = "{\"content\": \"hello there\"}";
            Assert.IsFalse(MenuStateJson.IsHiddenContextQueuedAck(normal));
        }
    }
}

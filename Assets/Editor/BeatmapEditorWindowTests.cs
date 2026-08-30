using NUnit.Framework;
using UnityEngine;

namespace MusicalSprite.Editor
{
    public class BeatmapEditorWindowTests
    {
        [System.Serializable]
        private class Dump
        {
            public float bpm;
            public NoteData[] notes;
            public float[] markers;
        }

        private BeatmapSO asset;

        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<BeatmapSO>();
            asset.bpm = 128f;
            asset.markers = new[] { 1.25f };
            asset.notes = new[]
            {
                new NoteData
                {
                    time = 2.5f,
                    lane = 1,
                    side = 0,
                    type = NoteData.NoteType.Hold,
                    chainTapCount = 3,
                    holdDuration = 1f,
                    holdEndLane = 2,
                    holdLanes = new[] { 1, 2 },
                    holdTimes = new[] { 2.5f, 3.5f },
                    holdLaneSpans = new[] { 1, 2 }
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void TextMirrorMustMatchEverySerializedBeatmapField()
        {
            string json = JsonUtility.ToJson(new Dump { bpm = asset.bpm, notes = asset.notes, markers = asset.markers });
            Assert.That(BeatmapEditorWindow.IsBeatmapTextConsistent(asset, json, out _), Is.True);

            AssertMismatch(json, dump => dump.bpm++);
            AssertMismatch(json, dump => dump.markers[0]++);
            AssertMismatch(json, dump => dump.notes[0].time++);
            AssertMismatch(json, dump => dump.notes[0].lane++);
            AssertMismatch(json, dump => dump.notes[0].type = NoteData.NoteType.Tap);
            AssertMismatch(json, dump => dump.notes[0].holdLaneSpans[1] = 1);
            AssertMismatch(json, dump => dump.notes = new NoteData[0]);
            AssertMismatch(json, dump => dump.notes = new[] { dump.notes[0], dump.notes[0] });
        }

        [Test]
        public void MatchingLegacyNullLaneSpansAreValid()
        {
            asset.notes[0].holdLaneSpans = null;
            string json = JsonUtility.ToJson(new Dump { bpm = asset.bpm, notes = asset.notes, markers = asset.markers });
            Assert.That(BeatmapEditorWindow.IsBeatmapTextConsistent(asset, json, out _), Is.True);
        }

        [Test]
        public void MissingLinkedLaneSpansStayTwoLaneDuringEditorConversions()
        {
            Assert.That(BeatmapEditorWindow.ResolveLaneSpan(NoteData.NoteType.Linked, null, 0), Is.EqualTo(2));
            Assert.That(BeatmapEditorWindow.ResolveLaneSpan(NoteData.NoteType.Linked, new[] { 1 }, 1), Is.EqualTo(2));
            Assert.That(BeatmapEditorWindow.ResolveLaneSpan(NoteData.NoteType.Hold, null, 0), Is.EqualTo(1));
            Assert.That(BeatmapEditorWindow.ResolveLaneSpan(NoteData.NoteType.Linked, new[] { 1 }, 0), Is.EqualTo(1));
        }

        private void AssertMismatch(string sourceJson, System.Action<Dump> mutate)
        {
            var dump = JsonUtility.FromJson<Dump>(sourceJson);
            mutate(dump);
            Assert.That(
                BeatmapEditorWindow.IsBeatmapTextConsistent(asset, JsonUtility.ToJson(dump), out _),
                Is.False);
        }
    }
}

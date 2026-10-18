#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopCity.Editor
{
    /// <summary>Pure Edit Mode catalogue selection and real media reconciliation; no decoding, network or saves.</summary>
    public static class SlopCityFeaturedSelectionSmokeTest
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string PreviousCreator = "machine_publishers", PreviousId = "7696449957347331358";
        private const string RequestedCreator = "1984drh", RequestedId = "7696938454575238430";
        private static int checks;
        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks++; }
        private static object Get(object owner, string field) => owner.GetType().GetField(field, Fields).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Fields).SetValue(owner, value);
        private static SlopCityTikTokVideo Video(string creator, string id) => new SlopCityTikTokVideo
        {
            creator = creator, id = id, kind = "video", title = "Real identity " + id,
            sourceUrl = "https://www.tiktok.com/@" + creator + "/video/" + id,
            bundledVideoFile = id + ".mp4", bundledThumbnailFile = id + "_thumb.jpg"
        };
        private static SlopCityTikTokCatalog Catalog(string creator, string id, params SlopCityTikTokVideo[] videos)
            => new SlopCityTikTokCatalog { featuredCreator = creator, featuredPostId = id, videos = videos };

        // Dedicated batch Editor with -quit. Never enters Play Mode or reads PlayerPrefs/persistent media.
        public static void Run()
        {
            if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Featured selection checks require batch Edit Mode.");
            checks = 0;
            var receipt = new Receipt { startedAtUtc = DateTime.UtcNow.ToString("O") };
            GameObject fixture = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                fixture = new GameObject("Featured_Selection_Offline_Fixture");
                var media = fixture.AddComponent<SlopCityCityMedia>();
                media.enabled = false; // No production Start, frame updates, network, decoder or persistence.
                var board = new GameObject(SlopCityFeaturedBillboard.RootName);
                board.transform.SetParent(fixture.transform, false);
                board.AddComponent<SlopCityFeaturedBillboard>(); // No Ensure/art/material allocation needed.
                var picture = new GameObject("Preserved_Physical_Picture");
                picture.transform.SetParent(board.transform, false);
                Renderer renderer = picture.AddComponent<MeshRenderer>();
                var screen = picture.AddComponent<SlopCityCityScreen>();
                screen.Configure(renderer, 0, 14f);
                screen.ConfigureFeaturedPost(PreviousCreator, PreviousId, -1f, 0f);
                Transform physicalParent = picture.transform.parent;
                Vector3 physicalPosition = picture.transform.localPosition, physicalScale = picture.transform.localScale;
                int componentCount = picture.GetComponents<Component>().Length;

                var channels = (Array)Get(media, "channels");
                Type channelType = typeof(SlopCityCityMedia).GetNestedType("Channel", BindingFlags.NonPublic);
                for (int index = 0; index < channels.Length; index++)
                { object channel = Activator.CreateInstance(channelType, true); Set(channel, "index", index); channels.SetValue(channel, index); }
                Type stateType = typeof(SlopCityCityMedia).GetNestedType("ScreenState", BindingFlags.NonPublic);
                object state = Activator.CreateInstance(stateType, true); Set(state, "screen", screen);
                ((IList)Get(media, "displays")).Add(state);
                ((IDictionary)Get(media, "displayLookup")).Add(screen, state);
                ((IList)Get(media, "screens")).Add(screen);
                Set(media, "initialized", true);
                int decoderBudget = (int)Get(media, "activeDecoderLimit");
                SlopCityTikTokVideo previous = Video(PreviousCreator, PreviousId), requested = Video(RequestedCreator, RequestedId);
                var videos = new[] { previous, requested };
                Check(SlopCityFeaturedBillboard.Creator == RequestedCreator && SlopCityFeaturedBillboard.PostId == RequestedId,
                    "Compiled fallback is the explicitly requested full creator/post identity.");
                var oldCatalog = JsonUtility.FromJson<SlopCityTikTokCatalog>("{\"version\":2,\"videos\":[]}");
                Check(!oldCatalog.TryGetFeaturedVideo(out _), "Old version-2 catalogue JSON without selection remains compatible.");
                var valid = Catalog(RequestedCreator, RequestedId, videos);
                Check(valid.TryGetFeaturedVideo(out SlopCityTikTokVideo selected) && ReferenceEquals(selected, requested),
                    "Selection resolves the actual validated post object, not an alias or renamed asset.");
                Check(!Catalog("", "", videos).TryGetFeaturedVideo(out _),
                    "A populated legacy catalogue cannot choose a random replacement for the physical pin.");
                var alternate = Catalog(PreviousCreator, PreviousId, videos);
                Check(alternate.TryGetFeaturedVideo(out selected) && ReferenceEquals(selected, previous),
                    "A different explicit selection is honored without inheriting another catalogue's choice.");
                for (int repeat = 0; repeat < 8; repeat++)
                    Check(valid.TryGetFeaturedVideo(out selected) && ReferenceEquals(selected, requested),
                        "Repeated catalogue refreshes retain the exact requested video despite other eligible videos.");
                var roundTrip = JsonUtility.FromJson<SlopCityTikTokCatalog>(JsonUtility.ToJson(valid));
                Check(roundTrip.TryGetFeaturedVideo(out selected) && selected.creator == RequestedCreator && selected.id == RequestedId,
                    "Native cache JSON preserves the optional selection and full post identity.");

                media.ApplyTikTokCatalog(Catalog("", "", videos));
                Check(screen.FeaturedClipKey == PreviousCreator + "/" + PreviousId && ReferenceEquals(Get(state, "post"), previous),
                    "A legacy catalogue retains the existing usable pin and assigns its actual metadata.");
                object slot = channels.GetValue(0);
                Set(slot, "owner", state); Set(slot, "currentVideo", previous); Set(slot, "pendingVideo", previous);
                Set(slot, "ready", true); Set(slot, "resolving", true); Set(slot, "operation", 7); Set(state, "slot", slot);
                int oldGeneration = (int)Get(state, "generation");
                media.ApplyTikTokCatalog(valid);
                Check(screen.FeaturedClipKey == RequestedCreator + "/" + RequestedId && screen.ClipKey == screen.FeaturedClipKey
                    && ReferenceEquals(Get(state, "post"), requested) && screen.ClipTitle.StartsWith("@1984drh /", StringComparison.Ordinal),
                    "Changing from Machine Publishers to 1984DRH reconciles the exact requested identity and truthful title.");
                Check(Get(state, "slot") == null && Get(slot, "owner") == null && Get(slot, "currentVideo") == null
                    && Get(slot, "pendingVideo") == null && !(bool)Get(slot, "resolving") && (int)Get(slot, "operation") > 7
                    && (int)Get(state, "generation") > oldGeneration,
                    "Existing ReleaseSlot/AssignNextPost invalidate old decoder, resolution and image ownership before replacement.");
                Check(screen.FeaturedHoldAtSeconds < 0 && screen.FeaturedHoldSeconds == 0,
                    "A content selection preserves continuous full-clip looping without a timed hold.");
                media.ApplyTikTokCatalog(Catalog("", "", videos));
                Check(screen.FeaturedClipKey == RequestedCreator + "/" + RequestedId,
                    "A later catalogue missing the optional pair retains the last usable pin.");

                var malformed = new List<SlopCityTikTokCatalog>
                {
                    Catalog("unknown_creator", RequestedId, videos),
                    Catalog(PreviousCreator, RequestedId, videos),
                    Catalog(RequestedCreator, "not-a-post-id", videos),
                    Catalog(RequestedCreator, "../7696938454575238430", videos),
                    Catalog(RequestedCreator, "", videos), Catalog("", RequestedId, videos),
                    Catalog(RequestedCreator, "7696938454575238431", videos),
                    Catalog(RequestedCreator, RequestedId, previous),
                    Catalog(RequestedCreator, RequestedId, previous, requested, requested),
                    Catalog("@1984drh", RequestedId, videos)
                };
                var wrongSource = Video(RequestedCreator, RequestedId); wrongSource.sourceUrl = previous.sourceUrl;
                malformed.Add(Catalog(RequestedCreator, RequestedId, previous, wrongSource));
                var wrongFile = Video(RequestedCreator, RequestedId); wrongFile.bundledVideoFile = PreviousId + ".mp4";
                malformed.Add(Catalog(RequestedCreator, RequestedId, previous, wrongFile));
                var photo = Video(RequestedCreator, RequestedId); photo.kind = "slideshow";
                malformed.Add(Catalog(RequestedCreator, RequestedId, previous, photo));
                foreach (SlopCityTikTokCatalog bad in malformed)
                {
                    Check(!bad.TryGetFeaturedVideo(out _), "Malformed, ambiguous, absent or misattributed selection is rejected.");
                    media.ApplyTikTokCatalog(bad);
                    Check(screen.FeaturedClipKey == RequestedCreator + "/" + RequestedId,
                        "Rejected optional content selection never replaces the last usable pin.");
                }
                media.ApplyTikTokCatalog(valid);
                Check(ReferenceEquals(Get(state, "post"), requested) && screen.ClipKey == RequestedCreator + "/" + RequestedId,
                    "A valid complete catalogue can recover after rejected partial or malformed metadata.");
                Check(screen.Display == renderer && picture.transform.parent == physicalParent
                    && picture.transform.localPosition == physicalPosition && picture.transform.localScale == physicalScale
                    && picture.GetComponents<Component>().Length == componentCount && media.ScreenCount == 1
                    && channels.Length == 3 && (int)Get(media, "activeDecoderLimit") == decoderBudget,
                    "Content selection preserves the physical display, transforms, registrations and existing decoder budgets.");
                var pendingOwner = new GameObject("Pre_Start_Catalogue"); pendingOwner.transform.SetParent(fixture.transform);
                var pendingMedia = pendingOwner.AddComponent<SlopCityCityMedia>(); pendingMedia.enabled = false;
                pendingMedia.ApplyTikTokCatalog(valid);
                Check(ReferenceEquals(Get(pendingMedia, "pendingCatalog"), valid),
                    "A catalogue arriving before Start retains its selection in the existing pending publication path.");
                receipt.passed = true;
            }
            catch (Exception error) { receipt.error = error.ToString(); throw; }
            finally
            {
                if (fixture) Object.DestroyImmediate(fixture);
                receipt.checks = checks; receipt.finishedAtUtc = DateTime.UtcNow.ToString("O");
                Directory.CreateDirectory("Builds/Reports");
                File.WriteAllText("Builds/Reports/featured-selection-check.json", JsonUtility.ToJson(receipt, true));
            }
            Debug.Log("SLOP_CITY_FEATURED_SELECTION_CHECK_PASSED checks=" + checks);
        }
        [Serializable] private sealed class Receipt
        {
            public bool passed; public int checks; public string error, startedAtUtc, finishedAtUtc;
            public string scope = "Pure Edit Mode actual catalogue validation/media reconciliation; no network, media decoding, user save access or physical-device acceptance.";
        }
    }
}
#endif

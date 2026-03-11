using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MissionMenuSearch
{
    [BepInPlugin("com.Spiny.MissionMenuSearch", "MissionMenuSearch", "0.0.1")]
    public class MissionMenuSearch : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Logger.LogInfo("MissionMenuSearch loading.");

            Harmony harmony = new Harmony("com.Spiny.MissionMenuSearch");
            harmony.PatchAll();

            Logger.LogInfo("MissionMenuSearch loaded.");
        }
    }

    /// <summary>
    /// Holds per-instance search query state, keyed on MissionSelectPanel instance.
    /// </summary>
    internal static class MissionSelectPanelState
    {
        private static readonly Dictionary<MissionSelectPanel, string> SearchQueries = new Dictionary<MissionSelectPanel, string>();

        public static string GetQuery(MissionSelectPanel panel)
        {
            return SearchQueries.TryGetValue(panel, out string q) ? q : string.Empty;
        }

        public static void SetQuery(MissionSelectPanel panel, string query)
        {
            SearchQueries[panel] = query ?? string.Empty;
        }
    }

    /// <summary>
    /// Postfix on RefreshLists — applies search filter on top of tag filtering.
    /// </summary>
    [HarmonyPatch(typeof(MissionSelectPanel), "RefreshLists")]
    internal static class MissionSelectPanelRefreshPatch
    {
        static void Postfix(MissionSelectPanel __instance)
        {
            string query = MissionSelectPanelState.GetQuery(__instance);
            if (string.IsNullOrWhiteSpace(query)) return;

            var filteredMissions = Traverse.Create(__instance)
                .Field("filteredMissions")
                .GetValue<List<(MissionKey key, MissionQuickLoad mission)>>();

            if (filteredMissions == null) return;

            string q = query.ToLowerInvariant();
            filteredMissions.RemoveAll(x =>
            {
                string name = x.key.Name ?? string.Empty;
                string desc = x.mission.missionSettings.description ?? string.Empty;
                return !name.ToLowerInvariant().Contains(q)
                    && !desc.ToLowerInvariant().Contains(q);
            });

            var missionSelectList = Traverse.Create(__instance)
                .Field("missionSelectList")
                .GetValue<MissionSelectList>();

            missionSelectList?.UpdateList(filteredMissions.Select(x =>
                new MissionSelectListItem.Item(__instance, x.key, x.mission)));
        }
    }

    [HarmonyPatch(typeof(MissionsPicker), "Start")]
    internal static class MissionsPickerSearchPatch
    {
        const float TotalOffset = 36f;

        static void Postfix(MissionsPicker __instance)
        {
            Transform panelTransform = __instance.GetComponentInChildren<MissionSelectPanel>()?.transform;
            if (panelTransform == null)
            {
                MissionMenuSearch.Log.LogWarning("Could not find MissionSelectPanel.");
                return;
            }

            if (panelTransform.Find("MissionSearchField") != null) return;

            MissionSelectPanel selectPanel = panelTransform.GetComponent<MissionSelectPanel>();

            Transform filterTags = panelTransform.Find("Filter Tags");
            Transform background = panelTransform.Find("Background");

            if (filterTags == null || background == null)
            {
                MissionMenuSearch.Log.LogWarning("Could not find Filter Tags or Background.");
                return;
            }

            RectTransform filterTagsRt = filterTags.GetComponent<RectTransform>();
            RectTransform backgroundRt = background.GetComponent<RectTransform>();

            // RectTransform is created by Unity when we instantiate the GameObject, so we get it via GetComponent rather than AddComponent.
            GameObject fieldObj = BuildSearchField(panelTransform, selectPanel);
            if (fieldObj == null) return;

            // Get the RectTransform Unity auto-created AddComponent<RectTransform> is a NONO
            RectTransform fieldRt = fieldObj.GetComponent<RectTransform>();

            // Anchor to top of panel, matching Filter Tags' horizontal anchoring.
            // Fill the existing 0 to -40 gap above Filter Tags.
            fieldRt.anchorMin = new Vector2(0f, 1f);
            fieldRt.anchorMax = new Vector2(1f, 1f);
            fieldRt.pivot = new Vector2(0.5f, 1f);
            fieldRt.offsetMin = new Vector2(0f, -TotalOffset);
            fieldRt.offsetMax = new Vector2(0f, 0f);

            MissionMenuSearch.Log.LogInfo($"SearchField offsetMin: {fieldRt.offsetMin}, offsetMax: {fieldRt.offsetMax}");

            // Push Filter Tags down
            filterTagsRt.offsetMin = new Vector2(filterTagsRt.offsetMin.x, filterTagsRt.offsetMin.y - TotalOffset);
            filterTagsRt.offsetMax = new Vector2(filterTagsRt.offsetMax.x, filterTagsRt.offsetMax.y - TotalOffset);

            // Push Background top down and make sure to bring the bottom edge up so it fits in the original box without clipping.
            backgroundRt.offsetMin = new Vector2(backgroundRt.offsetMin.x, backgroundRt.offsetMin.y - TotalOffset + 40f);
            backgroundRt.offsetMax = new Vector2(backgroundRt.offsetMax.x, backgroundRt.offsetMax.y - TotalOffset);

            MissionMenuSearch.Log.LogInfo($"FilterTags after: offsetMin={filterTagsRt.offsetMin}, offsetMax={filterTagsRt.offsetMax}");
            MissionMenuSearch.Log.LogInfo($"Background after: offsetMin={backgroundRt.offsetMin}, offsetMax={backgroundRt.offsetMax}");

            // Render on top of siblings
            fieldObj.transform.SetAsLastSibling();

            MissionMenuSearch.Log.LogInfo($"Search field injected, children shifted by {TotalOffset}px.");
        }

        private static GameObject BuildSearchField(Transform parent, MissionSelectPanel selectPanel)
        {
            var anyTmp = GameObject.FindObjectOfType<TextMeshProUGUI>();
            if (anyTmp == null)
                MissionMenuSearch.Log.LogWarning("No TextMeshProUGUI found in scene, font may be missing.");

            GameObject fieldObj = new GameObject("MissionSearchField", typeof(Image), typeof(CanvasGroup), typeof(TMP_InputField));
            fieldObj.transform.SetParent(parent, false);

            Image bg = fieldObj.GetComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            CanvasGroup cg = fieldObj.GetComponent<CanvasGroup>();
            cg.interactable = true;
            cg.blocksRaycasts = true;
            cg.alpha = 1f;

            TMP_InputField inputField = fieldObj.GetComponent<TMP_InputField>();
            inputField.interactable = true;
            inputField.caretWidth = 2;
            inputField.caretColor = Color.white;
            inputField.customCaretColor = true;

            // Text Area
            GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(fieldObj.transform, false);
            RectTransform taRt = textArea.GetComponent<RectTransform>();
            taRt.anchorMin = Vector2.zero;
            taRt.anchorMax = Vector2.one;
            taRt.offsetMin = new Vector2(8f, 2f);
            taRt.offsetMax = new Vector2(-8f, -2f);

            // Placeholder text
            GameObject phObj = new GameObject("Placeholder", typeof(RectTransform));
            phObj.transform.SetParent(textArea.transform, false);
            TMP_Text ph = phObj.AddComponent<TextMeshProUGUI>();
            if (anyTmp != null) ph.font = anyTmp.font;
            ph.text = "Search missions...";
            ph.fontSize = 14f;
            ph.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            ph.fontStyle = FontStyles.Italic;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform phRt = phObj.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = phRt.offsetMax = Vector2.zero;

            // Input
            GameObject textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(textArea.transform, false);
            TMP_Text tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (anyTmp != null) tmp.font = anyTmp.font;
            tmp.fontSize = 14f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = textRt.offsetMax = Vector2.zero;

            // Wire up references first, then force TMP to reinitialize so it creates its own caret at the correct size rather than us managing it manually
            inputField.textViewport = taRt;
            inputField.textComponent = tmp;
            inputField.placeholder = ph;
            inputField.enabled = false;
            inputField.enabled = true;

            inputField.onValueChanged.AddListener(q =>
            {
                MissionSelectPanelState.SetQuery(selectPanel, q);
                Traverse.Create(selectPanel).Method("RefreshLists").GetValue();
            });

            return fieldObj;
        }
    }
}
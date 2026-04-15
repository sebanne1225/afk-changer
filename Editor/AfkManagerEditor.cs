using System;
using System.Collections.Generic;
using Sebanne.AfkManager;
using Sebanne.AfkManager.Editor.Core;
using UnityEditor;
using UnityEditorInternal;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkManager.Editor
{
    [CustomEditor(typeof(AfkManagerComponent))]
    internal sealed class AfkManagerEditor : UnityEditor.Editor
    {
        private static readonly string[] InputTypeLabels = { "Avatar/Prefab", "Controller" };

        // --- SerializedProperties ---
        private SerializedProperty _removeActionAfkProp;
        private SerializedProperty _actionSourcesProp;
        private SerializedProperty _removeFxAfkProp;

        // --- ReorderableList ---
        private ReorderableList _slotList;

        // --- Target scan cache (avatar's own AFK) ---
        private AnimatorController _cachedTargetActionController;
        private AfkScanResult _targetActionScan;
        private string _targetActionError;

        // --- GoGoLoco detection cache ---
        private bool _gogoLocoDetected;

        // --- FX scan cache ---
        private AnimatorController _cachedFxController;
        private List<AfkFxLayerScanResult> _fxScanResults;

        // --- Per-slot scan cache ---
        private struct SlotScanCache
        {
            public UnityEngine.Object lastInput;
            public AfkScanResult scanResult;
            public string error;
        }

        private SlotScanCache[] _slotScans = Array.Empty<SlotScanCache>();

        // --- Avatar prefab scan cache (static, shared across instances) ---
        private static GameObject[] _avatarPrefabs = Array.Empty<GameObject>();
        private static string[] _avatarPrefabNames = Array.Empty<string>();
        private static bool _avatarPrefabsScanned;

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void OnEnable()
        {
            var component = (AfkManagerComponent)target;
            if (component.GetComponent<VRCAvatarDescriptor>() == null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (component == null) return;
                    EditorUtility.DisplayDialog(
                        "AFK Manager",
                        "このコンポーネントはアバタールートに追加してください。\nVRC Avatar Descriptor が見つかりません。",
                        "OK");
                    DestroyImmediate(component);
                };
                return;
            }

            _removeActionAfkProp = serializedObject.FindProperty("removeActionAfk");
            _actionSourcesProp = serializedObject.FindProperty("actionSources");
            _removeFxAfkProp = serializedObject.FindProperty("removeFxAfk");

            SetupReorderableList();
            InvalidateAllCaches();

            if (!_avatarPrefabsScanned)
                ScanAvatarPrefabs();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawActionSection();
            EditorGUILayout.Space(8);
            DrawFxSection();

            serializedObject.ApplyModifiedProperties();
        }

        // =====================================================================
        // Action Section
        // =====================================================================

        private void DrawActionSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Action", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("AFK モーションの入れ替え・削除・追加", EditorStyles.miniLabel);
            EditorGUILayout.Space(2);

            // Current AFK info
            RefreshTargetActionScan();
            DrawTargetActionInfo();

            EditorGUILayout.Space(4);

            // Remove checkbox
            EditorGUILayout.PropertyField(_removeActionAfkProp, new GUIContent("元の AFK を外す"));

            EditorGUILayout.Space(4);

            // Slot list
            _slotList.DoLayoutList();

            // Drop area
            DrawDropArea();

            // Warnings / Info
            DrawActionWarnings();

            EditorGUILayout.EndVertical();
        }

        private void DrawTargetActionInfo()
        {
            if (_targetActionError != null)
            {
                EditorGUILayout.LabelField($"現在の AFK: {_targetActionError}", EditorStyles.miniLabel);
                return;
            }

            if (_targetActionScan == null || !_targetActionScan.HasAfkStates)
            {
                EditorGUILayout.LabelField("現在の AFK: 未検出", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField(
                $"現在の AFK: {FormatScanResult(_targetActionScan)}",
                EditorStyles.miniLabel);
        }

        private void DrawActionWarnings()
        {
            if (_gogoLocoDetected)
                EditorGUILayout.HelpBox("GoGoLoco との併用には対応していません", MessageType.Warning);

            var removeAction = _removeActionAfkProp.boolValue;
            var sourceCount = _actionSourcesProp.arraySize;

            if (removeAction && sourceCount == 0)
                EditorGUILayout.HelpBox("AFK なしでは棒立ちになります", MessageType.Warning);

            if (NeedsModularAvatar())
            {
#if HAS_MODULAR_AVATAR
                EditorGUILayout.HelpBox("Expression Menu で切り替え", MessageType.Info);
#else
                EditorGUILayout.HelpBox("Modular Avatar が必要です", MessageType.Warning);
#endif
            }
        }

        // =====================================================================
        // FX Section
        // =====================================================================

        private void DrawFxSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("FX", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("AFK 関連ステートの管理", EditorStyles.miniLabel);
            EditorGUILayout.Space(2);

            // Current FX AFK info
            RefreshFxScan();
            DrawFxScanInfo();

            EditorGUILayout.Space(4);

            // Remove checkbox
            EditorGUILayout.PropertyField(_removeFxAfkProp, new GUIContent("元の FX AFK を外す"));

            EditorGUILayout.EndVertical();
        }

        private void DrawFxScanInfo()
        {
            if (_cachedFxController == null)
            {
                EditorGUILayout.LabelField("現在の FX AFK: FX Controller なし", EditorStyles.miniLabel);
                return;
            }

            if (_fxScanResults == null || _fxScanResults.Count == 0)
            {
                EditorGUILayout.LabelField("現在の FX AFK: 未検出", EditorStyles.miniLabel);
                return;
            }

            var totalStates = 0;
            foreach (var r in _fxScanResults)
                totalStates += r.ScanResult.AfkStates.Count;

            EditorGUILayout.LabelField(
                $"現在の FX AFK: {_fxScanResults.Count} レイヤー / {totalStates} ステート",
                EditorStyles.miniLabel);
        }

        // =====================================================================
        // ReorderableList
        // =====================================================================

        private void SetupReorderableList()
        {
            _slotList = new ReorderableList(serializedObject, _actionSourcesProp, true, true, true, true);
            _slotList.drawHeaderCallback = DrawSlotHeader;
            _slotList.drawElementCallback = DrawSlotElement;
            _slotList.elementHeightCallback = GetSlotElementHeight;
            _slotList.onAddCallback = OnSlotAdd;
            _slotList.onReorderCallbackWithDetails = OnSlotReorder;
            _slotList.drawNoneElementCallback = DrawEmptyListDropTarget;
        }

        private void DrawSlotHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "付ける AFK");
        }

        private void DrawSlotElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = _actionSourcesProp.GetArrayElementAtIndex(index);
            var inputTypeProp = element.FindPropertyRelative("inputType");
            var avatarPrefabProp = element.FindPropertyRelative("avatarPrefab");
            var sourceControllerProp = element.FindPropertyRelative("sourceController");
            var slotNameProp = element.FindPropertyRelative("slotName");

            var y = rect.y + 2;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            const float spacing = 2f;
            const float typeWidth = 110f;
            const float scanWidth = 112f;
            const float dropBtnWidth = 18f;
            const float gap = 4f;

            // InputType popup
            var typeRect = new Rect(rect.x, y, typeWidth, lineHeight);
            var newType = EditorGUI.Popup(typeRect, inputTypeProp.enumValueIndex, InputTypeLabels);
            if (newType != inputTypeProp.enumValueIndex)
                inputTypeProp.enumValueIndex = newType;

            var inputType = (AfkSourceInputType)inputTypeProp.enumValueIndex;
            var scanRect = new Rect(rect.xMax - scanWidth, y, scanWidth, lineHeight);

            if (inputType == AfkSourceInputType.AvatarPrefab)
            {
                // ObjectField + ▼ prefab picker button
                var btnRect = new Rect(scanRect.x - dropBtnWidth - gap, y, dropBtnWidth, lineHeight);
                var fieldWidth = btnRect.x - (rect.x + typeWidth + gap);
                if (fieldWidth < 40f) fieldWidth = 40f;
                var fieldRect = new Rect(rect.x + typeWidth + gap, y, fieldWidth, lineHeight);

                EditorGUI.PropertyField(fieldRect, avatarPrefabProp, GUIContent.none);

                if (GUI.Button(btnRect, "\u25BC", EditorStyles.miniButton))
                    ShowAvatarPrefabMenu(avatarPrefabProp);
            }
            else
            {
                var fieldWidth = scanRect.x - (rect.x + typeWidth + gap) - gap;
                if (fieldWidth < 40f) fieldWidth = 40f;
                var fieldRect = new Rect(rect.x + typeWidth + gap, y, fieldWidth, lineHeight);
                EditorGUI.PropertyField(fieldRect, sourceControllerProp, GUIContent.none);
            }

            // Scan info
            EnsureSlotCacheSize();
            RefreshSlotScan(index);
            DrawSlotScanInfo(scanRect, index);

            // Row 2: SlotName (only when MA is needed)
            if (NeedsModularAvatar())
            {
                y += lineHeight + spacing;
                var nameRect = new Rect(rect.x, y, rect.width, lineHeight);
                EditorGUI.PropertyField(nameRect, slotNameProp, new GUIContent("スロット名"));
            }
        }

        private float GetSlotElementHeight(int index)
        {
            var lineHeight = EditorGUIUtility.singleLineHeight;
            const float padding = 6f;
            const float spacing = 2f;

            if (NeedsModularAvatar())
                return lineHeight * 2 + spacing + padding;

            return lineHeight + padding;
        }

        private void OnSlotAdd(ReorderableList list)
        {
            var index = _actionSourcesProp.arraySize;
            _actionSourcesProp.InsertArrayElementAtIndex(index);

            var element = _actionSourcesProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("slotName").stringValue = "";
            element.FindPropertyRelative("inputType").enumValueIndex = (int)AfkSourceInputType.AvatarPrefab;
            element.FindPropertyRelative("avatarPrefab").objectReferenceValue = null;
            element.FindPropertyRelative("sourceController").objectReferenceValue = null;
        }

        private void OnSlotReorder(ReorderableList list, int oldIndex, int newIndex)
        {
            // Rebuild slot scan cache after reorder
            _slotScans = Array.Empty<SlotScanCache>();
        }

        private void DrawSlotScanInfo(Rect rect, int index)
        {
            if (index >= _slotScans.Length)
            {
                EditorGUI.LabelField(rect, "", EditorStyles.miniLabel);
                return;
            }

            var cache = _slotScans[index];

            if (cache.error != null)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.7f, 0.3f);
                EditorGUI.LabelField(rect, cache.error, EditorStyles.miniLabel);
                GUI.color = prev;
                return;
            }

            if (cache.scanResult == null || !cache.scanResult.HasAfkStates)
            {
                EditorGUI.LabelField(rect, "AFK 未検出", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.LabelField(rect, FormatScanResult(cache.scanResult), EditorStyles.miniLabel);
        }

        // =====================================================================
        // Drag & Drop
        // =====================================================================

        private void DrawEmptyListDropTarget(Rect rect)
        {
            EditorGUI.LabelField(rect, "Avatar / Controller をドラッグして追加",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleCenter });
            HandleDragDropInRect(rect);
        }

        private void DrawDropArea()
        {
            if (_actionSourcesProp.arraySize == 0) return;

            var dropArea = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));

            var evt = Event.current;
            var isHovering = (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
                             && dropArea.Contains(evt.mousePosition);

            var prevBg = GUI.backgroundColor;
            if (isHovering && HasValidDragObjects())
                GUI.backgroundColor = new Color(0.5f, 0.8f, 1f, 0.5f);

            var style = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter };
            GUI.Box(dropArea, "ドラッグして追加", style);
            GUI.backgroundColor = prevBg;

            HandleDragDropInRect(dropArea);
        }

        private void HandleDragDropInRect(Rect area)
        {
            var evt = Event.current;
            if (!area.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (HasValidDragObjects())
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;
                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    HandleDroppedObjects(DragAndDrop.objectReferences);
                    evt.Use();
                    break;
            }
        }

        private static bool HasValidDragObjects()
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is GameObject || obj is RuntimeAnimatorController)
                    return true;
            }
            return false;
        }

        private void HandleDroppedObjects(UnityEngine.Object[] objects)
        {
            foreach (var obj in objects)
            {
                if (obj is GameObject go)
                    AddSlotFromDrop(AfkSourceInputType.AvatarPrefab, go, null);
                else if (obj is RuntimeAnimatorController rac)
                    AddSlotFromDrop(AfkSourceInputType.Controller, null, rac);
            }
        }

        private void AddSlotFromDrop(AfkSourceInputType inputType, GameObject prefab, RuntimeAnimatorController controller)
        {
            var index = _actionSourcesProp.arraySize;
            _actionSourcesProp.InsertArrayElementAtIndex(index);

            var element = _actionSourcesProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("slotName").stringValue = "";
            element.FindPropertyRelative("inputType").enumValueIndex = (int)inputType;
            element.FindPropertyRelative("avatarPrefab").objectReferenceValue = prefab;
            element.FindPropertyRelative("sourceController").objectReferenceValue = controller;

            serializedObject.ApplyModifiedProperties();
            _slotScans = Array.Empty<SlotScanCache>();
        }

        // =====================================================================
        // Scan Refresh
        // =====================================================================

        private void InvalidateAllCaches()
        {
            _cachedTargetActionController = null;
            _targetActionScan = null;
            _targetActionError = null;
            _gogoLocoDetected = false;
            _cachedFxController = null;
            _fxScanResults = null;
            _slotScans = Array.Empty<SlotScanCache>();
        }

        private void RefreshTargetActionScan()
        {
            var avatarObj = ((Component)target).gameObject;
            var controller = ActionControllerResolver.TryResolve(
                avatarObj, VRCAvatarDescriptor.AnimLayerType.Action, out var error);

            if (controller == _cachedTargetActionController && _targetActionScan != null)
                return;

            _cachedTargetActionController = controller;

            if (controller == null)
            {
                _targetActionScan = null;
                _targetActionError = error ?? "Action Controller なし";
                return;
            }

            _targetActionError = null;
            _targetActionScan = AfkStateScanner.Scan(controller);
            RefreshGoGoLocoDetection();
        }

        private void RefreshGoGoLocoDetection()
        {
            _gogoLocoDetected = false;
#if HAS_MODULAR_AVATAR
            var avatarObj = ((Component)target).gameObject;
            var mergeAnimators = avatarObj.GetComponentsInChildren<nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator>(true);
            foreach (var merge in mergeAnimators)
            {
                if (merge.animator != null && merge.animator.name.Contains("GoLoco"))
                {
                    _gogoLocoDetected = true;
                    return;
                }
            }
#endif
        }

        private void RefreshSlotScan(int index)
        {
            if (index >= _actionSourcesProp.arraySize) return;

            var element = _actionSourcesProp.GetArrayElementAtIndex(index);
            var inputType = (AfkSourceInputType)element.FindPropertyRelative("inputType").enumValueIndex;

            UnityEngine.Object currentInput;
            if (inputType == AfkSourceInputType.AvatarPrefab)
                currentInput = element.FindPropertyRelative("avatarPrefab").objectReferenceValue;
            else
                currentInput = element.FindPropertyRelative("sourceController").objectReferenceValue;

            if (index < _slotScans.Length && _slotScans[index].lastInput == currentInput)
                return;

            EnsureSlotCacheSize();

            if (currentInput == null)
            {
                _slotScans[index] = new SlotScanCache
                {
                    lastInput = null,
                    scanResult = null,
                    error = null
                };
                return;
            }

            AnimatorController controller = null;
            string resolveError = null;

            if (inputType == AfkSourceInputType.AvatarPrefab)
            {
                var go = currentInput as GameObject;
                controller = ActionControllerResolver.TryResolve(
                    go, VRCAvatarDescriptor.AnimLayerType.Action, out resolveError);
            }
            else
            {
                controller = currentInput as AnimatorController;
                if (controller == null)
                    resolveError = "AnimatorController ではありません";
            }

            _slotScans[index] = new SlotScanCache
            {
                lastInput = currentInput,
                scanResult = controller != null ? AfkStateScanner.Scan(controller) : null,
                error = resolveError
            };
        }

        private void RefreshFxScan()
        {
            var avatarObj = ((Component)target).gameObject;
            var controller = ActionControllerResolver.TryResolve(
                avatarObj, VRCAvatarDescriptor.AnimLayerType.FX, out _);

            if (controller == _cachedFxController && _fxScanResults != null)
                return;

            _cachedFxController = controller;
            _fxScanResults = controller != null
                ? AfkStateScanner.ScanFxLayers(controller)
                : null;
        }

        private void EnsureSlotCacheSize()
        {
            var count = _actionSourcesProp.arraySize;
            if (_slotScans.Length == count) return;

            var newCache = new SlotScanCache[count];
            var copyLen = Math.Min(_slotScans.Length, count);
            Array.Copy(_slotScans, newCache, copyLen);
            _slotScans = newCache;
        }

        // =====================================================================
        // Avatar Prefab Scanner
        // =====================================================================

        private static void ScanAvatarPrefabs()
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");
            var prefabs = new List<GameObject>();
            var names = new List<string>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponent<VRCAvatarDescriptor>() != null)
                {
                    prefabs.Add(go);
                    names.Add(go.name);
                }
            }

            _avatarPrefabs = prefabs.ToArray();
            _avatarPrefabNames = names.ToArray();
            _avatarPrefabsScanned = true;
        }

        private void ShowAvatarPrefabMenu(SerializedProperty avatarPrefabProp)
        {
            if (!_avatarPrefabsScanned) ScanAvatarPrefabs();

            var menu = new GenericMenu();

            for (var i = 0; i < _avatarPrefabs.Length; i++)
            {
                var prefab = _avatarPrefabs[i];
                var isSelected = avatarPrefabProp.objectReferenceValue == prefab;
                menu.AddItem(new GUIContent(_avatarPrefabNames[i]), isSelected, () =>
                {
                    avatarPrefabProp.objectReferenceValue = prefab;
                    avatarPrefabProp.serializedObject.ApplyModifiedProperties();
                    _slotScans = Array.Empty<SlotScanCache>();
                });
            }

            if (_avatarPrefabs.Length == 0)
                menu.AddDisabledItem(new GUIContent("アバターが見つかりません"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("再スキャン"), false, () =>
            {
                _avatarPrefabsScanned = false;
                ScanAvatarPrefabs();
            });

            menu.ShowAsContext();
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private bool NeedsModularAvatar()
        {
            var sourceCount = _actionSourcesProp.arraySize;
            var removeAction = _removeActionAfkProp.boolValue;
            return sourceCount >= 2 || (!removeAction && sourceCount >= 1);
        }

        private static string FormatScanResult(AfkScanResult scan)
        {
            if (scan == null || !scan.HasAfkStates) return "AFK 未検出";

            var pattern = scan.HasSubStateMachineContent ? "SubSM" : "flat";
            return $"{pattern} / {scan.ContentStates.Count} ステート";
        }
    }
}

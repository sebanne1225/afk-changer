using System;
using System.Collections.Generic;
using System.Reflection;
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
        private SerializedProperty _originalAfkOrderProp;
        private SerializedProperty _actionSourcesProp;
        private SerializedProperty _removeFxAfkProp;

        // --- Virtual row model (unified Original + action source list) ---
        private struct VirtualRow
        {
            public bool IsOriginal;
            public int ActionSourceIndex; // -1 when IsOriginal
        }

        private readonly List<VirtualRow> _virtualRows = new();

        // --- ReorderableList ---
        private ReorderableList _slotList;
        private bool _isDragHovering;

        // --- Target scan cache (avatar's own AFK) ---
        private AnimatorController _cachedTargetActionController;
        private AfkScanResult _targetActionScan;
        private string _targetActionError;

        // --- GoGoLoco detection cache ---
        private bool _gogoLocoDetected;

        // --- FX scan cache ---
        private AnimatorController _cachedFxController;
        private List<AfkFxLayerScanResult> _fxScanResults;

        // --- MissingScript detection cache ---
        private readonly List<GameObject> _missingScriptObjects = new();

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

            _originalAfkOrderProp = serializedObject.FindProperty("originalAfkOrder");
            _actionSourcesProp = serializedObject.FindProperty("actionSources");
            _removeFxAfkProp = serializedObject.FindProperty("removeFxAfk");

            RebuildVirtualRows();
            SetupReorderableList();
            InvalidateAllCaches();
            RefreshMissingScripts();

            if (!_avatarPrefabsScanned)
                ScanAvatarPrefabs();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            RebuildVirtualRows();

            DrawMissingScriptWarning();
            DrawActionSection();
            EditorGUILayout.Space(8);
            DrawFxSection();

            serializedObject.ApplyModifiedProperties();
        }

        // =====================================================================
        // Virtual Row Model
        // =====================================================================

        private void RebuildVirtualRows()
        {
            _virtualRows.Clear();

            var sourceCount = _actionSourcesProp.arraySize;
            var originalOrder = _originalAfkOrderProp.intValue;

            for (var i = 0; i < sourceCount; i++)
                _virtualRows.Add(new VirtualRow { IsOriginal = false, ActionSourceIndex = i });

            if (originalOrder >= 0)
            {
                var insertPos = Math.Min(originalOrder, sourceCount);
                _virtualRows.Insert(insertPos, new VirtualRow { IsOriginal = true, ActionSourceIndex = -1 });
            }
        }

        private int GetEffectiveSlotCount()
        {
            var sourceCount = _actionSourcesProp.arraySize;
            var originalIncluded = _originalAfkOrderProp.intValue >= 0;
            return sourceCount + (originalIncluded ? 1 : 0);
        }

        // =====================================================================
        // MissingScript Detection
        // =====================================================================

        private void RefreshMissingScripts()
        {
            _missingScriptObjects.Clear();

            var avatarObj = ((Component)target).gameObject;
            if (avatarObj == null) return;

            var transforms = avatarObj.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                var components = t.gameObject.GetComponents<Component>();
                foreach (var c in components)
                {
                    if (c == null)
                    {
                        _missingScriptObjects.Add(t.gameObject);
                        break;
                    }
                }
            }
        }

        private void DrawMissingScriptWarning()
        {
            if (_missingScriptObjects.Count == 0) return;

            EditorGUILayout.HelpBox(
                "v1.x からアップデートされたアバターで、旧コンポーネント（MissingScript）が検出されました。2.0.0 と互換性がないため、該当コンポーネントを削除してください。",
                MessageType.Warning);

            var avatarRoot = ((Component)target).transform;
            foreach (var go in _missingScriptObjects)
            {
                if (go == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var path = go.transform == avatarRoot
                        ? "<root>"
                        : AnimationUtility.CalculateTransformPath(go.transform, avatarRoot);
                    EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    {
                        EditorGUIUtility.PingObject(go);
                        Selection.activeGameObject = go;
                    }
                }
            }

            if (GUILayout.Button("再スキャン"))
                RefreshMissingScripts();

            EditorGUILayout.Space(8);
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

            // Refresh avatar scan (used by Original row and warnings)
            RefreshTargetActionScan();

            // "元の AFK を含める" Toggle (常時表示)
            var includeOriginal = _originalAfkOrderProp.intValue >= 0;
            var newIncludeOriginal = EditorGUILayout.Toggle("元の AFK を含める", includeOriginal);
            if (newIncludeOriginal != includeOriginal)
            {
                var component = (AfkManagerComponent)target;
                Undo.RecordObject(component, newIncludeOriginal ? "Include Original AFK" : "Exclude Original AFK");
                component.originalAfkOrder = newIncludeOriginal ? 0 : -1;
                EditorUtility.SetDirty(component);
                serializedObject.Update();
                RebuildVirtualRows();
            }

            EditorGUILayout.Space(4);

            // Unified slot list (Original + action sources)
            _slotList.DoLayoutList();
            var listRect = GUILayoutUtility.GetLastRect();

            var evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    _isDragHovering = listRect.Contains(evt.mousePosition) && HasValidDragObjects();
                    break;
                case EventType.DragExited:
                case EventType.MouseUp:
                    _isDragHovering = false;
                    break;
            }

            if (evt.type == EventType.Repaint && _isDragHovering)
                EditorGUI.DrawRect(listRect, new Color(0.5f, 0.8f, 1f, 0.15f));

            HandleDragDropInRect(listRect);

            // Fallback hint (first slot becomes menu-off default when effectiveSlotCount >= 2)
            if (GetEffectiveSlotCount() >= 2)
                EditorGUILayout.LabelField("\u2605 先頭スロットがメニュー OFF 時のデフォルトになります", EditorStyles.miniLabel);

            // Warnings / Info
            DrawActionWarnings();

            EditorGUILayout.EndVertical();
        }

        private void DrawTargetScanInfo(Rect rect)
        {
            if (_targetActionError != null)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.7f, 0.3f);
                EditorGUI.LabelField(rect, _targetActionError, EditorStyles.miniLabel);
                GUI.color = prev;
                return;
            }

            if (_targetActionScan == null || !_targetActionScan.HasAfkStates)
            {
                EditorGUI.LabelField(rect, "AFK 未検出", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.LabelField(rect, FormatScanResult(_targetActionScan), EditorStyles.miniLabel);
        }

        private void DrawActionWarnings()
        {
            if (_gogoLocoDetected)
                EditorGUILayout.HelpBox("GoGoLoco との併用には対応していません", MessageType.Warning);

            var isOriginalRemoved = _originalAfkOrderProp.intValue == -1;
            var sourceCount = _actionSourcesProp.arraySize;

            if (isOriginalRemoved && sourceCount == 0)
                EditorGUILayout.HelpBox("AFK なしでは棒立ちになります", MessageType.Warning);

            if (NeedsModularAvatar())
            {
#if HAS_MODULAR_AVATAR
                EditorGUILayout.HelpBox("Expression Menu で切り替え", MessageType.Info);
                DrawMenuInstallTarget();
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
            _slotList = new ReorderableList(_virtualRows, typeof(VirtualRow), true, true, true, true)
            {
                drawHeaderCallback = DrawSlotHeader,
                drawElementCallback = DrawSlotElement,
                elementHeightCallback = GetSlotElementHeight,
                onAddCallback = OnSlotAdd,
                onRemoveCallback = OnSlotRemove,
                onReorderCallbackWithDetails = OnSlotReorder,
                drawNoneElementCallback = DrawEmptyPickerInList,
                elementHeight = 60f,
            };
        }

        private void DrawSlotHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "AFK スロット");
        }

        private void DrawSlotElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index >= _virtualRows.Count) return;

            var row = _virtualRows[index];
            if (row.IsOriginal)
                DrawOriginalSlotElement(rect, index);
            else
                DrawActionSourceSlotElement(rect, index, row.ActionSourceIndex);
        }

        private void DrawOriginalSlotElement(Rect rect, int displayIndex)
        {
            var y = rect.y + 2;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            const float spacing = 2f;
            const float badgeWidth = 18f;
            const float scanWidth = 112f;
            const float gap = 4f;

            var showFallbackBadge = displayIndex == 0 && GetEffectiveSlotCount() >= 2;

            var x = rect.x;
            if (showFallbackBadge)
            {
                EditorGUI.LabelField(new Rect(x, y, badgeWidth, lineHeight), "\u2605");
                x += badgeWidth + gap;
            }

            var scanRect = new Rect(rect.xMax - scanWidth, y, scanWidth, lineHeight);
            var labelWidth = scanRect.x - x - gap;
            if (labelWidth < 40f) labelWidth = 40f;
            var labelRect = new Rect(x, y, labelWidth, lineHeight);
            EditorGUI.LabelField(labelRect, "元の AFK", EditorStyles.boldLabel);

            DrawTargetScanInfo(scanRect);

            // Row 2: メニュー名 (only when MA is needed)
            if (NeedsModularAvatar())
            {
                y += lineHeight + spacing;
                var menuNameProp = serializedObject.FindProperty("originalAfkMenuName");
                var nameRect = new Rect(rect.x, y, rect.width, lineHeight);
                var labelText = showFallbackBadge ? "\u2605 メニュー名" : "メニュー名";
                EditorGUI.PropertyField(nameRect, menuNameProp, new GUIContent(labelText));
            }
        }

        private void DrawActionSourceSlotElement(Rect rect, int displayIndex, int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= _actionSourcesProp.arraySize) return;

            var element = _actionSourcesProp.GetArrayElementAtIndex(sourceIndex);
            var inputTypeProp = element.FindPropertyRelative("inputType");
            var avatarPrefabProp = element.FindPropertyRelative("avatarPrefab");
            var sourceControllerProp = element.FindPropertyRelative("sourceController");
            var slotNameProp = element.FindPropertyRelative("slotName");

            var y = rect.y + 2;
            var lineHeight = EditorGUIUtility.singleLineHeight;
            const float spacing = 2f;
            const float badgeWidth = 18f;
            const float typeWidth = 110f;
            const float scanWidth = 112f;
            const float dropBtnWidth = 18f;
            const float gap = 4f;

            var showFallbackBadge = displayIndex == 0 && GetEffectiveSlotCount() >= 2;

            var x = rect.x;
            if (showFallbackBadge)
            {
                EditorGUI.LabelField(new Rect(x, y, badgeWidth, lineHeight), "\u2605");
                x += badgeWidth + gap;
            }

            // InputType popup
            var typeRect = new Rect(x, y, typeWidth, lineHeight);
            var newType = EditorGUI.Popup(typeRect, inputTypeProp.enumValueIndex, InputTypeLabels);
            if (newType != inputTypeProp.enumValueIndex)
                inputTypeProp.enumValueIndex = newType;

            var inputType = (AfkSourceInputType)inputTypeProp.enumValueIndex;
            var scanRect = new Rect(rect.xMax - scanWidth, y, scanWidth, lineHeight);

            if (inputType == AfkSourceInputType.AvatarPrefab)
            {
                var btnRect = new Rect(scanRect.x - dropBtnWidth - gap, y, dropBtnWidth, lineHeight);
                var fieldWidth = btnRect.x - (x + typeWidth + gap);
                if (fieldWidth < 40f) fieldWidth = 40f;
                var fieldRect = new Rect(x + typeWidth + gap, y, fieldWidth, lineHeight);

                EditorGUI.PropertyField(fieldRect, avatarPrefabProp, GUIContent.none);

                if (GUI.Button(btnRect, "\u25BC", EditorStyles.miniButton))
                    ShowAvatarPrefabMenu(avatarPrefabProp);
            }
            else
            {
                var fieldWidth = scanRect.x - (x + typeWidth + gap) - gap;
                if (fieldWidth < 40f) fieldWidth = 40f;
                var fieldRect = new Rect(x + typeWidth + gap, y, fieldWidth, lineHeight);
                EditorGUI.PropertyField(fieldRect, sourceControllerProp, GUIContent.none);
            }

            // Scan info
            EnsureSlotCacheSize();
            RefreshSlotScan(sourceIndex);
            DrawSlotScanInfo(scanRect, sourceIndex);

            // Row 2: SlotName (only when MA is needed)
            if (NeedsModularAvatar())
            {
                y += lineHeight + spacing;
                var nameRect = new Rect(rect.x, y, rect.width, lineHeight);
                var labelText = showFallbackBadge ? "\u2605 メニュー名" : "メニュー名";
                EditorGUI.PropertyField(nameRect, slotNameProp, new GUIContent(labelText));
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
            var component = (AfkManagerComponent)target;
            Undo.RecordObject(component, "Add AFK Slot");

            component.actionSources.Add(new AfkSlot
            {
                slotName = "",
                inputType = AfkSourceInputType.AvatarPrefab,
                avatarPrefab = null,
                sourceController = null,
            });

            EditorUtility.SetDirty(component);
            serializedObject.Update();
            RebuildVirtualRows();
            _slotScans = Array.Empty<SlotScanCache>();
        }

        private void OnSlotRemove(ReorderableList list)
        {
            var removeIndex = list.index;
            if (removeIndex < 0 || removeIndex >= _virtualRows.Count) return;

            var row = _virtualRows[removeIndex];
            var component = (AfkManagerComponent)target;

            if (row.IsOriginal)
            {
                Undo.RecordObject(component, "Exclude Original AFK");
                component.originalAfkOrder = -1;
            }
            else
            {
                if (row.ActionSourceIndex < 0 || row.ActionSourceIndex >= component.actionSources.Count) return;
                Undo.RecordObject(component, "Remove AFK Slot");
                component.actionSources.RemoveAt(row.ActionSourceIndex);
            }

            EditorUtility.SetDirty(component);
            serializedObject.Update();
            RebuildVirtualRows();
            _slotScans = Array.Empty<SlotScanCache>();

            if (list.index >= _virtualRows.Count)
                list.index = _virtualRows.Count - 1;
        }

        private void OnSlotReorder(ReorderableList list, int oldIndex, int newIndex)
        {
            // ReorderableList has already mutated _virtualRows in-place.
            // Reconstruct actionSources order + originalAfkOrder from the new order.
            var component = (AfkManagerComponent)target;
            Undo.RecordObject(component, "Reorder AFK Slots");

            var oldSources = new List<AfkSlot>(component.actionSources);
            var newSources = new List<AfkSlot>(oldSources.Count);
            var newOriginalOrder = component.originalAfkOrder;

            for (var i = 0; i < _virtualRows.Count; i++)
            {
                var row = _virtualRows[i];
                if (row.IsOriginal)
                    newOriginalOrder = newSources.Count;
                else if (row.ActionSourceIndex >= 0 && row.ActionSourceIndex < oldSources.Count)
                    newSources.Add(oldSources[row.ActionSourceIndex]);
            }

            component.actionSources = newSources;
            component.originalAfkOrder = newOriginalOrder;

            EditorUtility.SetDirty(component);
            serializedObject.Update();
            RebuildVirtualRows();
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

        private void DrawEmptyPickerInList(Rect rect)
        {
            const float padding = 6f;
            var lineHeight = EditorGUIUtility.singleLineHeight;

            var buttonRect = new Rect(
                rect.x + padding,
                rect.y + padding,
                rect.width - padding * 2,
                lineHeight + 4f);

            if (GUI.Button(buttonRect, "アバター一覧から選ぶ \u25BC"))
                ShowAvatarPrefabMenuForNewSlot();

            var subLabelRect = new Rect(
                rect.x + padding,
                buttonRect.yMax + 4f,
                rect.width - padding * 2,
                lineHeight);
            var subStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUI.LabelField(subLabelRect, "または Avatar / Controller をここにドラッグ", subStyle);
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
            var component = (AfkManagerComponent)target;
            Undo.RecordObject(component, "Add AFK Slot (Drop)");

            component.actionSources.Add(new AfkSlot
            {
                slotName = "",
                inputType = inputType,
                avatarPrefab = prefab,
                sourceController = controller,
            });

            EditorUtility.SetDirty(component);
            serializedObject.Update();
            RebuildVirtualRows();
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
            var raw = new List<(GameObject prefab, RuntimeAnimatorController action, RuntimeAnimatorController fx)>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var desc = go.GetComponent<VRCAvatarDescriptor>();
                if (desc == null) continue;

                RuntimeAnimatorController action = null, fx = null;
                foreach (var layer in desc.baseAnimationLayers)
                {
                    if (layer.type == VRCAvatarDescriptor.AnimLayerType.Action && !layer.isDefault)
                        action = layer.animatorController;
                    else if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX && !layer.isDefault)
                        fx = layer.animatorController;
                }

                raw.Add((go, action, fx));
            }

            // Group by (Action, FX) controller pair
            var groups = new Dictionary<(int, int), List<GameObject>>();
            foreach (var entry in raw)
            {
                var key = (
                    entry.action != null ? entry.action.GetInstanceID() : 0,
                    entry.fx != null ? entry.fx.GetInstanceID() : 0
                );
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<GameObject>();
                    groups[key] = list;
                }
                list.Add(entry.prefab);
            }

            var prefabs = new List<GameObject>();
            var names = new List<string>();

            foreach (var group in groups.Values)
            {
                group.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                prefabs.Add(group[0]);
                names.Add(group.Count == 1
                    ? group[0].name
                    : $"{group[0].name} ({group.Count} variants)");
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

        private void ShowAvatarPrefabMenuForNewSlot()
        {
            if (!_avatarPrefabsScanned) ScanAvatarPrefabs();

            var menu = new GenericMenu();

            for (var i = 0; i < _avatarPrefabs.Length; i++)
            {
                var prefab = _avatarPrefabs[i];
                menu.AddItem(new GUIContent(_avatarPrefabNames[i]), false, () =>
                {
                    AddSlotFromPicker(prefab);
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

        private void AddSlotFromPicker(GameObject prefab)
        {
            var component = (AfkManagerComponent)target;
            Undo.RecordObject(component, "Add AFK Slot (Picker)");

            component.actionSources.Add(new AfkSlot
            {
                slotName = "",
                inputType = AfkSourceInputType.AvatarPrefab,
                avatarPrefab = prefab,
                sourceController = null,
            });

            EditorUtility.SetDirty(component);
            serializedObject.Update();
            RebuildVirtualRows();
            _slotScans = Array.Empty<SlotScanCache>();
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private bool NeedsModularAvatar() => GetEffectiveSlotCount() >= 2;

#if HAS_MODULAR_AVATAR
        // --- Menu tree reflection (static, one-time init) ---
        private static bool _menuTreeChecked;
        private static MethodInfo _menuTreeShowMethod;

        private static MethodInfo GetMenuTreeShowMethod()
        {
            if (_menuTreeChecked) return _menuTreeShowMethod;
            _menuTreeChecked = true;
            try
            {
                var asm = Assembly.Load("nadena.dev.modular-avatar.core.editor");
                var type = asm?.GetType("nadena.dev.modular_avatar.core.editor.AvMenuTreeViewWindow");
                _menuTreeShowMethod = type?.GetMethod("Show", BindingFlags.Static | BindingFlags.NonPublic);
            }
            catch
            {
                _menuTreeShowMethod = null;
            }
            return _menuTreeShowMethod;
        }

        private void DrawMenuInstallTarget()
        {
            var prop = serializedObject.FindProperty("menuInstallTarget");
            EditorGUILayout.PropertyField(prop, new GUIContent("メニュー配置先"));

            if (prop.objectReferenceValue == null)
                EditorGUILayout.LabelField(" ", "トップメニュー直下", EditorStyles.miniLabel);

            var showMethod = GetMenuTreeShowMethod();
            if (showMethod != null)
            {
                if (GUILayout.Button("メニューを選択"))
                {
                    try
                    {
                        var descriptor = ((Component)target).GetComponent<VRCAvatarDescriptor>();
                        Action<object> callback = selected =>
                        {
                            if (selected is ValueTuple<object, object> vt)
                                selected = vt.Item1;
                            if (selected is nadena.dev.modular_avatar.core.ModularAvatarMenuItem item
                                && item.MenuSource == nadena.dev.modular_avatar.core.SubmenuSource.MenuAsset
                                && item.Control?.subMenu != null)
                                selected = item.Control.subMenu;

                            if (selected is VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu expMenu)
                            {
                                prop.objectReferenceValue = (expMenu == descriptor.expressionsMenu) ? null : expMenu;
                            }
                            else
                            {
                                if (selected != null)
                                    AfkLog.Warn("MA コンポーネントで構成されたサブメニューへの配置は非対応です。" +
                                                "VRCExpressionsMenu アセットを配置先に指定してください。");
                                prop.objectReferenceValue = null;
                            }
                            serializedObject.ApplyModifiedProperties();
                        };
                        showMethod.Invoke(null, new object[] { descriptor, null, callback });
                    }
                    catch (Exception e)
                    {
                        AfkLog.Warn($"メニュー選択を開けませんでした: {e.Message}");
                    }
                }
            }

        }
#endif

        private static string FormatScanResult(AfkScanResult scan)
        {
            if (scan == null || !scan.HasAfkStates) return "AFK 未検出";

            var pattern = scan.HasSubStateMachineContent ? "SubSM" : "flat";
            return $"{pattern} / {scan.ContentStates.Count} ステート";
        }
    }
}

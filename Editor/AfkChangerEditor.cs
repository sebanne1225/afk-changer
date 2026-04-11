using Sebanne.AfkChanger.Editor.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sebanne.AfkChanger.Editor
{
    [CustomEditor(typeof(AfkChangerComponent))]
    internal sealed class AfkChangerEditor : UnityEditor.Editor
    {
        private SerializedProperty _sourceControllerProp;
        private GameObject _avatarObject;
        private string _avatarError;

        // Scan cache
        private RuntimeAnimatorController _lastScannedController;
        private AfkScanResult _scanResult;

        private void OnEnable()
        {
            _sourceControllerProp = serializedObject.FindProperty("_sourceController");
            RefreshScan();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Description
            EditorGUILayout.LabelField("AFK アニメーションを非破壊で入れ替えます", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            // Avatar / Prefab shortcut field
            EditorGUI.BeginChangeCheck();
            _avatarObject = (GameObject)EditorGUILayout.ObjectField(
                "Avatar / Prefab", _avatarObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck() && _avatarObject != null)
            {
                var controller = ActionControllerResolver.TryResolve(_avatarObject, out _avatarError);
                if (controller != null)
                {
                    _sourceControllerProp.objectReferenceValue = controller;
                    _avatarError = null;
                }
            }

            if (!string.IsNullOrEmpty(_avatarError))
                EditorGUILayout.HelpBox(_avatarError, MessageType.Warning);

            // Source Controller field
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(_sourceControllerProp, new GUIContent("Source Controller"));
            if (EditorGUI.EndChangeCheck())
            {
                // Clear avatar error when user directly sets the controller
                _avatarError = null;
            }

            serializedObject.ApplyModifiedProperties();

            // Scan info
            RefreshScan();
            DrawScanInfo();
        }

        private void RefreshScan()
        {
            var current = _sourceControllerProp != null
                ? _sourceControllerProp.objectReferenceValue as RuntimeAnimatorController
                : null;

            if (current == _lastScannedController && _scanResult != null)
                return;

            _lastScannedController = current;

            if (current is AnimatorController ac)
                _scanResult = AfkStateScanner.Scan(ac);
            else
                _scanResult = null;
        }

        private void DrawScanInfo()
        {
            if (_sourceControllerProp.objectReferenceValue == null)
                return;

            if (_scanResult == null)
                return;

            if (!_scanResult.HasAfkStates)
            {
                EditorGUILayout.HelpBox("AFK ステートが見つかりません。AFK パラメータを使った遷移があるか確認してください。", MessageType.Warning);
                return;
            }

            var pattern = _scanResult.HasSubStateMachineContent ? "SubSM" : "flat";
            var contentCount = _scanResult.ContentStates.Count;
            EditorGUILayout.LabelField(
                $"{pattern} パターン / AFK ステート: {contentCount}",
                EditorStyles.miniLabel);
        }
    }
}

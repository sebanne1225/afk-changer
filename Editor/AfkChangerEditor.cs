using System.Collections.Generic;
using System.Linq;
using Sebanne.AfkChanger.Editor.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkChanger.Editor
{
    [CustomEditor(typeof(AfkChangerComponent))]
    internal sealed class AfkChangerEditor : UnityEditor.Editor
    {
        private SerializedProperty _sourceControllerProp;
        private SerializedProperty _fxModeProp;
        private GameObject _avatarObject;
        private string _avatarError;

        // Action scan cache
        private RuntimeAnimatorController _lastScannedController;
        private AfkScanResult _scanResult;

        // FX scan cache
        private AnimatorController _fxController;
        private List<AfkFxLayerScanResult> _fxScanResults;

        private void OnEnable()
        {
            _sourceControllerProp = serializedObject.FindProperty("_sourceController");
            _fxModeProp = serializedObject.FindProperty("_fxMode");
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
                var controller = ActionControllerResolver.TryResolve(
                    _avatarObject, VRCAvatarDescriptor.AnimLayerType.Action, out _avatarError);
                if (controller != null)
                {
                    _sourceControllerProp.objectReferenceValue = controller;
                    _avatarError = null;
                }

                // Invalidate FX cache on avatar change
                _fxController = null;
                _fxScanResults = null;
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

            // Action scan info
            RefreshScan();
            DrawScanInfo();

            // --- FX section ---
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("FX レイヤー", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("AFK 関連ステートの削除", EditorStyles.miniLabel);
            EditorGUILayout.Space(2);

            serializedObject.Update();
            EditorGUILayout.PropertyField(_fxModeProp, new GUIContent("FX モード"));
            serializedObject.ApplyModifiedProperties();

            if (_fxModeProp.enumValueIndex != (int)AfkFxMode.None)
            {
                RefreshFxScan();
                DrawFxScanInfo();
            }

            EditorGUILayout.EndVertical();
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

        private void RefreshFxScan()
        {
            var avatarObj = ((Component)target).gameObject;
            var fxCtrl = ActionControllerResolver.TryResolve(
                avatarObj, VRCAvatarDescriptor.AnimLayerType.FX, out _);

            if (fxCtrl == _fxController && _fxScanResults != null)
                return;

            _fxController = fxCtrl;
            _fxScanResults = fxCtrl != null
                ? AfkStateScanner.ScanFxLayers(fxCtrl)
                : null;
        }

        private void DrawFxScanInfo()
        {
            if (_fxController == null)
            {
                EditorGUILayout.LabelField("FX Controller が見つかりません", EditorStyles.miniLabel);
                return;
            }

            if (_fxScanResults == null || _fxScanResults.Count == 0)
            {
                EditorGUILayout.LabelField("AFK ステートなし", EditorStyles.miniLabel);
                return;
            }

            var totalStates = _fxScanResults.Sum(r => r.ScanResult.AfkStates.Count);
            EditorGUILayout.LabelField(
                $"{_fxScanResults.Count} レイヤー / AFK ステート: {totalStates}",
                EditorStyles.miniLabel);
        }
    }
}

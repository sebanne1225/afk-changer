using System;
using System.IO;
using System.Linq;
using System.Text;
using Sebanne.AfkManager.Editor.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sebanne.AfkManager.Editor.Debug
{
    public static class ControllerDumper
    {
        private const string OutputDir = "Assets/_Temp";

        // --- Tools menu (existing) ---

        [MenuItem("Tools/AFK Manager/Dump Selected Controller")]
        private static void DumpSelected()
        {
            var obj = Selection.activeObject;
            if (obj == null || !(obj is AnimatorController controller))
            {
                UnityEngine.Debug.LogWarning("[AFK Manager] Select an AnimatorController in the Project window.");
                return;
            }

            DumpAndWrite(controller);
        }

        [MenuItem("Tools/AFK Manager/Dump Selected Controller", true)]
        private static bool DumpSelectedValidate()
        {
            return Selection.activeObject is AnimatorController;
        }

        // --- Assets context menu: AnimatorController ---

        [MenuItem("Assets/AFK Manager/Dump Controller")]
        private static void DumpControllerAsset()
        {
            var obj = Selection.activeObject;
            if (obj is AnimatorController controller)
                DumpAndWrite(controller);
        }

        [MenuItem("Assets/AFK Manager/Dump Controller", true)]
        private static bool DumpControllerAssetValidate()
        {
            return Selection.activeObject is AnimatorController;
        }

        // --- Assets context menu: Prefab / Hierarchy: GameObject with VRCAvatarDescriptor ---

        [MenuItem("Assets/AFK Manager/Dump Action Controller")]
        private static void DumpFromAsset()
        {
            DumpFromSelectedGameObject();
        }

        [MenuItem("Assets/AFK Manager/Dump Action Controller", true)]
        private static bool DumpFromAssetValidate()
        {
            return HasSelectedDescriptor();
        }

        [MenuItem("GameObject/AFK Manager/Dump Action Controller", false, 49)]
        private static void DumpFromHierarchy()
        {
            DumpFromSelectedGameObject();
        }

        [MenuItem("GameObject/AFK Manager/Dump Action Controller", true)]
        private static bool DumpFromHierarchyValidate()
        {
            return HasSelectedDescriptor();
        }

        private static bool HasSelectedDescriptor()
        {
            var obj = Selection.activeGameObject;
            return obj != null && ActionControllerResolver.TryResolve(obj, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Action, out _) != null;
        }

        private static void DumpFromSelectedGameObject()
        {
            var obj = Selection.activeGameObject;
            var controller = ActionControllerResolver.TryResolve(obj, VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.Action, out var error);
            if (controller == null)
            {
                UnityEngine.Debug.LogWarning($"[AFK Manager] {error}");
                return;
            }

            DumpAndWrite(controller);
        }

        // --- Core ---

        public static string DumpControllerAtPath(string assetPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
            {
                UnityEngine.Debug.LogWarning($"[AFK Manager] No AnimatorController at {assetPath}.");
                return null;
            }
            var content = BuildDump(controller);
            var path = WriteToFile(content, controller.name);
            UnityEngine.Debug.Log($"[AFK Manager] Controller dump saved to {path}");
            return path;
        }

        private static void DumpAndWrite(AnimatorController controller)
        {
            var content = BuildDump(controller);
            var path = WriteToFile(content, controller.name);
            UnityEngine.Debug.Log($"[AFK Manager] Controller dump saved to {path}");
        }

        private static string BuildDump(AnimatorController controller)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Controller: {controller.name} ===");
            sb.AppendLine();

            // Parameters
            sb.AppendLine("--- Parameters ---");
            foreach (var p in controller.parameters)
                sb.AppendLine($"  {p.name} ({p.type}) default={FormatDefault(p)}");
            sb.AppendLine();

            // Layers
            for (var i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                sb.AppendLine($"--- Layer [{i}]: {layer.name} ---");
                sb.AppendLine($"  defaultState: {(layer.stateMachine.defaultState != null ? layer.stateMachine.defaultState.name : "(none)")}");
                sb.AppendLine($"  blendingMode: {layer.blendingMode}");
                sb.AppendLine($"  weight: {layer.defaultWeight}");
                sb.AppendLine();

                DumpStateMachine(sb, layer.stateMachine, "  ");
            }

            return sb.ToString();
        }

        private static string WriteToFile(string content, string controllerName)
        {
            if (!Directory.Exists(OutputDir))
                Directory.CreateDirectory(OutputDir);

            var safeName = SanitizeFileName(controllerName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var path = $"{OutputDir}/dump_{safeName}_{timestamp}.txt";

            File.WriteAllText(path, content);
            AssetDatabase.Refresh();
            return path;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            return sb.ToString();
        }

        private static void DumpStateMachine(StringBuilder sb, AnimatorStateMachine sm, string indent)
        {
            // States
            sb.AppendLine($"{indent}States ({sm.states.Length}):");
            foreach (var cs in sm.states)
            {
                var s = cs.state;
                var motionName = s.motion != null ? s.motion.name : "(none)";
                var isDefault = sm.defaultState == s ? " [DEFAULT]" : "";
                var tagStr = string.IsNullOrEmpty(s.tag) ? "" : $"  tag={s.tag}";
                var speedParam = s.speedParameterActive ? $"  speedParam={s.speedParameter}" : "";
                var timeParam = s.timeParameterActive ? $"  timeParam={s.timeParameter}" : "";
                var mirrorParam = s.mirrorParameterActive ? $"  mirrorParam={s.mirrorParameter}" : "";
                var cycleParam = s.cycleOffsetParameterActive ? $"  cycleParam={s.cycleOffsetParameter}" : "";
                sb.AppendLine($"{indent}  [{s.name}]{isDefault}  motion={motionName}  wdv={s.writeDefaultValues}  speed={s.speed:F2}  mirror={s.mirror}  ikOnFeet={s.iKOnFeet}  cycleOffset={s.cycleOffset:F2}{tagStr}{speedParam}{timeParam}{mirrorParam}{cycleParam}  pos=({cs.position.x:F0},{cs.position.y:F0})");

                // State Behaviours
                if (s.behaviours != null && s.behaviours.Length > 0)
                {
                    foreach (var b in s.behaviours)
                        sb.AppendLine($"{indent}    behaviour: {FormatBehaviour(b)}");
                }

                // Transitions from this state (with index for ordering)
                for (var ti = 0; ti < s.transitions.Length; ti++)
                {
                    var t = s.transitions[ti];
                    sb.AppendLine($"{indent}    [{ti}]-> {FormatTransitionDest(t)}  {FormatConditions(t)}  exitTime={t.hasExitTime}/{t.exitTime:F2}  dur={t.duration:F2}  offset={t.offset:F2}  canSelf={t.canTransitionToSelf}  ordInt={t.orderedInterruption}  intSrc={t.interruptionSource}  mute={t.mute}  solo={t.solo}");
                }
            }
            sb.AppendLine();

            // AnyState Transitions (with index)
            if (sm.anyStateTransitions.Length > 0)
            {
                sb.AppendLine($"{indent}AnyState Transitions ({sm.anyStateTransitions.Length}):");
                for (var ti = 0; ti < sm.anyStateTransitions.Length; ti++)
                {
                    var t = sm.anyStateTransitions[ti];
                    sb.AppendLine($"{indent}  [{ti}] AnyState -> {FormatTransitionDest(t)}  {FormatConditions(t)}  exitTime={t.hasExitTime}/{t.exitTime:F2}  dur={t.duration:F2}  offset={t.offset:F2}  canSelf={t.canTransitionToSelf}  ordInt={t.orderedInterruption}  intSrc={t.interruptionSource}  mute={t.mute}  solo={t.solo}");
                }
                sb.AppendLine();
            }

            // Entry Transitions
            if (sm.entryTransitions.Length > 0)
            {
                sb.AppendLine($"{indent}Entry Transitions ({sm.entryTransitions.Length}):");
                foreach (var t in sm.entryTransitions)
                {
                    var dest = t.destinationState != null ? t.destinationState.name
                        : t.destinationStateMachine != null ? $"[SubSM:{t.destinationStateMachine.name}]"
                        : "(null)";
                    var conds = FormatBaseConditions(t);
                    sb.AppendLine($"{indent}  Entry -> {dest}  {conds}");
                }
                sb.AppendLine();
            }

            // SubStateMachines
            if (sm.stateMachines.Length > 0)
            {
                sb.AppendLine($"{indent}SubStateMachines ({sm.stateMachines.Length}):");
                foreach (var csm in sm.stateMachines)
                {
                    sb.AppendLine($"{indent}  [SubSM: {csm.stateMachine.name}]  pos=({csm.position.x:F0},{csm.position.y:F0})");
                    DumpStateMachine(sb, csm.stateMachine, indent + "    ");
                }
            }
        }

        private static string FormatTransitionDest(AnimatorStateTransition t)
        {
            if (t.isExit) return "(Exit)";
            if (t.destinationState != null) return t.destinationState.name;
            if (t.destinationStateMachine != null) return $"[SubSM:{t.destinationStateMachine.name}]";
            return "(null)";
        }

        private static string FormatConditions(AnimatorStateTransition t)
        {
            if (t.conditions.Length == 0) return "cond=[]";
            var parts = new string[t.conditions.Length];
            for (var i = 0; i < t.conditions.Length; i++)
            {
                var c = t.conditions[i];
                parts[i] = $"{c.parameter} {c.mode}" + (c.mode == AnimatorConditionMode.Greater || c.mode == AnimatorConditionMode.Less ? $" {c.threshold}" : "");
            }
            return $"cond=[{string.Join(", ", parts)}]";
        }

        private static string FormatBaseConditions(AnimatorTransition t)
        {
            if (t.conditions.Length == 0) return "cond=[]";
            var parts = new string[t.conditions.Length];
            for (var i = 0; i < t.conditions.Length; i++)
            {
                var c = t.conditions[i];
                parts[i] = $"{c.parameter} {c.mode}" + (c.mode == AnimatorConditionMode.Greater || c.mode == AnimatorConditionMode.Less ? $" {c.threshold}" : "");
            }
            return $"cond=[{string.Join(", ", parts)}]";
        }

        private static string FormatBehaviour(StateMachineBehaviour b)
        {
            if (b == null) return "(null)";
            var typeName = b.GetType().Name;

            // Extract key properties via serialized object
            var so = new SerializedObject(b);
            var sb = new StringBuilder(typeName);
            sb.Append(" {");

            var prop = so.GetIterator();
            var first = true;
            prop.NextVisible(true); // skip m_Script
            while (prop.NextVisible(false))
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append($"{prop.name}={FormatSerializedValue(prop)}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private static string FormatSerializedValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue.ToString();
                case SerializedPropertyType.Boolean: return prop.boolValue.ToString();
                case SerializedPropertyType.Float: return prop.floatValue.ToString("F2");
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum: return prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                    ? prop.enumDisplayNames[prop.enumValueIndex] : prop.enumValueIndex.ToString();
                case SerializedPropertyType.ObjectReference: return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "(null)";
                default:
                    if (prop.isArray)
                        return FormatArray(prop);
                    if (prop.propertyType == SerializedPropertyType.Generic && prop.hasChildren)
                        return FormatComposite(prop);
                    return $"({prop.propertyType})";
            }
        }

        private static string FormatArray(SerializedProperty arrayProp)
        {
            var sb = new StringBuilder("[");
            for (var i = 0; i < arrayProp.arraySize; i++)
            {
                if (i > 0) sb.Append(", ");
                var elem = arrayProp.GetArrayElementAtIndex(i);
                sb.Append(FormatSerializedValue(elem));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string FormatComposite(SerializedProperty parent)
        {
            var sb = new StringBuilder("{");
            var endProp = parent.GetEndProperty();
            var iter = parent.Copy();
            if (!iter.NextVisible(true) || SerializedProperty.EqualContents(iter, endProp))
            {
                sb.Append("}");
                return sb.ToString();
            }
            var first = true;
            do
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append($"{iter.name}={FormatSerializedValue(iter)}");
            } while (iter.NextVisible(false) && !SerializedProperty.EqualContents(iter, endProp));
            sb.Append("}");
            return sb.ToString();
        }

        private static string FormatDefault(AnimatorControllerParameter p)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float: return p.defaultFloat.ToString("F2");
                case AnimatorControllerParameterType.Int: return p.defaultInt.ToString();
                case AnimatorControllerParameterType.Bool: return p.defaultBool.ToString();
                case AnimatorControllerParameterType.Trigger: return "trigger";
                default: return "?";
            }
        }
    }
}

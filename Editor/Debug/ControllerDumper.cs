using System;
using System.IO;
using System.Linq;
using System.Text;
using Sebanne.AfkChanger.Editor.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sebanne.AfkChanger.Editor.Debug
{
    internal static class ControllerDumper
    {
        private const string OutputDir = "Assets/_Temp";

        // --- Tools menu (existing) ---

        [MenuItem("Tools/AFK Changer/Dump Selected Controller")]
        private static void DumpSelected()
        {
            var obj = Selection.activeObject;
            if (obj == null || !(obj is AnimatorController controller))
            {
                UnityEngine.Debug.LogWarning("[AFK Changer] Select an AnimatorController in the Project window.");
                return;
            }

            DumpAndWrite(controller);
        }

        [MenuItem("Tools/AFK Changer/Dump Selected Controller", true)]
        private static bool DumpSelectedValidate()
        {
            return Selection.activeObject is AnimatorController;
        }

        // --- Assets context menu: AnimatorController ---

        [MenuItem("Assets/AFK Changer/Dump Controller")]
        private static void DumpControllerAsset()
        {
            var obj = Selection.activeObject;
            if (obj is AnimatorController controller)
                DumpAndWrite(controller);
        }

        [MenuItem("Assets/AFK Changer/Dump Controller", true)]
        private static bool DumpControllerAssetValidate()
        {
            return Selection.activeObject is AnimatorController;
        }

        // --- Assets context menu: Prefab / Hierarchy: GameObject with VRCAvatarDescriptor ---

        [MenuItem("Assets/AFK Changer/Dump Action Controller")]
        private static void DumpFromAsset()
        {
            DumpFromSelectedGameObject();
        }

        [MenuItem("Assets/AFK Changer/Dump Action Controller", true)]
        private static bool DumpFromAssetValidate()
        {
            return HasSelectedDescriptor();
        }

        [MenuItem("GameObject/AFK Changer/Dump Action Controller", false, 49)]
        private static void DumpFromHierarchy()
        {
            DumpFromSelectedGameObject();
        }

        [MenuItem("GameObject/AFK Changer/Dump Action Controller", true)]
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
                UnityEngine.Debug.LogWarning($"[AFK Changer] {error}");
                return;
            }

            DumpAndWrite(controller);
        }

        // --- Core ---

        private static void DumpAndWrite(AnimatorController controller)
        {
            var content = BuildDump(controller);
            var path = WriteToFile(content, controller.name);
            UnityEngine.Debug.Log($"[AFK Changer] Controller dump saved to {path}");
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
                sb.AppendLine($"{indent}  [{s.name}]{isDefault}  motion={motionName}  wdv={s.writeDefaultValues}  pos=({cs.position.x:F0},{cs.position.y:F0})");

                // State Behaviours
                if (s.behaviours != null && s.behaviours.Length > 0)
                {
                    foreach (var b in s.behaviours)
                        sb.AppendLine($"{indent}    behaviour: {FormatBehaviour(b)}");
                }

                // Transitions from this state
                foreach (var t in s.transitions)
                    sb.AppendLine($"{indent}    -> {FormatTransitionDest(t)}  {FormatConditions(t)}  exitTime={t.hasExitTime}/{t.exitTime:F2}  dur={t.duration:F2}");
            }
            sb.AppendLine();

            // AnyState Transitions
            if (sm.anyStateTransitions.Length > 0)
            {
                sb.AppendLine($"{indent}AnyState Transitions ({sm.anyStateTransitions.Length}):");
                foreach (var t in sm.anyStateTransitions)
                    sb.AppendLine($"{indent}  AnyState -> {FormatTransitionDest(t)}  {FormatConditions(t)}  exitTime={t.hasExitTime}/{t.exitTime:F2}  dur={t.duration:F2}  canSelf={t.canTransitionToSelf}");
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
                default: return $"({prop.propertyType})";
            }
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

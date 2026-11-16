using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Apl.Client.EditorTools
{
    /// <summary>
    /// Builds the three animator controllers from the Mixamo clips: Seated
    /// (team tables), Auctioneer, Assistant. Each is an idle with one-shot
    /// reactions fired by triggers, IK pass on so the paddle raise works.
    /// </summary>
    public static class AnimatorBuilder
    {
        public const string Folder = "Assets/Apl/Generated/Animators";
        private const string Mixamo = "Assets/ThirdParty/Mixamo";

        public static AnimationClip? Clip(string name)
        {
            var path = $"{Mixamo}/{name}.fbx";
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        [MenuItem("APL/Build Animators")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Folder);
            Build("Seated", "sit_idle", "sit_idle_2", new Dictionary<string, string>
            {
                ["Talk"] = "sit_talk",
                ["Point"] = "sit_point",
                ["Clap"] = "sit_clap",
                ["ThumbsUp"] = "sit_thumbs_up",
                ["Disbelief"] = "sit_disbelief",
                ["Laugh"] = "sit_laugh",
                ["Talk2"] = "sit_talk_2",
                ["No"] = "sit_no",
                ["FistPump"] = "seated_fist_pump",
                ["NodYes"] = "seated_nod_yes",
                // Conversations at the table: lean in to discuss, or turn and talk to
                // the neighbour on the left (mirrored for the right).
                ["Discuss"] = "seated_lean_in_discuss_loop",
                ["TalkLeft"] = "seated_talk_to_left_loop",
                ["TalkRight"] = "seated_talk_to_left_loop",
            }, mirrored: new HashSet<string> { "TalkRight" });
            // Standing gestures played on the upper body only, so seated people stay seated.
            AddUpperBodyGestures(Load("Seated"), new Dictionary<string, string> { ["RaiseNo"] = "gen_raise_no" });
            // Typing: arms and fingers only (the clip's head turn is far past natural).
            AddUpperBodyGestures(Load("Seated"), new Dictionary<string, string> { ["Typing"] = "seated_laptop_typing_loop" }, withHead: false);
            // The gavel is in his right hand: point with the left (mirrored clip),
            // and keep the gavel arm still so no gesture looks like a hammer
            // stroke. Only the SOLD strike (procedural) moves it.
            var point = Clip("standing_auctioneer_point") != null ? "standing_auctioneer_point" : "point";
            Build("Auctioneer", "stand_idle", null, new Dictionary<string, string>
            {
                ["Talk"] = "talk_1",
                ["Point"] = point,
                ["Raise"] = "hand_raise",
                ["Clap"] = "clap_stand",
            }, mirrored: new HashSet<string> { "Point" });
            PinArm(Load("Auctioneer"), "stand_idle", AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.RightFingers);
            // Raise = the hand-over gesture: a "give" clip, else the generated card hand-over.
            Build("Assistant", "stand_idle", null, new Dictionary<string, string>
            {
                ["Pick"] = "pick_up",
                ["Raise"] = Clip("give") != null ? "give" : Clip("standing_hand_card") != null ? "standing_hand_card" : "hand_raise",
                ["Clap"] = "clap_stand",
                ["Talk"] = "talk_1",
            });
            AddWalk(Load("Assistant"));
            // Photographers in front of the stage (ceremony): idle, or a photo pose if downloaded.
            Build("Media", Clip("photo") != null ? "photo" : Clip("standing_photographer_camera") != null ? "standing_photographer_camera" : "stand_idle",
                null, new Dictionary<string, string>());
            BuildCeremony();
            BuildCricket();
            AssetDatabase.SaveAssets();
            Debug.Log("[APL] Animators built");
        }

        private const string CricketFolder = "Assets/ThirdParty/Cricket";

        /// <summary>
        /// Match mode: one controller for everyone on the field, one state per
        /// clip (named after the file), played from code at exact times — the
        /// shot's contact frame, the bowler's release frame, a fielder's catch.
        /// "Stance" holds the batting stance (frame 1 of the forward defence).
        /// A Mirror parameter turns any clip left-handed.
        /// </summary>
        private static void BuildCricket()
        {
            var path = $"{Folder}/Cricket.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Mirror", AnimatorControllerParameterType.Bool);
            var layer = controller.layers[0];
            layer.iKPass = true;
            var sm = layer.stateMachine;
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CricketFolder }))
            {
                var file = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip != null) clips[Path.GetFileNameWithoutExtension(file)] = clip;
            }
            AnimatorState Add(string name, Motion? motion, float speed = 1f)
            {
                var state = sm.AddState(name);
                state.motion = motion;
                state.speed = speed;
                state.mirrorParameterActive = true;
                state.mirrorParameter = "Mirror";
                return state;
            }
            // Default: a fielder's ready stance (or standing, before the clips exist).
            var ready = Add("Ready", clips.TryGetValue("run_00_ready_stance_idle_loop", out var r) ? r : Clip("stand_idle"));
            sm.defaultState = ready;
            if (clips.TryGetValue("01_forward_defence", out var stance)) Add("Stance", stance, 0f);
            // The left-hander's own stance: frame 1 of his forward defence (the pose his shots start from).
            if (clips.TryGetValue("lhb_01_forward_defence", out var lstance)) Add("StanceLhb", lstance, 0f);
            // Stance idle loops, one per hand. Until there's a right-handed loop, the left-handed
            // one mirrored stands in — a mirrored clip has the bowler on the batter's other side,
            // so BatterRig turns him round for it (and back for every shot).
            if (clips.TryGetValue("lhb_00_stance_idle_loop", out var guardLeft))
            {
                var left = sm.AddState("StanceLeft");
                left.motion = guardLeft;
                left.mirrorParameterActive = false;
                var right = sm.AddState("StanceRight");
                right.mirrorParameterActive = false;
                if (clips.TryGetValue("00_stance_idle_loop", out var guardRight)) right.motion = guardRight;
                else
                {
                    right.motion = guardLeft;
                    right.mirror = true;
                }
            }
            Add("Stand", Clip("stand_idle"));
            foreach (var kv in clips.OrderBy(k => k.Key, System.StringComparer.Ordinal))
            {
                var st = Add(kv.Key, kv.Value);
                // Round 10c (characters, the walk-out slide): the walking and jogging loops plant their feet
                // where the clip's own rig put them (humanoid Foot IK) — retargeted onto a body with other leg
                // proportions, the leading foot otherwise slid against the trailing one in double support.
                // Owner v26 #17 (sliding for anyone): the same for every clip that carries its travel or its footwork
                // in the pose — the bowlers' run-ups and follow-throughs, the walk back, running between the wickets,
                // the fielders' chases, catches and throws, the keeper, the celebrations and walk-offs, the batter's
                // backing-up and footwork. Not the batting shots (their bat paths are measured on the pose as it is).
                if (IsLocomotion(kv.Key) || IsTravel(kv.Key)) st.iKOnFeet = true;
            }
            var layers = controller.layers;
            layers[0] = layer;
            controller.layers = layers;
            Debug.Log($"[APL] Cricket controller: {clips.Count} clips");
            ProbeRestPoses();
            foreach (var probe in new[] { "01_forward_defence", "run_00_ready_stance_idle_loop", "ra_express_fast_over_stock", "run_01_walk_in_loop", "run_02_jog_loop", "run_03_sprint_loop" })
            {
                var file = AssetDatabase.FindAssets(probe, new[] { CricketFolder }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
                if (file == null || !(AssetImporter.GetAtPath(file) is ModelImporter mi) || mi.clipAnimations.Length == 0) continue;
                var c = mi.clipAnimations[0];
                Debug.Log($"[APL]   {probe}: frames {c.firstFrame}..{c.lastFrame}, length {(clips.TryGetValue(probe, out var ac) ? ac.length : -1):0.000}s, ground speed {(ac != null ? ac.averageSpeed.magnitude : -1):0.00} m/s");
            }
        }

        /// <summary>
        /// The pose each humanoid avatar takes as its zero (the T-pose the muscles are measured
        /// from): a motion clip's avatar and a body's avatar must agree, or every clip comes out
        /// offset (knees too straight, head tipped). Logs knee bend and trunk/head tilt as Unity
        /// sees them, from the avatar's own skeleton description.
        /// </summary>
        private static void ProbeRestPoses()
        {
            var files = new List<string>
            {
                $"{CricketFolder}/batting/00_stance_idle_loop.fbx", $"{CricketFolder}/batting/03_on_drive.fbx",
                $"{CricketFolder}/fielding/run_00_ready_stance_idle_loop.fbx", $"{Mixamo}/stand_idle.fbx",
            };
            files.AddRange(AssetDatabase.FindAssets("t:Model", new[] { "Assets/ThirdParty/Rocketbox" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".fbx") && !p.Contains("_facial")).Take(3));
            foreach (var file in files)
            {
                if (!(AssetImporter.GetAtPath(file) is ModelImporter mi)) continue;
                var desc = mi.humanDescription;
                if (desc.skeleton == null || desc.skeleton.Length == 0)
                {
                    Debug.Log($"[APL] rest {file}: no skeleton description (avatar {mi.avatarSetup})");
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                try
                {
                    // The avatar's zero pose: each SkeletonBone is local to its parent in the model's hierarchy.
                    var byName = new Dictionary<string, Transform>();
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);
                    var defaultKnee = Knee();
                    foreach (var sb in desc.skeleton)
                    {
                        if (!byName.TryGetValue(sb.name, out var t) || t == go.transform) continue;
                        t.localPosition = sb.position;
                        t.localRotation = sb.rotation;
                        t.localScale = sb.scale;
                    }
                    Transform? Bone(string human)
                    {
                        foreach (var h in desc.human)
                        {
                            if (h.humanName == human) return byName.TryGetValue(h.boneName, out var t) ? t : null;
                        }
                        return null;
                    }
                    float Knee()
                    {
                        var a = Bone("LeftUpperLeg"); var b = Bone("LeftLowerLeg"); var c = Bone("LeftFoot");
                        return a == null || b == null || c == null ? -1 : Vector3.Angle(b.position - a.position, c.position - b.position);
                    }
                    float Tilt(string from, string to)
                    {
                        var a = Bone(from); var b = Bone(to);
                        return a == null || b == null ? -1 : Vector3.Angle(b.position - a.position, Vector3.up);
                    }
                    Debug.Log($"[APL] rest {file}: avatar zero knee {Knee():0.0} (file default {defaultKnee:0.0}) | hips→chest {Tilt("Hips", "Chest"):0.0} | neck→head {Tilt("Neck", "Head"):0.0} | upper arm {Tilt("LeftUpperArm", "LeftLowerArm"):0.0}");
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        /// <summary>Standing controller for the walk-on: Walking bool plus pose triggers.</summary>
        private static void BuildCeremony()
        {
            Build("Ceremony", "stand_idle", null, new Dictionary<string, string>
            {
                ["ArmsCrossed"] = "arms_crossed",
                ["FistPump"] = "fist_pump",
                ["Wave"] = "wave",
                ["Victory"] = "victory",
                ["ThumbsUp2"] = "standing_pose_double_thumbs_up",
                ["HandFlash"] = "standing_pose_hand_flash",
            });
            AddWalk(Load("Ceremony"));
        }

        /// <summary>A Walking bool and a Walk state any state can enter.</summary>
        private static void AddWalk(AnimatorController controller)
        {
            controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.states.First(s => s.state.name == "Idle").state;
            var walk = sm.AddState("Walk");
            // Until "Walking (In Place)" is downloaded, glide in the idle pose.
            walk.motion = Clip("walk") ?? Clip("stand_idle");
            var toWalk = sm.AddAnyStateTransition(walk);
            toWalk.AddCondition(AnimatorConditionMode.If, 0, "Walking");
            toWalk.duration = 0.2f;
            toWalk.canTransitionToSelf = false;
            var toIdle = walk.AddTransition(idle);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Walking");
            toIdle.duration = 0.25f;
        }

        /// <summary>
        /// A second layer masked to head, arms and hands: an empty default state,
        /// plus one state per gesture that returns to empty when it finishes.
        /// </summary>
        private static void AddUpperBodyGestures(AnimatorController controller, Dictionary<string, string> gestures, bool withHead = true)
        {
            var maskPath = withHead ? $"{Folder}/UpperBody.mask" : $"{Folder}/Arms.mask";
            AssetDatabase.DeleteAsset(maskPath);
            var mask = new AvatarMask();
            for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
            {
                var p = (AvatarMaskBodyPart)part;
                mask.SetHumanoidBodyPartActive(p, (withHead && p == AvatarMaskBodyPart.Head) || p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm
                                                  || p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);
            }
            AssetDatabase.CreateAsset(mask, maskPath);

            var sm = new AnimatorStateMachine { name = withHead ? "Gestures" : "ArmGestures", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var empty = sm.AddState("Empty");
            sm.defaultState = empty;
            var added = 0;
            foreach (var kv in gestures)
            {
                var clip = Clip(kv.Value);
                if (clip == null) continue;
                controller.AddParameter(kv.Key, AnimatorControllerParameterType.Trigger);
                var state = sm.AddState(kv.Key);
                state.motion = clip;
                var enter = sm.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, kv.Key);
                enter.duration = 0.2f;
                enter.canTransitionToSelf = false;
                var back = state.AddTransition(empty);
                back.hasExitTime = true;
                back.exitTime = 0.95f;
                back.duration = 0.25f;
                added++;
            }
            if (added == 0) return;
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = withHead ? "Gestures" : "ArmGestures",
                stateMachine = sm,
                avatarMask = mask,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            });
        }

        /// <summary>
        /// A top layer that holds the given body parts in <paramref name="clip"/>'s
        /// pose whatever the base layer plays (procedural poses still win: they
        /// run after the animator).
        /// </summary>
        private static void PinArm(AnimatorController controller, string clip, params AvatarMaskBodyPart[] parts)
        {
            var motion = Clip(clip);
            if (motion == null) return;
            var maskPath = $"{Folder}/{controller.name}Pin.mask";
            AssetDatabase.DeleteAsset(maskPath);
            var mask = new AvatarMask();
            for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
            {
                var p = (AvatarMaskBodyPart)part;
                mask.SetHumanoidBodyPartActive(p, parts.Contains(p));
            }
            AssetDatabase.CreateAsset(mask, maskPath);
            var sm = new AnimatorStateMachine { name = "Pin", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            sm.defaultState = sm.AddState("Hold");
            sm.defaultState.motion = motion;
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "Pin",
                stateMachine = sm,
                avatarMask = mask,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            });
        }

        public static AnimatorController Load(string name) =>
            AssetDatabase.LoadAssetAtPath<AnimatorController>($"{Folder}/{name}.controller");

        private static void Build(string name, string idle, string? idleVariant, Dictionary<string, string> oneShots, HashSet<string>? mirrored = null)
        {
            var path = $"{Folder}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Variant", AnimatorControllerParameterType.Float);
            var layer = controller.layers[0];
            layer.iKPass = true;
            var sm = layer.stateMachine;

            // Idle: blend two idles by a per-character random value for variety.
            var idleState = sm.AddState("Idle");
            var idleClip = Clip(idle);
            var variantClip = idleVariant == null ? null : Clip(idleVariant);
            if (idleClip != null && variantClip != null)
            {
                var tree = new BlendTree { name = "IdleBlend", blendParameter = "Variant", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.AddChild(idleClip, 0f);
                tree.AddChild(variantClip, 1f);
                idleState.motion = tree;
            }
            else
            {
                idleState.motion = idleClip;
            }
            sm.defaultState = idleState;

            foreach (var kv in oneShots)
            {
                var clip = Clip(kv.Value);
                if (clip == null)
                {
                    Debug.LogWarning($"[APL] {name}: missing clip {kv.Value}, trigger {kv.Key} skipped");
                    continue;
                }
                controller.AddParameter(kv.Key, AnimatorControllerParameterType.Trigger);
                var state = sm.AddState(kv.Key);
                state.motion = clip;
                state.mirror = mirrored != null && mirrored.Contains(kv.Key);
                var enter = sm.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, kv.Key);
                enter.duration = 0.25f;
                enter.canTransitionToSelf = false;
                var back = state.AddTransition(idleState);
                back.hasExitTime = true;
                back.exitTime = 0.92f;
                back.duration = 0.3f;
            }

            var layers = controller.layers;
            layers[0] = layer;
            controller.layers = layers;
        }

        /// <summary>Walk / jog / run loops: the clips whose feet plant on the ground while the body is carried.</summary>
        /// <summary>
        /// Everything but a batting shot: shots are the numbered clips ("01_forward_defence", "lhb_63_..."), except the
        /// non-striker's idle and backing up (75-77) and the footwork moves (82-84), which walk.
        /// </summary>
        private static bool IsTravel(string name)
        {
            var stem = name.StartsWith("lhb_") ? name.Substring(4) : name;
            if (stem.Length < 3 || !char.IsDigit(stem[0]) || !char.IsDigit(stem[1]) || stem[2] != '_') return true;
            var n = (stem[0] - '0') * 10 + (stem[1] - '0');
            return n >= 75 && n <= 77 || n >= 82 && n <= 84;
        }

        private static bool IsLocomotion(string name) =>
            name.Contains("walk_out") || name.Contains("walk_in_loop") || name.Contains("walk_loop") || name.Contains("_jog_loop")
            || name.Contains("sprint_loop") || name.Contains("run_loop_with_bat") || name.Contains("walk_back");
    }
}

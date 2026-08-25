using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using SevenZip;
using System;
using System.Text.RegularExpressions;

namespace AnimeStudio
{
    public static class AnimationClipExtensions
    {
        public static float DefaultFloatWeight => 1.0f / 3.0f;
        public static Vector3 DefaultVector3Weight => new Vector3(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f);
        public static Quaternion DefaultQuaternionWeight => new Quaternion(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f);
        public static readonly Dictionary<uint, string> GlobalTOS = new Dictionary<uint, string>();

        static AnimationClipExtensions()
        {
            try
            {
                var assembly = typeof(AnimationClipExtensions).Assembly;
                var resName = assembly.GetManifestResourceNames().FirstOrDefault(x => x.EndsWith("global_tos_dict.json"));
                if (resName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resName);
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        var json = reader.ReadToEnd();
                        var dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                        if (dict != null)
                        {
                            foreach (var kvp in dict)
                            {
                                if (uint.TryParse(kvp.Key, out uint hash))
                                {
                                    GlobalTOS[hash] = kvp.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        #region AnimationClip
        public static IEnumerable<GameObject> FindRoots(this AnimationClip clip)
        {
            foreach (var asset in clip.assetsFile.assetsManager.assetsFileList.SelectMany(x => x.Objects))
            {
                switch (asset.type)
                {
                    case ClassIDType.Animator:
                        Animator animator = (Animator)asset;
                        if (clip.IsAnimatorContainsClip(animator))
                        {
                            if (animator.m_GameObject.TryGet(out var go))
                            {
                                yield return go;
                            }
                        }
                        break;

                    case ClassIDType.Animation:
                        Animation animation = (Animation)asset;
                        if (clip.IsAnimationContainsClip(animation))
                        {
                            if (animation.m_GameObject.TryGet(out var go))
                            {
                                yield return go;
                            }
                        }
                        break;
                }
            }

            yield break;
        }
        public static Dictionary<uint, string> FindTOS(this AnimationClip clip)
        {
            var tos = new Dictionary<uint, string>() { { 0, string.Empty } };

            // 1. Check GlobalTOS (populated with all known avatars/models)
            clip.AddTOS(GlobalTOS, tos);
            clip.AddTOS(Avatar.GlobalTOS, tos);

            // 2. Check loaded assets in current files
            var allObjects = clip.assetsFile?.assetsManager?.assetsFileList?.SelectMany(x => x.Objects)?.ToArray();
            if (allObjects != null)
            {
                foreach (var avatar in allObjects.OfType<Avatar>())
                {
                    clip.AddAvatarTOS(avatar, tos);
                }
                foreach (var animator in allObjects.OfType<Animator>())
                {
                    clip.AddAnimatorTOS(animator, tos);
                }
                foreach (var controller in allObjects.OfType<AnimatorController>())
                {
                    if (controller.m_TOS != null)
                    {
                        clip.AddTOS(controller.m_TOS, tos);
                    }
                }
                foreach (var go in allObjects.OfType<GameObject>())
                {
                    if (go.m_Transform != null && !go.m_Transform.m_Father.TryGet(out _))
                    {
                        var goTos = go.BuildTOS();
                        clip.AddTOS(goTos, tos);
                    }
                }
                foreach (var animation in allObjects.OfType<Animation>())
                {
                    clip.AddAnimationTOS(animation, tos);
                }
            }

            return tos;
        }
        private static bool AddAvatarTOS(this AnimationClip clip, Avatar avatar, Dictionary<uint, string> tos)
        {
            if (avatar?.m_TOS == null) return false;
            return clip.AddTOS(avatar.m_TOS, tos);
        }
        private static bool AddAnimatorTOS(this AnimationClip clip, Animator animator, Dictionary<uint, string> tos)
        {
            bool added = false;
            if (animator.m_Avatar.TryGet(out var avatar))
            {
                if (clip.AddAvatarTOS(avatar, tos))
                {
                    added = true;
                }
            }

            Dictionary<uint, string> animatorTOS = animator.BuildTOS();
            if (animatorTOS != null && clip.AddTOS(animatorTOS, tos))
            {
                added = true;
            }

            return added;
        }
        private static bool AddAnimationTOS(this AnimationClip clip, Animation animation, Dictionary<uint, string> tos)
        {
            if (animation.m_GameObject.TryGet(out var go))
            {
                Dictionary<uint, string> animationTOS = go.BuildTOS();
                return clip.AddTOS(animationTOS, tos);
            }
            return false;
        }
        private static bool AddTOS(this AnimationClip clip, Dictionary<uint, string> src, Dictionary<uint, string> dest)
        {
            if (src == null || clip.m_ClipBindingConstant?.genericBindings == null) return false;
            int tosCount = clip.m_ClipBindingConstant.genericBindings.Count;
            for (int i = 0; i < tosCount; i++)
            {
                var binding = clip.m_ClipBindingConstant.genericBindings[i];
                if (src.TryGetValue(binding.path, out string path))
                {
                    dest[binding.path] = path;
                }
            }
            return dest.Count >= tosCount;
        }
        private static bool IsAnimationContainsClip(this AnimationClip clip, Animation animation)
        {
            return animation.IsContainsAnimationClip(clip);
        }
        private static bool IsAnimatorContainsClip(this AnimationClip clip, Animator animator)
        {
            if (animator.m_Controller.TryGet(out var runtime))
            {
                return runtime.IsContainsAnimationClip(clip);
            }
            else
            {
                return false;
            }
        }
        public static string Convert(this AnimationClip clip)
        {
            if (!clip.m_Legacy || clip.m_MuscleClip != null)
            {
                var converter = AnimationClipConverter.Process(clip);
                clip.m_RotationCurves = converter.Rotations.Union(clip.m_RotationCurves).ToList();
                clip.m_EulerCurves = converter.Eulers.Union(clip.m_EulerCurves).ToList();
                clip.m_PositionCurves = converter.Translations.Union(clip.m_PositionCurves).ToList();
                clip.m_ScaleCurves = converter.Scales.Union(clip.m_ScaleCurves).ToList();
                clip.m_FloatCurves = converter.Floats.Union(clip.m_FloatCurves).ToList();
                clip.m_PPtrCurves = converter.PPtrs.Union(clip.m_PPtrCurves).ToList();
            }
            return ConvertSerializedAnimationClip(clip);
        }
        public static string ConvertSerializedAnimationClip(AnimationClip animationClip)
        {
            var sb = new StringBuilder();
            using (var stringWriter = new StringWriter(sb))
            {
                YAMLWriter writer = new YAMLWriter();
                YAMLDocument doc = ExportYAMLDocument(animationClip);
                writer.AddDocument(doc);
                writer.Write(stringWriter);
                
                string result = sb.ToString();
                
                result = Regex.Replace(result, @"(path:\s*)(\d+)", m => 
                {
                    if (uint.TryParse(m.Groups[2].Value, out uint hash) && GlobalTOS.TryGetValue(hash, out string p))
                        return m.Groups[1].Value + p;
                    return m.Value;
                });

                result = Regex.Replace(result, @"path_(\d+)", m => 
                {
                    if (uint.TryParse(m.Groups[1].Value, out uint hash) && GlobalTOS.TryGetValue(hash, out string p))
                        return p;
                    return m.Value;
                });

                return result;
            }
        }

        public static YAMLDocument ExportYAMLDocument(AnimationClip animationClip)
        {
            var document = new YAMLDocument();
            var root = document.CreateMappingRoot();
            root.Tag = ((int)ClassIDType.AnimationClip).ToString();
            root.Anchor = ((int)ClassIDType.AnimationClip * 100000).ToString();
            var node = animationClip.ExportYAML(animationClip.version);
            root.Add(ClassIDType.AnimationClip.ToString(), node);
            return document;
        }
        public static YAMLMappingNode ExportYAML(this AnimationClip clip, int[] version)
        {
            var node = new YAMLMappingNode();
            node.Add(nameof(clip.m_Name), clip.m_Name);
            node.AddSerializedVersion(ToSerializedVersion(version));
            node.Add(nameof(clip.m_Legacy), clip.m_Legacy);
            node.Add(nameof(clip.m_Compressed), clip.m_Compressed);
            node.Add(nameof(clip.m_UseHighQualityCurve), clip.m_UseHighQualityCurve);
            node.Add(nameof(clip.m_RotationCurves), clip.m_RotationCurves.ExportYAML(version));
            node.Add(nameof(clip.m_CompressedRotationCurves), clip.m_CompressedRotationCurves.ExportYAML(version));
            node.Add(nameof(clip.m_EulerCurves), clip.m_EulerCurves.ExportYAML(version));
            node.Add(nameof(clip.m_PositionCurves), clip.m_PositionCurves.ExportYAML(version));
            node.Add(nameof(clip.m_ScaleCurves), clip.m_ScaleCurves.ExportYAML(version));
            node.Add(nameof(clip.m_FloatCurves), clip.m_FloatCurves.ExportYAML(version));
            node.Add(nameof(clip.m_PPtrCurves), clip.m_PPtrCurves.ExportYAML(version));
            node.Add(nameof(clip.m_SampleRate), clip.m_SampleRate);
            node.Add(nameof(clip.m_WrapMode), clip.m_WrapMode);
            node.Add(nameof(clip.m_Bounds), clip.m_Bounds.ExportYAML(version));
            node.Add(nameof(clip.m_ClipBindingConstant), clip.m_ClipBindingConstant.ExportYAML(version));
            node.Add("m_AnimationClipSettings", clip.m_MuscleClip != null ? clip.m_MuscleClip.ExportYAML(version) : new YAMLMappingNode());
            node.Add(nameof(clip.m_Events), clip.m_Events.ExportYAML(version));
            return node;
        }

        public static int ToSerializedVersion(int[] version)
        {
            if (version[0] >= 5)
            {
                return 6;
            }
            if (version[0] > 4 || (version[0] == 4 && version[1] >= 3))
            {
                return 4;
            }
            if (version[0] > 2 || (version[0] == 2 && version[1] >= 6))
            {
                return 3;
            }
            return 2;
        }
        #endregion

        #region Others
        private static bool IsContainsAnimationClip(this Animation animation, AnimationClip clip)
        {
            foreach (PPtr<AnimationClip> ptr in animation.m_Animations)
            {
                if (ptr.TryGet(out var animationClip) && animationClip.Equals(clip))
                {
                    return true;
                }
            }
            return false;
        }
        
        private static Dictionary<uint, string> BuildTOS(this Animator animator)
        {
            if (animator.version[0] > 4 || (animator.version[0] == 4 && animator.version[1] >= 3))
            {
                if (animator.m_HasTransformHierarchy)
                {
                    if (animator.m_GameObject.TryGet(out var go))
                    {
                        return go.BuildTOS();
                    }
                }
                else
                {
                    return new Dictionary<uint, string>() { { 0, string.Empty } };
                }
            }
            else
            {
                if (animator.m_GameObject.TryGet(out var go))
                {
                    return go.BuildTOS();
                }
            }
            return null;
        }
        private static Dictionary<uint, string> BuildTOS(this GameObject gameObject)
        {
            Dictionary<uint, string> tos = new Dictionary<uint, string>() { { 0, string.Empty } };
            gameObject.BuildTOS(string.Empty, tos);
            return tos;
        }
        private static void BuildTOS(this GameObject parent, string parentPath, Dictionary<uint, string> tos)
        {
            Transform transform = parent.m_Transform;
            foreach (PPtr<Transform> childPtr in transform.m_Children)
            {
                if (childPtr.TryGet(out var childTransform))
                {
                    if (childTransform.m_GameObject.TryGet(out var child))
                    {
                        string path = parentPath != string.Empty ? parentPath + '/' + child.m_Name : child.m_Name;
                        var pathHash = CRC.CalculateDigestUTF8(path);
                        tos[pathHash] = path;
                        BuildTOS(child, path, tos);
                    }
                }
            }
        }

        
        private static bool IsContainsAnimationClip(this RuntimeAnimatorController runtimeAnimatorController, AnimationClip clip)
        {
            if (runtimeAnimatorController is AnimatorController animatorController)
            {
                foreach (PPtr<AnimationClip> ptr in animatorController.m_AnimationClips)
                {
                    if (ptr.TryGet(out var animationClip) && animationClip.Equals(clip))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        public static int GetDimension(this GenericBinding binding)
        {
            return binding.attribute == 2 ? 4 : 3;
        }

        public static HumanoidMuscleType GetHumanoidMuscle(this GenericBinding binding)
        {
            return ((HumanoidMuscleType)binding.attribute).Update(binding.version);
        }
        #endregion
    }

    public enum BindingCustomType : byte
    {
        None = 0,
        Transform = 4,
        AnimatorMuscle = 8,

        BlendShape = 20,
        Renderer = 21,
        RendererMaterial = 22,
        SpriteRenderer = 23,
        MonoBehaviour = 24,
        Light = 25,
        RendererShadows = 26,
        ParticleSystem = 27,
        RectTransform = 28,
        LineRenderer = 29,
        TrailRenderer = 30,
        PositionConstraint = 31,
        RotationConstraint = 32,
        ScaleConstraint = 33,
        AimConstraint = 34,
        ParentConstraint = 35,
        LookAtConstraint = 36,
        Camera = 37,
    }
}

﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Yuzu;

namespace Lime
{
	public class Model3DAttachment
	{
		public const string FileExtension = ".Attachment.txt";
		public const string DefaultAnimationName = "Default";

		public readonly ObservableCollection<MeshOption> MeshOptions = new ObservableCollection<MeshOption>();
		public readonly ObservableCollection<Animation> Animations = new ObservableCollection<Animation>();
		public readonly ObservableCollection<MaterialEffect> MaterialEffects = new ObservableCollection<MaterialEffect>();
		public float ScaleFactor { get; set; }

		public class MeshOption
		{
			public string Id { get; set; }
			public bool HitTestTarget { get; set; }
			public CullMode CullMode { get; set; }
		}

		public class Animation
		{
			public string Name { get; set; }
			public ObservableCollection<MarkerData> Markers = new ObservableCollection<MarkerData>();
			public ObservableCollection<NodeData> Nodes = new ObservableCollection<NodeData>();
			public ObservableCollection<NodeData> IgnoredNodes = new ObservableCollection<NodeData>();
			public BlendingOption Blending { get; set; }
			public readonly ObservableCollection<MarkerBlendingData> MarkersBlendings = new ObservableCollection<MarkerBlendingData>();
		}

		public class NodeData
		{
			public string Id { get; set; }
		}

		public class MarkerData
		{
			public Marker Marker { get; set; }
			public BlendingOption Blending { get; set; }
		}

		public class MarkerBlendingData
		{
			public string SourceMarkerId { get; set; }
			public string DestMarkerId { get; set; }
			public BlendingOption Blending { get; set; } = new BlendingOption();
		}

		public class MaterialEffect
		{
			public string Name { get; set; }
			public string MaterialName { get; set; }
			public string Path { get; set; }
			public BlendingOption Blending { get; set; }
		}

		public void Apply(Node3D model)
		{
			ProcessMeshOptions(model);
			ProcessAnimations(model);
			ProcessMaterialEffects(model);
		}

		public void ApplyScaleFactor(Node3D model)
		{
			if (ScaleFactor == 1) {
				return;
			}
			var sf = Vector3.One * ScaleFactor;
			var nodes = model.Descendants.OfType<Node3D>();
			Vector3 tranlation;
			Quaternion rotation;
			Vector3 scale;
			foreach (var node in nodes) {
				node.Position *= sf;
				if (node is Mesh3D) {
					foreach (var submesh in (node as Mesh3D).Submeshes) {
						foreach (VertexBuffer<Mesh3D.Vertex> vb in submesh.Mesh.VertexBuffers) {
							for (var i = 0; i < vb.Data.Length; i++) {
								vb.Data[i].Pos *= sf;
							}
						};
						for (int i = 0; i < submesh.BoneBindPoses.Count; i++) {
							submesh.BoneBindPoses[i].Decompose(out scale, out rotation, out tranlation);
							tranlation *= sf;
							submesh.BoneBindPoses[i] =
								Matrix44.CreateRotation(rotation) *
								Matrix44.CreateScale(scale) *
								Matrix44.CreateTranslation(tranlation);
						}
					}
				} else if (node is Camera3D) {
					var cam = node as Camera3D;
					cam.NearClipPlane *= ScaleFactor;
					cam.FarClipPlane *= ScaleFactor;
				}
				foreach (var animator in node.Animators) {
					if (animator.TargetProperty == "Position") {
						foreach (Keyframe<Vector3> key in animator.Keys) {
							key.Value *= sf;
						}
					}
				}
			}
		}

		private void ProcessMeshOptions(Node3D model)
		{
			if (MeshOptions.Count == 0) {
				return;
			}

			var meshes = model.Descendants
				.OfType<Mesh3D>()
				.Where(d => !string.IsNullOrEmpty(d.Id));
			foreach (var mesh in meshes) {
				foreach (var meshOption in MeshOptions) {
					if (mesh.Id != meshOption.Id) {
						continue;
					}

					if (meshOption.HitTestTarget) {
						mesh.HitTestTarget = true;
						mesh.SkipRender = true;
					}
					mesh.CullMode = meshOption.CullMode;
					break;
				}
			}
		}

		private void ProcessAnimations(Node3D model)
		{
			if (Animations.Count == 0) {
				return;
			}
			var srcAnimation = model.DefaultAnimation;
			foreach (var animationData in Animations) {
				var animation = srcAnimation;
				if (animationData.Name != DefaultAnimationName) {
					animation = new Lime.Animation {
						Id = animationData.Name
					};
					foreach (var node in GetAnimationNodes(model, animationData)) {
						CopyAnimationKeys(node, srcAnimation, animation);
					}
					model.Animations.Add(animation);
				}

				var animationBlending = new AnimationBlending() {
					Option = animationData.Blending
				};

				foreach (var markerData in animationData.Markers) {
					animation.Markers.AddOrdered(markerData.Marker.Clone());
					if (markerData.Blending != null) {
						animationBlending.MarkersOptions.Add(
							markerData.Marker.Id,
							new MarkerBlending {
								Option = markerData.Blending
							});
					}
				}

				foreach (var markersBlendings in animationData.MarkersBlendings) {
					if (!animationBlending.MarkersOptions.ContainsKey(markersBlendings.DestMarkerId)) {
						animationBlending.MarkersOptions.Add(markersBlendings.DestMarkerId, new MarkerBlending());
					}
					animationBlending.MarkersOptions[markersBlendings.DestMarkerId].SourceMarkersOptions
						.Add(markersBlendings.SourceMarkerId, markersBlendings.Blending);
				}

				if (animationBlending.Option != null || animationBlending.MarkersOptions.Count > 0) {
					animation.AnimationEngine = BlendAnimationEngine.Instance;
					model.Components.GetOrAdd<AnimationBlender>().Options.Add(animation.Id ?? "", animationBlending);
				}
			}
		}

		private void ProcessMaterialEffects(Node3D model)
		{
			var effectEngine = new MaterialEffectEngine();
			foreach (var effect in MaterialEffects) {
				try {
					var effectPresenter = new MaterialEffectPresenter(effect.MaterialName, effect.Name, effect.Path);
					model.CompoundPresenter.Add(effectPresenter);

					if (effect.Blending != null) {
						effectPresenter.Animation.AnimationEngine = BlendAnimationEngine.Instance;
						var blender = effectPresenter.Scene.Components.GetOrAdd<AnimationBlender>();
						var animationBlending = new AnimationBlending() {
							Option = effect.Blending
						};
						blender.Options.Add(effectPresenter.Animation.Id ?? "", animationBlending);
					}

					var animation = new Lime.Animation {
						Id = effect.Name,
						AnimationEngine = effectEngine
					};

					foreach (var marker in effectPresenter.Animation.Markers) {
						animation.Markers.Add(marker.Clone());
					}

					model.Animations.Add(animation);
				} catch {
					Console.WriteLine($"Warning: Unable to load material effect { effect.Path }");
				}
			}
		}

		private void CopyAnimationKeys(Node node, Lime.Animation srcAnimation, Lime.Animation dstAnimation)
		{
			var srcAnimators = new List<IAnimator>(node.Animators.Where(i => i.AnimationId == srcAnimation.Id));
			foreach (var srcAnimator in srcAnimators) {
				var dstAnimator = srcAnimator.Clone();
				dstAnimator.AnimationId = dstAnimation.Id;
				node.Animators.Add(dstAnimator);
			}
		}

		private IEnumerable<Node> GetAnimationNodes(Node3D model, Animation animationData)
		{
			if (animationData.Nodes.Count > 0) {
				return animationData.Nodes.Distinct().Select(n => model.FindNode(n.Id));
			}
			if (animationData.IgnoredNodes.Count > 0) {
				var ignoredNodes = new HashSet<Node>(animationData.IgnoredNodes.Select(n => model.FindNode(n.Id)));
				return model.Descendants.Where(i => !ignoredNodes.Contains(i));
			}
			return model.Descendants;
		}
	}

	public class Model3DAttachmentParser
	{
		public enum UVAnimationType
		{
			Rotation,
			Offset
		}

		public enum UVAnimationOverlayBlending
		{
			Multiply,
			Overlay,
			Add
		}

		public class ModelAttachmentFormat
		{
			[YuzuMember]
			public Dictionary<string, MeshOptionFormat> MeshOptions = null;

			[YuzuMember]
			public Dictionary<string, ModelAnimationFormat> Animations = null;

			[YuzuMember]
			public Dictionary<string, ModelMaterialEffectFormat> MaterialEffects = null;

			[YuzuMember]
			public List<UVAnimationFormat> UVAnimations = null;

			[YuzuMember]
			public float ScaleFactor = 1f;
		}

		public class MeshOptionFormat
		{
			[YuzuMember]
			public bool HitTestTarget = false;

			[YuzuMember]
			public string CullMode = null;
		}

		public class UVAnimationFormat
		{
			[YuzuMember]
			public string MeshName = null;

			[YuzuMember]
			public string DiffuseTexture = null;

			[YuzuMember]
			public string OverlayTexture = null;

			[YuzuMember]
			public string MaskTexture = null;

			[YuzuMember]
			public float AnimationSpeed = 0;

			[YuzuMember]
			public UVAnimationType AnimationType = UVAnimationType.Rotation;

			[YuzuMember]
			public UVAnimationOverlayBlending BlendingMode = UVAnimationOverlayBlending.Multiply;

			[YuzuMember]
			public bool AnimateOverlay = false;

			[YuzuMember]
			public float TileX = 1f;

			[YuzuMember]
			public float TileY = 1f;
		}

		public class ModelAnimationFormat
		{

			[YuzuMember]
			public List<string> Nodes = null;

			[YuzuMember]
			public List<string> IgnoredNodes = null;

			[YuzuMember]
			public Dictionary<string, ModelMarkerFormat> Markers = null;

			[YuzuMember]
			public int? Blending = null;
		}

		public class ModelMarkerFormat
		{
			[YuzuMember]
			public int Frame = 0;

			[YuzuMember]
			public string Action = null;

			[YuzuMember]
			public string JumpTarget = null;

			[YuzuMember]
			public Dictionary<string, int> SourceMarkersBlending = null;

			[YuzuMember]
			public int? Blending = null;
		}

		public class ModelMaterialEffectFormat
		{
			[YuzuMember]
			public string MaterialName = null;

			[YuzuMember]
			public string Path = null;

			[YuzuMember]
			public int? Blending = null;
		}

		public Model3DAttachment Parse(string modelPath, bool useBundle = true)
		{
			modelPath = AssetPath.CorrectSlashes(
				Path.Combine(Path.GetDirectoryName(modelPath) ?? "",
				Path.GetFileNameWithoutExtension(AssetPath.CorrectSlashes(modelPath) ?? "")
			));
			var attachmentPath = modelPath + Model3DAttachment.FileExtension;
			try {
				ModelAttachmentFormat modelAttachmentFormat;
				if (useBundle) {
					if (!AssetBundle.Current.FileExists(attachmentPath)) {
						return null;
					}
					modelAttachmentFormat = Serialization.ReadObject<ModelAttachmentFormat>(attachmentPath);
				} else {
					if (!File.Exists(attachmentPath)) {
						return null;
					}
					modelAttachmentFormat = Serialization.ReadObjectFromFile<ModelAttachmentFormat>(attachmentPath);
				}

				var attachment = new Model3DAttachment {
					ScaleFactor = modelAttachmentFormat.ScaleFactor
				};
				if (modelAttachmentFormat.MeshOptions != null) {
					foreach (var meshOptionFormat in modelAttachmentFormat.MeshOptions) {
						var meshOption = new Model3DAttachment.MeshOption() {
							Id = meshOptionFormat.Key,
							HitTestTarget = meshOptionFormat.Value.HitTestTarget
						};
						if (!string.IsNullOrEmpty(meshOptionFormat.Value.CullMode)) {
							switch (meshOptionFormat.Value.CullMode) {
								case "None":
									meshOption.CullMode = CullMode.None;
									break;
								case "CullClockwise":
									meshOption.CullMode = CullMode.CullClockwise;
									break;
								case "CullCounterClockwise":
									meshOption.CullMode = CullMode.CullCounterClockwise;
									break;
							}
						}
						attachment.MeshOptions.Add(meshOption);
					}
				}

				if (modelAttachmentFormat.Animations != null) {
					foreach (var animationFormat in modelAttachmentFormat.Animations) {
						var animation = new Model3DAttachment.Animation {
							Name = animationFormat.Key,
						};

						if (animationFormat.Value.Markers != null) {
							foreach (var markerFormat in animationFormat.Value.Markers) {
								var markerData = new Model3DAttachment.MarkerData {
									Marker = new Marker {
										Id = markerFormat.Key,
										Frame = FixFrame(markerFormat.Value.Frame)
									}
								};
								if (!string.IsNullOrEmpty(markerFormat.Value.Action)) {
									switch (markerFormat.Value.Action) {
										case "Start":
											markerData.Marker.Action = MarkerAction.Play;
											break;
										case "Stop":
											markerData.Marker.Action = MarkerAction.Stop;
											break;
										case "Jump":
											markerData.Marker.Action = MarkerAction.Jump;
											markerData.Marker.JumpTo = markerFormat.Value.JumpTarget;
											break;
									}
								}
								if (markerFormat.Value.Blending != null) {
									markerData.Blending = new BlendingOption((int)markerFormat.Value.Blending);
								}
								if (markerFormat.Value.SourceMarkersBlending != null) {
									foreach (var elem in markerFormat.Value.SourceMarkersBlending) {
										animation.MarkersBlendings.Add(new Model3DAttachment.MarkerBlendingData {
											DestMarkerId = markerFormat.Key,
											SourceMarkerId = elem.Key,
											Blending = new BlendingOption(elem.Value),
										});
									}
								}

								animation.Markers.Add(markerData);
							}
						}

						if (animationFormat.Value.Blending != null) {
							animation.Blending = new BlendingOption((int)animationFormat.Value.Blending);
						}

						if (animationFormat.Value.Nodes != null) {
							animation.Nodes = new ObservableCollection<Model3DAttachment.NodeData>(
								animationFormat.Value.Nodes.Select(n => new Model3DAttachment.NodeData { Id = n }));
						}

						if (animationFormat.Value.IgnoredNodes != null && animationFormat.Value.IgnoredNodes.Count > 0) {
							if (animation.Nodes.Count > 0) {
								throw new Exception("Conflict between 'Nodes' and 'IgnoredNodes' in animation '{0}", animation.Name);
							}
							animation.IgnoredNodes = new ObservableCollection<Model3DAttachment.NodeData>(
								animationFormat.Value.IgnoredNodes.Select(n => new Model3DAttachment.NodeData { Id = n }));
						}

						attachment.Animations.Add(animation);
					}
				}

				if (modelAttachmentFormat.MaterialEffects != null) {
					foreach (var materialEffectFormat in modelAttachmentFormat.MaterialEffects) {
						var materialEffect = new Model3DAttachment.MaterialEffect() {
							Name = materialEffectFormat.Key,
							MaterialName = materialEffectFormat.Value.MaterialName,
							Path = FixPath(modelPath, materialEffectFormat.Value.Path)
						};
						if (materialEffectFormat.Value.Blending != null) {
							materialEffect.Blending = new BlendingOption((int)materialEffectFormat.Value.Blending);
						}
						attachment.MaterialEffects.Add(materialEffect);
					}
				}

				return attachment;
			} catch (System.Exception e) {
				throw new System.Exception(modelPath + ": " + e.Message, e);
			}
		}

		public static void Save(Model3DAttachment attachment, string path)
		{
			var attachmentPath = path + ".Attachment.txt";
			Serialization.WriteObjectToFile(attachmentPath, CreateFromModel3DAttachment(attachment), Serialization.Format.JSON);
		}

		private static ModelAttachmentFormat CreateFromModel3DAttachment(Model3DAttachment attachment)
		{
			var origin = new ModelAttachmentFormat();
			origin.ScaleFactor = attachment.ScaleFactor;
			if (attachment.MeshOptions.Count > 0) {
				origin.MeshOptions = new Dictionary<string, MeshOptionFormat>();
			}
			if (attachment.Animations.Count > 0) {
				origin.Animations = new Dictionary<string, ModelAnimationFormat>();
			}
			if (attachment.MaterialEffects.Count > 0) {
				origin.MaterialEffects = new Dictionary<string, ModelMaterialEffectFormat>();
			}
			foreach (var meshOption in attachment.MeshOptions) {
				var meshOptionFormat = new MeshOptionFormat {
					HitTestTarget = meshOption.HitTestTarget,
				};
				switch (meshOption.CullMode) {
					case CullMode.None:
						meshOptionFormat.CullMode = "None";
						break;
					case CullMode.CullClockwise:
						meshOptionFormat.CullMode = "CullClockwise";
						break;
					case CullMode.CullCounterClockwise:
						meshOptionFormat.CullMode = "CullCounterClockwise";
						break;
				}
				origin.MeshOptions.Add(meshOption.Id, meshOptionFormat);
			}

			foreach (var animation in attachment.Animations) {
				var animationFormat = new ModelAnimationFormat {
					Markers = new Dictionary<string, ModelMarkerFormat>(),
				};
				foreach (var markerData in animation.Markers) {
					var markerFormat = new ModelMarkerFormat {
						Frame = markerData.Marker.Frame
					};
					switch (markerData.Marker.Action) {
						case MarkerAction.Play:
							markerFormat.Action = "Start";
							break;
						case MarkerAction.Stop:
							markerFormat.Action = "Stop";
							break;
						case MarkerAction.Jump:
							markerFormat.Action = "Jump";
							markerFormat.JumpTarget = markerData.Marker.JumpTo;
							break;
					}
					if (animation.MarkersBlendings.Count > 0) {
						markerFormat.SourceMarkersBlending = new Dictionary<string, int>();
						foreach (var markerBlending in animation.MarkersBlendings.Where(m => m.DestMarkerId == markerData.Marker.Id)) {
							markerFormat.SourceMarkersBlending.Add(markerBlending.SourceMarkerId, (int)markerBlending.Blending.DurationInFrames);
						}
					}
					if (markerData.Blending != null) {
						markerFormat.Blending = (int)markerData.Blending.DurationInFrames;
					}
					animationFormat.Markers.Add(markerData.Marker.Id, markerFormat);
				}

				if (animation.Blending != null) {
					animationFormat.Blending = (int)animation.Blending.DurationInFrames;
				}

				if (animation.Nodes.Count > 0) {
					animationFormat.Nodes = animation.Nodes.Count > 0 ? animation.Nodes.Select(n => n.Id).ToList() : null;
				} else if (animation.IgnoredNodes.Count > 0) {
					animationFormat.IgnoredNodes = animation.IgnoredNodes.Select(n => n.Id).ToList();
				}
				origin.Animations.Add(animation.Name, animationFormat);
			}

			foreach (var materialEffect in attachment.MaterialEffects) {
				var name = materialEffect.Path.Split('/');
				var materialEffectFormat = new ModelMaterialEffectFormat {
					Path = name.Last(),
					MaterialName = materialEffect.Name,
				};
				if (materialEffect.Blending != null) {
					materialEffectFormat.Blending = (int)materialEffect.Blending.Duration;
				}
				origin.MaterialEffects.Add(materialEffect.Name, materialEffectFormat);
			}

			return origin;
		}

		private static string FixPath(string modelPath, string path)
		{
			var baseDir = Path.GetDirectoryName(modelPath);
			return AssetPath.CorrectSlashes(Path.Combine(AssetPath.CorrectSlashes(baseDir), AssetPath.CorrectSlashes(path)));
		}

		private static int FixFrame(int frame, double fps = 30)
		{
			return AnimationUtils.SecondsToFrames(frame / fps);
		}
	}

	public class MaterialEffectEngine : DefaultAnimationEngine
	{
		public override void AdvanceAnimation(Animation animation, float delta)
		{
			var effectAnimation = GetPresenter(animation).Animation;
			effectAnimation.AnimationEngine.AdvanceAnimation(effectAnimation, delta);

			base.AdvanceAnimation(animation, delta);
		}

		public override void ApplyAnimators(Animation animation, bool invokeTriggers)
		{
			var effectAnimation = GetPresenter(animation).Animation;
			effectAnimation.AnimationEngine.ApplyAnimators(effectAnimation, invokeTriggers);

			base.ApplyAnimators(animation, invokeTriggers);
		}

		public override bool TryRunAnimation(Animation animation, string markerId)
		{
			var presenter = GetPresenter(animation);
			if (
				!base.TryRunAnimation(animation, markerId) ||
				!presenter.Animation.AnimationEngine.TryRunAnimation(presenter.Animation, markerId)
			) {
				return false;
			}

			presenter.WasSnapshotDeprecated = true;
			foreach (var material in GetMaterialsForAnimation(animation)) {
				material.DiffuseTexture = presenter.Snapshot;
			}
			return true;
		}

		private static MaterialEffectPresenter GetPresenter(Animation animation)
		{
			return animation.Owner.CompoundPresenter
				.OfType<MaterialEffectPresenter>()
				.First(p => p.EffectName == animation.Id);
		}

		public static IEnumerable<CommonMaterial> GetMaterialsForAnimation(Animation animation)
		{
			return GetMaterials(animation.Owner, GetPresenter(animation).MaterialName);
		}

		private static IEnumerable<CommonMaterial> GetMaterials(Node model, string name)
		{
			return model
				.Descendants
				.OfType<Mesh3D>()
				.SelectMany(i => i.Submeshes)
				.Select(i => i.Material)
				.Cast<CommonMaterial>()
				.Where(i => i.Name == name);
		}
	}

	public class MaterialEffectPresenter : CustomPresenter
	{
		private static readonly RenderChain renderChain = new RenderChain();
		private ITexture snapshot;

		public string MaterialName { get; }
		public string EffectName { get; }
		public Widget Scene { get; private set; }
		public bool WasSnapshotDeprecated { get; set; } = true;
		public Animation Animation => Scene.DefaultAnimation;
		public ITexture Snapshot => snapshot ?? (snapshot = new RenderTexture((int)Scene.Width, (int)Scene.Height));

		public MaterialEffectPresenter(string materialName, string effectName, string path)
		{
			MaterialName = materialName;
			EffectName = effectName;
			Scene = new Frame(path);
		}

		public override void Render(Node node)
		{
			if (!Animation.IsRunning && !WasSnapshotDeprecated) {
				return;
			}

			Scene.RenderToTexture(Snapshot, renderChain);
			renderChain.Clear();
			WasSnapshotDeprecated = false;
		}

		public override IPresenter Clone()
		{
			var clone = (MaterialEffectPresenter)MemberwiseClone();
			clone.snapshot = null;
			clone.Scene = Scene.Clone<Widget>();
			return clone;
		}
	}
}

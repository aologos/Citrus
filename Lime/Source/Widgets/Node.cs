using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Yuzu;

namespace Lime
{
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
	public sealed class YuzuSpecializeWithAttribute : Attribute
	{
		public Type Type;
		public YuzuSpecializeWithAttribute(Type type)
		{
			Type = type;
		}
	}

	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true)]
	public sealed class YuzuDontGenerateDeserializerAttribute : Attribute
	{ }

	[Flags]
	public enum TangerineFlags
	{
		None = 0,
		Hidden = 1,
		Locked = 2,
		Shown = 4,
		[TangerineIgnore]
		PropertiesExpanded = 8,
		[TangerineIgnore]
		ChildrenExpanded = 16,
		[TangerineIgnore]
		HiddenOnExposition = 32,
		[TangerineIgnore]
		IgnoreMarkers = 64,
		[TangerineIgnore]
		DisplayContent = 128,
		ColorBit1 = 256,
		ColorBit2 = 512,
		ColorBit3 = 1024,
		[TangerineIgnore]
		SceneNode = 2048
	}

	public delegate void UpdateHandler(float delta);

	/// <summary>
	/// Scene tree element.
	/// </summary>
	[YuzuDontGenerateDeserializer]
	[DebuggerTypeProxy(typeof(NodeDebugView))]
	public abstract class Node : IDisposable, IAnimationHost, IFolderItem, IFolderContext, IRenderChainBuilder, IAnimable
	{
		[Flags]
		protected internal enum DirtyFlags
		{
			None = 0,
			Visible = 1 << 0,
			Color = 1 << 1,
			Shader = 1 << 2,
			Blending = 1 << 3,
			LayoutManager = 1 << 4,
			LocalTransform = 1 << 5,
			GlobalTransform = 1 << 6,
			GlobalTransformInverse = 1 << 7,
			ParentBoundingRect = 1 << 8,
			All = ~None
		}

		/// <summary>
		/// Is invoked after default animation has been stopped (e.g. hit "Stop" marker).
		/// Note: DefaultAnimation.Stopped will be set to null after invocation or after RunAnimation call.
		/// The correct usage is to call RunAnimation first and then apply any required AnimationStopped handlers.
		/// </summary>
		public event Action AnimationStopped
		{
			add => DefaultAnimation.Stopped += value;
			remove => DefaultAnimation.Stopped -= value;
		}

		private string id;

		/// <summary>
		/// Name field in HotStudio.
		/// May be non-unique.
		/// </summary>
		[YuzuMember]
		[TangerineStaticProperty]
		public string Id
		{
			get => id;
			set
			{
				if (id != value) {
					id = value;
					InvalidateNodeReferenceCache();
				}
			}
		}

		private string contentsPath;
		/// <summary>
		/// Denotes the path to the external scene. If this path isn't null during node loading,
		/// the node children are replaced by the external scene nodes.
		/// </summary>
		[YuzuMember]
		[TangerineStaticProperty]
		public string ContentsPath
		{
			get => Yuzu.Current?.ShrinkPath(contentsPath) ?? contentsPath;
			set => contentsPath = Yuzu.Current?.ExpandPath(value) ?? value;
		}

		internal static long NodeReferenceCacheValidationCode = 1;

		internal static void InvalidateNodeReferenceCache()
		{
			System.Threading.Interlocked.Increment(ref NodeReferenceCacheValidationCode);
		}

		/// <summary>
		/// May contain the marker id of the default animation.  When an animation hits a trigger keyframe,
		/// it automatically runs the child animation from the given marker id.
		/// </summary>
		[Trigger]
		[TangerineKeyframeColor(1)]
		public string Trigger { get; set; }

		IAnimable IAnimable.Owner { get => null; set => throw new NotSupportedException(); }

		private Node parent;
		public Node Parent
		{
			get => parent;
			internal set
			{
				if (parent != value) {
					var oldParent = parent;
					parent = value;
					PropagateDirtyFlags();
					OnParentChanged(oldParent);
				}
			}
		}

		private TangerineFlags tangerineFlags;

		[TangerineIgnore]
		[YuzuMember]
		public TangerineFlags TangerineFlags
		{
			get => tangerineFlags;
			set
			{
				if (tangerineFlags != value) {
					tangerineFlags = value;
					PropagateDirtyFlags(DirtyFlags.Visible);
				}
			}
		}

		public bool GetTangerineFlag(TangerineFlags flag)
		{
			return (tangerineFlags & flag) != 0;
		}

		public void SetTangerineFlag(TangerineFlags flag, bool value)
		{
			if (value) {
				TangerineFlags |= flag;
			} else {
				TangerineFlags &= ~flag;
			}
		}

		protected virtual void OnParentChanged(Node oldParent) { }

		public Widget AsWidget { get; internal set; }

		public Node3D AsNode3D { get; internal set; }

		/// <summary>
		/// The presenter used for rendering and hit-testing the node.
		/// TODO: Add Add/RemovePresenter methods. Remove CompoundPresenter property and Presenter setter.
		/// </summary>
		public IPresenter Presenter { get; set; }

		public CompoundPresenter CompoundPresenter =>
			(Presenter as CompoundPresenter) ?? (CompoundPresenter)(Presenter = new CompoundPresenter(Presenter));

		/// <summary>
		/// The presenter used for rendering the node after rendering its children.
		/// </summary>
		public IPresenter PostPresenter { get; set; }

		public CompoundPresenter CompoundPostPresenter =>
			(PostPresenter as CompoundPresenter) ?? (CompoundPresenter)(PostPresenter = new CompoundPresenter(PostPresenter));

		internal int RunningAnimationCount;

		/// <summary>
		/// Gets the cached reference to the first animation in the animation collection.
		/// </summary>
		public Animation FirstAnimation { get; internal set; }

		/// <summary>
		/// Gets the cached reference to the first children node.
		/// Use it for fast iteration through nodes collection in a performance-critical code.
		/// </summary>
		public Node FirstChild { get; internal set; }

		/// <summary>
		/// Gets the cached reference to the next node in the NodeList.
		/// Use it for fast iteration through nodes collection in a performance-critical code.
		/// </summary>
		public Node NextSibling { get; internal set; }

		public event Action<Node> Awoke
		{
			add => Components.GetOrAdd<AwakeBehavior>().Action += value;
			remove => Components.GetOrAdd<AwakeBehavior>().Action -= value;
		}

		internal NodeBehavior[] Behaviours = NodeComponentCollection.EmptyBehaviors;
		internal NodeBehavior[] LateBehaviours = NodeComponentCollection.EmptyBehaviors;

		/// <summary>
		/// Called before Update.
		/// </summary>
		public UpdateHandler Updating
		{
			get => Components.GetOrAdd<UpdateBehaviour>().Updating;
			set => Components.GetOrAdd<UpdateBehaviour>().Updating = value;
		}

		/// <summary>
		/// Called after Update.
		/// </summary>
		public UpdateHandler Updated
		{
			get => Components.GetOrAdd<UpdatedBehaviour>().Updated;
			set => Components.GetOrAdd<UpdatedBehaviour>().Updated = value;
		}

		/// <summary>
		/// Tasks that are called before Update.
		/// </summary>
		public TaskList Tasks => Components.GetOrAdd<TasksBehaviour>().Tasks;

		/// <summary>
		/// Tasks that are called after Update.
		/// </summary>
		public TaskList LateTasks => Components.GetOrAdd<LateTasksBehaviour>().Tasks;

		/// <summary>
		/// Animation speed multiplier.
		/// </summary>
		public float AnimationSpeed { get; set; }

		public IRenderChainBuilder RenderChainBuilder { get; set; }

		/// <summary>
		/// Gets or sets Z-order (the rendering order).
		/// 0 - inherit Z-order from the parent node. 1 - the lowest, (RenderChain.LayerCount - 1) - the topmost.
		/// </summary>
		public int Layer { get; set; }

		/// <summary>
		/// Collections of Components.
		/// If node component class don't need to be serialized mark it with <see cref="NodeComponentDontSerializeAttribute">.
		/// </summary>
		[YuzuMember]
		public NodeComponentCollection Components { get; private set; }

		/// <summary>
		/// Collections of Animators.
		/// </summary>
		[YuzuMember]
		public AnimatorCollection Animators { get; private set; }

		/// <summary>
		/// Child nodes.
		/// For enumerating all descendants use Descendants.
		/// </summary>
		[TangerineIgnore]
		[YuzuMember]
		[YuzuSerializeIf(nameof(ShouldSerializeNodes))]
		public NodeList Nodes { get; private set; }

		/// <summary>
		/// Markers of default animation.
		/// </summary>
		public MarkerList Markers => DefaultAnimation.Markers;

		/// <summary>
		/// Returns true if this node is running animation.
		/// </summary>
		public bool IsRunning
		{
			get => DefaultAnimation.IsRunning;
			set => DefaultAnimation.IsRunning = value;
		}

		/// <summary>
		/// Returns true if this node isn't running animation.
		/// </summary>
		public bool IsStopped {
			get => !IsRunning;
			set => IsRunning = !value;
		}

		/// <summary>
		/// Gets or sets time of current frame of default animation (in milliseconds).
		/// </summary>
		public double AnimationTime
		{
			get => DefaultAnimation.Time;
			set => DefaultAnimation.Time = value;
		}

		/// <summary>
		/// Returns the first animation in the animation collection
		/// or creates an animation if the collection is empty.
		/// </summary>
		public Animation DefaultAnimation
		{
			get
			{
				if (FirstAnimation == null) {
					Animations.Add(new Animation());
				}
				return FirstAnimation;
			}
		}

		/// <summary>
		/// Custom data. Can be set via Tangerine (this way it will contain path to external scene).
		/// Can't use obsolete attribute because of Yuzu Generated Binary Deserializers. But its obsolete, dont use it.
		/// Use Node Components instead.
		/// </summary>
		[TangerineIgnore]
		[YuzuMember]
		public string Tag
		{
			get => tag;
			set
			{
				if (tag == value) {
					return;
				}
				tag = value;
			}
		}
		private string tag;

		[YuzuMember]
		[YuzuSerializeIf(nameof(NeedSerializeAnimations))]
		public AnimationCollection Animations { get; private set; }

		public bool NeedSerializeAnimations() =>
			Animations.Count > 1 || (Animations.Count == 1 && (FirstAnimation.Id != null || FirstAnimation.Markers.Count > 0));

		[TangerineIgnore]
		[YuzuMember]
		public List<Folder.Descriptor> Folders { get; set; }

		/// <summary>
		/// Name of last started marker of default animation. Is set to null by default.
		/// </summary>
		public string CurrentAnimation
		{
			get => DefaultAnimation.RunningMarkerId;
			set => DefaultAnimation.RunningMarkerId = value;
		}

		/// <summary>
		/// Custom data. Can only be set programmatically.
		/// </summary>
		public object UserData { get; set; }

		/// <summary>
		/// TODO: Add summary
		/// </summary>
		internal protected DirtyFlags DirtyMask = DirtyFlags.All;

		/// <summary>
		/// TODO: Add summary
		/// </summary>
		protected bool IsDirty(DirtyFlags mask) { return (DirtyMask & mask) != 0; }

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected bool CleanDirtyFlags(DirtyFlags mask)
		{
			if ((DirtyMask & mask) != 0) {
				DirtyMask &= ~mask;
				return true;
			}
			return false;
		}

		public bool HitTestTarget;

		public static int CreatedCount = 0;
		public static int FinalizedCount = 0;

		protected Node()
		{
			AnimationSpeed = 1;
			Components = new NodeComponentCollection(this);
			Animators = new AnimatorCollection(this);
			Animations = new AnimationCollection(this);
			Nodes = new NodeList(this);
			Presenter = DefaultPresenter.Instance;
			RenderChainBuilder = this;
			++CreatedCount;
		}

		private GestureList gestures;
		public GestureList Gestures => gestures ?? (gestures = new GestureList(this));
		public bool HasGestures() => gestures != null;

		public virtual bool IsNotDecorated() => true;

		public bool ShouldSerializeNodes()
		{
			return IsNotDecorated() && Nodes.Count > 0;
		}

		public virtual void Dispose()
		{
			foreach (var component in Components) {
				component.Dispose();
			}
			for (var n = FirstChild; n != null; n = n.NextSibling) {
				n.Dispose();
			}
			Nodes.Clear();
			Animators.Dispose();
		}

		internal void RefreshRunningAnimationCount()
		{
			RunningAnimationCount = 0;
			for (var a = FirstAnimation; a != null; a = a.Next) {
				if (a.IsRunning) {
					RunningAnimationCount++;
				}
			}
		}

		/// <summary>
		/// Propagates dirty flags to all descendants by provided mask.
		/// </summary>
		protected internal void PropagateDirtyFlags(DirtyFlags mask = DirtyFlags.All)
		{
			if ((DirtyMask & mask) == mask)
				return;
			Window.Current?.Invalidate();
			PropagateDirtyFlagsHelper(mask);
		}

		private void PropagateDirtyFlagsHelper(DirtyFlags mask)
		{
			DirtyMask |= mask;
			for (var n = FirstChild; n != null; n = n.NextSibling) {
				if ((n.DirtyMask & mask) != mask) {
					n.PropagateDirtyFlagsHelper(mask);
				}
			}
		}

#if LIME_COUNT_NODES
		~Node() {
			++FinalizedCount;
		}
#endif

		public Node GetRoot()
		{
			Node node = this;
			while (node.Parent != null) {
				node = node.Parent;
			}
			return node;
		}

		/// <summary>
		/// Returns true if this node is a descendant of the provided node.
		/// </summary>
		public bool DescendantOf(Node node)
		{
			for (var n = Parent; n != null; n = n.Parent) {
				if (n == node)
					return true;
			}
			return false;
		}

		public bool SameOrDescendantOf(Node node)
		{
			return node == this || DescendantOf(node);
		}

		/// <summary>
		/// Runs animation with provided Id and provided marker.
		/// Returns false if sought-for animation or marker doesn't exist.
		/// </summary>
		public bool TryRunAnimation(string markerId, string animationId = null, double animationTimeCorrection = 0)
		{
			return Animations.TryRun(animationId, markerId, animationTimeCorrection);
		}

		/// <summary>
		/// Runs animation with provided Id and provided marker.
		/// Throws an exception if sought-for animation or marker doesn't exist.
		/// Returns this (to use with yield return)
		/// </summary>
		public Node RunAnimation(string markerId, string animationId = null)
		{
			Animations.Run(animationId, markerId);
			return this;
		}

		/// <summary>
		/// Get or sets the current animation frame.
		/// </summary>
		public int AnimationFrame
		{
			get => AnimationUtils.SecondsToFrames(AnimationTime);
			set => AnimationTime = AnimationUtils.FramesToSeconds(value);
		}

		/// <summary>
		/// Returns a clone of the node hierarchy.
		/// </summary>
		public virtual Node Clone()
		{
			var clone = (Node)MemberwiseClone();
			++CreatedCount;
			clone.Parent = null;
			clone.FirstAnimation = null;
			clone.FirstChild = null;
			clone.NextSibling = null;
			clone.gestures = null;
			clone.AsWidget = clone as Widget;
			clone.Animations = Animations.Clone(clone);
			clone.Animators = AnimatorCollection.SharedClone(clone, Animators);
			clone.Nodes = Nodes.Clone(clone);
			clone.Components = Components.Clone(clone);
			if (RenderChainBuilder != null) {
				clone.RenderChainBuilder = RenderChainBuilder.Clone(clone);
			}
			if (Presenter != null) {
				clone.Presenter = Presenter.Clone();
			}
			if (PostPresenter != null) {
				clone.PostPresenter = PostPresenter.Clone();
			}
			clone.DirtyMask = DirtyFlags.All;
			clone.UserData = null;
			return clone;
		}

		/// <summary>
		/// Returns a clone of the node hierarchy.
		/// </summary>
		public T Clone<T>() where T : Node
		{
			return (T)Clone();
		}

		/// <summary>
		/// Returns the <see cref="string"/> representation of this <see cref="Node"/>.
		/// </summary>
		public override string ToString()
		{
			string r = "";
			for (var p = this; p != null; p = p.Parent) {
				if (p != this) {
					r += " in ";
				}
				r += string.IsNullOrEmpty(p.Id) ? p.GetType().Name : $"'{p.Id}'";
				if (!string.IsNullOrEmpty(p.Tag)) {
					r += $" ({p.Tag})";
				}
				var bundlePath = Components.Get<Node.AssetBundlePathComponent>();
				if (bundlePath != null) {
					r += $" ({bundlePath})";
				}
			}
			return r;
		}

		/// <summary>
		/// Removes this node from parent node.
		/// </summary>
		public void Unlink()
		{
			Parent?.Nodes.Remove(this);
		}

		public void UnlinkAndDispose()
		{
			Unlink();
			Dispose();
		}

		/// <summary>
		/// Advances animations of this node and calls Update of all its children.
		/// Usually called once a frame.
		/// </summary>
		/// <param name="delta">Time delta since last Update.</param>
		public virtual void Update(float delta)
		{
			if (delta > Application.MaxDelta) {
				SafeUpdate(delta);
			} else {
				foreach (var b in Behaviours) {
					b.Update(delta);
				}
				AdvanceAnimation(delta);
				for (var node = FirstChild; node != null;) {
					var next = node.NextSibling;
					node.Update(node.AnimationSpeed * delta);
					node = next;
				}
				foreach (var b in LateBehaviours) {
					b.LateUpdate(delta);
				}
			}
		}

		protected void SafeUpdate(float delta)
		{
			var remainDelta = delta;
			do {
				delta = Mathf.Min(remainDelta, Application.MaxDelta);
				Update(delta);
				remainDelta -= delta;
			} while (remainDelta > 0f);
		}

		protected internal virtual RenderObject GetRenderObject() => null;

		/// <summary>
		/// Decides what descendant nodes should be added to render chain and under which conditions.
		/// This includes children Nodes as well as this Node itself. i.e. if you want Render()
		/// of this node to be called you should invoke AddSelfToRenderChain in AddToRenderChain override.
		/// </summary>
		public abstract void AddToRenderChain(RenderChain chain);

		IRenderChainBuilder IRenderChainBuilder.Clone(Node newOwner) => newOwner;

		protected void AddSelfAndChildrenToRenderChain(RenderChain chain, int layer)
		{
			if (layer == 0) {
				if (PostPresenter != null) {
					chain.Add(this, PostPresenter);
				}
				for (var node = FirstChild; node != null; node = node.NextSibling) {
					node.RenderChainBuilder?.AddToRenderChain(chain);
				}
				if (Presenter != null) {
					chain.Add(this, Presenter);
				}
			} else {
				var savedLayer = chain.CurrentLayer;
				chain.CurrentLayer = layer;
				AddSelfAndChildrenToRenderChain(chain, 0);
				chain.CurrentLayer = savedLayer;
			}
		}

		protected void AddSelfToRenderChain(RenderChain chain, int layer)
		{
			if (layer == 0) {
				if (PostPresenter != null) {
					chain.Add(this, PostPresenter);
				}
				if (Presenter != null) {
					chain.Add(this, Presenter);
				}
			} else {
				var savedLayer = chain.CurrentLayer;
				chain.CurrentLayer = layer;
				AddSelfToRenderChain(chain, 0);
				chain.CurrentLayer = savedLayer;
			}
		}

		/// <summary>
		/// TODO: Add summary
		/// </summary>
		public virtual void OnTrigger(string property, double animationTimeCorrection = 0)
		{
			if (property != "Trigger") {
				return;
			}
			if (string.IsNullOrEmpty(Trigger)) {
				AnimationTime = animationTimeCorrection;
				IsRunning = true;
			} else {
				TriggerMultipleAnimations(animationTimeCorrection);
			}
		}

		private void TriggerMultipleAnimations(double animationTimeCorrection = 0)
		{
			if (Trigger.IndexOf(',') >= 0) {
				foreach (var s in Trigger.Split(',')) {
					TriggerAnimation(s.Trim(), animationTimeCorrection);
				}
			} else {
				TriggerAnimation(Trigger, animationTimeCorrection);
			}
		}

		private void TriggerAnimation(string markerWithOptionalAnimationId, double animationTimeCorrection = 0)
		{
			if (markerWithOptionalAnimationId.IndexOf('@') >= 0) {
				var s = markerWithOptionalAnimationId.Split('@');
				if (s.Length == 2) {
					var markerId = s[0];
					var animationId = s[1];
					TryRunAnimation(markerId, animationId, animationTimeCorrection);
				}
			} else {
				TryRunAnimation(markerWithOptionalAnimationId, null, animationTimeCorrection);
			}
		}

		/// <summary>
		/// Adds provided node to the lowermost layer of this node (i.e. it will be covered
		/// by other nodes). Provided node shouldn't belong to any node (check for Parent == null
		/// and use Unlink if needed).
		/// </summary>
		public void AddNode(Node node)
		{
			Nodes.Add(node);
		}

		/// <summary>
		/// Adds this node to the lowermost layer of provided node (i.e. it will be covered
		/// by other nodes). This node shouldn't belong to any node (check for Parent == null
		/// and use Unlink if needed).
		/// </summary>
		public void AddToNode(Node node)
		{
			node.Nodes.Add(this);
		}

		/// <summary>
		/// Adds provided node to the topmost layer of this node (i.e. it will cover
		/// other nodes). Provided node shouldn't belong to any node (check for Parent == null
		/// and use Unlink if needed).
		/// </summary>
		public void PushNode(Node node)
		{
			Nodes.Push(node);
		}

		/// <summary>
		/// Adds this node to the topmost layer of provided node (i.e. it will cover
		/// other nodes). This node shouldn't belong to any node (check for Parent == null
		/// and use Unlink if needed).
		/// </summary>
		public void PushToNode(Node node)
		{
			node.Nodes.Push(this);
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Throws an exception if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <typeparam name="T">Type of sought-for node.</typeparam>
		/// <param name="path">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public T Find<T>(string path) where T : Node
		{
			if (!(TryFindNode(path) is T result)) {
				throw new Lime.Exception("'{0}' of {1} not found for '{2}'", path, typeof(T).Name, ToString());
			}
			return result;
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <typeparam name="T">Type of sought-for node.</typeparam>
		/// <param name="format">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public T Find<T>(string format, params object[] args) where T : Node
		{
			return Find<T>(string.Format(format, args));
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <typeparam name="T">Type of sought-for node.</typeparam>
		/// <param name="path">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public bool TryFind<T>(string path, out T node) where T : Node
		{
			node = TryFindNode(path) as T;
			return node != null;
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <typeparam name="T">Type of sought-for node.</typeparam>
		/// <param name="path">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public T TryFind<T>(string path) where T : Node
		{
			return TryFindNode(path) as T;
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <typeparam name="T">Type of sought-for node.</typeparam>
		/// <param name="format">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public T TryFind<T>(string format, params object[] args) where T : Node
		{
			return TryFind<T>(string.Format(format, args));
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Throws an exception if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <param name="path">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public Node FindNode(string path)
		{
			var node = TryFindNode(path);
			if (node == null) {
				throw new Lime.Exception("'{0}' not found for '{1}'", path, ToString());
			}
			return node;
		}

		/// <summary>
		/// Searches for node with provided path or id in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <param name="path">Id or path of Node. Path can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		public Node TryFindNode(string path)
		{
			if (path == null) {
				return null;
			}
			if (path.IndexOf('/') >= 0) {
				return TryFindNodeByPath(path);
			} else {
				return TryFindNodeById(path);
			}
		}

		/// <summary>
		/// Searches for node with provided path in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		/// <param name="path">Path of node. Can be incomplete
		/// (i.e. for path Root/Human/Head/Eye Human or Head can be ommited).</param>
		private Node TryFindNodeByPath(string path)
		{
			Node child = this;
			var start = 0;

			while (start <= path.Length) {
				var end = path.IndexOf('/', start);
				end = end >= 0 ? end : path.Length;
				child = child.TryFindNodeById(path, start, end - start);
				if (child == null) {
					break;
				}
				start = end + 1;
			}

			return child;
		}

		/// <summary>
		/// Gets a DFS-enumeration of all descendant nodes.
		/// </summary>
		public IEnumerable<Node> Descendants => new DescendantsEnumerable(this);

		/// <summary>
		/// Enumerate all node's ancestors.
		/// </summary>
		public IEnumerable<Node> Ancestors
		{
			get
			{
				for (var p = Parent; p != null; p = p.Parent) {
					yield return p;
				}
			}
		}

		/// <summary>
		/// TODO: Translate
		/// Этот метод масштабирует позицию и размер данного нода и всех его потомков.
		/// Метод полезен для адаптирования сцены под экраны с нестандартным DPI.
		/// Если позиция или размер виджета анинимированы, то ключи анимации также подвергаются соответствующему
		/// преобразованию.
		/// Для текстовых виджетов, размер шрифта масштабируется.
		/// </summary>
		/// <param name="roundCoordinates">Если true, то после масштабирования виджета, его размер округляется, а сам виджет сдвигается таким образом,
		/// чтобы его левый верхний угол (точка (0,0) в локальных координатах виджета) перешел в целочисленную позицию.</param>
		public virtual void StaticScale(float ratio, bool roundCoordinates)
		{
			foreach (var node in Nodes) {
				node.StaticScale(ratio, roundCoordinates);
			}
		}

		[ThreadStatic]
		private static Queue<Node> nodeSearchQueue;

		/// <summary>
		/// Searches for node with provided id in this node's descendants.
		/// Returns null if sought-for node doesn't exist.
		/// <para>This function is thread safe.</para>
		/// </summary>
		private Node TryFindNodeById(string id)
		{
			return TryFindNodeById(id, 0, id.Length);
		}

		private Node TryFindNodeById(string path, int start, int length)
		{
			if (nodeSearchQueue == null) {
				nodeSearchQueue = new Queue<Node>();
			}
			var queue = nodeSearchQueue;
			queue.Enqueue(this);
			while (queue.Count > 0) {
				var parent = queue.Dequeue();
				var child = parent.FirstChild;
				for (; child != null; child = child.NextSibling) {
					var isEqual =
						child.Id != null &&
						child.Id.Length == length &&
						string.CompareOrdinal(child.Id, 0, path, start, length) == 0;
					if (isEqual) {
						queue.Clear();
						return child;
					}
					queue.Enqueue(child);
				}
			}
			return null;
		}

		/// <summary>
		/// Advances all running animations by provided delta.
		/// </summary>
		/// <param name="delta">Time delta (in seconds).</param>
		public void AdvanceAnimation(float delta)
		{
			if (RunningAnimationCount > 0) {
				for (var a = FirstAnimation; a != null; a = a.Next) {
					a.Advance(delta);
				}
			}
		}

		/// <summary>
		/// Loads all textures, fonts and animators for this node and for all its descendants.
		/// Forces reloading of textures if they are null.
		/// </summary>
		public void PreloadAssets()
		{
			foreach (var prop in GetType().GetProperties()) {
				if (prop.PropertyType == typeof(ITexture)) {
					PreloadTexture(prop);
				}
			}
			foreach (var animator in Animators) {
				PreloadAnimatedTextures(animator);
			}
			foreach (var node in Nodes) {
				node.PreloadAssets();
			}
		}

		private static void PreloadAnimatedTextures(IAnimator animator)
		{
			if (animator is Animator<ITexture> textureAnimator) {
				foreach (var key in textureAnimator.ReadonlyKeys) {
					key.Value.GetHandle();
				}
			}
		}

		private void PreloadTexture(PropertyInfo prop)
		{
			var getter = prop.GetGetMethod();
			var texture = getter.Invoke(this, new object[]{}) as ITexture;
			texture?.GetHandle();
		}

		private static readonly ThreadLocal<HashSet<string>> scenesBeingLoaded = new ThreadLocal<HashSet<string>>(() => new HashSet<string>());

		public delegate bool SceneLoadingDelegate(string path, ref Node instance, bool external);
		public delegate void SceneLoadedDelegate(string path, Node instance, bool external);
		public static ThreadLocal<SceneLoadingDelegate> SceneLoading;
		public static ThreadLocal<SceneLoadedDelegate> SceneLoaded;

		/// <summary>
		/// For each root node of deserialized node hierarchy this component will be added and set to path in asset bundle.
		/// </summary>
		[NodeComponentDontSerialize]
		public class AssetBundlePathComponent : NodeComponent
		{
			public string Path;
			public AssetBundlePathComponent()
			{

			}
			public AssetBundlePathComponent(string path)
			{
				this.Path = path;
			}
			public override string ToString()
			{
				return Path;
			}
		}

		public static Node CreateFromAssetBundle(string path, Node instance = null, Yuzu yuzu = null)
		{
			yuzu = yuzu ?? Yuzu.Instance.Value;
			return CreateFromAssetBundleHelper(path, instance, yuzu, external: false);
		}

		private static Node CreateFromAssetBundleHelper(string path, Node instance = null, Yuzu yuzu = null, bool external = false)
		{
			if (SceneLoading?.Value?.Invoke(path, ref instance, external) ?? false) {
				SceneLoaded?.Value?.Invoke(path, instance, external);
				return instance;
			}
			var fullPath = ResolveScenePath(path);
			if (fullPath == null) {
				throw new Exception($"Scene '{path}' not found in current asset bundle");
			}
			if (scenesBeingLoaded.Value.Contains(fullPath)) {
				throw new Exception($"Cyclic scenes dependency was detected: {fullPath}");
			}
			scenesBeingLoaded.Value.Add(fullPath);
			try {
				using (Stream stream = AssetBundle.Current.OpenFileLocalized(fullPath)) {
					instance = yuzu.ReadObject<Node>(fullPath, stream, instance);
				}
				instance.LoadExternalScenes(yuzu);
				instance.Components.Add(new AssetBundlePathComponent(fullPath));
			} finally {
				scenesBeingLoaded.Value.Remove(fullPath);
			}
			if (instance is Model3D model3DInstance) {
				var attachment = new Model3DAttachmentParser().Parse(path);
				attachment?.Apply(model3DInstance);
			}
			SceneLoaded?.Value?.Invoke(path, instance, external);
			return instance;
		}

		public void LoadExternalScenes(Yuzu yuzu = null)
		{
			yuzu = yuzu ?? Yuzu.Instance.Value;
			if (string.IsNullOrEmpty(ContentsPath)) {
				foreach (var child in Nodes) {
					child.LoadExternalScenes();
				}
			} else if (ResolveScenePath(ContentsPath) != null) {
				var content = CreateFromAssetBundleHelper(ContentsPath, null, yuzu, external: true);
				ReplaceContent(content);
			}
		}

		public void ReplaceContent(Node content)
		{
			var nodeType = GetType();
			var contentType = content.GetType();
			if (nodeType != contentType && !contentType.IsSubclassOf(nodeType)) {
				// Handle legacy case: Replace Button content by external Frame
				if (nodeType == typeof(Button) && contentType == typeof(Frame)) {
					Components.Remove(typeof(AssetBundlePathComponent));
					var assetBundlePathComponent = content.Components.Get<AssetBundlePathComponent>();
					if (assetBundlePathComponent != null) {
						Components.Add(assetBundlePathComponent.Clone());
					}
				} else {
					throw new Exception($"Can not replace {nodeType.FullName} content with {contentType.FullName}");
				}
			} else {
				Components = content.Components.Clone(this);
			}

			if (content is IExternalScenePropertyOverrideChecker coordinator) {
				var properties = nodeType.GetProperties(
					BindingFlags.Instance |
					BindingFlags.Public
				);

				foreach (var property in properties) {
					if (coordinator.IsPropertyOverridden(property)) {
						continue;
					}

					property.SetValue(this, property.GetValue(content));
				}
			} else {
				if ((content is Widget widget) && (this is Widget)) {
					widget.Size = ((Widget)this).Size;
				}
			}

			Animations.Clear();
			var animations = content.Animations.ToList();
			content.Animations.Clear();
			Animations.AddRange(animations);
			if ((content is Viewport3D) && (this is Node3D) && (content.Nodes.Count > 0)) {
				// Handle a special case: the 3d scene is wrapped up with a Viewport3D.
				var node = content.Nodes[0];
				node.Unlink();
				Nodes.Clear();
				Nodes.Add(node);
			} else {
				var nodes = content.Nodes.ToList();
				content.Nodes.Clear();
				Nodes.Clear();
				Nodes.AddRange(nodes);
			}

			RenderChainBuilder = content.RenderChainBuilder?.Clone(this);
			Presenter = content.Presenter?.Clone();
			PostPresenter = content.PostPresenter?.Clone();
		}

		private static readonly string[] sceneExtensions = { ".scene", ".t3d", ".tan" };

		/// <summary>
		/// Returns path to scene if it exists in bundle. Returns null otherwise.
		/// Throws exception if there is more than one scene file with such path.
		/// </summary>
		public static string ResolveScenePath(string path)
		{
			var candidates = sceneExtensions.Select(ext => Path.ChangeExtension(path, ext)).Where(AssetBundle.Current.FileExists);
			if (candidates.Count() > 1) {
				throw new Exception("Ambiguity between: {0}", string.Join("; ", candidates.ToArray()));
			}
			return candidates.FirstOrDefault();
		}

		public bool IsMouseOver()
		{
#if WIN || MAC
			if (Application.WindowUnderMouse != Window.Current) {
				return false;
			}
#endif
			var context = WidgetContext.Current;
			if (context.NodeUnderMouse != this) {
				return false;
			}
			return context.NodeCapturedByMouse == null || context.NodeCapturedByMouse == this;
		}

		public bool IsMouseOverThisOrDescendant()
		{
#if WIN || MAC
			if (Application.WindowUnderMouse != Window.Current) {
				return false;
			}
#endif
			var context = WidgetContext.Current;
			var nodeUnderMouse = context.NodeUnderMouse;
			if (nodeUnderMouse == null || !nodeUnderMouse.SameOrDescendantOf(this)) {
				return false;
			}
			return context.NodeCapturedByMouse?.SameOrDescendantOf(this) ?? true;
		}

		internal protected virtual bool PartialHitTest(ref HitTestArgs args)
		{
			return false;
		}

		private class DescendantsEnumerable : IEnumerable<Node>
		{
			private readonly Node root;

			public DescendantsEnumerable(Node root)
			{
				this.root = root;
			}

			public IEnumerator<Node> GetEnumerator()
			{
				return new Enumerator(root);
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}

			private class Enumerator : IEnumerator<Node>
			{
				private readonly Node root;
				private Node current;

				public Enumerator(Node root)
				{
					this.root = root;
				}

				public Node Current
				{
					get
					{
						if (current == null) {
							throw new InvalidOperationException();
						}
						return current;
					}
				}

				object IEnumerator.Current => Current;

				public void Dispose()
				{
					current = null;
				}

				public bool MoveNext()
				{
					if (current == null) {
						current = root;
					}
					var node = current.FirstChild;
					if (node != null) {
						current = node;
						return true;
					}
					if (current == root) {
						return false;
					}
					while (current.NextSibling == null) {
						current = current.Parent;
						if (current == root) {
							return false;
						}
					}
					current = current.NextSibling;
					return true;
				}

				public void Reset()
				{
					current = null;
				}
			}
		}
	}
}

using System;
using System.IO;
using System.Linq;
using Lime;
using Tangerine.Core;
using System.Collections.Generic;

namespace Tangerine.UI
{
	public interface IPropertyEditor
	{
		IPropertyEditorParams EditorParams { get; }
		Widget ContainerWidget { get; }
		void SetFocus();
		void DropFiles(IEnumerable<string> files);
	}

	public interface IPropertyEditorParams
	{
		Widget InspectorPane { get; set; }
		List<object> Objects { get; set; }
		Type Type { get; set; }
		string PropertyName { get; set; }
		string DisplayName { get; set; }
		TangerineKeyframeColorAttribute TangerineAttribute { get; set; }
		System.Reflection.PropertyInfo PropertyInfo { get; set; }
		Func<NumericEditBox> NumericEditBoxFactory { get; set; }
		PropertySetterDelegate PropertySetter { get; set; }
	}

	public delegate void PropertySetterDelegate(object obj, string propertyName, object value);

	public class PropertyEditorParams : IPropertyEditorParams
	{
		public Widget InspectorPane { get; set; }
		public List<object> Objects { get; set; }
		public Type Type { get; set; }
		public string PropertyName { get; set; }
		public string DisplayName { get; set; }
		public TangerineKeyframeColorAttribute TangerineAttribute { get; set; }
		public System.Reflection.PropertyInfo PropertyInfo { get; set; }
		public Func<NumericEditBox> NumericEditBoxFactory { get; set; }
		public PropertySetterDelegate PropertySetter { get; set; }

		public PropertyEditorParams(Widget inspectorPane, List<object> objects, Type type, string propertyName)
		{
			InspectorPane = inspectorPane;
			Objects = objects;
			Type = type;
			PropertyName = propertyName;
			TangerineAttribute = PropertyAttributes<TangerineKeyframeColorAttribute>.Get(Type, PropertyName) ?? new TangerineKeyframeColorAttribute(0);
			PropertyInfo = Type.GetProperty(PropertyName);
			PropertySetter = SetProperty;
			NumericEditBoxFactory = () => new NumericEditBox();
		}

		public PropertyEditorParams(Widget inspectorPane, object obj, string propertyName, string displayName = null)
			: this(inspectorPane, new List<object> { obj }, obj.GetType(), propertyName)
		{
			DisplayName = displayName;
		}

		private void SetProperty(object obj, string propertyName, object value) => PropertyInfo.SetValue(obj, value);
	}

	public class CommonPropertyEditor : IPropertyEditor
	{
		public IPropertyEditorParams EditorParams { get; private set; }
		public Widget ContainerWidget { get; private set; }
		public SimpleText PropertyNameLabel { get; private set; }

		public CommonPropertyEditor(IPropertyEditorParams editorParams)
		{
			EditorParams = editorParams;
			ContainerWidget = new Widget {
				Layout = new HBoxLayout { IgnoreHidden = false },
				Padding = new Thickness { Left = 4, Right = 12 },
				LayoutCell = new LayoutCell { StretchY = 0 },
			};
			editorParams.InspectorPane.AddNode(ContainerWidget);
			PropertyNameLabel = new SimpleText {
				Text = editorParams.DisplayName ?? editorParams.PropertyName,
				VAlignment = VAlignment.Center,
				LayoutCell = new LayoutCell(Alignment.LeftCenter, stretchX: 0.5f),
				AutoSizeConstraints = false,
			};
			ContainerWidget.AddNode(PropertyNameLabel);
		}

		public virtual void DropFiles(IEnumerable<string> files) { }

		public virtual void SetFocus() { }

		protected static IDataflowProvider<T> CoalescedPropertyValue<T>(IPropertyEditorParams editorParams, T defaultValue = default(T))
		{
			IDataflowProvider<T> provider = null;
			foreach (var o in editorParams.Objects) {
				var p = new Property<T>(o, editorParams.PropertyName);
				provider = (provider == null) ? p : provider.SameOrDefault(p, defaultValue);
			}
			return provider;
		}

		protected static IDataflowProvider<T2> CoalescedPropertyValue<T1, T2>(IPropertyEditorParams editorParams, Func<T1, T2> selector, T2 defaultValue = default(T2))
		{
			IDataflowProvider<T2> provider = null;
			foreach (var o in editorParams.Objects) {
				var p = new Property<T1>(o, editorParams.PropertyName).Select(selector);
				provider = (provider == null) ? p : provider.SameOrDefault(p, defaultValue);
			}
			return provider;
		}

		protected void SetProperty(string name, object value)
		{
			foreach (var o in EditorParams.Objects) {
				EditorParams.PropertySetter(o, name, value);
			}
		}
	}

	public class Vector2PropertyEditor : CommonPropertyEditor
	{
		private NumericEditBox editorX, editorY;

		public Vector2PropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			ContainerWidget.AddNode(new Widget {
				Layout = new HBoxLayout { CellDefaults = new LayoutCell(Alignment.Center), Spacing = 4 },
				Nodes = {
					(editorX = editorParams.NumericEditBoxFactory()),
					(editorY = editorParams.NumericEditBoxFactory()),
				}
			});
			var currentX = CoalescedPropertyValue<Vector2, float>(editorParams, v => v.X);
			var currentY = CoalescedPropertyValue<Vector2, float>(editorParams, v => v.Y);
			editorX.TextWidget.HAlignment = HAlignment.Right;
			editorY.TextWidget.HAlignment = HAlignment.Right;
			editorX.Submitted += text => SetComponent(editorParams, 0, editorX, currentX.GetValue());
			editorY.Submitted += text => SetComponent(editorParams, 1, editorY, currentY.GetValue());
			editorX.AddChangeWatcher(currentX, v => editorX.Text = v.ToString());
			editorY.AddChangeWatcher(currentY, v => editorY.Text = v.ToString());
		}

		public override void SetFocus() => editorX.SetFocus();

		void SetComponent(IPropertyEditorParams editorParams, int component, CommonEditBox editor, float currentValue)
		{
			float newValue;
			if (float.TryParse(editor.Text, out newValue)) {
				foreach (var obj in editorParams.Objects) {
					var current = new Property<Vector2>(obj, editorParams.PropertyName).Value;
					current[component] = newValue;
					editorParams.PropertySetter(obj, editorParams.PropertyName, current);
				}
			} else {
				editor.Text = currentValue.ToString();
			}
		}
	}

	public class Vector3PropertyEditor : CommonPropertyEditor
	{
		private NumericEditBox editorX, editorY, editorZ;

		public Vector3PropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			ContainerWidget.AddNode(new Widget {
				Layout = new HBoxLayout { CellDefaults = new LayoutCell(Alignment.Center), Spacing = 4 },
				Nodes = {
					(editorX = editorParams.NumericEditBoxFactory()),
					(editorY = editorParams.NumericEditBoxFactory()),
					(editorZ = editorParams.NumericEditBoxFactory())
				}
			});
			var currentX = CoalescedPropertyValue<Vector3, float>(editorParams, v => v.X);
			var currentY = CoalescedPropertyValue<Vector3, float>(editorParams, v => v.Y);
			var currentZ = CoalescedPropertyValue<Vector3, float>(editorParams, v => v.Z);
			editorX.TextWidget.HAlignment = HAlignment.Right;
			editorY.TextWidget.HAlignment = HAlignment.Right;
			editorZ.TextWidget.HAlignment = HAlignment.Right;
			editorX.Submitted += text => SetComponent(editorParams, 0, editorX, currentX.GetValue());
			editorY.Submitted += text => SetComponent(editorParams, 1, editorY, currentY.GetValue());
			editorZ.Submitted += text => SetComponent(editorParams, 2, editorZ, currentZ.GetValue());
			editorX.AddChangeWatcher(currentX, v => editorX.Text = v.ToString());
			editorY.AddChangeWatcher(currentY, v => editorY.Text = v.ToString());
			editorZ.AddChangeWatcher(currentZ, v => editorZ.Text = v.ToString());
		}

		public override void SetFocus() => editorX.SetFocus();

		void SetComponent(IPropertyEditorParams editorParams, int component, NumericEditBox editor, float currentValue)
		{
			float newValue;
			if (float.TryParse(editor.Text, out newValue)) {
				foreach (var obj in editorParams.Objects) {
					var current = new Property<Vector3>(obj, editorParams.PropertyName).Value;
					current[component] = newValue;
					editorParams.PropertySetter(obj, editorParams.PropertyName, current);
				}
			} else {
				editor.Text = currentValue.ToString();
			}
		}
	}

	public class QuaternionPropertyEditor : CommonPropertyEditor
	{
		private NumericEditBox editorX, editorY, editorZ;

		public QuaternionPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			ContainerWidget.AddNode(new Widget {
				Layout = new HBoxLayout { CellDefaults = new LayoutCell(Alignment.Center), Spacing = 4 },
				Nodes = {
					(editorX = editorParams.NumericEditBoxFactory()),
					(editorY = editorParams.NumericEditBoxFactory()),
					(editorZ = editorParams.NumericEditBoxFactory())
				}
			});
			var current = CoalescedPropertyValue<Quaternion>(editorParams);
			editorX.TextWidget.HAlignment = HAlignment.Right;
			editorY.TextWidget.HAlignment = HAlignment.Right;
			editorZ.TextWidget.HAlignment = HAlignment.Right;
			editorX.Submitted += text => SetComponent(editorParams, 0, editorX, current.GetValue());
			editorY.Submitted += text => SetComponent(editorParams, 1, editorY, current.GetValue());
			editorZ.Submitted += text => SetComponent(editorParams, 2, editorZ, current.GetValue());
			editorX.AddChangeWatcher(current, v => {
				var ea = v.ToEulerAngles() * Mathf.RadToDeg;
				editorX.Text = RoundAngle(ea.X).ToString();
				editorY.Text = RoundAngle(ea.Y).ToString();
				editorZ.Text = RoundAngle(ea.Z).ToString();
			});
		}

		public override void SetFocus() => editorX.SetFocus();

		float RoundAngle(float value) => (value * 1000f).Round() / 1000f;

		void SetComponent(IPropertyEditorParams editorParams, int component, NumericEditBox editor, Quaternion currentValue)
		{
			float newValue;
			if (float.TryParse(editor.Text, out newValue)) {
				foreach (var obj in editorParams.Objects) {
					var current = new Property<Quaternion>(obj, editorParams.PropertyName).Value.ToEulerAngles();
					current[component] = newValue * Mathf.DegToRad;
					editorParams.PropertySetter(obj, editorParams.PropertyName,
						Quaternion.CreateFromEulerAngles(current));
				}
			} else {
				editor.Text = RoundAngle(currentValue.ToEulerAngles()[component] * Mathf.RadToDeg).ToString();
			}
		}
	}

	public class NumericRangePropertyEditor : CommonPropertyEditor
	{
		private NumericEditBox medEditor, dispEditor;

		public NumericRangePropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			ContainerWidget.AddNode(new Widget {
				Layout = new HBoxLayout { CellDefaults = new LayoutCell(Alignment.Center), Spacing = 4 },
				Nodes = {
					(medEditor = editorParams.NumericEditBoxFactory()),
					(dispEditor = editorParams.NumericEditBoxFactory()),
				}
			});
			var currentMed = CoalescedPropertyValue<NumericRange, float>(editorParams, v => v.Median);
			var currentDisp = CoalescedPropertyValue<NumericRange, float>(editorParams, v => v.Dispersion);
			medEditor.TextWidget.HAlignment = HAlignment.Right;
			dispEditor.TextWidget.HAlignment = HAlignment.Right;
			medEditor.Submitted += text => SetComponent(editorParams, 0, medEditor, currentMed.GetValue());
			dispEditor.Submitted += text => SetComponent(editorParams, 1, dispEditor, currentDisp.GetValue());
			medEditor.AddChangeWatcher(currentMed, v => medEditor.Text = v.ToString());
			dispEditor.AddChangeWatcher(currentDisp, v => dispEditor.Text = v.ToString());
		}

		public override void SetFocus() => medEditor.SetFocus();

		void SetComponent(IPropertyEditorParams editorParams, int component, NumericEditBox editor, float currentValue)
		{
			float newValue;
			if (float.TryParse(editor.Text, out newValue)) {
				foreach (var obj in editorParams.Objects) {
					var current = new Property<NumericRange>(obj, editorParams.PropertyName).Value;
					if (component == 0) {
						current.Median = newValue;
					} else {
						current.Dispersion = newValue;
					}
					editorParams.PropertySetter(obj, editorParams.PropertyName, current);
				}
			} else {
				editor.Text = currentValue.ToString();
			}
		}
	}

	public class NodeReferencePropertyEditor<T> : CommonPropertyEditor where T : Node
	{
		private EditBox editor;

		public NodeReferencePropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			var propName = editorParams.PropertyName;
			if (propName.EndsWith("Ref")) {
				PropertyNameLabel.Text = propName.Substring(0, propName.Length - 3);
			}
			editor = new EditBox { LayoutCell = new LayoutCell(Alignment.Center) };
			ContainerWidget.AddNode(editor);
			editor.Submitted += text => {
				var value = new NodeReference<T>(text);
				SetProperty(editorParams.PropertyName, value);
			};
			editor.AddChangeWatcher(CoalescedPropertyValue<NodeReference<T>>(editorParams), v => editor.Text = v.Id);
		}

		public override void SetFocus() => editor.SetFocus();
	}

	public class StringPropertyEditor : CommonPropertyEditor
	{
		const int maxLines = 5;
		private EditBox editor;

		public StringPropertyEditor(IPropertyEditorParams editorParams, bool multiline = false) : base(editorParams)
		{
			editor = new EditBox { LayoutCell = new LayoutCell(Alignment.Center) };
			editor.Editor.EditorParams.MaxLines = multiline ? maxLines : 1;
			editor.MinHeight += multiline ? editor.TextWidget.FontHeight * (maxLines - 1) : 0;
			ContainerWidget.AddNode(editor);
			editor.Submitted += text => SetProperty(editorParams.PropertyName, text);
			editor.AddChangeWatcher(CoalescedPropertyValue<string>(editorParams), v => editor.Text = v);
		}

		public override void SetFocus() => editor.SetFocus();
	}

	public class EnumPropertyEditor<T> : CommonPropertyEditor
	{
		private DropDownList selector;

		public EnumPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			selector = new DropDownList { LayoutCell = new LayoutCell(Alignment.Center) };
			ContainerWidget.AddNode(selector);
			var propType = editorParams.PropertyInfo.PropertyType;
			foreach (var i in Enum.GetNames(propType).Zip(Enum.GetValues(propType).Cast<object>(), (a, b) => new DropDownList.Item(a, b))) {
				selector.Items.Add(i);
			}
			selector.Changed += a => SetProperty(editorParams.PropertyName, (T)selector.Items[a.Index].Value);
			selector.AddChangeWatcher(CoalescedPropertyValue<T>(editorParams), v => selector.Value = v);
		}

		public override void SetFocus() => selector.SetFocus();
	}

	public class BooleanPropertyEditor : CommonPropertyEditor
	{
		private CheckBox checkBox;

		public BooleanPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			checkBox = new CheckBox { LayoutCell = new LayoutCell(Alignment.Center) };
			ContainerWidget.AddNode(checkBox);
			checkBox.Changed += value => SetProperty(editorParams.PropertyName, value);
			checkBox.AddChangeWatcher(CoalescedPropertyValue<bool>(editorParams), v => checkBox.Checked = v);
		}

		public override void SetFocus() => checkBox.SetFocus();
	}

	public class FloatPropertyEditor : CommonPropertyEditor
	{
		private NumericEditBox editor;

		public FloatPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			editor = editorParams.NumericEditBoxFactory();
			editor.LayoutCell = new LayoutCell(Alignment.Center);
			editor.TextWidget.HAlignment = HAlignment.Right;
			ContainerWidget.AddNode(editor);
			var current = CoalescedPropertyValue<float>(editorParams);
			editor.Submitted += text => {
				float newValue;
				if (float.TryParse(text, out newValue)) {
					SetProperty(editorParams.PropertyName, newValue);
				} else {
					editor.Text = current.GetValue().ToString();
				}
			};
			editor.AddChangeWatcher(current, v => editor.Text = v.ToString());
		}

		public override void SetFocus() => editor.SetFocus();
	}

	public class IntPropertyEditor : CommonPropertyEditor
	{
		private EditBox editor;

		public IntPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			editor = new EditBox { LayoutCell = new LayoutCell(Alignment.Center) };
			editor.TextWidget.HAlignment = HAlignment.Right;
			ContainerWidget.AddNode(editor);
			var current = CoalescedPropertyValue<int>(editorParams);
			editor.Submitted += text => {
				int newValue;
				if (int.TryParse(text, out newValue)) {
					SetProperty(editorParams.PropertyName, newValue);
				} else {
					editor.Text = current.GetValue().ToString();
				}
			};
			editor.AddChangeWatcher(current, v => editor.Text = v.ToString());
		}

		public override void SetFocus() => editor.SetFocus();
	}

	public class Color4PropertyEditor : CommonPropertyEditor
	{
		private EditBox editor;

		public Color4PropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			ColorBoxButton colorBox;
			var currentColor = CoalescedPropertyValue(editorParams, Color4.White).DistinctUntilChanged();
			ContainerWidget.AddNode(new Widget {
				Layout = new HBoxLayout { CellDefaults = new LayoutCell(Alignment.Center) },
				Nodes = {
					(editor = new EditBox()),
					new HSpacer(4),
					(colorBox = new ColorBoxButton(currentColor)),
				}
			});
			var panel = new ColorPickerPanel();
			editorParams.InspectorPane.AddNode(panel.RootWidget);
			panel.RootWidget.Visible = false;
			panel.RootWidget.Padding.Right = 12;
			panel.RootWidget.Tasks.Add(currentColor.Consume(v => panel.Color = v));
			panel.Changed += () => SetProperty(editorParams.PropertyName, panel.Color);
			panel.DragStarted += Document.Current.History.BeginTransaction;
			panel.DragEnded += Document.Current.History.EndTransaction;
			colorBox.Clicked += () => panel.RootWidget.Visible = !panel.RootWidget.Visible;
			var currentColorString = currentColor.Select(i => i.ToString(Color4.StringPresentation.Dec));
			editor.Submitted += text => {
				Color4 newColor;
				if (Color4.TryParse(text, out newColor)) {
					SetProperty(editorParams.PropertyName, newColor);
				} else {
					editor.Text = currentColorString.GetValue();
				}
			};
			editor.Tasks.Add(currentColorString.Consume(v => editor.Text = v));
		}

		public override void SetFocus() => editor.SetFocus();

		class ColorBoxButton : Button
		{
			public ColorBoxButton(IDataflowProvider<Color4> colorProvider)
			{
				Nodes.Clear();
				Size = MinMaxSize = new Vector2(25, DesktopTheme.Metrics.DefaultButtonSize.Y);
				var color = colorProvider.GetDataflow();
				PostPresenter = new DelegatePresenter<Widget>(widget => {
					widget.PrepareRendererState();
					Renderer.DrawRect(Vector2.Zero, widget.Size, Color4.White);
					color.Poll();
					var checkSize = new Vector2(widget.Width / 4, widget.Height / 3);
					for (int i = 0; i < 3; i++) {
						var checkPos = new Vector2(widget.Width / 2 + ((i == 1) ? widget.Width / 4 : 0), i * checkSize.Y);
						Renderer.DrawRect(checkPos, checkPos + checkSize, Color4.Black);
					}
					Renderer.DrawRect(Vector2.Zero, widget.Size, color.Value);
					Renderer.DrawRectOutline(Vector2.Zero, widget.Size, ColorTheme.Current.Inspector.BorderAroundKeyframeColorbox);
				});
			}
		}
	}

	public abstract class FilePropertyEditor : CommonPropertyEditor
	{
		protected readonly EditBox editor;
		protected readonly Button button;

		protected FilePropertyEditor(IPropertyEditorParams editorParams, string[] allowedFileTypes) : base(editorParams)
		{
			ContainerWidget.AddNode(new Widget {
				Layout = new HBoxLayout(),
				Nodes = {
					(editor = new EditBox { LayoutCell = new LayoutCell(Alignment.Center) }),
					new HSpacer(4),
					(button = new Button {
						Text = "...",
						MinMaxWidth = 20,
						Draggable = true,
						LayoutCell = new LayoutCell(Alignment.Center)
					})
				}
			});
			editor.Submitted += text => AssignAsset(AssetPath.CorrectSlashes(text));
			button.Clicked += () => {
				var dlg = new FileDialog {
					AllowedFileTypes = allowedFileTypes,
					Mode = FileDialogMode.Open,
					InitialDirectory = Project.Current.GetSystemDirectory(Document.Current.Path),
				};
				if (dlg.RunModal()) {
					SetFilePath(dlg.FileName);
				}
			};
		}

		private void SetFilePath(string path)
		{
			string asset, type;
			if (Utils.ExtractAssetPathOrShowAlert(path, out asset, out type)) {
				AssignAsset(AssetPath.CorrectSlashes(asset));
			}
		}

		public override void DropFiles(IEnumerable<string> files)
		{
			if (editor.IsMouseOverThisOrDescendant() && files.Any()) {
				SetFilePath(files.First());
			}
		}

		public override void SetFocus() => editor.SetFocus();

		protected abstract void AssignAsset(string path);
	}

	public class TexturePropertyEditor : FilePropertyEditor
	{
		public TexturePropertyEditor(IPropertyEditorParams editorParams) : base(editorParams, new string[] { "png" })
		{
			editor.AddChangeWatcher(CoalescedPropertyValue<ITexture>(editorParams), v => editor.Text = v?.SerializationPath ?? "");
		}

		protected override void AssignAsset(string path)
		{
			SetProperty(EditorParams.PropertyName, new SerializableTexture(path));
		}
	}

	public class AudioSamplePropertyEditor : FilePropertyEditor
	{
		public AudioSamplePropertyEditor(IPropertyEditorParams editorParams) : base(editorParams, new string[] { "ogg" })
		{
			editor.AddChangeWatcher(CoalescedPropertyValue<SerializableSample>(editorParams), v => editor.Text = v?.SerializationPath ?? "");
		}

		protected override void AssignAsset(string path)
		{
			SetProperty(EditorParams.PropertyName, new SerializableSample(path));
		}
	}

	public class ContentsPathPropertyEditor : FilePropertyEditor
	{
		public ContentsPathPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams, Document.AllowedFileTypes)
		{
			editor.AddChangeWatcher(CoalescedPropertyValue<string>(editorParams), v => editor.Text = v);
		}

		protected override void AssignAsset(string path)
		{
			SetProperty(EditorParams.PropertyName, path);
			Document.Current.RefreshExternalScenes();
		}
	}

	public class FontPropertyEditor : CommonPropertyEditor
	{
		private DropDownList selector;

		public FontPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			selector = new DropDownList { LayoutCell = new LayoutCell(Alignment.Center) };
			ContainerWidget.AddNode(selector);
			var propType = editorParams.PropertyInfo.PropertyType;
			var items = AssetBundle.Instance.EnumerateFiles("Fonts").
				Where(i => i.EndsWith(".fnt") || i.EndsWith(".tft")).
				Select(i => new DropDownList.Item(Path.ChangeExtension(Path.GetFileName(i), null)));
			foreach (var i in items) {
				selector.Items.Add(i);
			}
			selector.Changed += a => {
				var font = new SerializableFont((string)a.Value);
				SetProperty(editorParams.PropertyName, font);
			};
			selector.AddChangeWatcher(CoalescedPropertyValue<SerializableFont>(editorParams), i => {
				selector.Text = string.IsNullOrEmpty(i?.Name) ? "Default" : i.Name;
			});
		}

		public override void SetFocus() => selector.SetFocus();
	}

	public class TriggerPropertyEditor : CommonPropertyEditor
	{
		private ComboBox comboBox;

		public TriggerPropertyEditor(IPropertyEditorParams editorParams, bool multiline = false) : base(editorParams)
		{
			comboBox = new ComboBox { LayoutCell = new LayoutCell(Alignment.Center) };
			ContainerWidget.AddNode(comboBox);
			comboBox.Changed += ComboBox_Changed;
			if (editorParams.Objects.Count == 1) {
				var node = (Node)editorParams.Objects[0];
				foreach (var a in node.Animations) {
					foreach (var m in a.Markers.Where(i => i.Action != MarkerAction.Jump && !string.IsNullOrEmpty(i.Id))) {
						var id = a.Id != null ? m.Id + '@' + a.Id : m.Id;
						comboBox.Items.Add(new DropDownList.Item(id));
					}
				}
			}
			comboBox.AddChangeWatcher(CoalescedPropertyValue<string>(editorParams), v => comboBox.Text = v);
		}

		void ComboBox_Changed(ChangedEventArgs args)
		{
			var newTrigger = (string)args.Value;
			var currentTriggers = CoalescedPropertyValue<string>(EditorParams).GetValue();
			if (string.IsNullOrWhiteSpace(currentTriggers) || args.Index < 0) {
				SetProperty(EditorParams.PropertyName, newTrigger);
				return;
			}
			var triggers = new List<string>();
			var added = false;
			string newMarker, newAnimation;
			SplitTrigger(newTrigger, out newMarker, out newAnimation);
			foreach (var trigger in currentTriggers.Split(',').Select(i => i.Trim())) {
				string marker, animation;
				SplitTrigger(trigger, out marker, out animation);
				if (animation == newAnimation) {
					if (!added) {
						added = true;
						triggers.Add(newTrigger);
					}
				} else {
					triggers.Add(trigger);
				}
			}
			if (!added) {
				triggers.Add(newTrigger);
			}
			var newValue = string.Join(",", triggers);
			SetProperty(EditorParams.PropertyName, newValue);
			comboBox.Text = newValue;
		}

		private static void SplitTrigger(string trigger, out string markerId, out string animationId)
		{
			if (!trigger.Contains('@')) {
				markerId = trigger;
				animationId = null;
			} else {
				var t = trigger.Split('@');
				markerId = t[0];
				animationId = t[1];
			}
		}

		public override void SetFocus() => comboBox.SetFocus();
	}

	public class AnchorsPropertyEditor : CommonPropertyEditor
	{
		private ToolbarButton firstButton;
		private Widget group;

		public AnchorsPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			group = new Widget { Layout = new HBoxLayout { CellDefaults = new LayoutCell(Alignment.Center), Spacing = 4 } };
			ContainerWidget.AddNode(group);
			group.AddNode(new Widget());
			firstButton = AddButton(Anchors.Left, "Anchor to the left");
			AddButton(Anchors.Right, "Anchor to the right");
			AddButton(Anchors.Top, "Anchor to the top");
			AddButton(Anchors.Bottom, "Anchor to the bottom");
			AddButton(Anchors.CenterH, "Anchor to the center horizontally");
			AddButton(Anchors.CenterV, "Anchor to the center vertically");
		}

		ToolbarButton AddButton(Anchors anchor, string tip)
		{
			var tb = new AnchorButton { LayoutCell = new LayoutCell(Alignment.Center), Tip = tip };
			group.AddNode(tb);
			var current = CoalescedPropertyValue<Anchors>(EditorParams);
			tb.CompoundPresenter.Insert(0, new DelegatePresenter<Widget>(w => DrawIcon(w, anchor)));
			tb.Clicked += () => {
				tb.Checked = !tb.Checked;
				SetProperty(
					EditorParams.PropertyName,
					tb.Checked ? current.GetValue() | anchor : current.GetValue() & ~anchor);
			};
			tb.AddChangeWatcher(current, v => tb.Checked = (v & anchor) != 0);
			return tb;
		}

		float[] a = { 0, 0, 1, 0, 0, 0, 0, 1, 0, 0.5f, 0.5f, 0 };
		float[] b = { 0, 1, 1, 1, 1, 0, 1, 1, 1, 0.5f, 0.5f, 1 };

		void DrawIcon(Widget button, Anchors anchor)
		{
			button.PrepareRendererState();
			int t = -1;
			while (anchor != Anchors.None) {
				anchor = (Anchors)((int)anchor >> 1);
				t++;
			}
			var w = button.Width;
			var h = button.Height;
			Renderer.DrawLine(
				Scale(a[t * 2], w), Scale(a[t * 2 + 1], h), 
				Scale(b[t * 2], w), Scale(b[t * 2 + 1], h), 
				ColorTheme.Current.Basic.BlackText);
		}

		float Scale(float x, float s)
		{
			x *= s;
			if (x == 0) x += 4;
			if (x == s) x -= 4;
			return x;
		}

		public override void SetFocus() => firstButton.SetFocus();

		class AnchorButton : ToolbarButton
		{
			protected override void GetColors(State state, out Color4 bgColor, out Color4 borderColor)
			{
				base.GetColors(state, out bgColor, out borderColor);
				if (state == State.Default && !Checked) {
					bgColor = ColorTheme.Current.Basic.WhiteBackground;
					borderColor = ColorTheme.Current.Basic.ControlBorder;
				}
			}
		}
	}
}
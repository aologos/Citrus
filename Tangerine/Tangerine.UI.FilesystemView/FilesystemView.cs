using Lime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Tangerine.Core;

using Cmds = Tangerine.UI.FilesystemCommands;

namespace Tangerine.UI.FilesystemView
{
	// Single instance of view on filesystem, cooking rules editor and preview
	// Created and managed by FilesystemPane
	public class FilesystemView
	{
		private enum DragState
		{
			None,
			WaitingForSelecting,
			WaitingForDragging,
			Selecting,
			Dragging,
		}

		public Widget RootWidget { get; private set; }
		public ThemedScrollView ScrollView { get; private set; }
		private FilesystemToolbar toolbar;
		private Model model;
		private readonly Selection selection = new Selection();
		private Lime.FileSystemWatcher fsWatcher;
		private CookingRulesEditor crEditor;
		private Preview preview;
		private List<Tuple<string, Selection>> navHystory = new List<Tuple<string, Selection>>();
		private int navHystoryIndex = -1;
		private NodeToggler toggleCookingRules;
		private NodeToggler togglePreview;
		private DragState dragState;
		private ThemedHSplitter cookingRulesSplitter;
		private ThemedVSplitter selectionPreviewSplitter;
		private FilesystemItem lastKeyboardSelectedFilesystemItem;
		private FilesystemItem lastKeyboardRangeSelectionEndFilesystemItem;
		private Vector2 dragStartPosition;
		private Vector2 dragEndPosition;
		private Selection savedSelection;

		private SortType sortType = SortType.Name;
		public SortType SortType {
			get
			{
				return sortType;
			}
		}

		private OrderType orderType = OrderType.Ascending;
		public OrderType OrderType {
			get
			{
				return orderType;
			}
		}

		public void Split(SplitterType type)
		{
			FilesystemPane.Instance.Split(this, type);
		}

		public void Close()
		{
			FilesystemPane.Instance.Close(this);
		}

		public void GoBackward()
		{
			var newIndex = navHystoryIndex - 1;
			if (newIndex < 0 || newIndex >= navHystory.Count) {
				return;
			}
			var i = navHystory[newIndex];
			GoTo(i.Item1);
			foreach (var s in i.Item2) {
				selection.Select(s);
			}
			navHystoryIndex = newIndex;
		}

		public void GoForward()
		{
			var newIndex = navHystoryIndex + 1;
			if (newIndex >= navHystory.Count) {
				return;
			}
			var i = navHystory[newIndex];
			GoTo(i.Item1);
			foreach (var s in i.Item2) {
				selection.Select(s);
			}
			navHystoryIndex = newIndex;
		}

		private void AddToNavHystory(string path)
		{
			if (navHystory.Count > 0 && navHystory[navHystoryIndex].Item1 == path) {
				return;
			}
			var i = new Tuple<string, Selection>(path, selection.Clone());
			navHystory.Add(i);
			int newIndex = navHystoryIndex + 1;
			navHystory.RemoveRange(newIndex, navHystory.Count - newIndex - 1);
			navHystoryIndex = newIndex;
		}

		public void SortByType(SortType sortType, OrderType orderType)
		{
			this.sortType = sortType;
			this.orderType = OrderType;
			InvalidateView(model.CurrentPath, sortType, orderType);
		}

		private void InvalidateFSWatcher(string path)
		{
			fsWatcher?.Dispose();
			fsWatcher = new Lime.FileSystemWatcher(path, includeSubdirectories: false);
			// TODO: throttle
			Action<string> OnFsWatcherChanged = p => {
				InvalidateView(model.CurrentPath);
				preview.ClearTextureCache(p);
			};
			fsWatcher.Deleted += p => {
				selection.Deselect(p);
				OnFsWatcherChanged(p);
			};
			fsWatcher.Created += OnFsWatcherChanged;
			fsWatcher.Renamed += OnFsWatcherChanged;
			fsWatcher.Changed += OnFsWatcherChanged;
		}

		public FilesystemView()
		{
			RootWidget = new Widget() { Id = "FSRoot" };
			RootWidget.FocusScope = new KeyboardFocusScope(RootWidget);
			ScrollView = new ThemedScrollView(ScrollDirection.Horizontal) {
				TabTravesable = new TabTraversable(),
			};
			crEditor = new CookingRulesEditor(NavigateAndSelect);
			crEditor.RootWidget.TabTravesable = new TabTraversable();
			preview = new Preview();
			preview.RootWidget.TabTravesable = new TabTraversable();
		}

		// Component with user preferences should be added to rootWidget at this moment
		public void Initialize()
		{
			var up = RootWidget.Components.Get<ViewNodeComponent>().ViewNode as FSViewNode;
			model = new Model(up.Path);
			toolbar = new FilesystemToolbar(this, model);
			toolbar.TabTravesable = new TabTraversable();
			InitializeWidgets();
			selectionPreviewSplitter.Stretches = Splitter.GetStretchesList(up.SelectionPreviewSplitterStretches, 1, 1);
			cookingRulesSplitter.Stretches = Splitter.GetStretchesList(up.CookingRulesSplitterStretches, 1, 1);
			toggleCookingRules = new NodeToggler(crEditor.RootWidget, () => { crEditor.Invalidate(selection); });
			togglePreview = new NodeToggler(preview.RootWidget, () => { preview.Invalidate(selection); });
			if (!up.ShowCookingRulesEditor) {
				toggleCookingRules.Toggle();
			}
			if (!up.ShowSelectionPreview) {
				togglePreview.Toggle();
			}
			foreach (var n in RootWidget.Descendants) {
				var w = n.AsWidget;
				if (w.TabTravesable != null) {
					w.HitTestTarget = true;
				}
			}
			RootWidget.Updating += (float delta) => {
				if (
					RootWidget.Input.IsKeyPressed(Key.Control) &&
					RootWidget.Input.WasKeyReleased(Key.L)
				) {
					toolbar.AddressBar.SetFocusOnEditor();
				}
			};
		}

		private void NavigateAndSelect(string filename)
		{
			GoTo(Path.GetDirectoryName(filename));
			selection.Select(filename);
		}

		void InitializeWidgets()
		{
			RootWidget.AddChangeWatcher(() => selection.Version, Selection_Changed);
			ScrollView.Content.Layout = new FlowLayout(LayoutDirection.TopToBottom) { Spacing = 1.0f };
			ScrollView.Content.Padding = new Thickness(5.0f);
			ScrollView.Content.CompoundPostPresenter.Insert(0, new SyncDelegatePresenter<Widget>(RenderFilesWidgetRectSelection));
			ScrollView.Updated += ScrollViewUpdated;
			ScrollView.Content.Presenter = new SyncDelegatePresenter<Widget>((w) => {
				w.PrepareRendererState();
				var wp = w.ParentWidget;
				var p = wp.Padding;
				Renderer.DrawRect(-w.Position + Vector2.Zero - new Vector2(p.Left, p.Top),
					-w.Position + wp.Size + new Vector2(p.Right, p.Bottom), Theme.Colors.WhiteBackground);
			});
			RootWidget.AddChangeWatcher(() => dragState, (ds) => Window.Current.Invalidate());
			RootWidget.AddChangeWatcher(() => dragEndPosition, WhenSelectionRectChanged);
			RootWidget.AddChangeWatcher(() => WidgetContext.Current.NodeUnderMouse, (value) => {
				if (value != null && ScrollView.Content == value.Parent) {
					Window.Current.Invalidate();
				}
			});
			RootWidget.AddChangeWatcher(() => model.CurrentPath, (p) => {
				var up = RootWidget.Components.Get<ViewNodeComponent>().ViewNode as FSViewNode;
				up.Path = p;
				AddToNavHystory(p);
				selection.Clear();
				InvalidateView(p);
				InvalidateFSWatcher(p);
				preview.ClearTextureCache();
				lastKeyboardSelectedFilesystemItem = ScrollView.Content.FirstChild as FilesystemItem;
			});
			RootWidget.Layout = new VBoxLayout();
			RootWidget.AddNode((cookingRulesSplitter = new ThemedHSplitter {
				Nodes = {
					(new Widget {
						Layout = new VBoxLayout(),
						Nodes = {
							toolbar,
							(selectionPreviewSplitter = new ThemedVSplitter {
								Nodes = {
									ScrollView,
									preview.RootWidget,
								}
							})
						}}),
						crEditor.RootWidget,
				}
			}));
		}

		private void Selection_Changed(int version)
		{
			crEditor.Invalidate(selection);
			preview.Invalidate(selection);
			Window.Current.Invalidate();
		}

		private void InvalidateView(string path, SortType sortType, OrderType orderType)
		{
			ScrollView.Content.Nodes.Clear();
			foreach (var item in model.EnumerateItems(sortType, orderType)) {
				var fsItem = new FilesystemItem(item);
				ScrollView.Content.AddNode(fsItem);
				fsItem.CompoundPresenter.Insert(0, new SyncDelegatePresenter<FilesystemItem>(RenderFSItemSelection));
			}
		}

		private void InvalidateView(string path)
		{
			InvalidateView(path, sortType, orderType);
		}

		public bool Open(string path)
		{
			try {
				var attr = File.GetAttributes(path);
				if ((attr & FileAttributes.Directory) == FileAttributes.Directory) {
					GoTo(path);
				} else {
					if (path.EndsWith(".scene") || path.EndsWith(".tan")) {
						Project.Current.OpenDocument(path, true);
					}
				}
				return true;
			} catch (ArgumentException) {
				AlertDialog.Show("The path is empty, contains only white spaces, or contains invalid characters.");
			} catch (PathTooLongException) {
				AlertDialog.Show("The specified path, file name, or both exceed the system-defined maximum length.");
			} catch (NotSupportedException) {
				AlertDialog.Show("The path is in an invalid format.");
			} catch (FileNotFoundException) {
				AlertDialog.Show($"Tangerine can not find \"{path}\".\nCheck the spelling and try again.");
			} catch (DirectoryNotFoundException) {
				AlertDialog.Show("The path represents a directory and is invalid, such as being on an unmapped drive, or the directory cannot be found.");
			} catch (IOException) {
				AlertDialog.Show("This file is being used by another process.");
			} catch (UnauthorizedAccessException) {
				AlertDialog.Show("Tangerine does not have the required permission.");
			}
			return false;
		}

		private void OpenSpecial(string path)
		{
			System.Diagnostics.Process.Start(path);
		}

		private void RenderFSItemSelection(FilesystemItem filesystemItem)
		{
			if (selection.Contains(filesystemItem.FilesystemPath)) {
				filesystemItem.PrepareRendererState();
				Renderer.DrawRect(Vector2.Zero, filesystemItem.Size, Theme.Colors.SelectedBackground);
			} else if (filesystemItem.IsMouseOverThisOrDescendant()) {
				filesystemItem.PrepareRendererState();
				Renderer.DrawRect(
					Vector2.Zero,
					filesystemItem.Size,
					Theme.Colors.HoveredBackground);
			}
			if (filesystemItem == lastKeyboardRangeSelectionEndFilesystemItem) {
				filesystemItem.PrepareRendererState();
				Renderer.DrawRectOutline(Vector2.Zero, filesystemItem.Size, Theme.Colors.SelectedBorder);
			}
		}

		private void WhenSelectionRectChanged(Vector2 value)
		{
			if (dragState != DragState.Selecting) {
				return;
			}
			var p0 = dragStartPosition;
			var p1 = dragEndPosition;
			var r0 = new Rectangle(new Vector2(Mathf.Min(p0.X, p1.X), Mathf.Min(p0.Y, p1.Y)),
				new Vector2(Mathf.Max(p0.X, p1.X), Mathf.Max(p0.Y, p1.Y)));
			foreach (var n in ScrollView.Content.Nodes) {
				var ic = n as FilesystemItem;
				var r1 = new Rectangle(ic.Position, ic.Position + ic.Size);
				if (Rectangle.Intersect(r0, r1) != Rectangle.Empty) {
					if (savedSelection != null) {
						if (savedSelection.Contains(ic.FilesystemPath)) {
							selection.Deselect(ic.FilesystemPath);
						} else {
							selection.Select(ic.FilesystemPath);
						}
					} else {
						selection.Select(ic.FilesystemPath);
					}
				} else {
					if (savedSelection != null) {
						if (savedSelection.Contains(ic.FilesystemPath)) {
							selection.Select(ic.FilesystemPath);
						} else {
							selection.Deselect(ic.FilesystemPath);
						}
					} else if (selection.Contains(ic.FilesystemPath) && !ScrollView.Input.IsKeyPressed(Key.Shift)) {
						selection.Deselect(ic.FilesystemPath);
					}
				}
			}
		}

		private const float typeNavigationInterval = 0.5f;
		private float typeNavigationTimeout = 0.0f;
		private string typeNavigationPrefix = string.Empty;

		private void ScrollViewUpdated(float dt)
		{
			ProcessInputOverFSItem();
			ProcessDragState(dt);
			ProcessChangeViewMode();
			typeNavigationTimeout -= dt;
			if (ScrollView.IsFocused()) {
				ProcessTypingNavigation();
				ProcessOtherCommands();
				ProcessSelectionCommands();
				foreach (var c in printableKeysCommands) {
					c.Consume();
				}
			}
		}

		private void ProcessChangeViewMode()
		{
			if (
				ScrollView.Input.IsKeyPressed(Key.Control) &&
				(ScrollView.Input.WasKeyPressed(Key.MouseWheelDown) || ScrollView.Input.WasKeyPressed(Key.MouseWheelUp))
			) {
				ScrollView.Unlink();
				if (ScrollView.Direction == ScrollDirection.Horizontal) {
					ScrollView = new ThemedScrollView(ScrollDirection.Vertical) {
						TabTravesable = new TabTraversable(),
					};
					ScrollView.Content.Layout = new FlowLayout(LayoutDirection.LeftToRight) { Spacing = 1.0f };
				} else {
					ScrollView = new ThemedScrollView(ScrollDirection.Horizontal) {
						TabTravesable = new TabTraversable(),
					};
					ScrollView.Content.Layout = new FlowLayout(LayoutDirection.TopToBottom) { Spacing = 1.0f };
				}

				ScrollView.Content.Padding = new Thickness(5.0f);
				ScrollView.Content.CompoundPostPresenter.Insert(0, new SyncDelegatePresenter<Widget>(RenderFilesWidgetRectSelection));
				ScrollView.Updated += ScrollViewUpdated;
				ScrollView.Content.Presenter = new SyncDelegatePresenter<Widget>((w) => {
					w.PrepareRendererState();
					var wp = w.ParentWidget;
					var p = wp.Padding;
					Renderer.DrawRect(-w.Position + Vector2.Zero - new Vector2(p.Left, p.Top),
						-w.Position + wp.Size + new Vector2(p.Right, p.Bottom), Theme.Colors.WhiteBackground);
				});

				InvalidateView(model.CurrentPath);
				lastKeyboardSelectedFilesystemItem = ScrollView.Content.FirstChild as FilesystemItem;

				selectionPreviewSplitter.Nodes.Insert(0, ScrollView);
			}
		}

		private static readonly List<Command> printableKeysCommands =
				Key.Enumerate().Where(k => k.IsPrintable()).Select(i => new Command(i)).Union(
					Key.Enumerate().Where(k => k.IsPrintable()).Select(i => new Command(new Shortcut(Modifiers.Shift, i)))).ToList();

		private void ProcessOtherCommands()
		{
			if (!Command.SelectAll.IsConsumed()) {
				Command.SelectAll.Enabled = true;
			}

			if (Cmds.Cancel.Consume()) {
				typeNavigationTimeout = typeNavigationInterval;
				typeNavigationPrefix = string.Empty;
			} else if (Window.Current.Input.WasKeyReleased(Key.Menu)) {
				if (!selection.Empty) {
					Window.Current.Input.ConsumeKey(Key.Menu);
					SystemShellContextMenu.Instance.Show(selection.ToArray(), lastKeyboardSelectedFilesystemItem.GlobalPosition);
				}
			} else if (Cmds.GoBack.Consume()) {
				GoBackward();
			} else if (Cmds.GoForward.Consume()) {
				GoForward();
			} else if (Cmds.GoUp.Consume() || Cmds.GoUpAlso.Consume()) {
				GoUp();
			} else if (Cmds.Enter.Consume()) {
				if (lastKeyboardSelectedFilesystemItem != null) {
					Open(lastKeyboardSelectedFilesystemItem.FilesystemPath);
				}
			} else if (Cmds.EnterSpecial.Consume()) {
				if (lastKeyboardSelectedFilesystemItem != null) {
					OpenSpecial(lastKeyboardSelectedFilesystemItem.FilesystemPath);
				}
			} else if (Command.SelectAll.Consume()) {
				selection.Clear();
				selection.SelectRange(ScrollView.Content.Nodes.Select(n => (n as FilesystemItem).FilesystemPath));
			} else if (Cmds.ToggleSelection.Consume()) {
				if (lastKeyboardRangeSelectionEndFilesystemItem != null) {
					var path = lastKeyboardRangeSelectionEndFilesystemItem.FilesystemPath;
					if (selection.Contains(path)) {
						selection.Deselect(path);
					} else {
						selection.Select(path);
					}
				}
			}
		}

		private void ProcessDragState(float dt)
		{
			var input = ScrollView.Input;

			switch (dragState) {
			case DragState.None: {
					if (ScrollView.IsMouseOver()) {
						if (input.ConsumeKeyPress(Key.Mouse0)) {
							dragEndPosition = dragStartPosition = ScrollView.Content.LocalMousePosition();
							dragState = DragState.WaitingForSelecting;
						}
						if (input.ConsumeKeyRelease(Key.Mouse1)) {
							dragState = DragState.None;
							selection.Clear();
							SystemShellContextMenu.Instance.Show(model.CurrentPath);
						}
					}
					break;
				}
			case DragState.Selecting: {
					if (Application.Input.WasKeyReleased(Key.Mouse0)) {
						Application.Input.ConsumeKey(Key.Mouse0);
						ScrollView.SetFocus();
						dragState = DragState.None;
					}
					dragEndPosition = ScrollView.Content.LocalMousePosition();
					var scrollOffset = 0.0f;
					var pos = ScrollView.LocalMousePosition();
					if (ScrollView.Direction == ScrollDirection.Vertical) {
						if (pos.Y < 0) {
							scrollOffset = pos.Y;

						} else if (pos.Y > ScrollView.Height) {
							scrollOffset = pos.Y - ScrollView.Height;
						}
					} else if (ScrollView.Direction == ScrollDirection.Horizontal) {
						if (pos.X < 0) {
							scrollOffset = pos.X;

						} else if (pos.X > ScrollView.Width) {
							scrollOffset = pos.X - ScrollView.Width;
						}
					}
					ScrollView.ScrollPosition += Math.Sign(scrollOffset) * Mathf.Sqr(scrollOffset) * 0.1f * dt;
					ScrollView.ScrollPosition = Mathf.Clamp(ScrollView.ScrollPosition, ScrollView.MinScrollPosition, ScrollView.MaxScrollPosition);
					Window.Current.Invalidate();
				}
				break;
			case DragState.WaitingForDragging:
				if ((dragStartPosition - Window.Current.Input.MousePosition).Length > 5.0f) {
					dragState = DragState.Dragging;
					CommonWindow.Current.DragFiles(selection.ToArray());
				}
				if (
					Window.Current.Input.WasKeyReleased(Key.Mouse0) ||
					Window.Current.Input.WasKeyReleased(Key.Mouse0DoubleClick)
				) {
					dragState = DragState.None;
					Window.Current.Input.ConsumeKey(Key.Mouse0);
				}
				break;
			case DragState.WaitingForSelecting:
				if (input.ConsumeKeyRelease(Key.Mouse0)) {
					dragState = DragState.None;
					if (!input.IsKeyPressed(Key.Control) && !input.IsKeyPressed(Key.Shift)) {
						selection.Clear();
					}
				} else if (input.IsKeyPressed(Key.Mouse0)) {
					if ((ScrollView.Content.LocalMousePosition() - dragStartPosition).Length > 6.0f) {
						dragState = DragState.Selecting;
						if (input.IsKeyPressed(Key.Control)) {
							savedSelection = selection.Clone();
						} else {
							savedSelection = null;
						}
					}
				}
				break;
			case DragState.Dragging:
				if (Window.Current.Input.WasKeyReleased(Key.Mouse0)) {
					Window.Current.Input.ConsumeKey(Key.Mouse0);
					dragState = DragState.None;
				}
				break;
			}
		}

		private string lastSelected;

		private void ProcessInputOverFSItem()
		{
			// TODO: Ctrl + Shift clicks
			var nodeUnderMouse = WidgetContext.Current.NodeUnderMouse;
			if (
				nodeUnderMouse == null ||
				!(
					nodeUnderMouse is FilesystemItem &&
					nodeUnderMouse.Parent == ScrollView.Content ||
					nodeUnderMouse.Parent is FilesystemItem &&
					nodeUnderMouse.Parent.Parent == ScrollView.Content
				)
			) {
				return;
			}
			var fsItem = nodeUnderMouse as FilesystemItem ?? nodeUnderMouse.Parent as FilesystemItem;
			var path = fsItem.FilesystemPath;
			var input = fsItem.Input;
			if (input.ConsumeKeyPress(Key.Mouse0DoubleClick)) {
				ScrollView.SetFocus();
				Open(path);
			}
			if (fsItem.Input.ConsumeKeyRelease(Key.Mouse1)) {
				ScrollView.SetFocus();
				if (!selection.Contains(path)) {
					selection.Clear();
					selection.Select(path);
				}
				SystemShellContextMenu.Instance.Show(selection);
			}
			if (fsItem.Input.ConsumeKeyRelease(Key.Mouse0)) {
				ScrollView.SetFocus();
				if (!fsItem.IsMouseOver() || selection.Contains(path)) {
					if (
						dragState != DragState.Selecting &&
						dragState != DragState.Dragging &&
						!fsItem.Input.IsKeyPressed(Key.Control) &&
						!fsItem.Input.IsKeyPressed(Key.Shift)
					) {
						selection.Clear();
					}
					selection.Select(path);
					lastKeyboardSelectedFilesystemItem = fsItem;
				}
				dragState = DragState.None;
			}
			if (fsItem.Input.WasKeyPressed(Key.Mouse0)) {
				ScrollView.SetFocus();
				input.ConsumeKey(Key.Mouse0);
				if (input.IsKeyPressed(Key.Control) && !input.IsKeyPressed(Key.Shift)) {
					input.ConsumeKey(Key.Control);
					if (selection.Contains(path)) {
						selection.Deselect(path);
					} else {
						selection.Select(path);
						lastSelected = path;
					}
				} else if (!input.IsKeyPressed(Key.Control) && input.IsKeyPressed(Key.Shift)) {
					input.ConsumeKey(Key.Shift);
					var items = model.EnumerateItems(sortType, orderType).ToList();
					var currentIndex = items.IndexOf(path);
					int prevIndex;
					if (lastSelected == default) {
						prevIndex = items.FindIndex(i => selection.Contains(i));
					} else {
						prevIndex = items.IndexOf(lastSelected);
					}
					selection.Clear();
					if (prevIndex == -1) {
						selection.Select(path);
						lastSelected = path;
						return;
					}
					lastSelected = items[prevIndex];
					for (int i = Math.Min(currentIndex, prevIndex); i <= Math.Max(currentIndex, prevIndex); ++i) {
						selection.Select(items[i]);
					}
					fsItem.Input.ConsumeKeyRelease(Key.Mouse0);
				} else {
					if (selection.Contains(path)) {
						dragState = DragState.WaitingForDragging;
						dragStartPosition = Window.Current.Input.MousePosition;
						lastKeyboardSelectedFilesystemItem = fsItem;
					} else {
						if (!fsItem.IsMouseOver()) {
							dragState = DragState.WaitingForSelecting;
							dragStartPosition = ScrollView.Content.LocalMousePosition();
						} else {
							if (!selection.Contains(path)) {
								selection.Clear();
								selection.Select(path);
								lastSelected = path;
							}
							dragState = DragState.WaitingForDragging;
							dragStartPosition = Window.Current.Input.MousePosition;
							lastKeyboardSelectedFilesystemItem = fsItem;
						}
					}
				}
				Window.Current?.Invalidate();
			}
		}

		private void ProcessTypingNavigation()
		{
			var input = ScrollView.Input;
			if (string.IsNullOrEmpty(input.TextInput)) {
				return;
			}
			if (typeNavigationTimeout <= 0.0f) {
				typeNavigationPrefix = string.Empty;
			}
			typeNavigationTimeout = typeNavigationInterval;
			var prevPrefix = typeNavigationPrefix;
			bool offset = false;
			if (prevPrefix == input.TextInput) {
				offset = true;
			} else {
				typeNavigationPrefix += input.TextInput;
			}
			var matches = ScrollView.Content.Nodes
				.Select(i => i as FilesystemItem)
				.Where(i => {
					var a = Path.GetFileName(i.FilesystemPath);
					var b = typeNavigationPrefix;
					return a.StartsWith(b, true, CultureInfo.CurrentCulture);
				})
				.ToList();
			if (matches.Count != 0) {
				var index = matches.IndexOf(lastKeyboardSelectedFilesystemItem);
				if (index == -1) {
					index = 0;
				}
				if (offset) {
					index = (index + 1) % matches.Count;
				}
				selection.Clear();
				selection.Select(matches[index].FilesystemPath);
				lastKeyboardSelectedFilesystemItem = matches[index];
				EnsureFSItemVisible(lastKeyboardSelectedFilesystemItem);
			}
		}

		private void ProcessSelectionCommands()
		{
			int indexDelta = 0;
			bool select = false;
			bool toggle = false;
			var index = 0;
			var maxIndex = ScrollView.Content.Nodes.Count - 1;
			if (lastKeyboardSelectedFilesystemItem != null) {
				index = ScrollView.Content.Nodes.IndexOf(lastKeyboardSelectedFilesystemItem);
			}
			int rangeSelectionIndex = index;
			if (lastKeyboardRangeSelectionEndFilesystemItem != null) {
				rangeSelectionIndex = ScrollView.Content.Nodes.IndexOf(lastKeyboardRangeSelectionEndFilesystemItem);
			}
			var flowLayout = (ScrollView.Content.Layout as FlowLayout);
			int columnCount = flowLayout.ColumnCount(0);
			int rowCount = flowLayout.RowCount(0);
			float rowHeight = FilesystemItem.ItemPadding * 2 + FilesystemItem.IconSize;
			for (int navType = 0; navType < navCommands.Count; navType++) {
				for (int navOffset = 0; navOffset < navCommands[navType].Count; navOffset++) {
					var cmd = navCommands[navType][navOffset];
					if (cmd.Consume()) {
							select = navType == 1;
							toggle = navType == 2;
							var sign = (navOffset % 2 == 0 ? -1 : 1);
						if (ScrollView.Direction == ScrollDirection.Vertical) {
							switch (navOffset) {
								// Left, Right
								case 0: case 1: indexDelta = sign * 1; break;
								// Up,  Down
								case 2: case 3: indexDelta = sign * columnCount; break;
								// PageUp, PageDown
								case 4: case 5:
									int currentColumn = index % columnCount;
									int count = ScrollView.Content.Nodes.Count;
									bool lastRow = currentColumn < count % columnCount;
									indexDelta =
										(sign * columnCount * ((int)(ScrollView.Size.Y / (rowHeight + flowLayout.Spacing)) - 1))
										.Clamp(
											currentColumn - index,
											currentColumn + columnCount * (count / columnCount - (lastRow ? 0 : 1)) - index
										);
									if (indexDelta == 0) {
										indexDelta = sign < 0 ? -index : count - index - 1;
									}
									break;
								// Home
								case 6: indexDelta = -rangeSelectionIndex; break;
								// End
								case 7: indexDelta = maxIndex - rangeSelectionIndex; break;
							}
						} else if (ScrollView.Direction == ScrollDirection.Horizontal) {
							switch (navOffset) {
								// Left, Right
								case 0: case 1: indexDelta = sign * rowCount; break;
								// Up,  Down
								case 2: case 3: indexDelta = sign * 1; break;
								// PageUp, PageDown
								case 4: case 5:
									indexDelta = (sign * rowCount).Clamp(-index, ScrollView.Content.Nodes.Count - index - 1);
									break;
								// Home
								case 6: indexDelta = -rangeSelectionIndex; break;
								// End
								case 7: indexDelta = maxIndex - rangeSelectionIndex; break;
							}
						}
					}
				}
			}
			if (indexDelta != 0) {
				if (select) {
					int selectionEndIndex = lastKeyboardRangeSelectionEndFilesystemItem != null
						? ScrollView.Content.Nodes.IndexOf(lastKeyboardRangeSelectionEndFilesystemItem)
						: index;
					int newIndex = selectionEndIndex + indexDelta;
					if (newIndex >= 0 && newIndex <= maxIndex) {
						selection.Clear();
						for (int i = Math.Min(index, newIndex); i <= Math.Max(index, newIndex); i++) {
							var path = (ScrollView.Content.Nodes[i] as FilesystemItem).FilesystemPath;
							selection.Select(path);
						}
						lastKeyboardRangeSelectionEndFilesystemItem = ScrollView.Content.Nodes[newIndex] as FilesystemItem;
						EnsureFSItemVisible(lastKeyboardRangeSelectionEndFilesystemItem);
					}
				} else {
					if (!toggle) {
						int newIndex = index + indexDelta;
						if (newIndex >= 0 && newIndex <= maxIndex) {

							lastKeyboardSelectedFilesystemItem = ScrollView.Content.Nodes[newIndex] as FilesystemItem;
							var path = lastKeyboardSelectedFilesystemItem.FilesystemPath;
							selection.Clear();
							selection.Select(path);
							lastKeyboardRangeSelectionEndFilesystemItem = null;
							EnsureFSItemVisible(lastKeyboardSelectedFilesystemItem);
						}
					} else {
						int selectionEndIndex = lastKeyboardRangeSelectionEndFilesystemItem != null
							? ScrollView.Content.Nodes.IndexOf(lastKeyboardRangeSelectionEndFilesystemItem)
							: index;
						int newIndex = selectionEndIndex + indexDelta;
						if (newIndex >= 0 && newIndex <= maxIndex) {
							lastKeyboardRangeSelectionEndFilesystemItem = ScrollView.Content.Nodes[newIndex] as FilesystemItem;
							EnsureFSItemVisible(lastKeyboardRangeSelectionEndFilesystemItem);
							Window.Current.Invalidate();
						}
					}
				}
			}
		}

		private void EnsureFSItemVisible(FilesystemItem fsItem)
		{
			float min = 0;
			float offset = 0;
			var pos = fsItem.CalcPositionInSpaceOf(ScrollView);
			if (ScrollView.Direction == ScrollDirection.Vertical) {
				min = pos.Y;
				offset = min + fsItem.Height - ScrollView.Height;
			} else if (ScrollView.Direction == ScrollDirection.Horizontal) {
				min = pos.X;
				offset = min + fsItem.Width - ScrollView.Width;
			}
			EnsureRangeVisible(min, offset);
		}

		private void EnsureSelectionVisible()
		{
			float min = float.MaxValue;
			float offset = float.MinValue;
			foreach (var n in ScrollView.Content.Nodes) {
				var fsItem = n as FilesystemItem;
				if (!selection.Contains(fsItem.FilesystemPath)) {
					continue;
				}
				var pos = fsItem.CalcPositionInSpaceOf(ScrollView);
				if (ScrollView.Direction == ScrollDirection.Vertical) {
					min = Mathf.Min(min, pos.Y);
					offset = Mathf.Max(offset, pos.Y + fsItem.Height - ScrollView.Height);
				} else if (ScrollView.Direction == ScrollDirection.Horizontal) {
					min = Mathf.Min(min, pos.X);
					offset = Mathf.Max(offset, pos.X + fsItem.Width - ScrollView.Width);
				}
			}
			EnsureRangeVisible(min, offset);
		}

		private void EnsureRangeVisible(float min, float offset)
		{
			if (offset > 0.0f) {
				ScrollView.ScrollPosition += offset;
			}
			if (min < 0.0f) {
				ScrollView.ScrollPosition += min;
			}
			ScrollView.ScrollPosition = Mathf.Clamp(ScrollView.ScrollPosition, ScrollView.MinScrollPosition, ScrollView.MaxScrollPosition);
		}

		static readonly List<List<ICommand>> navCommands = new List<List<ICommand>> {
			// simple navigation
			new List<ICommand> {
				Cmds.Left,
				Cmds.Right,
				Cmds.Up,
				Cmds.Down,
				Cmds.PageUp,
				Cmds.PageDown,
				Cmds.Home,
				Cmds.End,
			},
			// Range-select (shift) navigation
			new List<ICommand> {
				Cmds.SelectLeft,
				Cmds.SelectRight,
				Cmds.SelectUp,
				Cmds.SelectDown,
				Cmds.SelectPageUp,
				Cmds.SelectPageDown,
				Cmds.SelectHome,
				Cmds.SelectEnd,
			},
			// Toggle-select (hold ctrl, navigate, toggle with space)
			new List<ICommand> {
				Cmds.ToggleLeft,
				Cmds.ToggleRight,
				Cmds.ToggleUp,
				Cmds.ToggleDown,
				Cmds.TogglePageUp,
				Cmds.TogglePageDown,
				Cmds.ToggleHome,
				Cmds.ToggleEnd,
			},
		};

		private void RenderFilesWidgetRectSelection(Widget canvas)
		{
				if (dragState != DragState.Selecting) {
					return;
				}
				canvas.PrepareRendererState();
				Renderer.DrawRect(dragStartPosition, dragEndPosition, new Color4(150, 180, 230, 128));
				Renderer.DrawRectOutline(dragStartPosition, dragEndPosition, Theme.Colors.KeyboardFocusBorder);
		}

		public void GoUp()
		{
			model.GoUp();
		}

		public void GoTo(string path)
		{
			model.GoTo(path);
		}

		private class NodeToggler
		{
			private Node savedParent;
			private int savedIndex;
			private Node node;
			private Action invalidator;
			public NodeToggler(Node n, Action invalidator)
			{
				node = n;
				this.invalidator = invalidator;
			}
			public void Toggle()
			{
				if (node.Parent != null) {
					savedParent = node.Parent;
					savedIndex = savedParent.Nodes.IndexOf(node);
					node.Unlink();
				} else {
					savedParent.Nodes.Insert(Mathf.Clamp(savedIndex, 0, savedParent.Nodes.Count), node);
					invalidator?.Invoke();
				}
			}
		}

		public void TogglePreview()
		{
			togglePreview.Toggle();
		}

		public void ToggleCookingRules()
		{
			toggleCookingRules.Toggle();
		}

		public void SelectAsset(string path)
		{
			var dir = Path.GetDirectoryName(path);
			path = path.Replace('/', '\\');
			selection.Clear();
			foreach (string f in Directory.GetFiles(dir)) {
				if (Path.ChangeExtension(f, null).EndsWith(path)) {
					selection.Select(f);
				}
			}
			EnsureSelectionVisible();
		}
	}
}

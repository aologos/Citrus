﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Lime;
using Tangerine.UI;
using Tangerine.Core;

namespace Tangerine
{
	public class DeleteRulerDialog
	{
		readonly Window window;
		readonly WindowWidget rootWidget;
		readonly Button okButton;
		readonly Button cancelButton;
		readonly Frame Frame;

		public DeleteRulerDialog()
		{
			window = new Window(new WindowOptions {
				ClientSize = new Vector2(300, 150),
				FixedSize = true,
				Title = "Rulers",
				MinimumDecoratedSize = new Vector2(200, 100)
			});
			Frame = new ThemedFrame {
				Padding = new Thickness(8),
				LayoutCell = new LayoutCell { StretchY = float.MaxValue },
				Layout = new StackLayout(),
			};
			var collection = new ObservableCollection<RulerData>(Project.Current.Rulers);
			ThemedScrollView Container;
			rootWidget = new ThemedInvalidableWindowWidget(window) {
				Padding = new Thickness(8),
				Layout = new VBoxLayout(),
				Nodes = {
					(Container = new ThemedScrollView {
						Padding = new Thickness { Right = 10 },
					}),
					new Widget {
						Padding = new Thickness { Top = 10 },
						Layout = new HBoxLayout { Spacing = 8 },
						LayoutCell = new LayoutCell(Alignment.RightCenter),
						Nodes = {
							(okButton = new ThemedButton { Text = "Ok" }),
							(cancelButton = new ThemedButton { Text = "Cancel" }),
						}
					}
				}
			};
			Container.Content.Layout = new VBoxLayout { Spacing = 4 };
			Container.Content.AddNode(new ListWidget<RulerData>((w) => new RulerRowView(w, collection), collection) {
				Layout = new VBoxLayout()
			});

			okButton.Clicked += () => {
				window.Close();
				Core.UserPreferences.Instance.Save();
				var temp = Project.Current.Rulers.ToList();
				foreach (var overlay in temp.Except(collection)) {
					Project.Current.RemoveRuler(overlay);
				}
			};
			cancelButton.Clicked += () => {
				window.Close();
				Core.UserPreferences.Instance.Load();
			};
			rootWidget.FocusScope = new KeyboardFocusScope(rootWidget);
			rootWidget.LateTasks.AddLoop(() => {
				if (rootWidget.Input.ConsumeKeyPress(Key.Escape)) {
					window.Close();
					Core.UserPreferences.Instance.Load();
				}
			});
			okButton.SetFocus();
		}

		internal class OverlayRowView : Widget
		{
			private ThemedSimpleText Label;
			private ThemedTabCloseButton CloseButton;
			private static IPresenter StripePresenter = new DelegatePresenter<Widget>(
				w => {
					if (w.Parent != null) {
						var i = w.Parent.AsWidget.Nodes.IndexOf(w);
						w.PrepareRendererState();
						Renderer.DrawRect(Vector2.Zero, w.Size,
							i % 2 == 0 ? ColorTheme.Current.Inspector.StripeBackground2 : ColorTheme.Current.Inspector.StripeBackground1);
					}
				});

			public OverlayRowView(RulerData overlay, IList<RulerData> overlays) : base()
			{
				LayoutCell = new LayoutCell { StretchY = 0, RowSpan = 5 };
				Layout = new HBoxLayout() { Spacing = 5, IgnoreHidden = true };
				Nodes.Add(Label = new ThemedSimpleText {
					LayoutCell = new LayoutCell(Alignment.LeftTop) { StretchY = 0 },
					Padding = new Thickness { Left = 10 },
				});
				this.AddChangeWatcher(() => overlay.Name, (name) => Label.Text = name);
				Nodes.Add(new Widget());
				Nodes.Add(CloseButton = new ThemedTabCloseButton {
					Anchors = Anchors.Right,
					LayoutCell = new LayoutCell(Alignment.LeftTop)
				});
				CompoundPresenter.Add(StripePresenter);
				CloseButton.Clicked = () => overlays.Remove(overlay);
				MinMaxHeight = 15;
			}
		}
	}
}
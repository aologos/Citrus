﻿using System;
using System.Linq;
using System.Collections.Generic;
using Lime;
using Tangerine.Core;
using Tangerine.UI.Timeline.Components;

namespace Tangerine.UI.Timeline
{
	public class SelectAndDragRowsProcessor : IProcessor
	{
		int dragPosition;

		Timeline timeline => Timeline.Instance;
		RollPane roll => Timeline.Instance.Roll;

		public IEnumerator<object> Loop()
		{
			var input = roll.RootWidget.Input;
			while (true) {
				yield return null;
				if (!input.WasMousePressed()) {
					continue;
				}
				var hitNode = WidgetContext.Current.NodeUnderMouse;
				if (hitNode == null || !hitNode.DescendantOf(roll.RootWidget)) {
					continue;
				}
				Timeline.Instance.PanelWidget.SetFocus();
				var initialMousePosition = input.MousePosition;
				var row = MousePositionToRow(initialMousePosition);
				if (input.IsKeyPressed(Key.LShift)) {
					if (timeline.SelectedRows.Count > 0) {
						Operations.ClearRowSelection.Perform();
						Operations.SelectRowRange.Perform(timeline.SelectedRows[0], row);
					} else {
						Operations.ClearRowSelection.Perform();
						Operations.SelectRow.Perform(row);
					}
				} else {
					input.CaptureMouse();
					if (!timeline.SelectedRows.Contains(row)) {
						Operations.ClearRowSelection.Perform();
						Operations.SelectRow.Perform(row);
					}
					while (input.IsMousePressed() && Math.Abs(initialMousePosition.Y - input.MousePosition.Y) < Metrics.TimelineDefaultRowHeight / 4) {
						yield return null;
					}
					if (input.IsMousePressed()) {
						yield return DragTask();
					}
					input.ReleaseMouse();
				}
			}
		}

		private IEnumerator<object> DragTask()
		{
			roll.OnRenderOverlay += RenderDragCursor;
			var input = roll.RootWidget.Input;
			while (input.IsMousePressed()) {
				dragPosition = MouseToDragPosition(input.MousePosition);
				Window.Current.Invalidate();
				yield return null;
			}
			roll.OnRenderOverlay -= RenderDragCursor;
			Window.Current.Invalidate();
			Operations.DragRows.Perform(dragPosition);
		}

		private void RenderDragCursor(Widget widget)
		{
			roll.ContentWidget.PrepareRendererState();
			var y = dragPosition == 0 ? 0 : timeline.Rows[dragPosition - 1].Bottom;
			Renderer.DrawRect(new Vector2(0, y - 1), new Vector2(roll.ContentWidget.Width, y + 1), Colors.DragCursor);
		}

		Row MousePositionToRow(Vector2 position)
		{
			position -= roll.ContentWidget.GlobalPosition;
			if (position.Y < 0) {
				return timeline.Rows[0];
			}
			foreach (var row in timeline.Rows) {
				if (position.Y >= row.Top && position.Y < row.Bottom + Metrics.TimelineRowSpacing) {
					return timeline.Rows[row.Index];
				}
			}
			return timeline.Rows[Math.Max(0, timeline.Rows.Count - 1)];
		}

		int MouseToDragPosition(Vector2 position)
		{
			position -= roll.ContentWidget.GlobalPosition;
			if (position.Y < 0) {
				return 0;
			}
			foreach (var row in timeline.Rows) {
				if (position.Y >= row.Top && position.Y < row.Bottom + Metrics.TimelineRowSpacing) {
					return position.Y > (row.Top + row.Bottom) / 2 ? row.Index + 1 : row.Index;
				}
			}
			return timeline.Rows.Count;
		}
	}
}
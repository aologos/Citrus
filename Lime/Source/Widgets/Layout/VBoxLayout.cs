﻿using System;
using System.Linq;
using System.Collections.Generic;

namespace Lime
{
	public class VBoxLayout : CommonLayout, ILayout
	{
		public float Spacing { get; set; }

		public VBoxLayout()
		{
			DebugRectangles = new List<Rectangle>();
		}

		public void OnSizeChanged(Widget widget, Vector2 sizeDelta)
		{
			// Size changing could only affect children arrangement, not the widget's size constraints.
			InvalidateArrangement(widget);
		}

		public override void ArrangeChildren(Widget widget)
		{
			ArrangementValid = true;
			var widgets = widget.Nodes.OfType<Widget>().ToList();
			if (widgets.Count == 0) {
				return;
			}
			var constraints = new LinearAllocator.Constraints[widgets.Count];
			var margins = CalcCellMargins(widget.Padding, widgets.Count);
			int i = 0;
			foreach (var w in widgets) {
				var extraSpace = margins[i].Top + margins[i].Bottom;
				constraints[i++] = new LinearAllocator.Constraints {
					MinSize = w.MinSize.Y + extraSpace,
					MaxSize = w.MaxSize.Y + extraSpace,
					Stretch = (w.LayoutCell ?? LayoutCell.Default).StretchY
				};
			}
			var sizes = LinearAllocator.Allocate(widget.Height, constraints, roundSizes: true);
			i = 0;
			DebugRectangles.Clear();
			var position = Vector2.Zero;
			foreach (var w in widgets) {
				var size = new Vector2(Mathf.Clamp(widget.Width, w.MinWidth, w.MaxWidth), sizes[i]);
				TableLayout.LayoutCell(w, position, size, margins[i], DebugRectangles);
				position.Y += size.Y;
				i++;
			}
		}

		private Thickness[] CalcCellMargins(Thickness padding, int numCells)
		{
			var margins = new Thickness[numCells];
			for (int i = 0; i < numCells; i++) {
				margins[i] = new Thickness {
					Left = padding.Left,
					Right = padding.Right,
					Top = (i == 0) ? padding.Top : (Spacing / 2).Round(),
					Bottom = (i == numCells - 1) ? padding.Bottom : (Spacing / 2).Round(),
				};
			}
			return margins;
		}

		public override void MeasureSizeConstraints(Widget widget)
		{
			ConstraintsValid = true;
			var widgets = widget.Nodes.OfType<Widget>().ToList();
			var minSize = new Vector2(
				widgets.Max(i => i.MinSize.X),
				widgets.Sum(i => i.MinSize.Y)
			);
			var maxSize = new Vector2(
				widgets.Max(i => i.MaxSize.X),
				widgets.Sum(i => i.MaxSize.Y)
			);
			var extraSpace = new Vector2(0, (widgets.Count - 1) * Spacing) + widget.Padding;
			widget.MinSize = minSize + extraSpace;
			widget.MaxSize = maxSize + extraSpace;
		}
	}
}
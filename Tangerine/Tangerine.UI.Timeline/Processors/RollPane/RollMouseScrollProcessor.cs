﻿using System;
using System.Collections.Generic;
using Lime;

namespace Tangerine.UI.Timeline
{
	class RollMouseScrollProcessor : Core.IProcessor
	{
		Timeline timeline => Timeline.Instance;

		public IEnumerator<object> Loop()
		{
			var input = timeline.Roll.RootWidget.Input;
			while (true) {
				if (input.IsMouseOwner()) {
					var rect = timeline.Roll.RootWidget.CalcAABBInSpaceOf(timeline.PanelWidget);
					if (input.MousePosition.Y > rect.B.Y) {
						timeline.ScrollOrigin.Y += Metrics.TimelineDefaultRowHeight;
					} else if (input.MousePosition.Y < rect.A.Y) {
						timeline.ScrollOrigin.Y -= Metrics.TimelineDefaultRowHeight;
					}
					Window.Current.Invalidate();
				}
				yield return null;
			}
		}
	}	
}
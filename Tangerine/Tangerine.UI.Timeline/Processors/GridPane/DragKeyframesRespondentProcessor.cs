using System;
using System.Linq;
using System.Collections.Generic;
using Lime;
using Tangerine.Core;
using Tangerine.UI.Timeline.Components;

namespace Tangerine.UI.Timeline
{
	public class DragKeyframesRespondentProcessor : IProcessor
	{
		public IEnumerator<object> Loop()
		{
			var g = Timeline.Instance.Globals;
			while (true) {
				var r = g.Components.Get<DragKeyframesRequest>();
				if (r != null) {
					DragKeys(r.Offset, r.Selection);
					ShiftSelection(r.Offset);
					g.Components.Remove<DragKeyframesRequest>();
				}
				yield return null;
			}
		}

		static void ShiftSelection(IntVector2 offset)
		{
			Operations.ShiftSelection.Perform(offset);
		}

		static void DragKeys(IntVector2 offset, GridSelection selection)
		{
			var processedKeys = new HashSet<IKeyframe>();
			var operations = new List<Action>();
			foreach (var rect in Timeline.Instance.GridSelection.GetNonOverlappedRects()) {
				for (int row = rect.A.Y; row < rect.B.Y; row++) {
					if (!CheckRowRange(row)) {
						continue;
					}
					var rowComponents = Timeline.Instance.Rows[row].Components;
					var node = rowComponents.Get<NodeRow>()?.Node ?? rowComponents.Get<PropertyRow>()?.Node;
					if (node == null) {
						continue;
					}
					var property = rowComponents.Get<PropertyRow>()?.Animator.TargetProperty;
					foreach (var a in node.Animators) {
						if (property != null && a.TargetProperty != property) {
							continue;
						}
						foreach (var k in a.Keys.Where(k => k.Frame >= rect.A.X && k.Frame < rect.B.X)) {
							if (processedKeys.Contains(k)) {
								continue;
							}
							processedKeys.Add(k);
							operations.Insert(0, () => Core.Operations.RemoveKeyframe.Perform(a, k.Frame));
							var destRow = row + offset.Y;
							if (!CheckRowRange(destRow)) {
								continue;
							}
							var destRowComponents = Timeline.Instance.Rows[destRow].Components;
							var destNode = destRowComponents.Get<NodeRow>()?.Node ?? destRowComponents.Get<PropertyRow>()?.Node;
							if (destNode == null || !ArePropertiesCompatible(node, destNode, a.TargetProperty)) {
								continue;
							}
							if (k.Frame + offset.X >= 0) {
								var k1 = k.Clone();
								k1.Frame += offset.X;
								operations.Add(() => Core.Operations.SetKeyframe.Perform(destNode, a.TargetProperty, Document.Current.AnimationId, k1));
							}
						}
					}
				}
			}
			foreach (var o in operations) {
				o();
			}
		}

		static bool CheckRowRange(int row)
		{
			return row >= 0 && row < Timeline.Instance.Rows.Count;
		}

		static bool ArePropertiesCompatible(object object1, object object2, string property)
		{
			var m1 = object1.GetType().GetProperty(property)?.GetSetMethod();
			var m2 = object2.GetType().GetProperty(property)?.GetSetMethod();
			return m1 != null && m1 == m2;
		}
	}
}
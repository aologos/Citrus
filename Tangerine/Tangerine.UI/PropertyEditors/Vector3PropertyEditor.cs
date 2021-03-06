using Lime;
using Tangerine.Core;
using Tangerine.Core.ExpressionParser;

namespace Tangerine.UI
{
	public class Vector3PropertyEditor : CommonPropertyEditor<Vector3>
	{
		private NumericEditBox editorX, editorY, editorZ;

		public Vector3PropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			EditorContainer.AddNode(new Widget {
				Layout = new HBoxLayout { DefaultCell = new DefaultLayoutCell(Alignment.Center), Spacing = 4 },
				Nodes = {
					(editorX = editorParams.NumericEditBoxFactory()),
					(editorY = editorParams.NumericEditBoxFactory()),
					(editorZ = editorParams.NumericEditBoxFactory()),
					Spacer.HStretch(),
				}
			});
			var currentX = CoalescedPropertyComponentValue(v => v.X);
			var currentY = CoalescedPropertyComponentValue(v => v.Y);
			var currentZ = CoalescedPropertyComponentValue(v => v.Z);
			editorX.Submitted += text => SetComponent(editorParams, 0, editorX, currentX.GetValue());
			editorY.Submitted += text => SetComponent(editorParams, 1, editorY, currentY.GetValue());
			editorZ.Submitted += text => SetComponent(editorParams, 2, editorZ, currentZ.GetValue());
			editorX.AddChangeWatcher(currentX, v => editorX.Text = v.ToString("0.###"));
			editorY.AddChangeWatcher(currentY, v => editorY.Text = v.ToString("0.###"));
			editorZ.AddChangeWatcher(currentZ, v => editorZ.Text = v.ToString("0.###"));
		}

		void SetComponent(IPropertyEditorParams editorParams, int component, NumericEditBox editor, float currentValue)
		{
			if (Parser.TryParse(editor.Text, out double newValue)) {
				DoTransaction(() => {
					SetProperty<Vector3>((current) => {
						current[component] = (float)newValue;
						return current;
					});
				});
				editor.Text = newValue.ToString("0.###");
			} else {
				editor.Text = currentValue.ToString("0.###");
			}
		}

		public override void Submit()
		{
			var currentX = CoalescedPropertyComponentValue(v => v.X);
			var currentY = CoalescedPropertyComponentValue(v => v.Y);
			var currentZ = CoalescedPropertyComponentValue(v => v.Z);
			SetComponent(EditorParams, 0, editorX, currentX.GetValue());
			SetComponent(EditorParams, 1, editorY, currentY.GetValue());
			SetComponent(EditorParams, 2, editorZ, currentZ.GetValue());
		}
	}
}

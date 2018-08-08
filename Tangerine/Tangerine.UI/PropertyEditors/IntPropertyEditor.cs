using Lime;
using Tangerine.Core;

namespace Tangerine.UI
{
	public class IntPropertyEditor : CommonPropertyEditor<int>
	{
		private EditBox editor;

		public IntPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			editor = editorParams.EditBoxFactory();
			editor.MinMaxWidth = 80;
			editor.LayoutCell = new LayoutCell(Alignment.Center);
			ContainerWidget.AddNode(editor);
			var current = CoalescedPropertyValue();
			editor.Submitted += text => SetComponent(text, current);
			editor.AddChangeWatcher(current, v => editor.Text = v.ToString());
		}

		public void SetComponent(string text, IDataflowProvider<int> current)
		{
			int newValue;
			if (int.TryParse(text, out newValue)) {
				SetProperty(newValue);
			}
			else {
				editor.Text = current.GetValue().ToString();
			}
		}

		public override void Submit()
		{
			var current = CoalescedPropertyValue();
			SetComponent(editor.Text, current);
		}
	}
}
﻿namespace Lime
{
	/// <summary>
	/// Root widget of scene.
	/// </summary>
	public sealed class World : Frame
	{
		/// <summary>
		/// Widget which holds text input focus. Before processing Input.TextInput string 
		/// you should test whether ActiveTextWidget == this. For revoking text input focus from widget 
		/// you should nullify ActiveTextWidget.
		/// </summary>
		public IKeyboardInputProcessor ActiveTextWidget;

		/// <summary>
		/// On each update cycle active text widget must set this flag true.
		/// </summary>
		public bool IsActiveTextWidgetUpdated;

		internal float DistanceToNodeUnderCursor { get; set; }
		public Node NodeUnderCursor { get; internal set; }

		public static World Instance = new World();

#if iOS || ANDROID
		private IKeyboardInputProcessor prevActiveTextWidget;
#endif

		protected override void SelfUpdate(float delta)
		{
			DistanceToNodeUnderCursor = float.MaxValue;
			WidgetInput.RemoveInvalidatedCaptures();
			ParticleEmitter.NumberOfUpdatedParticles = 0;
			IsActiveTextWidgetUpdated = false;
		}

		protected override void SelfLateUpdate(float delta)
		{
			if (!IsActiveTextWidgetUpdated) {
				ActiveTextWidget = null;
			}
#if iOS || ANDROID
			if (Application.IsMainThread) {
				bool showKeyboard = ActiveTextWidget != null && ActiveTextWidget.Visible;
				if (prevActiveTextWidget != ActiveTextWidget) {
					Application.Instance.SoftKeyboard.Show(showKeyboard, ActiveTextWidget != null ? ActiveTextWidget.Text : "");
				}
#if ANDROID
				if (!Application.Instance.SoftKeyboard.Visible) {
					ActiveTextWidget = null;
				}
#endif
				// Handle switching between various text widgets
				if (prevActiveTextWidget != ActiveTextWidget && ActiveTextWidget != null && prevActiveTextWidget != null) {
					Application.Instance.SoftKeyboard.ChangeText(ActiveTextWidget.Text);
				}

				prevActiveTextWidget = ActiveTextWidget;
			}
#endif
		}
	}
}

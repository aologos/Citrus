﻿using EmptyProject.Application;
using Lime;

namespace EmptyProject
{
	public static class The
	{
		public static WindowWidget World { get { return Application.Application.World; } }
		public static IWindow Window { get { return World.Window; } }
		public static AppData AppData { get { return AppData.Instance; } }
	}
}
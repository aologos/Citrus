using System;
using System.Linq;
using Lime;
using Yuzu;
using System.Collections.Generic;

namespace Tangerine.Core
{
	public class UserPreferences : ComponentCollection<Component>
	{
		public static UserPreferences Instance { get; private set; }

		public static bool Initialize()
		{
			if (Instance != null) {
				throw new InvalidOperationException();
			}
			Instance = new UserPreferences();
			return Instance.Load();
		}

		public bool Load()
		{
			var path = GetPath();
			if (System.IO.File.Exists(path)) {
				try {
					Clear();
					TangerineYuzu.Instance.Value.ReadObjectFromFile<UserPreferences>(path, this);
					return true;
				} catch (System.Exception e) {
					Debug.Write($"Failed to load the user preferences ({path}): {e}");
					return false;
				}
			} else {
				return false;
			}
		}

		public void Save()
		{
			TangerineYuzu.Instance.Value.WriteObjectToFile(GetPath(), this, Serialization.Format.JSON);
		}

		public static string GetPath()
		{
			return System.IO.Path.Combine(Lime.Environment.GetDataDirectory("Tangerine"), "UserPreferences");
		}
	}
}

using System;

namespace Lime
{
	public class Exception : System.Exception
	{
		public Exception(string message) 
			: base(message)
		{
		}

		public Exception(string format, params object[] args) 
			: base(String.Format(format, args))
		{
		}
	}
}
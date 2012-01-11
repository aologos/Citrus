using System;
using ProtoBuf;

namespace Lime
{
	[System.Diagnostics.DebuggerStepThrough]
	[ProtoContract]
	public struct Size : IEquatable<Size>
	{
		[ProtoMember(1)]
		public int Width;
		
		[ProtoMember(2)]
		public int Height;

		public Size(int width, int height)
		{
			Width = width;
			Height = height;
		}

		public static explicit operator Vector2(Size size)
		{
			return new Vector2((float)size.Width, (float)size.Height);
		}

		bool IEquatable<Size>.Equals(Size rhs)
		{
			return Width == rhs.Width && Height == rhs.Height;
		}

		public override string ToString()
		{
			return String.Format("{0}, {1}", Width, Height);
		}
	}
}